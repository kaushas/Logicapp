using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using LogicAppProcessor.Services;
using LogicAppProcessor.Repositories;
using Microsoft.EntityFrameworkCore;
using System;

[assembly: FunctionsStartup(typeof(LogicAppProcessor.FunctionStartup))]
namespace LogicAppProcessor
{
    public class FunctionStartup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            // Register services
            builder.Services.AddSingleton<IMessageIdService, MessageIdService>();
            builder.Services.AddSingleton<ILiquidMapper, LiquidMapper>();
            builder.Services.AddScoped<IInboxRepository, EFInboxRepository>();
            builder.Services.AddScoped<IOutboxRepository, EFOutboxRepository>();
            builder.Services.AddScoped<IManhattanPublisher, ManhattanPublisherHttp>();

            // Configure DbContext with scoped lifetime for proper transaction support
            var conn = Environment.GetEnvironmentVariable("PROCESSING_DB_CONN") ?? "Server=(localdb)\\mssqllocaldb;Database=ProcessingDb;Trusted_Connection=True;";
            builder.Services.AddDbContext<ProcessingDbContext>(options => 
                options.UseSqlServer(conn),
                ServiceLifetime.Scoped);

            // HttpClient for publisher
            builder.Services.AddHttpClient<IManhattanPublisher, ManhattanPublisherHttp>();

            // Service Bus client and canonical publisher
            var sbConn = Environment.GetEnvironmentVariable("SERVICEBUS_CONN") ?? "Endpoint=sb://<namespace>.servicebus.windows.net/;SharedAccessKeyName=<keyname>;SharedAccessKey=<key>";
            builder.Services.AddSingleton(sp => new Azure.Messaging.ServiceBus.ServiceBusClient(sbConn));
            builder.Services.AddScoped<ICanonicalPublisher, ServiceBusCanonicalPublisher>();
        }
    }
}
