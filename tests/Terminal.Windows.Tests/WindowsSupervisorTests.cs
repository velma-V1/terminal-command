using System.Text;
using Terminal.Core.Actions;
using Terminal.Execution;
using Terminal.Windows;

namespace Terminal.Windows.Tests;

public sealed class WindowsCommandLineTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("two words", "\"two words\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData("C:\\path with space\\", "\"C:\\path with space\\\\\"")]
    [InlineData("", "\"\"")]
    public void Quote_argument_matches_windows_crt_rules(string argument, string expected)
        => Assert.Equal(expected, WindowsCommandLine.QuoteArgument(argument));

    [Fact]
    public void Environment_block_applies_case_insensitive_delta_and_removal()
    {
        var parent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Alpha"] = "1",
            ["REMOVE_ME"] = "x"
        };
        var delta = new Dictionary<string, string?>
        {
            ["ALPHA"] = "2",
            ["remove_me"] = null,
            ["Beta"] = "3"
        };

        var block = WindowsEnvironmentBlock.Build(parent, delta);
        var entries = block.Split('\0', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(["Alpha=2", "Beta=3"], entries);
        Assert.EndsWith("\0\0", block, StringComparison.Ordinal);
    }
}

public sealed class WindowsJobObjectSupervisorTests
{
    [Fact]
    public async Task Executes_with_explicit_working_directory_and_environment()
    {
        if (!OperatingSystem.IsWindows()) return;
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("terminal-v3-win-");
        try
        {
            var action = CreateAction(
                "powershell.exe",
                ["-NoProfile", "-Command", "$env:TC_TEST_VALUE; (Get-Location).Path"],
                directory.FullName,
                new Dictionary<string, string?> { ["TC_TEST_VALUE"] = "bound-value" });
            var supervisor = new WindowsJobObjectSupervisor(maxCaptureBytes: 64 * 1024);

            var result = await supervisor.ExecuteAsync(Request(action), cancellationToken);
            var text = Encoding.UTF8.GetString(result.Stdout!.Captured.Span).Replace("\r", string.Empty, StringComparison.Ordinal);

            Assert.Equal(ProcessExecutionStatus.Exited, result.Status);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("bound-value\n", text, StringComparison.Ordinal);
            Assert.Contains(directory.FullName, text, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(ProcessContainmentBoundary.WindowsJobObject, result.Metrics!.Containment);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Output_is_bounded_but_process_is_fully_drained()
    {
        if (!OperatingSystem.IsWindows()) return;
        var cancellationToken = TestContext.Current.CancellationToken;
        var action = CreateAction(
            "powershell.exe",
            ["-NoProfile", "-Command", "[Console]::Out.Write('x' * 200000)"],
            Environment.CurrentDirectory);
        var supervisor = new WindowsJobObjectSupervisor(maxCaptureBytes: 4096);

        var result = await supervisor.ExecuteAsync(Request(action), cancellationToken);

        Assert.Equal(ProcessExecutionStatus.Exited, result.Status);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(4096, result.Stdout!.Captured.Length);
        Assert.True(result.Stdout.Truncated);
        Assert.True(result.Stdout.TotalBytes >= 200000);
    }

    [Fact]
    public async Task Timeout_terminates_entire_job_tree_before_descendant_can_escape()
    {
        if (!OperatingSystem.IsWindows()) return;
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("terminal-v3-tree-");
        var marker = Path.Combine(directory.FullName, "escaped.txt");
        try
        {
            var escapedMarker = marker.Replace("'", "''", StringComparison.Ordinal);
            var child = $"Start-Sleep -Seconds 2; Set-Content -LiteralPath '{escapedMarker}' -Value escaped";
            var script = $"Start-Process powershell.exe -ArgumentList @('-NoProfile','-Command',\"{child.Replace("\"", "`\"", StringComparison.Ordinal)}\"); Start-Sleep -Seconds 30";
            var action = CreateAction(
                "powershell.exe",
                ["-NoProfile", "-Command", script],
                directory.FullName,
                timeout: TimeSpan.FromMilliseconds(500));
            var supervisor = new WindowsJobObjectSupervisor(maxCaptureBytes: 8192);

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
    public async Task Caller_cancellation_terminates_job_and_is_distinct_from_timeout()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(300));
        var action = CreateAction(
            "powershell.exe",
            ["-NoProfile", "-Command", "Start-Sleep -Seconds 30"],
            Environment.CurrentDirectory,
            timeout: TimeSpan.FromSeconds(20));
        var supervisor = new WindowsJobObjectSupervisor(maxCaptureBytes: 8192);

        var result = await supervisor.ExecuteAsync(Request(action), cts.Token);

        Assert.Equal(ProcessExecutionStatus.Cancelled, result.Status);
        Assert.Equal(ProcessContainmentBoundary.WindowsJobObject, result.Metrics!.Containment);
    }

    private static ProcessExecutionRequest Request(TerminalAction action)
        => new(Guid.NewGuid(), Guid.NewGuid(), action);

    private static TerminalAction CreateAction(
        string operation,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string?>? environment = null,
        TimeSpan? timeout = null)
        => new(
            Guid.NewGuid(),
            "test",
            "test.windows",
            operation,
            arguments,
            ActionBackend.Windows,
            workingDirectory,
            environment ?? new Dictionary<string, string?>(),
            $"process:{Guid.NewGuid():N}",
            new Dictionary<string, string> { ["process"] = "test" },
            timeout ?? TimeSpan.FromSeconds(10),
            512L * 1024 * 1024,
            MutationClass.Ephemeral,
            RecoveryClass.None,
            "test",
            DateTimeOffset.UtcNow);
}
