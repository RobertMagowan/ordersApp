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
        var readmePath = Path.Combine(repositoryRoot, "README.md");

        var workflow = File.ReadAllText(workflowPath);
        var mainBicep = File.ReadAllText(mainBicepPath);
        var testParameters = File.ReadAllText(testParametersPath);
        var readme = File.ReadAllText(readmePath);

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
        Assert.Contains("required deployment reviewer", readme, StringComparison.OrdinalIgnoreCase);

        var unsafeMarkdownCommands = workflow.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("echo ", StringComparison.Ordinal) && line.Contains('`'))
            .ToArray();
        Assert.Empty(unsafeMarkdownCommands);
        Assert.Contains("printf -- '- Release: `%s`", workflow, StringComparison.Ordinal);

        var previewFoundationIndex = workflow.IndexOf("preview_foundation:", StringComparison.Ordinal);
        var prepareReleaseIndex = workflow.IndexOf("prepare_release:", StringComparison.Ordinal);
        var previewReleaseIndex = workflow.IndexOf("name: Preview immutable release", StringComparison.Ordinal);
        var deployReleaseIndex = workflow.IndexOf("deploy_release:", StringComparison.Ordinal);
        Assert.True(previewFoundationIndex >= 0 && prepareReleaseIndex > previewFoundationIndex,
            "Foundation mutation must be in a downstream job after foundation preview.");
        Assert.True(previewReleaseIndex >= 0 && deployReleaseIndex > previewReleaseIndex,
            "Release mutation must be in a downstream job after the digest what-if.");
        var releasePreviewSummaryIndex = workflow.IndexOf("name: Publish immutable release preview summary", StringComparison.Ordinal);
        Assert.Contains("deployMigrationJob=false", workflow[previewReleaseIndex..releasePreviewSummaryIndex], StringComparison.Ordinal);

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

    [Fact]
    public void DeploymentSmokeWaitsForTheCandidateRevisionBeforeProbingIngress()
    {
        var workflowPath = Path.Combine(FindRepositoryRoot(), ".github", "workflows", "deploy.yml");
        var workflow = File.ReadAllText(workflowPath);

        var candidateStart = workflow.IndexOf("name: Wait for candidate revision", StringComparison.Ordinal);
        var smokeStart = workflow.IndexOf("name: Smoke test the deployed API", StringComparison.Ordinal);
        var summaryStart = workflow.IndexOf("name: Publish deployment summary", StringComparison.Ordinal);

        Assert.True(candidateStart >= 0, "Expected a candidate-revision readiness gate.");
        Assert.True(smokeStart > candidateStart, "Ingress smoke must run after candidate readiness is verified.");
        Assert.True(summaryStart > smokeStart, "The summary must be published after candidate smoke.");

        var candidateStep = workflow[candidateStart..smokeStart];
        var smokeStep = workflow[smokeStart..summaryStart];
        var summaryStep = workflow[summaryStart..];

        Assert.Contains("properties.latestRevisionName", candidateStep, StringComparison.Ordinal);
        Assert.Contains("az containerapp revision show", candidateStep, StringComparison.Ordinal);
        Assert.Contains("CANDIDATE_IMAGE", candidateStep, StringComparison.Ordinal);
        Assert.Contains("CANDIDATE_PROVISIONING_STATE", candidateStep, StringComparison.Ordinal);
        Assert.Contains("CANDIDATE_RUNNING_STATE", candidateStep, StringComparison.Ordinal);
        Assert.Contains("CANDIDATE_HEALTH_STATE", candidateStep, StringComparison.Ordinal);
        Assert.Contains("CANDIDATE_TRAFFIC_WEIGHT", candidateStep, StringComparison.Ordinal);
        Assert.Contains("LATEST_READY_REVISION", candidateStep, StringComparison.Ordinal);
        Assert.Contains("APP_RELEASE", candidateStep, StringComparison.Ordinal);
        Assert.Contains("$EXPECTED_IMAGE", candidateStep, StringComparison.Ordinal);
        Assert.Contains("$GITHUB_SHA", candidateStep, StringComparison.Ordinal);
        Assert.Contains("revision=$CANDIDATE_REVISION", candidateStep, StringComparison.Ordinal);

        Assert.Contains("CANDIDATE_REVISION: ${{ steps.candidate.outputs.revision }}", smokeStep, StringComparison.Ordinal);
        Assert.Contains("echo \"revision=$CANDIDATE_REVISION\"", smokeStep, StringComparison.Ordinal);
        Assert.DoesNotContain("properties.latestReadyRevisionName", smokeStep, StringComparison.Ordinal);
        Assert.Contains("REVISION: ${{ steps.candidate.outputs.revision }}", summaryStep, StringComparison.Ordinal);
    }

    [Fact]
    public void DeploymentWorkflowRunsSqlMigrationBeforeApiCandidatePromotion()
    {
        var workflowPath = Path.Combine(FindRepositoryRoot(), ".github", "workflows", "deploy.yml");
        var workflow = File.ReadAllText(workflowPath);

        Assert.Contains("preview_sql:", workflow, StringComparison.Ordinal);
        Assert.Contains("bootstrap_sql:", workflow, StringComparison.Ordinal);
        Assert.Contains("run_migration:", workflow, StringComparison.Ordinal);
        Assert.Contains("deploy_release:", workflow, StringComparison.Ordinal);
        Assert.Contains("az containerapp job start", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("JOB_IDENTITY=", workflow, StringComparison.Ordinal);
        Assert.Contains("deployMigrationJob=false", workflow, StringComparison.Ordinal);
        Assert.Contains("deployMigrationJob=true", workflow, StringComparison.Ordinal);
        Assert.Contains("deployContainerApp=false", workflow, StringComparison.Ordinal);
        Assert.Contains("for attempt in {1..5}", workflow, StringComparison.Ordinal);
        Assert.Contains("grep -qiF 'InvalidParameterValueInContainerTemplate'", workflow, StringComparison.Ordinal);
        Assert.Contains("grep -qiF 'unable to pull image'", workflow, StringComparison.Ordinal);
        var sqlBootstrapIndex = workflow.IndexOf("bootstrap_sql:", StringComparison.Ordinal);
        var migrationIndex = workflow.IndexOf("run_migration:", StringComparison.Ordinal);
        var sqlBootstrapSection = workflow[sqlBootstrapIndex..migrationIndex];
        Assert.True(
            sqlBootstrapSection.IndexOf("deployMigrationJob=false", StringComparison.Ordinal) < sqlBootstrapSection.IndexOf("deployMigrationJob=true", StringComparison.Ordinal),
            "The migration identity and AcrPull role must be deployed before the private-image migration job.");
        Assert.DoesNotContain("Password=", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("--allow-insecure", workflow, StringComparison.Ordinal);

        var deployIndex = workflow.IndexOf("deploy_release:", StringComparison.Ordinal);
        var candidateIndex = workflow.IndexOf("name: Wait for candidate revision", StringComparison.Ordinal);
        Assert.True(migrationIndex >= 0 && deployIndex > migrationIndex,
            "The API deployment must wait for a successful migration job.");
        Assert.True(candidateIndex > deployIndex,
            "Candidate readiness must remain after migration execution.");

        var deploySection = workflow[deployIndex..candidateIndex];
        Assert.Contains("deployMigrationJob=false", deploySection, StringComparison.Ordinal);
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
