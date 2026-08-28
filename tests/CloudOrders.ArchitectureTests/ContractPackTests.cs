namespace CloudOrders.ArchitectureTests;

public sealed class ContractPackTests
{
    [Fact]
    public void RepositoryContainsVersionedContractPackAndTraceability()
    {
        var contractsDirectory = Path.Combine(FindRepositoryRoot(), "docs", "contracts");

        AssertContractDocument(contractsDirectory, "frontend-design.md", "Source handoff section: 19");
        AssertContractDocument(contractsDirectory, "v1-contracts.md", "Source handoff sections: 25-35");
        AssertContractDocument(contractsDirectory, "traceability.md", "Sprint gate");

        var frontendContract = File.ReadAllText(Path.Combine(contractsDirectory, "frontend-design.md"));
        Assert.Contains("standalone .NET 10 Blazor WebAssembly", frontendContract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Received → Processing", frontendContract, StringComparison.Ordinal);

        var v1Contract = File.ReadAllText(Path.Combine(contractsDirectory, "v1-contracts.md"));
        Assert.Contains("## 25. Business scope and status model", v1Contract, StringComparison.Ordinal);
        Assert.Contains("## 35. Version-1 definition of done", v1Contract, StringComparison.Ordinal);

        var traceability = File.ReadAllText(Path.Combine(contractsDirectory, "traceability.md"));
        Assert.Contains("DeploymentWorkflowPolicyTests.DeploymentWorkflowEnforcesPinnedPromotionAndReleasePolicy", traceability, StringComparison.Ordinal);
        Assert.Contains("ContractPackTests.RepositoryContainsVersionedContractPackAndTraceability", traceability, StringComparison.Ordinal);
    }

    [Fact]
    public void Sprint4AContractsUseExternalIdCustomerOwnershipAndBareDelegatedScopes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var contractsDirectory = Path.Combine(repositoryRoot, "docs", "contracts");
        var v1Contract = File.ReadAllText(Path.Combine(contractsDirectory, "v1-contracts.md"));
        var frontendContract = File.ReadAllText(Path.Combine(contractsDirectory, "frontend-design.md"));
        var traceability = File.ReadAllText(Path.Combine(contractsDirectory, "traceability.md"));

        Assert.Contains("verified External ID customer", v1Contract, StringComparison.Ordinal);
        Assert.Contains("`/me`", v1Contract, StringComparison.Ordinal);
        Assert.Contains("`Orders.Read`", v1Contract, StringComparison.Ordinal);
        Assert.Contains("`Orders.Write`", v1Contract, StringComparison.Ordinal);
        Assert.Contains("`user.admin`", v1Contract, StringComparison.Ordinal);
        Assert.Contains("CustomerProfileId", v1Contract, StringComparison.Ordinal);
        Assert.Contains("actor/target", v1Contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SHA-256 over the canonical request payload plus ActorCustomerProfileId and TargetCustomerProfileId", v1Contract, StringComparison.Ordinal);
        Assert.Contains("`(ActorCustomerProfileId, TargetCustomerProfileId, IdempotencyKey)`", v1Contract, StringComparison.Ordinal);
        Assert.Contains("E1 legacy compatibility: existing `(SubjectId, IdempotencyKey)` records remain readable only for a retry that resolves to the same actor and target CustomerProfileId; E1 creates no subject-only hash or key.", v1Contract, StringComparison.Ordinal);
        Assert.DoesNotContain("IdempotencyRecords has primary key `(SubjectId, IdempotencyKey)`", v1Contract, StringComparison.Ordinal);
        Assert.Contains("401", v1Contract, StringComparison.Ordinal);
        Assert.Contains("403", v1Contract, StringComparison.Ordinal);
        Assert.Contains("404", v1Contract, StringComparison.Ordinal);
        Assert.Contains("expand/migrate/contract", v1Contract, StringComparison.Ordinal);
        Assert.Contains("api://{api-client-id}/Orders.Read", frontendContract, StringComparison.Ordinal);
        Assert.Contains("bare `scp`", frontendContract, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderUser", frontendContract, StringComparison.Ordinal);
        Assert.DoesNotContain("group-to-customer", v1Contract, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CloudOrders.Orders.Read", v1Contract, StringComparison.Ordinal);
        Assert.DoesNotContain("CloudOrders.Orders.Write", v1Contract, StringComparison.Ordinal);
        Assert.Contains("Sprint 4A", traceability, StringComparison.Ordinal);
    }

    private static void AssertContractDocument(string contractsDirectory, string fileName, string requiredText)
    {
        var path = Path.Combine(contractsDirectory, fileName);

        Assert.True(File.Exists(path), $"Expected contract document at {path}.");
        Assert.Contains(requiredText, File.ReadAllText(path), StringComparison.Ordinal);
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
