namespace CloudOrders.ArchitectureTests;

public sealed class DeploymentWorkflowPolicyTests
{
    [Fact]
    public void DeploymentWorkflowEnforcesPinnedPromotionAndReleasePolicy()
    {
        var workflowPath = Path.Combine(FindRepositoryRoot(), ".github", "workflows", "deploy.yml");

        Assert.True(File.Exists(workflowPath), $"Expected deployment workflow at {workflowPath}.");
        var workflow = File.ReadAllText(workflowPath);

        Assert.Contains("actions/checkout@de0fac2e4500dabe0009e67214ff5f5447ce83dd # v6.0.2", workflow, StringComparison.Ordinal);
        Assert.Contains("azure/login@532459ea530d8321f2fb9bb10d1e0bcf23869a43 # v3.0.0", workflow, StringComparison.Ordinal);
        Assert.Contains("name: Validate promotion ref", workflow, StringComparison.Ordinal);
        Assert.Contains("GITHUB_REF_TYPE", workflow, StringComparison.Ordinal);
        Assert.Contains("[[ \"$GITHUB_REF_TYPE\" == \"branch\" ]]", workflow, StringComparison.Ordinal);
        Assert.Contains("development|test", workflow, StringComparison.Ordinal);
        Assert.Contains("master)", workflow, StringComparison.Ordinal);
        Assert.Contains("Manual deployments are allowed only from development, test, or master", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--insecure", workflow, StringComparison.Ordinal);
        Assert.Contains("$GITHUB_STEP_SUMMARY", workflow, StringComparison.Ordinal);
        Assert.Contains("Immutable API image", workflow, StringComparison.Ordinal);
        Assert.Contains("API endpoint", workflow, StringComparison.Ordinal);
        Assert.Contains("cloudorders-api@$DIGEST", workflow, StringComparison.Ordinal);

        var actionUsages = workflow.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("uses:", StringComparison.Ordinal));
        Assert.All(actionUsages, actionUsage => Assert.Matches(@"^uses:\s+[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+@[a-f0-9]{40}\s+# v[0-9].*$", actionUsage));
    }

    [Fact]
    public void DeploymentWorkflowPreservesReleaseAndRollbackState()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workflowPath = Path.Combine(repositoryRoot, ".github", "workflows", "deploy.yml");
        var mainBicepPath = Path.Combine(repositoryRoot, "infra", "main.bicep");
        var testParametersPath = Path.Combine(repositoryRoot, "infra", "environments", "test.bicepparam");

        var workflow = File.ReadAllText(workflowPath);
        var mainBicep = File.ReadAllText(mainBicepPath);
        var testParameters = File.ReadAllText(testParametersPath);

        Assert.Contains("name: Inspect existing release", workflow, StringComparison.Ordinal);
        Assert.Contains("steps.existing_release.outputs.exists != 'true'", workflow, StringComparison.Ordinal);
        Assert.Contains("name: Preview immutable release", workflow, StringComparison.Ordinal);
        Assert.Contains("releaseId=\"$GITHUB_SHA\"", workflow, StringComparison.Ordinal);
        Assert.Contains("--name \"$DEPLOYMENT_NAME\"", workflow, StringComparison.Ordinal);
        Assert.Contains("Rollback image", workflow, StringComparison.Ordinal);
        Assert.Contains("Container App revision", workflow, StringComparison.Ordinal);

        var buildImageIndex = workflow.IndexOf("name: Build and publish immutable API image", StringComparison.Ordinal);
        var previewReleaseIndex = workflow.IndexOf("name: Preview immutable release", StringComparison.Ordinal);
        Assert.True(buildImageIndex >= 0 && previewReleaseIndex > buildImageIndex,
            "The reviewed what-if must use the resolved immutable image after it is published.");

        Assert.Contains("param releaseId string = 'bootstrap'", mainBicep, StringComparison.Ordinal);
        Assert.Contains("release: releaseId", mainBicep, StringComparison.Ordinal);
        Assert.Contains("output releaseId string = releaseId", mainBicep, StringComparison.Ordinal);
        Assert.Contains("param releaseId = 'bootstrap'", testParameters, StringComparison.Ordinal);
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
