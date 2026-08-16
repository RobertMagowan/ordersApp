using System.Text.Json;

namespace CloudOrders.UnitTests;

public sealed class RepositoryBootstrapTests
{
    [Fact]
    public void RepositoryPinsAStableDotnetTenSdk()
    {
        var repositoryRoot = FindRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "global.json")));

        var sdk = document.RootElement.GetProperty("sdk");
        var version = sdk.GetProperty("version").GetString();
        var allowPrerelease = sdk.GetProperty("allowPrerelease").GetBoolean();

        Assert.StartsWith("10.0.", version, StringComparison.Ordinal);
        Assert.False(allowPrerelease);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CloudOrders.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the CloudOrders repository root.");
    }
}
