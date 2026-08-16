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
