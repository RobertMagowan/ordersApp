using System.Text.Json;
using CloudOrders.OutboxPublisher;

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

    [Fact]
    public void LocalPublisherSettingsUseEmulatorModeAndAConfigurableAmqpEndpoint()
    {
        using var settings = ReadLocalSettings();
        var values = settings.RootElement.GetProperty("Values");

        Assert.Contains("UseDevelopmentEmulator=true", values.GetProperty("ServiceBusConnection").GetString(), StringComparison.Ordinal);
        Assert.Equal("amqp://localhost:5672", values.GetProperty("ServiceBusCustomEndpointAddress").GetString());
    }

    [Fact]
    public void ConfiguredPublisherEndpointIsAppliedToTheServiceBusClientOptions()
    {
        var options = OutboxServiceBusClientConfiguration.CreateOptions("amqp://localhost:5673");

        Assert.Equal(new Uri("amqp://localhost:5673"), options.CustomEndpointAddress);
    }

    [Fact]
    public void MissingPublisherEndpointLeavesTheProductionClientOptionsUnchanged()
    {
        var options = OutboxServiceBusClientConfiguration.CreateOptions(null);

        Assert.Null(options.CustomEndpointAddress);
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

    private static JsonDocument ReadLocalSettings()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CloudOrders.slnx")))
            {
                return JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "local", "local.settings.json.example")));
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the CloudOrders repository root.");
    }
}
