namespace CloudOrders.ArchitectureTests;

public sealed class RepositoryPolicyTests
{
    [Fact]
    public void RepositoryContainsContributorGuideWithRequiredTitle()
    {
        var repositoryRoot = FindRepositoryRoot();
        var guidePath = Path.Combine(repositoryRoot, "AGENTS.md");

        Assert.True(File.Exists(guidePath), $"Expected contributor guide at {guidePath}.");
        var guide = File.ReadAllText(guidePath);
        Assert.Contains("# Repository Guidelines", guide, StringComparison.Ordinal);
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
