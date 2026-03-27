using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        // Register Service Bus client
        var serviceBusConnectionString = context.Configuration["SERVICEBUS_CONNECTION_STRING"];
        services.AddSingleton(_ => new ServiceBusClient(serviceBusConnectionString));
    })
    .Build();

host.Run();
