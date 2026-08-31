using System.Text.RegularExpressions;

namespace CloudOrders.ArchitectureTests;

public sealed class ExternalIdentityInfrastructureTests
{
    [Fact]
    public void NonproductionIdentitySettingsAreProtectedAndMappedToContainerAppConfiguration()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainBicep = Read(repositoryRoot, "infra", "main.bicep");
        var containerAppBicep = Read(repositoryRoot, "infra", "modules", "container-app.bicep");

        Assert.Contains("param externalIdentityEnabled bool = false", mainBicep, StringComparison.Ordinal);
        Assert.Contains("@secure()", mainBicep, StringComparison.Ordinal);
        Assert.Contains("externalIdentityAuthority", mainBicep, StringComparison.Ordinal);
        Assert.Contains("externalIdentityValidIssuer", mainBicep, StringComparison.Ordinal);
        Assert.Contains("externalIdentityTenantId", mainBicep, StringComparison.Ordinal);
        Assert.Contains("externalIdentityAudience", mainBicep, StringComparison.Ordinal);
        Assert.Contains("externalIdentityAllowedClientIds", mainBicep, StringComparison.Ordinal);
        Assert.Contains("externalIdentityEnabled: externalIdentityConfigurationEnabled", mainBicep, StringComparison.Ordinal);
        Assert.Contains("ExternalIdentity__Authority", containerAppBicep, StringComparison.Ordinal);
        Assert.Contains("ExternalIdentity__ValidIssuer", containerAppBicep, StringComparison.Ordinal);
        Assert.Contains("ExternalIdentity__TenantId", containerAppBicep, StringComparison.Ordinal);
        Assert.Contains("ExternalIdentity__Audience", containerAppBicep, StringComparison.Ordinal);
        Assert.Contains("ExternalIdentity__AllowedClientIds__0", containerAppBicep, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionOverlayCannotEnableSprintFourExternalIdentityMaterial()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainBicep = Read(repositoryRoot, "infra", "main.bicep");
        var productionParameters = Read(repositoryRoot, "infra", "environments", "production.bicepparam");

        Assert.Contains("environmentName == 'production' && externalIdentityEnabled", mainBicep, StringComparison.Ordinal);
        Assert.Contains("fail('Sprint 4 External ID configuration is restricted to development and test.')", mainBicep, StringComparison.Ordinal);
        Assert.Contains("param externalIdentityEnabled = false", productionParameters, StringComparison.Ordinal);
        Assert.DoesNotContain("externalIdentityAuthority", productionParameters, StringComparison.Ordinal);
        Assert.DoesNotContain("externalIdentityValidIssuer", productionParameters, StringComparison.Ordinal);
    }

    [Fact]
    public void EnvironmentParameterFilesContainNoIdentityIdentifiersOrKeys()
    {
        var repositoryRoot = FindRepositoryRoot();
        var parameterFiles = new[] { "development.bicepparam", "test.bicepparam", "production.bicepparam" };
        var guidPattern = new Regex(@"(?i)\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", RegexOptions.CultureInvariant);

        foreach (var parameterFile in parameterFiles)
        {
            var contents = Read(repositoryRoot, "infra", "environments", parameterFile);

            Assert.DoesNotMatch(guidPattern, contents);
            Assert.DoesNotContain("ciamlogin.com", contents, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("externalIdentityAuthority", contents, StringComparison.Ordinal);
            Assert.DoesNotContain("externalIdentityValidIssuer", contents, StringComparison.Ordinal);
            Assert.DoesNotContain("externalIdentityTenantId", contents, StringComparison.Ordinal);
            Assert.DoesNotContain("externalIdentityAudience", contents, StringComparison.Ordinal);
            Assert.DoesNotContain("externalIdentityAllowedClientIds", contents, StringComparison.Ordinal);
            Assert.DoesNotContain("cursor", contents, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void DeploymentWorkflowUsesProtectedVariablesWithoutEchoingIdentityValues()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workflow = Read(repositoryRoot, ".github", "workflows", "deploy.yml");
        var validationWorkflow = Read(repositoryRoot, ".github", "workflows", "bicep-validation.yml");

        Assert.Contains("${{ vars.EXTERNAL_IDENTITY_AUTHORITY }}", workflow, StringComparison.Ordinal);
        Assert.Contains("${{ vars.EXTERNAL_IDENTITY_VALID_ISSUER }}", workflow, StringComparison.Ordinal);
        Assert.Contains("${{ vars.EXTERNAL_IDENTITY_TENANT_ID }}", workflow, StringComparison.Ordinal);
        Assert.Contains("${{ vars.EXTERNAL_IDENTITY_AUDIENCE }}", workflow, StringComparison.Ordinal);
        Assert.Contains("${{ vars.EXTERNAL_IDENTITY_ALLOWED_CLIENT_IDS }}", workflow, StringComparison.Ordinal);
        Assert.Contains("Validate nonproduction External ID settings", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("echo \"$EXTERNAL_IDENTITY_", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("user.admin", workflow, StringComparison.Ordinal);
        Assert.Contains("Reject identity identifiers in parameter overlays", validationWorkflow, StringComparison.Ordinal);
        Assert.Contains("development.bicepparam", validationWorkflow, StringComparison.Ordinal);
        Assert.Contains("test.bicepparam", validationWorkflow, StringComparison.Ordinal);
        Assert.Contains("production.bicepparam", validationWorkflow, StringComparison.Ordinal);
    }

    [Fact]
    public void DeploymentWhatIfCommandsAvoidIdentityBearingResourcePayloads()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workflow = Read(repositoryRoot, ".github", "workflows", "deploy.yml");

        Assert.DoesNotContain("--result-format FullResourcePayloads", workflow, StringComparison.Ordinal);
        Assert.Equal(3, new Regex("az deployment group what-if", RegexOptions.CultureInvariant).Count(workflow));
        Assert.Equal(3, new Regex("--result-format ResourceIdOnly", RegexOptions.CultureInvariant).Count(workflow));
    }

    [Fact]
    public void ExternalIdSetupRecordsDeferredCursorAccountabilityWithoutAddingCursorMaterial()
    {
        var repositoryRoot = FindRepositoryRoot();
        var runbook = Read(repositoryRoot, "ops", "runbooks", "external-id-setup.md");

        Assert.Contains("future Sprint 4B cursor-key owner and rotation date", runbook, StringComparison.Ordinal);
        Assert.Contains("Do not create or commit a cursor key, cursor configuration, or cursor runbook during Sprint 4A.", runbook, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthSmokeUsesInteractivePkceAndDoesNotPersistOrExposeTokens()
    {
        var repositoryRoot = FindRepositoryRoot();
        var program = Read(repositoryRoot, "tools", "CloudOrders.AuthSmoke", "Program.cs");

        Assert.Contains("PublicClientApplicationBuilder", program, StringComparison.Ordinal);
        Assert.Contains("Create(arguments.ClientId.ToString(\"D\"))", program, StringComparison.Ordinal);
        Assert.Contains("WithRedirectUri(\"http://localhost\")", program, StringComparison.Ordinal);
        Assert.Contains("AcquireTokenInteractive", program, StringComparison.Ordinal);
        Assert.Contains("/api/v1/me", program, StringComparison.Ordinal);
        Assert.DoesNotContain("SerializeMsalV3", program, StringComparison.Ordinal);
        Assert.DoesNotContain("TokenCache", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Console.WriteLine(authentication.AccessToken", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.Graph", program, StringComparison.Ordinal);
        Assert.DoesNotContain("ServerCertificateCustomValidationCallback", program, StringComparison.Ordinal);
    }

    private static string Read(string repositoryRoot, params string[] segments) =>
        File.ReadAllText(Path.Combine(new[] { repositoryRoot }.Concat(segments).ToArray()));

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
