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

    [Fact]
    public void NestedAzureSqlDeploymentNamesAreDistinct()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainBicep = File.ReadAllText(Path.Combine(repositoryRoot, "infra", "main.bicep"));
        var sqlServerModule = File.ReadAllText(Path.Combine(repositoryRoot, "infra", "modules", "sql-server.bicep"));
        var sqlDatabaseModule = File.ReadAllText(Path.Combine(repositoryRoot, "infra", "modules", "sql-database.bicep"));

        const string outerSqlServerDeploymentName = "name: 'cloudOrdersSqlServer'";
        const string outerSqlDatabaseDeploymentName = "name: 'cloudOrdersSqlDatabase'";

        Assert.Contains(outerSqlServerDeploymentName, mainBicep, StringComparison.Ordinal);
        Assert.DoesNotContain(outerSqlServerDeploymentName, sqlServerModule, StringComparison.Ordinal);
        Assert.Contains(outerSqlDatabaseDeploymentName, mainBicep, StringComparison.Ordinal);
        Assert.DoesNotContain(outerSqlDatabaseDeploymentName, sqlDatabaseModule, StringComparison.Ordinal);
    }

    [Fact]
    public void MigrationJobReceivesAcrPullBeforePrivateImageValidation()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainBicep = File.ReadAllText(Path.Combine(repositoryRoot, "infra", "main.bicep"));
        var migrationJobModule = File.ReadAllText(Path.Combine(repositoryRoot, "infra", "modules", "migration-job.bicep"));

        Assert.Contains("param registryName string", migrationJobModule, StringComparison.Ordinal);
        Assert.Contains("resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing", migrationJobModule, StringComparison.Ordinal);
        Assert.Contains("resource migrationAcrPullRole 'Microsoft.Authorization/roleAssignments@2022-04-01'", migrationJobModule, StringComparison.Ordinal);
        Assert.Contains("scope: registry", migrationJobModule, StringComparison.Ordinal);
        Assert.Contains("principalId: migrationIdentity.properties.principalId", migrationJobModule, StringComparison.Ordinal);
        Assert.Contains("principalType: 'ServicePrincipal'", migrationJobModule, StringComparison.Ordinal);
        Assert.Contains("dependsOn: [", migrationJobModule, StringComparison.Ordinal);
        Assert.Contains("migrationAcrPullRole", migrationJobModule, StringComparison.Ordinal);
        Assert.Contains("param createJob bool", migrationJobModule, StringComparison.Ordinal);
        Assert.Contains("resource migrationJob 'Microsoft.App/jobs@2024-03-01' = if (createJob)", migrationJobModule, StringComparison.Ordinal);
        Assert.Contains("param deployMigrationJob bool = true", mainBicep, StringComparison.Ordinal);
        Assert.Contains("createJob: deployMigrationJob", mainBicep, StringComparison.Ordinal);
        Assert.Contains("param deployContainerApp bool = true", mainBicep, StringComparison.Ordinal);
        Assert.Contains("module containerApp 'modules/container-app.bicep' = if (deployContainerApp)", mainBicep, StringComparison.Ordinal);
        Assert.Contains("registryName: registryModule.outputs.name", mainBicep, StringComparison.Ordinal);
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
