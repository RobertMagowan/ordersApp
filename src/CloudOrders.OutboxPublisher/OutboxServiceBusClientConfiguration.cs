using Azure.Messaging.ServiceBus;

namespace CloudOrders.OutboxPublisher;

public static class OutboxServiceBusClientConfiguration
{
    public static ServiceBusClientOptions CreateOptions(string? customEndpointAddress)
    {
        var options = new ServiceBusClientOptions();
        if (!string.IsNullOrWhiteSpace(customEndpointAddress))
        {
            options.CustomEndpointAddress = new Uri(customEndpointAddress, UriKind.Absolute);
        }

        return options;
    }
}
