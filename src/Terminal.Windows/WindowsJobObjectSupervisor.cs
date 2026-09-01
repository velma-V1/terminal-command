using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Terminal.Execution;

namespace Terminal.Windows;

public sealed class WindowsJobObjectSupervisor : IProcessSupervisor
{
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateNoWindow = 0x08000000;
    private const uint StartfUseStdHandles = 0x00000100;
    private const uint HandleFlagInherit = 0x00000001;
    private const uint JobObjectLimitJobMemory = 0x00000200;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private static readonly IntPtr ProcThreadAttributeHandleList = new(0x00020002);
    private readonly int _maxCaptureBytes;

    public WindowsJobObjectSupervisor(int maxCaptureBytes = 1024 * 1024)
    {
        if (maxCaptureBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCaptureBytes));
        }

        _maxCaptureBytes = maxCaptureBytes;
    }

    public async ValueTask<ProcessExecutionResult> ExecuteAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows Job Object supervision requires Windows.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var action = request.Action;
        SafeFileHandle? stdoutRead = null;
        SafeFileHandle? stdoutWrite = null;
        SafeFileHandle? stderrRead = null;
        SafeFileHandle? stderrWrite = null;
        SafeFileHandle? stdinNull = null;
        SafeFileHandle? job = null;
        SafeFileHandle? processHandle = null;
        SafeFileHandle? threadHandle = null;
        IntPtr attributeList = IntPtr.Zero;
        IntPtr handleList = IntPtr.Zero;
        IntPtr environmentBlock = IntPtr.Zero;
        Process? process = null;

        try
        {
            CreateChildPipe(out stdoutRead, out stdoutWrite);
            CreateChildPipe(out stderrRead, out stderrWrite);
            stdinNull = CreateInheritableNullInput();
            job = CreateConfiguredJob(action.MemoryLimitBytes);

            var startup = new StartupInfoEx
            {
                StartupInfo = new StartupInfo
                {
                    cb = Marshal.SizeOf<StartupInfoEx>(),
                    dwFlags = StartfUseStdHandles,
                    hStdInput = stdinNull.DangerousGetHandle(),
                    hStdOutput = stdoutWrite.DangerousGetHandle(),
                    hStdError = stderrWrite.DangerousGetHandle()
                }
            };
            (attributeList, handleList) = BuildHandleWhitelist(
                stdinNull.DangerousGetHandle(),
                stdoutWrite.DangerousGetHandle(),
                stderrWrite.DangerousGetHandle());
            startup.lpAttributeList = attributeList;

            var parentEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                if (entry.Key is string key && entry.Value is string value)
                {
                    parentEnvironment[key] = value;
                }
            }

            var environment = WindowsEnvironmentBlock.Build(parentEnvironment, action.EnvironmentDelta);
            environmentBlock = Marshal.StringToHGlobalUni(environment);
            var executable = ResolveExecutable(action.Operation);
            var commandLine = new StringBuilder(WindowsCommandLine.Build(executable, action.Arguments));
            var flags = CreateSuspended | CreateUnicodeEnvironment | ExtendedStartupInfoPresent | CreateNoWindow;

            if (!CreateProcessW(
                    executable,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    true,
                    flags,
                    environmentBlock,
                    action.WorkingDirectory,
                    ref startup,
                    out var processInformation))
            {
                return Failed(request.ExecutionId, new Win32Exception(Marshal.GetLastPInvokeError()).Message);
            }

            processHandle = new SafeFileHandle(processInformation.hProcess, ownsHandle: true);
            threadHandle = new SafeFileHandle(processInformation.hThread, ownsHandle: true);
            process = Process.GetProcessById(checked((int)processInformation.dwProcessId));

            if (!AssignProcessToJobObject(job, processHandle))
            {
                _ = TerminateProcess(processHandle, 1);
                return Failed(request.ExecutionId, $"AssignProcessToJobObject failed with error {Marshal.GetLastPInvokeError()}.");
            }

            await using var stdoutStream = new FileStream(stdoutRead, FileAccess.Read, 16 * 1024, isAsync: false);
            stdoutRead = null;
            await using var stderrStream = new FileStream(stderrRead, FileAccess.Read, 16 * 1024, isAsync: false);
            stderrRead = null;
            var stdoutTask = StreamCapture.CaptureAsync(stdoutStream, _maxCaptureBytes, CancellationToken.None).AsTask();
            var stderrTask = StreamCapture.CaptureAsync(stderrStream, _maxCaptureBytes, CancellationToken.None).AsTask();

            stdoutWrite.Dispose();
            stdoutWrite = null;
            stderrWrite.Dispose();
            stderrWrite = null;
            stdinNull.Dispose();
            stdinNull = null;

            if (ResumeThread(threadHandle) == uint.MaxValue)
            {
                _ = TerminateJobObject(job, 1);
                return Failed(request.ExecutionId, $"ResumeThread failed with error {Marshal.GetLastPInvokeError()}.");
            }

            threadHandle.Dispose();
            threadHandle = null;

            var exitTask = process.WaitForExitAsync(CancellationToken.None);
            var timeoutTask = action.Timeout is { } timeout
                ? Task.Delay(timeout)
                : Task.Delay(Timeout.InfiniteTimeSpan);
            var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            var completed = await Task.WhenAny(exitTask, timeoutTask, cancellationTask).ConfigureAwait(false);

            var status = ProcessExecutionStatus.Exited;
            if (completed != exitTask)
            {
                status = cancellationToken.IsCancellationRequested
                    ? ProcessExecutionStatus.Cancelled
                    : ProcessExecutionStatus.TimedOut;
                if (!TerminateJobObject(job, 1))
                {
                    throw new IOException($"TerminateJobObject failed with error {Marshal.GetLastPInvokeError()}.");
                }

                await exitTask.ConfigureAwait(false);
            }

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            var metrics = QueryMetrics(job);
            if (!GetExitCodeProcess(processHandle, out var nativeExitCode))
            {
                throw new IOException($"GetExitCodeProcess failed with error {Marshal.GetLastPInvokeError()}.");
            }

            return new ProcessExecutionResult(
                request.ExecutionId,
                status,
                unchecked((int)nativeExitCode),
                stdout,
                stderr,
                metrics);
        }
        catch (Exception exception) when (exception is Win32Exception or IOException or InvalidOperationException or ArgumentException)
        {
            if (processHandle is not null && !processHandle.IsInvalid && job is not null && !job.IsInvalid)
            {
                _ = TerminateJobObject(job, 1);
            }

            return Failed(request.ExecutionId, exception.Message);
        }
        finally
        {
            process?.Dispose();
            threadHandle?.Dispose();
            processHandle?.Dispose();
            job?.Dispose();
            stdinNull?.Dispose();
            stdoutWrite?.Dispose();
            stderrWrite?.Dispose();
            stdoutRead?.Dispose();
            stderrRead?.Dispose();
            if (attributeList != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }

            if (handleList != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(handleList);
            }

            if (environmentBlock != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(environmentBlock);
            }
        }
    }

    private static void CreateChildPipe(out SafeFileHandle read, out SafeFileHandle write)
    {
        var attributes = new SecurityAttributes
        {
            nLength = Marshal.SizeOf<SecurityAttributes>(),
            bInheritHandle = true
        };
        if (!CreatePipe(out read, out write, ref attributes, 0))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        if (!SetHandleInformation(read, HandleFlagInherit, 0))
        {
            read.Dispose();
            write.Dispose();
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    private static SafeFileHandle CreateInheritableNullInput()
    {
        var attributes = new SecurityAttributes
        {
            nLength = Marshal.SizeOf<SecurityAttributes>(),
            bInheritHandle = true
        };
        var handle = CreateFileW(
            "NUL",
            0x80000000,
            0x00000001 | 0x00000002,
            ref attributes,
            3,
            0x00000080,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return handle;
    }

    private static SafeFileHandle CreateConfiguredJob(long? memoryLimitBytes)
    {
        var job = CreateJobObjectW(IntPtr.Zero, null);
        if (job.IsInvalid)
        {
            job.Dispose();
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var limits = new JobObjectExtendedLimitInformation();
        limits.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
        if (memoryLimitBytes is { } memoryLimit)
        {
            limits.BasicLimitInformation.LimitFlags |= JobObjectLimitJobMemory;
            limits.JobMemoryLimit = checked((UIntPtr)(ulong)memoryLimit);
        }

        var size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(limits, pointer, fDeleteOld: false);
            if (!SetInformationJobObject(job, 9, pointer, (uint)size))
            {
                job.Dispose();
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }

        return job;
    }

    private static (IntPtr AttributeList, IntPtr HandleList) BuildHandleWhitelist(params IntPtr[] handles)
    {
        IntPtr size = IntPtr.Zero;
        _ = InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        var attributeList = Marshal.AllocHGlobal(size);
        if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref size))
        {
            Marshal.FreeHGlobal(attributeList);
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        var handleList = Marshal.AllocHGlobal(IntPtr.Size * handles.Length);
        for (var index = 0; index < handles.Length; index++)
        {
            Marshal.WriteIntPtr(handleList, index * IntPtr.Size, handles[index]);
        }

        if (!UpdateProcThreadAttribute(
                attributeList,
                0,
                ProcThreadAttributeHandleList,
                handleList,
                (IntPtr)(IntPtr.Size * handles.Length),
                IntPtr.Zero,
                IntPtr.Zero))
        {
            DeleteProcThreadAttributeList(attributeList);
            Marshal.FreeHGlobal(attributeList);
            Marshal.FreeHGlobal(handleList);
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return (attributeList, handleList);
    }

    private static string ResolveExecutable(string executable)
    {
        if (Path.IsPathRooted(executable))
        {
            return executable;
        }

        var buffer = new StringBuilder(32768);
        var length = SearchPathW(null, executable, null, (uint)buffer.Capacity, buffer, IntPtr.Zero);
        return length > 0 && length < buffer.Capacity ? buffer.ToString() : executable;
    }

    private static ProcessExecutionMetrics QueryMetrics(SafeFileHandle job)
    {
        long? peakMemory = null;
        TimeSpan? user = null;
        TimeSpan? kernel = null;

        var extendedSize = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        var extendedPointer = Marshal.AllocHGlobal(extendedSize);
        try
        {
            if (QueryInformationJobObject(job, 9, extendedPointer, (uint)extendedSize, IntPtr.Zero))
            {
                var extended = Marshal.PtrToStructure<JobObjectExtendedLimitInformation>(extendedPointer);
                peakMemory = checked((long)extended.PeakJobMemoryUsed.ToUInt64());
            }
        }
        finally
        {
            Marshal.FreeHGlobal(extendedPointer);
        }

        var accountingSize = Marshal.SizeOf<JobObjectBasicAccountingInformation>();
        var accountingPointer = Marshal.AllocHGlobal(accountingSize);
        try
        {
            if (QueryInformationJobObject(job, 1, accountingPointer, (uint)accountingSize, IntPtr.Zero))
            {
                var accounting = Marshal.PtrToStructure<JobObjectBasicAccountingInformation>(accountingPointer);
                user = TimeSpan.FromTicks(accounting.TotalUserTime);
                kernel = TimeSpan.FromTicks(accounting.TotalKernelTime);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(accountingPointer);
        }

        return new ProcessExecutionMetrics(
            ProcessContainmentBoundary.WindowsJobObject,
            peakMemory,
            user,
            kernel);
    }

    private static ProcessExecutionResult Failed(Guid executionId, string message)
        => new(
            executionId,
            ProcessExecutionStatus.FailedToStart,
            exitCode: null,
            metrics: new ProcessExecutionMetrics(ProcessContainmentBoundary.None),
            errorMessage: message);

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public uint dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicAccountingInformation
    {
        public long TotalUserTime;
        public long TotalKernelTime;
        public long ThisPeriodTotalUserTime;
        public long ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount;
        public uint TotalProcesses;
        public uint ActiveProcesses;
        public uint TotalTerminatedProcesses;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreatePipe(
        out SafeFileHandle hReadPipe,
        out SafeFileHandle hWritePipe,
        ref SecurityAttributes lpPipeAttributes,
        int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetHandleInformation(SafeFileHandle hObject, uint dwMask, uint dwFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        ref SecurityAttributes lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObjectW(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeFileHandle hJob,
        int jobObjectInfoClass,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(
        SafeFileHandle hJob,
        int jobObjectInfoClass,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength,
        IntPtr lpReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle hJob, SafeFileHandle hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(SafeFileHandle hJob, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(SafeFileHandle hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(SafeFileHandle hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(SafeFileHandle hProcess, out uint lpExitCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(
        string lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref StartupInfoEx lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList,
        int dwAttributeCount,
        int dwFlags,
        ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        IntPtr attribute,
        IntPtr lpValue,
        IntPtr cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint SearchPathW(
        string? lpPath,
        string lpFileName,
        string? lpExtension,
        uint nBufferLength,
        StringBuilder lpBuffer,
        IntPtr lpFilePart);
}
