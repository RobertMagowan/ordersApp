using System.Text.RegularExpressions;

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
    public void DeploymentWorkflowHasNoIndependentPromotionLineageGate()
    {
        var workflowPath = Path.Combine(FindRepositoryRoot(), ".github", "workflows", "deploy.yml");
        var workflow = File.ReadAllText(workflowPath);

        Assert.DoesNotContain("pull-requests: read", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("name: Check out protected commit", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("name: Reject a protected push without merge lineage", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Expected a two-parent merge commit", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("gh api", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("git merge-base --is-ancestor", workflow, StringComparison.Ordinal);

        AssertEventBeforeIsScopedToClassifierStep(workflow);
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
        AssertJobLevelNeedsInclude(workflow, "prepare_release", "preview_foundation", "validate_promotion_ref", "classify_changes");
        AssertJobLevelNeedsInclude(workflow, "preview_sql", "prepare_release", "validate_promotion_ref", "classify_changes");
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

    [Fact]
    public void DeploymentWorkflowPollsOnlyTheStartedMigrationExecution()
    {
        var workflowPath = Path.Combine(FindRepositoryRoot(), ".github", "workflows", "deploy.yml");
        var workflow = File.ReadAllText(workflowPath);

        Assert.Contains("az containerapp job start --name \"$JOB_NAME\" --resource-group \"$AZURE_RESOURCE_GROUP\" --query name --output tsv", workflow, StringComparison.Ordinal);
        Assert.Contains("[[ -n \"$EXECUTION\" ]]", workflow, StringComparison.Ordinal);
        Assert.Contains("az containerapp job execution show --name \"$JOB_NAME\" --resource-group \"$AZURE_RESOURCE_GROUP\" --job-execution-name \"$EXECUTION\"", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("az containerapp job execution list", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--query '[0].name'", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void DeploymentWorkflowRestoresNormalD1ReleaseAfterTheE1ManifestIsRemoved()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(repositoryRoot, ".github", "workflows", "deploy.yml"));
        var manifestPath = Path.Combine(repositoryRoot, "ops", "releases", "sprint-4a-e1-migration-only.json");

        Assert.False(File.Exists(manifestPath), "The reviewed D1 release must remove the one-time E1 manifest so protected-branch deployment resumes normally.");
        Assert.Contains("Validate Sprint 4A E1 migration-only manifest", workflow, StringComparison.Ordinal);
        Assert.Contains("AddCustomerProfileOwnershipExpand", workflow, StringComparison.Ordinal);
        Assert.Contains("migration_only", workflow, StringComparison.Ordinal);
        Assert.Contains("Run Sprint 4A E1 migration only", workflow, StringComparison.Ordinal);
        Assert.Contains("migration_only != 'true'", workflow, StringComparison.Ordinal);
        Assert.Contains("BEFORE_REVISION", workflow, StringComparison.Ordinal);
        Assert.Contains("BEFORE_DIGEST", workflow, StringComparison.Ordinal);
        Assert.Contains("BEFORE_TRAFFIC", workflow, StringComparison.Ordinal);
        Assert.Contains("Migration-only run changed API revision, digest, or traffic", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Sprint4APlanCreatesTheE1ManifestWithTheTask3Migration()
    {
        var plan = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "docs",
            "superpowers",
            "plans",
            "2026-08-27-sprint-4-external-id-authorization.md"));

        var task1Start = plan.IndexOf("### Task 1:", StringComparison.Ordinal);
        var task2Start = plan.IndexOf("### Task 2:", StringComparison.Ordinal);
        var task3Start = plan.IndexOf("### Task 3:", StringComparison.Ordinal);
        var task4Start = plan.IndexOf("### Task 4:", StringComparison.Ordinal);
        Assert.True(task1Start >= 0 && task2Start > task1Start, "Expected bounded Task 1 plan text.");
        Assert.True(task3Start >= 0 && task4Start > task3Start, "Expected bounded Task 3 plan text.");

        var task1 = plan[task1Start..task2Start];
        var task3 = plan[task3Start..task4Start];
        const string manifestInventory = "- Create: `ops/releases/sprint-4a-e1-migration-only.json`";
        Assert.DoesNotContain(manifestInventory, task1, StringComparison.Ordinal);
        Assert.Contains(manifestInventory, task3, StringComparison.Ordinal);
        Assert.Contains("AddCustomerProfileOwnershipExpand", task3, StringComparison.Ordinal);
    }

    [Fact]
    public void Sprint4AE1WorkflowConstrictsProtectedBranchDispatchesToTheNamedMigration()
    {
        var workflow = File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".github", "workflows", "deploy.yml"));

        const string protectedBranchPredicate = "github.ref_name == 'development' || github.ref_name == 'test'";
        Assert.Contains($"if: {protectedBranchPredicate}", workflow, StringComparison.Ordinal);
        Assert.Contains("[[ ( \"$GITHUB_REF_NAME\" == development || \"$GITHUB_REF_NAME\" == test ) ]] || exit 0", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("[[ \"$GITHUB_EVENT_NAME\" == push", workflow, StringComparison.Ordinal);
        Assert.Contains("expected = {\"migration\": \"AddCustomerProfileOwnershipExpand\", \"deployApi\": False}", workflow, StringComparison.Ordinal);
        Assert.Contains("if manifest != expected:", workflow, StringComparison.Ordinal);
        Assert.Contains("MIGRATION: ${{ needs.validate_promotion_ref.outputs.migration }}", workflow, StringComparison.Ordinal);
        Assert.Contains("--yaml \"$JOB_TEMPLATE\"", workflow, StringComparison.Ordinal);
        Assert.Contains("EXECUTION_ARGS=$(az containerapp job execution show", workflow, StringComparison.Ordinal);
        Assert.Contains("expected = [\"--migration\", sys.argv[1]]", workflow, StringComparison.Ordinal);
        Assert.Contains("if json.loads(sys.argv[2]) != expected:", workflow, StringComparison.Ordinal);
        Assert.Contains("E1 migration execution $EXECUTION completed with status $STATUS.", workflow, StringComparison.Ordinal);

        var normalJobs = new[] { "preview_foundation", "prepare_release", "preview_sql", "bootstrap_sql", "run_migration", "deploy_release" };
        AssertNormalAzureMutatingJobsNeedClassification(workflow, normalJobs);
        var normalWorkflowStart = workflow.IndexOf("preview_foundation:", StringComparison.Ordinal);
        var e1WorkflowStart = workflow.IndexOf("run_sprint_4a_e1_migration_only:", StringComparison.Ordinal);
        var normalWorkflow = workflow[normalWorkflowStart..e1WorkflowStart];
        Assert.Equal(
            normalJobs.Length - 1,
            normalWorkflow.Split("needs.validate_promotion_ref.outputs.migration_only != 'true'", StringSplitOptions.None).Length - 1);
        var deployReleaseStart = workflow.IndexOf("  deploy_release:", StringComparison.Ordinal);
        Assert.Contains("needs.validate_promotion_ref.outputs.migration_only != 'true'", workflow[deployReleaseStart..], StringComparison.Ordinal);
        Assert.True(e1WorkflowStart >= 0 && deployReleaseStart > e1WorkflowStart,
            "Expected a bounded E1 migration-only job before deploy_release.");
        AssertE1JobHasDirectDispatchGuard(workflow);

        var migrationRunner = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "CloudOrders.Migrations", "Program.cs"));
        Assert.Contains("--migration", migrationRunner, StringComparison.Ordinal);
        Assert.Contains("FindMigrationId(targetMigration)", migrationRunner, StringComparison.Ordinal);
        Assert.Contains("GetPendingMigrationsAsync", migrationRunner, StringComparison.Ordinal);
        Assert.Contains("targetIsAlreadyApplied", migrationRunner, StringComparison.Ordinal);
        Assert.Contains("Named SQL migration was already applied, but one or more later migrations are pending.", migrationRunner, StringComparison.Ordinal);
        Assert.Contains("if (targetIsAlreadyApplied)", migrationRunner, StringComparison.Ordinal);
        Assert.Contains("pendingMigrations.SequenceEqual([resolvedTargetMigration], StringComparer.Ordinal)", migrationRunner, StringComparison.Ordinal);
        Assert.Contains("appliedMigrationsBefore", migrationRunner, StringComparison.Ordinal);
        Assert.Contains("MigrateAsync(resolvedTargetMigration)", migrationRunner, StringComparison.Ordinal);
        Assert.Contains("appliedMigrationsAfter", migrationRunner, StringComparison.Ordinal);
        Assert.Contains("appliedMigrationDelta", migrationRunner, StringComparison.Ordinal);
        Assert.Contains("appliedMigrationDelta.SequenceEqual([resolvedTargetMigration], StringComparer.Ordinal)", migrationRunner, StringComparison.Ordinal);
        Assert.Contains("GetAppliedMigrationsAsync", migrationRunner, StringComparison.Ordinal);
        Assert.DoesNotContain("appliedMigrations.Contains(targetMigration", migrationRunner, StringComparison.Ordinal);
    }

    [Fact]
    public void Sprint4AE1WorkflowRunsTheNamedMigrationFromTheCurrentImmutableRunnerImage()
    {
        var workflow = File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".github", "workflows", "deploy.yml"));
        var e1JobStart = workflow.IndexOf("  run_sprint_4a_e1_migration_only:", StringComparison.Ordinal);
        var deployReleaseStart = workflow.IndexOf("  deploy_release:", StringComparison.Ordinal);

        Assert.True(e1JobStart >= 0 && deployReleaseStart > e1JobStart, "Expected a bounded E1 migration-only job.");
        var e1Job = workflow[e1JobStart..deployReleaseStart];

        Assert.Contains("name: Check out E1 migration source", e1Job, StringComparison.Ordinal);
        Assert.Contains("id: e1_migration_image", e1Job, StringComparison.Ordinal);
        Assert.Contains("docker build --file src/CloudOrders.Migrations/Dockerfile --tag \"$IMAGE_TAG\" .", e1Job, StringComparison.Ordinal);
        Assert.Contains("docker push \"$IMAGE_TAG\"", e1Job, StringComparison.Ordinal);
        Assert.Contains("image=$LOGIN_SERVER/cloudorders-migrations@$DIGEST", e1Job, StringComparison.Ordinal);
        Assert.Contains("MIGRATION_IMAGE: ${{ steps.e1_migration_image.outputs.image }}", e1Job, StringComparison.Ordinal);
        Assert.Contains("JOB_PROVISIONING_STATE=$(az containerapp job show", e1Job, StringComparison.Ordinal);
        Assert.Contains("Migration-only release requires a provisioned migration Job.", e1Job, StringComparison.Ordinal);
        Assert.Contains("JOB_TEMPLATE=$(mktemp)", e1Job, StringComparison.Ordinal);
        Assert.Contains("trap 'rm -f \"$JOB_TEMPLATE\"' EXIT", e1Job, StringComparison.Ordinal);
        Assert.Contains("--query properties.template --output json > \"$JOB_TEMPLATE\"", e1Job, StringComparison.Ordinal);
        Assert.Contains("template = json.load(source)", e1Job, StringComparison.Ordinal);
        Assert.Contains("if template.get(\"volumes\") or template.get(\"initContainers\"):", e1Job, StringComparison.Ordinal);
        Assert.Contains("Migration Job template uses execution-override-unsupported volumes or init containers.", e1Job, StringComparison.Ordinal);
        Assert.Contains("if len(containers) != 1:", e1Job, StringComparison.Ordinal);
        Assert.Contains("Migration Job template must contain exactly one container.", e1Job, StringComparison.Ordinal);
        Assert.Contains("migrations = [container for container in containers if container.get(\"name\") == \"migrations\"]", e1Job, StringComparison.Ordinal);
        Assert.Contains("ConnectionStrings__CloudOrders", e1Job, StringComparison.Ordinal);
        Assert.Contains("if migration_container.get(\"volumeMounts\") or migration_container.get(\"probes\"):", e1Job, StringComparison.Ordinal);
        Assert.Contains("Migration Job template uses execution-override-unsupported container settings.", e1Job, StringComparison.Ordinal);
        Assert.Contains("MIGRATION_IMAGE", e1Job, StringComparison.Ordinal);
        Assert.Contains("[\"--migration\", migration]", e1Job, StringComparison.Ordinal);
        Assert.Contains("json.dump(template, destination)", e1Job, StringComparison.Ordinal);
        Assert.Contains("--yaml \"$JOB_TEMPLATE\"", e1Job, StringComparison.Ordinal);
        Assert.DoesNotContain("--args \"--migration\" \"$MIGRATION\"", e1Job, StringComparison.Ordinal);
        Assert.DoesNotContain("cat > \"$EXECUTION_TEMPLATE\"", e1Job, StringComparison.Ordinal);
        Assert.Contains("EXECUTION_IMAGE=$(az containerapp job execution show", e1Job, StringComparison.Ordinal);
        Assert.Contains("if sys.argv[3] != sys.argv[4]:", e1Job, StringComparison.Ordinal);
        Assert.Contains("Migration execution did not use the current immutable migration image.", e1Job, StringComparison.Ordinal);
        Assert.DoesNotContain("docker build --file src/CloudOrders.Api/Dockerfile", e1Job, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkflowContractRejectsEventBeforeOutsideTheClassifierStep()
    {
        var weakenedWorkflows = new[]
        {
            """
            jobs:
              classify_changes:
                name: Leaks ${{ github.event.before }} before the classifier step
                steps:
                  - name: Classify changed paths
                    env:
                      EVENT_BEFORE: ${{ github.event.before }}
                    run: echo classify
              deployment_not_required:
                steps: []
            """,
            """
            jobs:
              classify_changes:
                steps:
                  - name: Classify changed paths
                    env:
                      EVENT_BEFORE: ${{ github.event.before }}
                    run: echo classify
                  - name: Later step
                    run: echo "${{ github.event.before }}"
              deployment_not_required:
                steps: []
            """,
        };

        Assert.All(weakenedWorkflows, weakenedWorkflow =>
        {
            var failure = Record.Exception(() => AssertEventBeforeIsScopedToClassifierStep(weakenedWorkflow));

            Assert.NotNull(failure);
            Assert.Contains("outside the exact Classify changed paths step", failure.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void WorkflowContractRejectsClassifierDependencyOutsideJobLevelNeeds()
    {
        const string weakenedWorkflow = """
            jobs:
              preview_foundation:
                needs: [validate_promotion_ref]
                if: needs.classify_changes.outputs.deployable == 'true'
                steps:
                  - name: Misleading mention
                    run: echo classify_changes
            """;

        var failure = Record.Exception(() => AssertNormalAzureMutatingJobsNeedClassification(
            weakenedWorkflow,
            ["preview_foundation"]));

        Assert.NotNull(failure);
        Assert.Contains("job-level needs declaration", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkflowContractRejectsE1ConditionsOutsideTheJobLevelGuard()
    {
        const string deployableCondition = "needs.classify_changes.outputs.deployable == 'true'";
        const string migrationOnlyCondition = "needs.validate_promotion_ref.outputs.migration_only == 'true'";
        static string BuildWorkflow(string condition, string script) => $"""
            jobs:
              run_sprint_4a_e1_migration_only:
                needs: [validate_promotion_ref, classify_changes]
                if: {condition}
                steps:
                  - name: Misleading comment
                    run: |
                      {script}
              deploy_release:
                steps: []
            """;

        var weakenedWorkflows = new[]
        {
            BuildWorkflow(migrationOnlyCondition, $"# if: {deployableCondition} && {migrationOnlyCondition}"),
            BuildWorkflow($"{deployableCondition} || {migrationOnlyCondition}", "echo run"),
            BuildWorkflow($"true || ({deployableCondition} && {migrationOnlyCondition})", "echo run"),
            BuildWorkflow($"{deployableCondition} # {migrationOnlyCondition}", "echo run"),
            BuildWorkflow($"{deployableCondition} && && {migrationOnlyCondition}", "echo run"),
            BuildWorkflow($"&& {deployableCondition} && {migrationOnlyCondition}", "echo run"),
            BuildWorkflow($"{deployableCondition} && {migrationOnlyCondition} &&", "echo run"),
        };

        Assert.All(weakenedWorkflows, weakenedWorkflow =>
        {
            var failure = Record.Exception(() => AssertE1JobHasDirectDispatchGuard(weakenedWorkflow));

            Assert.NotNull(failure);
            Assert.Contains("direct job-level if declaration", failure.Message, StringComparison.Ordinal);
        });
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

    private static void AssertEventBeforeIsScopedToClassifierStep(string workflow)
    {
        const string eventBeforeToken = "github.event.before";
        var classifyJob = GetJobSection(workflow, "classify_changes");
        var classifyStep = GetStepSection(classifyJob.Value, "Classify changed paths");
        var eventBeforeMappingCount = Regex.Count(
            classifyStep.Value,
            @"^          EVENT_BEFORE: \$\{\{ github\.event\.before \}\}[ \t]*\r?$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        Assert.True(eventBeforeMappingCount == 1,
            "Expected exactly one EVENT_BEFORE: ${{ github.event.before }} mapping in the exact Classify changed paths step.");

        var eventBeforeUseCount = Regex.Count(
            classifyStep.Value,
            Regex.Escape(eventBeforeToken),
            RegexOptions.CultureInvariant);
        Assert.True(eventBeforeUseCount == 1,
            "Expected github.event.before to be used only by the EVENT_BEFORE mapping in the exact Classify changed paths step.");

        var classifyStepStart = classifyJob.Index + classifyStep.Index;
        var workflowOutsideClassifyStep = workflow.Remove(classifyStepStart, classifyStep.Length);
        Assert.True(!workflowOutsideClassifyStep.Contains(eventBeforeToken, StringComparison.Ordinal),
            "Expected no github.event.before reference outside the exact Classify changed paths step.");
    }

    private static void AssertNormalAzureMutatingJobsNeedClassification(string workflow, IEnumerable<string> jobNames)
    {
        Assert.All(jobNames, jobName => AssertJobLevelNeedsInclude(workflow, jobName, "classify_changes"));
    }

    private static void AssertJobLevelNeedsInclude(string workflow, string jobName, params string[] expectedDependencies)
    {
        var job = GetJobSection(workflow, jobName);
        var needsMatches = Regex.Matches(
            job.Value,
            @"^    needs:[ \t]*\[(?<dependencies>[^\]\r\n]*)\][ \t]*\r?$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        Assert.True(needsMatches.Count == 1,
            $"Expected exactly one array-form job-level needs declaration for {jobName}.");

        var dependencies = needsMatches[0].Groups["dependencies"].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.All(expectedDependencies, expectedDependency => Assert.True(
            dependencies.Contains(expectedDependency, StringComparer.Ordinal),
            $"Expected the {jobName} job-level needs declaration to include {expectedDependency}."));
    }

    private static void AssertE1JobHasDirectDispatchGuard(string workflow)
    {
        const string deployableCondition = "needs.classify_changes.outputs.deployable == 'true'";
        const string migrationOnlyCondition = "needs.validate_promotion_ref.outputs.migration_only == 'true'";
        var e1Header = GetJobHeader(workflow, "run_sprint_4a_e1_migration_only");
        var ifMatches = Regex.Matches(
            e1Header,
            @"^    if:[ \t]*(?<condition>[^\r\n]+?)[ \t]*\r?$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        Assert.True(ifMatches.Count == 1,
            "Expected exactly one direct job-level if declaration in the E1 migration-only job header.");

        var condition = ifMatches[0].Groups["condition"].Value;
        var commentStart = condition.IndexOf('#', StringComparison.Ordinal);
        var executableCondition = (commentStart >= 0 ? condition[..commentStart] : condition).Trim();
        var operands = executableCondition.Split(
            "&&",
            StringSplitOptions.TrimEntries);
        var isRequiredConjunction = operands.Length == 2
            && operands.Contains(deployableCondition, StringComparer.Ordinal)
            && operands.Contains(migrationOnlyCondition, StringComparer.Ordinal);
        Assert.True(isRequiredConjunction,
            $"Expected the direct job-level if declaration in the E1 migration-only job header to be the conjunction of '{deployableCondition}' and '{migrationOnlyCondition}'.");
    }

    private static Match GetJobSection(string workflow, string jobName)
    {
        var jobMatches = Regex.Matches(
            workflow,
            $@"^  {Regex.Escape(jobName)}:[ \t]*\r?\n.*?(?=^  [A-Za-z0-9_-]+:[ \t]*(?:#[^\r\n]*)?\r?$|\z)",
            RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        Assert.True(jobMatches.Count == 1,
            $"Expected exactly one bounded job section for {jobName}; found {jobMatches.Count}.");
        return jobMatches[0];
    }

    private static Match GetStepSection(string jobSection, string stepName)
    {
        var stepMatches = Regex.Matches(
            jobSection,
            $@"^      - name:[ \t]+{Regex.Escape(stepName)}[ \t]*\r?\n.*?(?=^      -(?=[ \t]|\r?$)|\z)",
            RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        Assert.True(stepMatches.Count == 1,
            $"Expected exactly one bounded '{stepName}' step; found {stepMatches.Count}.");
        return stepMatches[0];
    }

    private static string GetJobHeader(string workflow, string jobName)
    {
        var job = GetJobSection(workflow, jobName);
        var stepsMatches = Regex.Matches(
            job.Value,
            @"^    steps:[ \t]*\r?$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        Assert.True(stepsMatches.Count == 1,
            $"Expected exactly one direct job-level steps declaration for {jobName}; found {stepsMatches.Count}.");
        return job.Value[..stepsMatches[0].Index];
    }
}
