using System.Text.Json;

namespace CloudOrders.ArchitectureTests;

public sealed class LocalServiceBusConfigurationTests
{
    [Fact]
    public void EmulatorUsesTheRequiredNamespaceName()
    {
        using var configuration = ReadConfiguration();
        var namespaces = configuration.RootElement.GetProperty("UserConfig").GetProperty("Namespaces").EnumerateArray();

        var emulatorNamespace = Assert.Single(namespaces);
        Assert.Equal("sbemulatorns", emulatorNamespace.GetProperty("Name").GetString());
    }

    [Fact]
    public void EmulatorIncludesConsoleLoggingForStartupDiagnostics()
    {
        using var configuration = ReadConfiguration();
        var userConfig = configuration.RootElement.GetProperty("UserConfig");

        Assert.True(userConfig.TryGetProperty("Logging", out var logging), "The emulator requires a Logging configuration.");
        Assert.Equal("Console", logging.GetProperty("Type").GetString());
    }

    [Fact]
    public void EmulatorPreservesTheOrdersQueueContract()
    {
        using var configuration = ReadConfiguration();
        var emulatorNamespace = Assert.Single(configuration.RootElement.GetProperty("UserConfig").GetProperty("Namespaces").EnumerateArray());
        var queue = Assert.Single(emulatorNamespace.GetProperty("Queues").EnumerateArray());

        Assert.Equal("orders", queue.GetProperty("Name").GetString());
        Assert.False(queue.GetProperty("Properties").GetProperty("DeadLetteringOnMessageExpiration").GetBoolean());
        Assert.Equal(5, queue.GetProperty("Properties").GetProperty("MaxDeliveryCount").GetInt32());
    }

    private static JsonDocument ReadConfiguration()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CloudOrders.slnx")))
            {
                return JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "local", "servicebus-config.json")));
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the CloudOrders repository root.");
    }
}
