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
        Assert.Contains("LOOKUP_STATUS=$?", workflow, StringComparison.Ordinal);
        Assert.Contains("ResourceNotFound", workflow, StringComparison.Ordinal);
        Assert.Contains("exit \"$LOOKUP_STATUS\"", workflow, StringComparison.Ordinal);
        Assert.Contains("az containerapp revision show", workflow, StringComparison.Ordinal);
        Assert.Contains("properties.latestReadyRevisionName", workflow, StringComparison.Ordinal);
        Assert.Contains("preview_foundation:", workflow, StringComparison.Ordinal);
        Assert.Contains("prepare_release:", workflow, StringComparison.Ordinal);
        Assert.Contains("deploy_release:", workflow, StringComparison.Ordinal);
        Assert.Contains("needs: preview_foundation", workflow, StringComparison.Ordinal);
        Assert.Contains("needs: prepare_release", workflow, StringComparison.Ordinal);
        Assert.Contains("name: Preview immutable release", workflow, StringComparison.Ordinal);
        Assert.Contains("releaseId=\"$GITHUB_SHA\"", workflow, StringComparison.Ordinal);
        Assert.Contains("releaseId=bootstrap", workflow, StringComparison.Ordinal);
        Assert.Contains("--name \"$DEPLOYMENT_NAME\"", workflow, StringComparison.Ordinal);
        Assert.Contains("Rollback image", workflow, StringComparison.Ordinal);
        Assert.Contains("Container App revision", workflow, StringComparison.Ordinal);
        Assert.Contains("if: always()", workflow, StringComparison.Ordinal);

        var previewFoundationIndex = workflow.IndexOf("preview_foundation:", StringComparison.Ordinal);
        var prepareReleaseIndex = workflow.IndexOf("prepare_release:", StringComparison.Ordinal);
        var previewReleaseIndex = workflow.IndexOf("name: Preview immutable release", StringComparison.Ordinal);
        var deployReleaseIndex = workflow.IndexOf("deploy_release:", StringComparison.Ordinal);
        Assert.True(previewFoundationIndex >= 0 && prepareReleaseIndex > previewFoundationIndex,
            "Foundation mutation must be in a downstream job after foundation preview.");
        Assert.True(previewReleaseIndex >= 0 && deployReleaseIndex > previewReleaseIndex,
            "Release mutation must be in a downstream job after the digest what-if.");

        var bootstrapStart = workflow.IndexOf("name: Provision MVP foundation with public bootstrap image", StringComparison.Ordinal);
        var buildImageIndex = workflow.IndexOf("name: Build and publish immutable API image", StringComparison.Ordinal);
        Assert.True(bootstrapStart >= 0 && buildImageIndex > bootstrapStart, "Expected bootstrap before image publication.");
        var bootstrapStep = workflow[bootstrapStart..buildImageIndex];
        Assert.Contains("releaseId=bootstrap", bootstrapStep, StringComparison.Ordinal);
        Assert.DoesNotContain("releaseId=\"$GITHUB_SHA\"", bootstrapStep, StringComparison.Ordinal);

        var summaryStart = workflow.IndexOf("name: Publish deployment summary", StringComparison.Ordinal);
        Assert.True(summaryStart >= 0, "Expected an always-running deployment summary.");
        Assert.Contains("if: always()", workflow[summaryStart..], StringComparison.Ordinal);

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
