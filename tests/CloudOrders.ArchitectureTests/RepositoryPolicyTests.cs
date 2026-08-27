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

    [Fact]
    public void InfrastructureDeclaresNonProductionAzureSqlDeploymentContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainBicep = File.ReadAllText(Path.Combine(repositoryRoot, "infra", "main.bicep"));

        Assert.Contains("param deploySql bool", mainBicep, StringComparison.Ordinal);
        Assert.Contains("param sqlServerName string", mainBicep, StringComparison.Ordinal);
        Assert.Contains("param sqlDatabaseName string", mainBicep, StringComparison.Ordinal);
        Assert.Contains("param migrationIdentityName string", mainBicep, StringComparison.Ordinal);
        Assert.Contains("output sqlServerFqdn string", mainBicep, StringComparison.Ordinal);
        Assert.Contains("output databaseName string", mainBicep, StringComparison.Ordinal);
        Assert.Contains("output migrationJobName string", mainBicep, StringComparison.Ordinal);
        Assert.Contains("output migrationIdentityClientId string", mainBicep, StringComparison.Ordinal);
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
