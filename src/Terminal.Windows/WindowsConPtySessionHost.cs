using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Terminal.Core.Actions;
using Terminal.Execution;

namespace Terminal.Windows;

public sealed class WindowsConPtySessionHost : ITerminalSessionHost
{
    private static readonly IReadOnlySet<ActionBackend> Backends =
        new HashSet<ActionBackend> { ActionBackend.Windows };

    public IReadOnlySet<ActionBackend> SupportedBackends => Backends;

    public ValueTask<ITerminalSession> StartAsync(
        TerminalSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows ConPTY requires Windows.");
        }

        if (request.Action.Backend != ActionBackend.Windows)
        {
            throw new NotSupportedException(
                $"Windows ConPTY does not support backend {request.Action.Backend}.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<ITerminalSession>(WindowsConPtySession.Start(request));
    }

    private sealed class WindowsConPtySession : ITerminalSession
    {
        private const uint CreateSuspended = 0x00000004;
        private const uint CreateUnicodeEnvironment = 0x00000400;
        private const uint ExtendedStartupInfoPresent = 0x00080000;
        private const uint StartfUseStdHandles = 0x00000100;
        private static readonly IntPtr ProcThreadAttributePseudoConsole = new(0x00020016);
        private readonly FileStream _input;
        private readonly FileStream _output;
        private readonly Process _process;
        private readonly WindowsJobLease _job;
        private readonly Task _exitMonitor;
        private IntPtr _pseudoConsole;
        private int _state = (int)TerminalSessionState.Running;
        private int _disposed;

        private WindowsConPtySession(
            Guid sessionId,
            FileStream input,
            FileStream output,
            Process process,
            WindowsJobLease job,
            IntPtr pseudoConsole)
        {
            SessionId = sessionId;
            _input = input;
            _output = output;
            _process = process;
            _job = job;
            _pseudoConsole = pseudoConsole;
            _exitMonitor = MonitorExitAsync();
        }

        public Guid SessionId { get; }
        public ActionBackend Backend => ActionBackend.Windows;
        public TerminalSessionFeatures Features { get; } = new(
            Interactive: true,
            Resize: true,
            CtrlC: true,
            StreamingOutput: true);
        public TerminalSessionState State => (TerminalSessionState)Volatile.Read(ref _state);

        public static WindowsConPtySession Start(TerminalSessionRequest request)
        {
            SafeFileHandle? pseudoInputRead = null;
            SafeFileHandle? hostInputWrite = null;
            SafeFileHandle? hostOutputRead = null;
            SafeFileHandle? pseudoOutputWrite = null;
            SafeFileHandle? processHandle = null;
            SafeFileHandle? threadHandle = null;
            FileStream? inputStream = null;
            FileStream? outputStream = null;
            Process? process = null;
            WindowsJobLease? job = null;
            IntPtr pseudoConsole = IntPtr.Zero;
            IntPtr attributeList = IntPtr.Zero;
            IntPtr environmentBlock = IntPtr.Zero;

            try
            {
                CreatePipePair(out pseudoInputRead, out hostInputWrite);
                CreatePipePair(out hostOutputRead, out pseudoOutputWrite);

                var size = new Coord(
                    request.InitialDimensions.Columns,
                    request.InitialDimensions.Rows);
                var createResult = CreatePseudoConsole(
                    size,
                    pseudoInputRead,
                    pseudoOutputWrite,
                    0,
                    out pseudoConsole);
                ThrowIfFailedHResult(createResult, "CreatePseudoConsole");

                pseudoInputRead.Dispose();
                pseudoInputRead = null;
                pseudoOutputWrite.Dispose();
                pseudoOutputWrite = null;

                attributeList = BuildPseudoConsoleAttribute(pseudoConsole);
                var startup = new StartupInfoEx
                {
                    StartupInfo = new StartupInfo
                    {
                        cb = Marshal.SizeOf<StartupInfoEx>(),
                        dwFlags = StartfUseStdHandles,
                        hStdInput = IntPtr.Zero,
                        hStdOutput = IntPtr.Zero,
                        hStdError = IntPtr.Zero
                    },
                    lpAttributeList = attributeList
                };

                var action = request.Action;
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
                var flags = CreateSuspended | CreateUnicodeEnvironment | ExtendedStartupInfoPresent;

                if (!CreateProcessW(
                        executable,
                        commandLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        false,
                        flags,
                        environmentBlock,
                        action.WorkingDirectory.CanonicalIdentity,
                        ref startup,
                        out var processInformation))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                processHandle = new SafeFileHandle(processInformation.hProcess, ownsHandle: true);
                threadHandle = new SafeFileHandle(processInformation.hThread, ownsHandle: true);
                job = WindowsJobLease.Create(action.MemoryLimitBytes);
                job.Assign(processHandle);
                process = Process.GetProcessById(checked((int)processInformation.dwProcessId));

                inputStream = new FileStream(
                    hostInputWrite,
                    FileAccess.Write,
                    16 * 1024,
                    isAsync: false);
                hostInputWrite = null;
                outputStream = new FileStream(
                    hostOutputRead,
                    FileAccess.Read,
                    16 * 1024,
                    isAsync: false);
                hostOutputRead = null;

                if (ResumeThread(threadHandle) == uint.MaxValue)
                {
                    job.Terminate();
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }

                threadHandle.Dispose();
                threadHandle = null;
                processHandle.Dispose();
                processHandle = null;

                var session = new WindowsConPtySession(
                    request.SessionId,
                    inputStream,
                    outputStream,
                    process,
                    job,
                    pseudoConsole);
                inputStream = null;
                outputStream = null;
                process = null;
                job = null;
                pseudoConsole = IntPtr.Zero;
                return session;
            }
            catch
            {
                if (job is not null)
                {
                    try
                    {
                        job.Terminate();
                    }
                    catch (Win32Exception)
                    {
                    }
                }

                throw;
            }
            finally
            {
                process?.Dispose();
                inputStream?.Dispose();
                outputStream?.Dispose();
                threadHandle?.Dispose();
                processHandle?.Dispose();
                job?.Dispose();
                pseudoInputRead?.Dispose();
                hostInputWrite?.Dispose();
                hostOutputRead?.Dispose();
                pseudoOutputWrite?.Dispose();
                if (pseudoConsole != IntPtr.Zero)
                {
                    ClosePseudoConsole(pseudoConsole);
                }

                if (attributeList != IntPtr.Zero)
                {
                    DeleteProcThreadAttributeList(attributeList);
                    Marshal.FreeHGlobal(attributeList);
                }

                if (environmentBlock != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(environmentBlock);
                }
            }
        }

        public async ValueTask WriteAsync(
            ReadOnlyMemory<byte> input,
            CancellationToken cancellationToken = default)
        {
            ThrowIfNotRunning();
            await _input.WriteAsync(input, cancellationToken).ConfigureAwait(false);
            await _input.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public ValueTask ResizeAsync(
            TerminalDimensions dimensions,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfNotRunning();
            var handle = Volatile.Read(ref _pseudoConsole);
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("The pseudoconsole is already closed.");
            }

            var result = ResizePseudoConsole(
                handle,
                new Coord(dimensions.Columns, dimensions.Rows));
            ThrowIfFailedHResult(result, "ResizePseudoConsole");
            return ValueTask.CompletedTask;
        }

        public async ValueTask SignalAsync(
            TerminalSignal signal,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            if (signal == TerminalSignal.CtrlC)
            {
                ThrowIfNotRunning();
                await _input.WriteAsync(new byte[] { 0x03 }, cancellationToken).ConfigureAwait(false);
                await _input.FlushAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (State is TerminalSessionState.Running or TerminalSessionState.Starting)
            {
                _job.Terminate();
                _ = Interlocked.CompareExchange(
                    ref _state,
                    (int)TerminalSessionState.Cancelled,
                    (int)TerminalSessionState.Running);
            }

            await _exitMonitor.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadOutputAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var read = await _output
                    .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                yield return buffer.AsMemory(0, read).ToArray();
            }

            await _exitMonitor.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (State is TerminalSessionState.Running or TerminalSessionState.Starting)
            {
                try
                {
                    _job.Terminate();
                    _ = Interlocked.CompareExchange(
                        ref _state,
                        (int)TerminalSessionState.Cancelled,
                        (int)TerminalSessionState.Running);
                }
                catch (Win32Exception)
                {
                    Interlocked.Exchange(ref _state, (int)TerminalSessionState.Failed);
                }
            }

            _input.Dispose();
            try
            {
                await _exitMonitor.ConfigureAwait(false);
            }
            finally
            {
                ClosePseudoConsoleOnce();
                _output.Dispose();
                _process.Dispose();
                _job.Dispose();
                Interlocked.Exchange(ref _state, (int)TerminalSessionState.Closed);
            }
        }

        private async Task MonitorExitAsync()
        {
            try
            {
                await _process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                _ = Interlocked.CompareExchange(
                    ref _state,
                    (int)TerminalSessionState.Exited,
                    (int)TerminalSessionState.Running);
            }
            catch (InvalidOperationException)
            {
                _ = Interlocked.CompareExchange(
                    ref _state,
                    (int)TerminalSessionState.Failed,
                    (int)TerminalSessionState.Running);
            }
            finally
            {
                ClosePseudoConsoleOnce();
            }
        }

        private void ClosePseudoConsoleOnce()
        {
            var handle = Interlocked.Exchange(ref _pseudoConsole, IntPtr.Zero);
            if (handle != IntPtr.Zero)
            {
                ClosePseudoConsole(handle);
            }
        }

        private void ThrowIfNotRunning()
        {
            ThrowIfDisposed();
            if (State != TerminalSessionState.Running)
            {
                throw new InvalidOperationException($"Terminal session is not running; state is {State}.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(WindowsConPtySession));
            }
        }

        private static void CreatePipePair(
            out SafeFileHandle read,
            out SafeFileHandle write)
        {
            var attributes = new SecurityAttributes
            {
                nLength = Marshal.SizeOf<SecurityAttributes>(),
                bInheritHandle = false
            };
            if (!CreatePipe(out read, out write, ref attributes, 0))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }

        private static IntPtr BuildPseudoConsoleAttribute(IntPtr pseudoConsole)
        {
            IntPtr size = IntPtr.Zero;
            _ = InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            if (size == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            var attributeList = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref size))
            {
                Marshal.FreeHGlobal(attributeList);
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            if (!UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    ProcThreadAttributePseudoConsole,
                    pseudoConsole,
                    (IntPtr)IntPtr.Size,
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            return attributeList;
        }

        private static string ResolveExecutable(string executable)
        {
            if (Path.IsPathRooted(executable))
            {
                return executable;
            }

            var buffer = new StringBuilder(32768);
            var length = SearchPathW(
                null,
                executable,
                null,
                (uint)buffer.Capacity,
                buffer,
                IntPtr.Zero);
            return length > 0 && length < buffer.Capacity
                ? buffer.ToString()
                : executable;
        }

        private static void ThrowIfFailedHResult(int result, string operation)
        {
            if (result != 0)
            {
                throw new IOException($"{operation} failed with HRESULT 0x{result:X8}.");
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct Coord(short x, short y)
        {
            public readonly short X = x;
            public readonly short Y = y;
        }

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

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreatePipe(
            out SafeFileHandle hReadPipe,
            out SafeFileHandle hWritePipe,
            ref SecurityAttributes lpPipeAttributes,
            int nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int CreatePseudoConsole(
            Coord size,
            SafeFileHandle hInput,
            SafeFileHandle hOutput,
            uint dwFlags,
            out IntPtr phPC);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int ResizePseudoConsole(IntPtr hPC, Coord size);

        [DllImport("kernel32.dll")]
        private static extern void ClosePseudoConsole(IntPtr hPC);

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

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint ResumeThread(SafeFileHandle hThread);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint SearchPathW(
            string? lpPath,
            string lpFileName,
            string? lpExtension,
            uint nBufferLength,
            StringBuilder lpBuffer,
            IntPtr lpFilePart);
    }

    private sealed class WindowsJobLease : IDisposable
    {
        private const uint JobObjectLimitJobMemory = 0x00000200;
        private const uint JobObjectLimitKillOnJobClose = 0x00002000;
        private readonly SafeFileHandle _handle;
        private int _disposed;

        private WindowsJobLease(SafeFileHandle handle)
        {
            _handle = handle;
        }

        public static WindowsJobLease Create(long? memoryLimitBytes)
        {
            var handle = CreateJobObjectW(IntPtr.Zero, null);
            if (handle.IsInvalid)
            {
                handle.Dispose();
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
                if (!SetInformationJobObject(handle, 9, pointer, (uint)size))
                {
                    handle.Dispose();
                    throw new Win32Exception(Marshal.GetLastPInvokeError());
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
            }

            return new WindowsJobLease(handle);
        }

        public void Assign(SafeFileHandle processHandle)
        {
            ThrowIfDisposed();
            if (!AssignProcessToJobObject(_handle, processHandle))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }

        public void Terminate()
        {
            ThrowIfDisposed();
            if (!TerminateJobObject(_handle, 1))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _handle.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(WindowsJobLease));
            }
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

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateJobObjectW(
            IntPtr lpJobAttributes,
            string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(
            SafeFileHandle hJob,
            int jobObjectInfoClass,
            IntPtr lpJobObjectInfo,
            uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(
            SafeFileHandle hJob,
            SafeFileHandle hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateJobObject(
            SafeFileHandle hJob,
            uint uExitCode);
    }
}
