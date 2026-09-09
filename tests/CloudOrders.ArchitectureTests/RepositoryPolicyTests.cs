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

    [Fact]
    public void MigrationJobSelectsItsUserAssignedIdentityForAzureSql()
    {
        var repositoryRoot = FindRepositoryRoot();
        var migrationJobModule = File.ReadAllText(Path.Combine(repositoryRoot, "infra", "modules", "migration-job.bicep"));

        Assert.Contains("User Id=${migrationIdentity.properties.clientId}", migrationJobModule, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionProjectsDoNotReferencePolicyTestAuthentication()
    {
        var repositoryRoot = FindRepositoryRoot();
        var productionSource = Directory.EnumerateFiles(Path.Combine(repositoryRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText);

        Assert.DoesNotContain(productionSource, source => source.Contains("PolicyTestAuthenticationHandler", StringComparison.Ordinal));
        Assert.DoesNotContain(productionSource, source => source.Contains("PolicyTest", StringComparison.Ordinal));
    }

    [Fact]
    public void PromotionPolicyAcceptsOnlyTheThreePullRequestPromotionPaths()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(repositoryRoot, ".github", "workflows", "branch-policy.yml"));

        Assert.Contains("pull_request:", workflow, StringComparison.Ordinal);
        Assert.Contains("      - development", workflow, StringComparison.Ordinal);
        Assert.Contains("      - test", workflow, StringComparison.Ordinal);
        Assert.Contains("      - master", workflow, StringComparison.Ordinal);
        Assert.Contains("contents: read", workflow, StringComparison.Ordinal);
        Assert.Contains("name: Enforce promotion source branch", workflow, StringComparison.Ordinal);
        Assert.Contains("development)", workflow, StringComparison.Ordinal);
        Assert.Contains("[[ \"$HEAD\" == feature/* ]]", workflow, StringComparison.Ordinal);
        Assert.Contains("test)", workflow, StringComparison.Ordinal);
        Assert.Contains("[[ \"$HEAD\" == development ]]", workflow, StringComparison.Ordinal);
        Assert.Contains("master)", workflow, StringComparison.Ordinal);
        Assert.Contains("[[ \"$HEAD\" == test ]]", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("pull_request_target:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("push:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("checks: write", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("gh api", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("two-parent merge", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonProductionAutoMergeWorkflowIsNarrowlyGuarded()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workflowPath = Path.Combine(repositoryRoot, ".github", "workflows", "auto-merge-nonproduction.yml");

        Assert.True(File.Exists(workflowPath), $"Expected nonproduction auto-merge workflow at {workflowPath}.");
        var workflow = File.ReadAllText(workflowPath);

        Assert.Contains("pull_request:", workflow, StringComparison.Ordinal);
        Assert.Contains("    types: [opened, reopened, synchronize, ready_for_review, edited]", workflow, StringComparison.Ordinal);
        Assert.Contains("contents: write", workflow, StringComparison.Ordinal);
        Assert.Contains("pull-requests: write", workflow, StringComparison.Ordinal);
        Assert.Contains("actions: write", workflow, StringComparison.Ordinal);
        Assert.Contains("github.event.pull_request.draft == false", workflow, StringComparison.Ordinal);
        Assert.Contains("github.event.pull_request.head.repo.full_name == github.repository", workflow, StringComparison.Ordinal);
        Assert.Contains("github.event.pull_request.base.ref == 'development'", workflow, StringComparison.Ordinal);
        Assert.Contains("github.event.pull_request.head.ref", workflow, StringComparison.Ordinal);
        Assert.Contains("feature/", workflow, StringComparison.Ordinal);
        Assert.Contains("github.event.pull_request.base.ref == 'test'", workflow, StringComparison.Ordinal);
        Assert.Contains("github.event.pull_request.head.ref == 'development'", workflow, StringComparison.Ordinal);
        Assert.Contains("gh pr merge \"$PR_URL\" --auto --merge", workflow, StringComparison.Ordinal);
        Assert.Contains("gh pr view \"$PR_URL\" --json state,mergedAt,mergeCommit,baseRefName,headRefOid", workflow, StringComparison.Ordinal);
        Assert.Contains("CURRENT_STATE", workflow, StringComparison.Ordinal);
        Assert.Contains("MERGED", workflow, StringComparison.Ordinal);
        Assert.Contains("CURRENT_MERGE_COMMIT", workflow, StringComparison.Ordinal);
        Assert.Contains("GITHUB_REPOSITORY: ${{ github.repository }}", workflow, StringComparison.Ordinal);
        Assert.Contains("gh workflow run deploy.yml --repo \"$GITHUB_REPOSITORY\" --ref \"$CURRENT_BASE_REF\" -f release_sha=\"$CURRENT_MERGE_COMMIT\"", workflow, StringComparison.Ordinal);
        Assert.Contains("gh pr view \"$PR_URL\" --json baseRefName,headRefName,isDraft,headRepository,headRefOid", workflow, StringComparison.Ordinal);
        Assert.Contains("CURRENT_BASE_REF", workflow, StringComparison.Ordinal);
        Assert.Contains("CURRENT_HEAD_REF", workflow, StringComparison.Ordinal);
        Assert.Contains("CURRENT_IS_DRAFT", workflow, StringComparison.Ordinal);
        Assert.Contains("CURRENT_HEAD_REPOSITORY", workflow, StringComparison.Ordinal);
        Assert.Contains("CURRENT_HEAD_OID", workflow, StringComparison.Ordinal);
        Assert.Contains("EXPECTED_HEAD_SHA", workflow, StringComparison.Ordinal);
        Assert.Contains("CURRENT_HEAD_OID\" != \"$EXPECTED_HEAD_SHA\"", workflow, StringComparison.Ordinal);
        Assert.Contains("exit 1", workflow, StringComparison.Ordinal);

        Assert.DoesNotContain("master", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("production", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pull_request_target:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("checkout", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dismiss", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("conversation", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gh api", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeploymentWorkflowRejectsAnAutomatedDispatchForTheWrongCommit()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(repositoryRoot, ".github", "workflows", "deploy.yml"));

        Assert.Contains("release_sha:", workflow, StringComparison.Ordinal);
        Assert.Contains("RELEASE_SHA: ${{ inputs.release_sha }}", workflow, StringComparison.Ordinal);
        Assert.Contains("[[ \"$RELEASE_SHA\" == \"$GITHUB_SHA\" ]]", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void NonproductionPromotionDocumentationStatesAutomaticMergeAndDeployment()
    {
        var repositoryRoot = FindRepositoryRoot();
        var guide = File.ReadAllText(Path.Combine(repositoryRoot, "AGENTS.md"));
        var runbook = File.ReadAllText(Path.Combine(repositoryRoot, "docs", "operations", "sprint-delivery-workflow.md"));

        Assert.Contains("development/test deployments run automatically after protected merges", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("environments have no required-reviewer gate and deploy automatically after their protected merge", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("review feedback is assessed and addressed before resolution", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("test-to-master and production remain excluded from auto-merge", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("development/test merge and deploy automatically after required checks and resolved conversations", runbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("review feedback is assessed and addressed before resolution", runbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("test-to-master and production remain excluded from auto-merge", runbook, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deployment failure requires diagnosis, a new feature branch, validation, and normal promotion rather than a blind retry", runbook, StringComparison.OrdinalIgnoreCase);
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
