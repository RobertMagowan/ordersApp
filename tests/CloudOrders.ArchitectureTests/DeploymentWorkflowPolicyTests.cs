using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CloudOrders.ArchitectureTests;

public sealed class DeploymentWorkflowPolicyTests
{
    private static readonly string[] AllowedEnvironments = ["development", "test"];
    private static readonly string[] AllowedPreconditions = ["none", "ownership-read-only-transaction"];
    private static readonly string[] AllowedTrafficPolicies = ["none", "controlled-nonproduction-access"];
    private static readonly string[] AllowedCompatibilityModes = ["api-compatible", "maintenance-required"];

    [Fact]
    public void CurrentReleaseDescriptorDeclaresVersionedMigrationContract()
    {
        var descriptorPath = Path.Combine(FindRepositoryRoot(), "ops", "releases", "current-release.json");
        using var descriptor = JsonDocument.Parse(File.ReadAllText(descriptorPath));
        var root = descriptor.RootElement;

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.NotEmpty(root.GetProperty("requiredMigrationBaseline").EnumerateArray());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("authorisedMigrations").ValueKind);
    }

    [Fact]
    public void CurrentReleaseDescriptorConformsToStrictSchema()
    {
        var (descriptor, schema) = ReadReleaseDocuments();
        AssertDescriptorConforms(descriptor, schema);
        Assert.Equal(["development", "test"], descriptor["environments"]!.AsArray().Select(x => (string)x!).ToArray());
        Assert.DoesNotContain("production", descriptor["environments"]!.AsArray().Select(x => (string)x!));
        Assert.Equal("20260909213051_AddOutboxLeasing", descriptor["authorisedMigrations"]!.AsArray().Last()!.GetValue<string>());
    }

    [Fact]
    public void SchemaAcceptsCodeOnlyDescriptorWithCumulativeAuthorisation()
    {
        var (descriptor, schema) = ReadReleaseDocuments();
        descriptor["compatibility"] = "maintenance-required";

        AssertDescriptorConforms(descriptor, schema);
    }

    [Fact]
    public void SchemaAcceptsDescriptorWithApiDeploymentDisabled()
    {
        var (descriptor, schema) = ReadReleaseDocuments();
        descriptor["deployApi"] = false;

        AssertDescriptorConforms(descriptor, schema);
    }

    [Fact]
    public void ReleaseSchemaRejectsMalformedJson()
    {
        Assert.ThrowsAny<JsonException>(() => JsonNode.Parse("{\"schemaVersion\":1"));
    }

    [Fact]
    public void ReleaseSchemaRejectsDuplicateMigrationIds()
    {
        var (descriptor, schema) = ReadReleaseDocuments();
        descriptor["authorisedMigrations"]!.AsArray().Add("20260909213051_AddOutboxLeasing");
        Assert.ThrowsAny<Exception>(() => AssertDescriptorConforms(descriptor, schema));
    }

    [Fact]
    public void ReleaseSchemaRejectsUnauthorisedEnvironments()
    {
        var (descriptor, schema) = ReadReleaseDocuments();
        descriptor["environments"]!.AsArray().Add("production");
        Assert.ThrowsAny<Exception>(() => AssertDescriptorConforms(descriptor, schema));
    }

    [Fact]
    public void ReleaseSchemaRejectsUnknownPolicyValues()
    {
        var (descriptor, schema) = ReadReleaseDocuments();
        descriptor["trafficPolicy"] = "all-access";
        Assert.ThrowsAny<Exception>(() => AssertDescriptorConforms(descriptor, schema));
    }

    [Fact]
    public void AuthorisedMigrationsMustBeCodeOnlyCumulativePrefix()
    {
        var (descriptor, schema) = ReadReleaseDocuments();
        descriptor["authorisedMigrations"] = new JsonArray("20260816221235_InitialSqlPersistence", "20260909213051_AddOutboxLeasing");
        Assert.ThrowsAny<Exception>(() => AssertDescriptorConforms(descriptor, schema));
    }

    [Fact]
    public void MigrationArraysMustRemainChronologicallyOrdered()
    {
        var (descriptor, schema) = ReadReleaseDocuments();
        var baseline = descriptor["requiredMigrationBaseline"]!.AsArray();
        var first = baseline[0]!.GetValue<string>();
        baseline[0] = baseline[1]!.GetValue<string>();
        baseline[1] = first;

        Assert.ThrowsAny<Exception>(() => AssertDescriptorConforms(descriptor, schema));
    }

    private static (JsonObject Descriptor, JsonObject Schema) ReadReleaseDocuments()
    {
        var root = FindRepositoryRoot();
        return (
            JsonNode.Parse(File.ReadAllText(Path.Combine(root, "ops", "releases", "current-release.json")))!.AsObject(),
            JsonNode.Parse(File.ReadAllText(Path.Combine(root, "ops", "releases", "release-schema.json")))!.AsObject());
    }

    private static void AssertDescriptorConforms(JsonObject descriptor, JsonObject schema)
    {
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", schema["$schema"]!.GetValue<string>());
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
        var required = schema["required"]!.AsArray().Select(x => x!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(required, descriptor.Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal));
        AssertSchemaProperties(schema);
        Assert.NotEmpty(descriptor["releaseId"]!.GetValue<string>());
        Assert.Equal(1, descriptor["schemaVersion"]!.GetValue<int>());
        _ = descriptor["deployApi"]!.GetValue<bool>();
        var environments = descriptor["environments"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
        Assert.NotEmpty(environments);
        Assert.All(environments, environment => Assert.Contains(environment, AllowedEnvironments));
        Assert.Equal(descriptor["environments"]!.AsArray().Count, environments.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(descriptor["precondition"]!.GetValue<string>(), AllowedPreconditions);
        Assert.Contains(descriptor["trafficPolicy"]!.GetValue<string>(), AllowedTrafficPolicies);
        Assert.Contains(descriptor["compatibility"]!.GetValue<string>(), AllowedCompatibilityModes);

        var baseline = descriptor["requiredMigrationBaseline"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
        var authorised = descriptor["authorisedMigrations"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
        Assert.NotEmpty(baseline);
        Assert.Equal(baseline, baseline.Order(StringComparer.Ordinal));
        Assert.Equal(baseline.Length, baseline.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(authorised.Length, authorised.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(authorised, baseline[..authorised.Length]);
        Assert.All(baseline, id => Assert.Matches(@"^[0-9]{14}_[A-Za-z][A-Za-z0-9]*$", id));
        Assert.All(authorised, id => Assert.Contains(id, baseline, StringComparer.Ordinal));
    }

    private static void AssertSchemaProperties(JsonObject schema)
    {
        var properties = schema["properties"]!.AsObject();
        AssertProperty(properties, "releaseId", "string", minLength: 1);
        Assert.Equal(1, properties["schemaVersion"]!["const"]!.GetValue<int>());
        AssertProperty(properties, "environments", "array", uniqueItems: true);
        Assert.Equal(["development", "test"], properties["environments"]!["items"]!["enum"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray());
        AssertProperty(properties, "deployApi", "boolean");
        AssertMigrationArrayProperty(properties, "requiredMigrationBaseline", minItems: 1);
        AssertMigrationArrayProperty(properties, "authorisedMigrations");
        Assert.Equal(AllowedPreconditions, properties["precondition"]!["enum"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray());
        Assert.Equal(AllowedTrafficPolicies, properties["trafficPolicy"]!["enum"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray());
        Assert.Equal(AllowedCompatibilityModes, properties["compatibility"]!["enum"]!.AsArray().Select(x => x!.GetValue<string>()).ToArray());

        var migrationDefinition = schema["$defs"]!["efMigrationId"]!;
        Assert.Equal("string", migrationDefinition["type"]!.GetValue<string>());
        Assert.Equal(@"^[0-9]{14}_[A-Za-z][A-Za-z0-9]*$", migrationDefinition["pattern"]!.GetValue<string>());
    }

    private static void AssertProperty(JsonObject properties, string name, string type, int? minLength = null, bool? uniqueItems = null)
    {
        var property = properties[name]!.AsObject();
        Assert.Equal(type, property["type"]!.GetValue<string>());
        if (minLength is not null)
        {
            Assert.Equal(minLength.Value, property["minLength"]!.GetValue<int>());
        }

        if (uniqueItems is not null)
        {
            Assert.Equal(uniqueItems.Value, property["uniqueItems"]!.GetValue<bool>());
        }
    }

    private static void AssertMigrationArrayProperty(JsonObject properties, string name, int? minItems = null)
    {
        var property = properties[name]!.AsObject();
        AssertProperty(properties, name, "array", uniqueItems: true);
        Assert.Equal("#/$defs/efMigrationId", property["items"]!["$ref"]!.GetValue<string>());
        if (minItems is not null)
        {
            Assert.Equal(minItems.Value, property["minItems"]!.GetValue<int>());
        }
    }

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
    public void DeploymentWorkflowRetriesOnlyTheFinalAzureOidcLogin()
    {
        var workflow = File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".github", "workflows", "deploy.yml"));
        var releaseJob = GetJobSection(workflow, "deploy_release");
        var initialLogin = GetStepSection(releaseJob.Value, "Sign in to Azure with OIDC (initial attempt)");
        var retryDelay = GetStepSection(releaseJob.Value, "Wait before Azure OIDC retry");
        var retryLogin = GetStepSection(releaseJob.Value, "Sign in to Azure with OIDC (retry)");
        var failureGuard = GetStepSection(releaseJob.Value, "Fail after repeated Azure OIDC login failures");

        Assert.Contains("id: azure_login_initial", initialLogin.Value, StringComparison.Ordinal);
        Assert.Contains("continue-on-error: true", initialLogin.Value, StringComparison.Ordinal);
        Assert.Contains("azure/login@532459ea530d8321f2fb9bb10d1e0bcf23869a43 # v3.0.0", initialLogin.Value, StringComparison.Ordinal);
        Assert.Contains("if: steps.azure_login_initial.outcome == 'failure'", retryDelay.Value, StringComparison.Ordinal);
        Assert.Contains("sleep 10", retryDelay.Value, StringComparison.Ordinal);
        Assert.Contains("id: azure_login_retry", retryLogin.Value, StringComparison.Ordinal);
        Assert.Contains("continue-on-error: true", retryLogin.Value, StringComparison.Ordinal);
        Assert.Contains("if: steps.azure_login_initial.outcome == 'failure'", retryLogin.Value, StringComparison.Ordinal);
        Assert.Contains("if: steps.azure_login_initial.outcome == 'failure' && steps.azure_login_retry.outcome == 'failure'", failureGuard.Value, StringComparison.Ordinal);
        Assert.Contains("exit 1", failureGuard.Value, StringComparison.Ordinal);

        Assert.Equal(2, Regex.Count(
            releaseJob.Value,
            "azure/login@532459ea530d8321f2fb9bb10d1e0bcf23869a43 # v3.0.0",
            RegexOptions.CultureInvariant));
        Assert.Equal(1, Regex.Count(workflow, Regex.Escape("Sign in to Azure with OIDC (initial attempt)"), RegexOptions.CultureInvariant));
        Assert.Equal(1, Regex.Count(workflow, Regex.Escape("Wait before Azure OIDC retry"), RegexOptions.CultureInvariant));
        Assert.Equal(1, Regex.Count(workflow, Regex.Escape("Sign in to Azure with OIDC (retry)"), RegexOptions.CultureInvariant));
        Assert.Equal(1, Regex.Count(workflow, Regex.Escape("Fail after repeated Azure OIDC login failures"), RegexOptions.CultureInvariant));

        Assert.True(
            initialLogin.Index < retryDelay.Index
            && retryDelay.Index < retryLogin.Index
            && retryLogin.Index < failureGuard.Index,
            "The retry steps must execute in their fail-closed order.");

        var firstMutation = releaseJob.Value.IndexOf("name: Install Bicep", StringComparison.Ordinal);
        Assert.True(firstMutation > failureGuard.Index,
            "The repeated-failure guard must run before installation or any release mutation.");
    }

    [Fact]
    public void DeploymentWorkflowUsesTheCurrentReleaseDescriptorBeforeMigrationOrApiDeployment()
    {
        var workflowPath = Path.Combine(FindRepositoryRoot(), ".github", "workflows", "deploy.yml");
        var workflow = File.ReadAllText(workflowPath);
        var migrationJob = GetJobSection(workflow, "run_migration");

        Assert.Contains("preview_sql:", workflow, StringComparison.Ordinal);
        Assert.Contains("bootstrap_sql:", workflow, StringComparison.Ordinal);
        Assert.Contains("run_migration:", workflow, StringComparison.Ordinal);
        Assert.Contains("deploy_release:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("EnforceCustomerProfileOwnership", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("sprint-4", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("migration_only", workflow, StringComparison.Ordinal);
        Assert.Contains("current-release.json", workflow, StringComparison.Ordinal);
        Assert.Contains("release-schema.json", workflow, StringComparison.Ordinal);
        Assert.Contains("jsonschema", workflow, StringComparison.Ordinal);
        Assert.Contains("--verify-release", migrationJob.Value, StringComparison.Ordinal);
        Assert.Contains("--apply-release", migrationJob.Value, StringComparison.Ordinal);
        Assert.Contains("/workspace/ops/releases/current-release.json", migrationJob.Value, StringComparison.Ordinal);
        Assert.Contains("DESCRIPTOR_SHA256", migrationJob.Value, StringComparison.Ordinal);
        Assert.Contains("RELEASE_SHA", migrationJob.Value, StringComparison.Ordinal);
        Assert.Contains("MIGRATION_IMAGE", migrationJob.Value, StringComparison.Ordinal);
        Assert.Contains("EXECUTION_ARGS", migrationJob.Value, StringComparison.Ordinal);
        Assert.Contains("EXECUTION_ENV", migrationJob.Value, StringComparison.Ordinal);
        Assert.Contains("az containerapp job start", workflow, StringComparison.Ordinal);
        Assert.Contains("JOB_TEMPLATE=$(mktemp)", migrationJob.Value, StringComparison.Ordinal);
        Assert.Contains("--query properties.template --output json > \"$JOB_TEMPLATE\"", migrationJob.Value, StringComparison.Ordinal);
        Assert.Contains("--yaml \"$JOB_TEMPLATE\"", migrationJob.Value, StringComparison.Ordinal);
        Assert.Contains("--ownership-precondition", migrationJob.Value, StringComparison.Ordinal);
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
    public void MigrationUsesSqlTargetsProducedByTheProvisioningDeployment()
    {
        var workflowPath = Path.Combine(FindRepositoryRoot(), ".github", "workflows", "deploy.yml");
        var workflow = File.ReadAllText(workflowPath);
        var bootstrapJob = GetJobSection(workflow, "bootstrap_sql");
        var migrationJob = GetJobSection(workflow, "run_migration");

        Assert.Contains("sql_server_name: ${{ steps.sql_target.outputs.server_name }}", bootstrapJob.Value, StringComparison.Ordinal);
        Assert.Contains("sql_database_name: ${{ steps.sql_target.outputs.database_name }}", bootstrapJob.Value, StringComparison.Ordinal);
        Assert.Contains("id: sql_target", bootstrapJob.Value, StringComparison.Ordinal);
        Assert.Contains("properties.outputs.sqlServerFqdn.value", bootstrapJob.Value, StringComparison.Ordinal);
        Assert.Contains("properties.outputs.databaseName.value", bootstrapJob.Value, StringComparison.Ordinal);
        Assert.Contains("AZURE_SQL_SERVER_NAME: ${{ needs.bootstrap_sql.outputs.sql_server_name }}", migrationJob.Value, StringComparison.Ordinal);
        Assert.Contains("AZURE_SQL_DATABASE_NAME: ${{ needs.bootstrap_sql.outputs.sql_database_name }}", migrationJob.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("AZURE_SQL_SERVER_NAME: ${{ vars.AZURE_SQL_SERVER_NAME }}", migrationJob.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("AZURE_SQL_DATABASE_NAME: ${{ vars.AZURE_SQL_DATABASE_NAME }}", migrationJob.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void DeploymentWorkflowReconcilesAmbiguousMigrationTimeoutsWithoutAnotherApply()
    {
        var workflowPath = Path.Combine(FindRepositoryRoot(), ".github", "workflows", "deploy.yml");
        var workflow = File.ReadAllText(workflowPath);

        var migrationJob = GetJobSection(workflow, "run_migration");

        Assert.Contains("reconcile", migrationJob.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fresh read-only verification Job", migrationJob.Value, StringComparison.Ordinal);
        Assert.Contains("MIGRATION_BASELINE_CONFLICT", migrationJob.Value, StringComparison.Ordinal);
        Assert.Contains("run_release_job verify", migrationJob.Value, StringComparison.Ordinal);
        Assert.Contains("run_release_job apply", migrationJob.Value, StringComparison.Ordinal);
        Assert.Contains("run_release_job verify", migrationJob.Value, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Count(migrationJob.Value, Regex.Escape("run_release_job apply"), RegexOptions.CultureInvariant));
        Assert.DoesNotContain("az containerapp job execution list", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--query '[0].name'", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseDescriptorValidationPrecedesAzureMutationAndExcludesProduction()
    {
        var workflow = File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".github", "workflows", "deploy.yml"));
        var validateJob = GetJobSection(workflow, "validate_promotion_ref");
        var migrationJob = GetJobSection(workflow, "run_migration");
        var deployJob = GetJobSection(workflow, "deploy_release");

        Assert.Contains("descriptor_sha256", validateJob.Value, StringComparison.Ordinal);
        Assert.Contains("deploy_api", validateJob.Value, StringComparison.Ordinal);
        Assert.Contains("permitted_environment", validateJob.Value, StringComparison.Ordinal);
        Assert.Contains("git checkout --detach \"$GITHUB_SHA\"", validateJob.Value, StringComparison.Ordinal);
        Assert.Contains("-m pip install", validateJob.Value, StringComparison.Ordinal);
        Assert.Contains("current-release.json", validateJob.Value, StringComparison.Ordinal);
        Assert.Contains("release-schema.json", validateJob.Value, StringComparison.Ordinal);
        Assert.Contains("production", migrationJob.Value, StringComparison.Ordinal);
        Assert.Contains("Release migrations are explicitly excluded from production.", migrationJob.Value, StringComparison.Ordinal);
        AssertJobLevelNeedsInclude(workflow, "run_migration", "validate_promotion_ref", "prepare_release", "bootstrap_sql", "classify_changes");
        AssertJobLevelNeedsInclude(workflow, "deploy_release", "validate_promotion_ref", "run_migration", "prepare_release", "classify_changes");
        Assert.Contains("needs.validate_promotion_ref.outputs.deploy_api == 'true'", deployJob.Value, StringComparison.Ordinal);
        Assert.Contains("BEFORE_REVISION", migrationJob.Value, StringComparison.Ordinal);
        Assert.Contains("BEFORE_DIGEST", migrationJob.Value, StringComparison.Ordinal);
        Assert.Contains("BEFORE_TRAFFIC", migrationJob.Value, StringComparison.Ordinal);
        Assert.Contains("deployApi=false release changed API revision, digest, or traffic.", migrationJob.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("ops/releases/sprint-4b-r2.json", workflow, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(FindRepositoryRoot(), "ops", "releases", "sprint-4b-r2.json")));
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
