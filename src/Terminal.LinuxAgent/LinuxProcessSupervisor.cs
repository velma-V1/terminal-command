using System.Diagnostics;
using System.Runtime.InteropServices;
using Terminal.Core.Actions;
using Terminal.Execution;

namespace Terminal.LinuxAgent;

public sealed class LinuxProcessSupervisor : IProcessSupervisor
{
    private const int SigKill = 9;
    private readonly int _maxCaptureBytes;

    public LinuxProcessSupervisor(int maxCaptureBytes = 1024 * 1024)
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
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("Linux process supervision requires Linux.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var action = request.Action;
        CgroupV2Lease? cgroup = null;
        ProcessContainmentBoundary containment;
        ProcessStartInfo startInfo;

        try
        {
            cgroup = CgroupV2Lease.TryCreate(request.ExecutionId, action.MemoryLimitBytes);
            if (cgroup is not null)
            {
                containment = ProcessContainmentBoundary.LinuxCgroupV2;
                startInfo = BuildCgroupStartInfo(action, cgroup.Path);
            }
            else
            {
                containment = ProcessContainmentBoundary.LinuxProcessGroup;
                startInfo = BuildProcessGroupStartInfo(action);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            cgroup?.Dispose();
            return Failed(request.ExecutionId, exception.Message);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
            {
                cgroup?.Dispose();
                return Failed(request.ExecutionId, "Process.Start returned false.");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            cgroup?.Dispose();
            return Failed(request.ExecutionId, exception.Message);
        }

        var stdoutTask = StreamCapture.CaptureAsync(
            process.StandardOutput.BaseStream,
            _maxCaptureBytes,
            CancellationToken.None).AsTask();
        var stderrTask = StreamCapture.CaptureAsync(
            process.StandardError.BaseStream,
            _maxCaptureBytes,
            CancellationToken.None).AsTask();

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

            if (containment == ProcessContainmentBoundary.LinuxCgroupV2)
            {
                cgroup!.Kill();
            }
            else
            {
                KillProcessGroup(process.Id);
            }

            await exitTask.ConfigureAwait(false);
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        var metrics = ReadMetrics(process, containment);
        var exitCode = process.ExitCode;
        cgroup?.Dispose();

        return new ProcessExecutionResult(
            request.ExecutionId,
            status,
            exitCode,
            stdout,
            stderr,
            metrics);
    }

    private static ProcessStartInfo BuildCgroupStartInfo(TerminalAction action, string cgroupPath)
    {
        const string launcher = "cg=\"$1\"; shift; printf '%s\\n' \"$$\" > \"$cg/cgroup.procs\" || exit 126; exec \"$@\"";
        var info = BaseStartInfo("/bin/sh", action);
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(launcher);
        info.ArgumentList.Add("terminal-cgroup-launcher");
        info.ArgumentList.Add(cgroupPath);
        info.ArgumentList.Add(action.Operation);
        foreach (var argument in action.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    private static ProcessStartInfo BuildProcessGroupStartInfo(TerminalAction action)
    {
        var setsid = File.Exists("/usr/bin/setsid")
            ? "/usr/bin/setsid"
            : File.Exists("/bin/setsid")
                ? "/bin/setsid"
                : throw new IOException("setsid is required for Linux process-group containment fallback.");
        var info = BaseStartInfo(setsid, action);
        info.ArgumentList.Add("--wait");
        info.ArgumentList.Add(action.Operation);
        foreach (var argument in action.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    private static ProcessStartInfo BaseStartInfo(string executable, TerminalAction action)
    {
        var info = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
            WorkingDirectory = action.WorkingDirectory
        };

        foreach (var (key, value) in action.EnvironmentDelta)
        {
            if (value is null)
            {
                info.Environment.Remove(key);
            }
            else
            {
                info.Environment[key] = value;
            }
        }

        return info;
    }

    private static ProcessExecutionMetrics ReadMetrics(
        Process process,
        ProcessContainmentBoundary containment)
    {
        try
        {
            return new ProcessExecutionMetrics(
                containment,
                process.PeakWorkingSet64,
                process.UserProcessorTime,
                process.PrivilegedProcessorTime);
        }
        catch (InvalidOperationException)
        {
            return new ProcessExecutionMetrics(containment);
        }
    }

    private static ProcessExecutionResult Failed(Guid executionId, string message)
        => new(
            executionId,
            ProcessExecutionStatus.FailedToStart,
            exitCode: null,
            metrics: new ProcessExecutionMetrics(ProcessContainmentBoundary.None),
            errorMessage: message);

    private static void KillProcessGroup(int processGroupId)
    {
        var result = kill(-processGroupId, SigKill);
        if (result != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            const int Esrch = 3;
            if (error != Esrch)
            {
                throw new IOException($"killpg failed with errno {error}.");
            }
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int sig);

    private sealed class CgroupV2Lease : IDisposable
    {
        private CgroupV2Lease(string path) => Path = path;

        public string Path { get; }

        public static CgroupV2Lease? TryCreate(Guid executionId, long? memoryLimitBytes)
        {
            const string root = "/sys/fs/cgroup";
            if (!File.Exists(System.IO.Path.Combine(root, "cgroup.controllers")) ||
                !File.Exists("/proc/self/cgroup"))
            {
                return null;
            }

            var unified = File.ReadLines("/proc/self/cgroup")
                .FirstOrDefault(static line => line.StartsWith("0::", StringComparison.Ordinal));
            if (unified is null)
            {
                return null;
            }

            var relative = unified[3..].TrimStart('/');
            var current = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, relative));
            var normalizedRoot = System.IO.Path.GetFullPath(root) + System.IO.Path.DirectorySeparatorChar;
            if (!current.StartsWith(normalizedRoot, StringComparison.Ordinal) &&
                !string.Equals(current, System.IO.Path.GetFullPath(root), StringComparison.Ordinal))
            {
                return null;
            }

            var path = System.IO.Path.Combine(current, $"terminal-v3-{executionId:N}");
            try
            {
                Directory.CreateDirectory(path);
                if (memoryLimitBytes is { } memoryLimit)
                {
                    File.WriteAllText(System.IO.Path.Combine(path, "memory.max"), memoryLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                return new CgroupV2Lease(path);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                TryDelete(path);
                return null;
            }
        }

        public void Kill()
        {
            try
            {
                File.WriteAllText(System.IO.Path.Combine(Path, "cgroup.kill"), "1");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new IOException("Failed to kill the Linux cgroup containment boundary.", exception);
            }
        }

        public void Dispose() => TryDelete(Path);

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
