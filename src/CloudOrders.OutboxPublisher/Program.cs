using Azure.Messaging.ServiceBus;
using CloudOrders.Application.Abstractions;
using CloudOrders.Infrastructure.Persistence;
using CloudOrders.OutboxPublisher;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddDbContextFactory<CloudOrdersDbContext>(options => options.UseSqlServer(context.Configuration.GetConnectionString("CloudOrders")));
        services.AddScoped<SqlIdempotentOrderStore>();
        services.AddScoped<IOutboxLeaseStore>(sp => sp.GetRequiredService<SqlIdempotentOrderStore>());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(sp => new ServiceBusClient(context.Configuration["ServiceBusConnection"]));
        services.AddSingleton(sp => sp.GetRequiredService<ServiceBusClient>().CreateSender(context.Configuration["ServiceBusQueue"] ?? "orders"));
        services.AddSingleton<IOutboxMessageSender, ServiceBusOutboxMessageSender>();
        services.AddScoped<OutboxPublisherFunction>();
    })
    .Build();

await host.RunAsync();
