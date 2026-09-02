using System.Text;
using Terminal.Core.Actions;
using Terminal.Core.Evidence;
using Terminal.Execution;
using Terminal.LinuxAgent;

namespace Terminal.LinuxAgent.Tests;

public sealed class LinuxProcessSupervisorTests
{
    [Fact]
    public async Task Linux_execution_reports_real_containment_and_bounded_output()
    {
        if (!OperatingSystem.IsLinux()) return;
        var cancellationToken = TestContext.Current.CancellationToken;
        var action = CreateAction("/bin/sh", ["-c", "printf '%*s' 200000 '' | tr ' ' x"], Environment.CurrentDirectory);
        var supervisor = new LinuxProcessSupervisor(maxCaptureBytes: 4096);

        var result = await supervisor.ExecuteAsync(Request(action), cancellationToken);

        Assert.Equal(ProcessExecutionStatus.Exited, result.Status);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Stdout!.Truncated);
        Assert.Equal(4096, result.Stdout.Captured.Length);
        Assert.True(result.Stdout.TotalBytes >= 200000);
        Assert.True(result.Metrics!.Containment is ProcessContainmentBoundary.LinuxCgroupV2 or ProcessContainmentBoundary.LinuxProcessGroup);
    }

    [Fact]
    public async Task Timeout_kills_linux_descendants_not_just_parent()
    {
        if (!OperatingSystem.IsLinux()) return;
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("terminal-v3-linux-");
        var marker = Path.Combine(directory.FullName, "escaped.txt");
        try
        {
            var escaped = marker.Replace("'", "'\"'\"'", StringComparison.Ordinal);
            var action = CreateAction("/bin/sh", ["-c", $"(sleep 2; printf escaped > '{escaped}') & sleep 30"], directory.FullName, timeout: TimeSpan.FromMilliseconds(400));
            var supervisor = new LinuxProcessSupervisor(maxCaptureBytes: 8192);

            var result = await supervisor.ExecuteAsync(Request(action), cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);

            Assert.Equal(ProcessExecutionStatus.TimedOut, result.Status);
            Assert.False(File.Exists(marker));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Environment_delta_and_working_directory_are_explicit()
    {
        if (!OperatingSystem.IsLinux()) return;
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("terminal-v3-linux-env-");
        try
        {
            var action = CreateAction(
                "/bin/sh",
                ["-c", "printf '%s\\n%s\\n' \"$TC_TEST_VALUE\" \"$PWD\""],
                directory.FullName,
                new Dictionary<string, string?> { ["TC_TEST_VALUE"] = "bound-value" });
            var supervisor = new LinuxProcessSupervisor(maxCaptureBytes: 8192);

            var result = await supervisor.ExecuteAsync(Request(action), cancellationToken);
            var text = Encoding.UTF8.GetString(result.Stdout!.Captured.Span);

            Assert.Equal(ProcessExecutionStatus.Exited, result.Status);
            Assert.Contains("bound-value", text, StringComparison.Ordinal);
            Assert.Contains(directory.FullName, text, StringComparison.Ordinal);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static ProcessExecutionRequest Request(TerminalAction action)
        => new(Guid.NewGuid(), Guid.NewGuid(), action);

    private static TerminalAction CreateAction(
        string operation,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environment = null,
        TimeSpan? timeout = null)
    {
        var now = DateTimeOffset.UtcNow;
        var processIdentity = $"process:{Guid.NewGuid():N}";
        return new TerminalAction(
            Guid.NewGuid(),
            "test",
            "test.linux",
            operation,
            arguments,
            ActionBackend.Wsl,
            new ResourceRef(ResourceEnvironment.Wsl, ResourceKind.Directory, workingDirectory, workingDirectory, $"dir:{workingDirectory}", "wsl", null, now, RevalidationMethod.DirectoryIdentity),
            environment ?? new Dictionary<string, string?>(),
            [new ResourceRef(ResourceEnvironment.Wsl, ResourceKind.Process, processIdentity, processIdentity, processIdentity, "wsl", null, now, RevalidationMethod.ProcessIdentity)],
            new ScopeContract([new ScopeEntry(ScopeDimension.Process, "test")]),
            timeout ?? TimeSpan.FromSeconds(10),
            512L * 1024 * 1024,
            MutationClass.Ephemeral,
            RecoveryClass.None,
            new Provenance(ProvenanceSourceType.System, "test", TrustClass.TrustedLocal, now, "test:linux", []),
            now);
    }
}
