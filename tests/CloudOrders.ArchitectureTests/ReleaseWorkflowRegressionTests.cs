using System.Diagnostics;

namespace CloudOrders.ArchitectureTests;

public sealed class ReleaseWorkflowRegressionTests
{
    [Fact]
    public async Task ExecutableReleaseWorkflowRegressionsPass()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CloudOrders.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var startInfo = new ProcessStartInfo(OperatingSystem.IsWindows() ? "python" : "python3")
        {
            WorkingDirectory = directory.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("tests/workflow/test_release_workflow.py");
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.ExitCode == 0, $"{await output}\n{await error}");
    }
}
