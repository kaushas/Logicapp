using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using LogicAppProcessor.Models;
using LogicAppProcessor.Repositories;
using Microsoft.Extensions.Logging;

namespace LogicAppProcessor.Services
{
    public class ServiceBusCanonicalPublisher : ICanonicalPublisher, IAsyncDisposable
    {
        private readonly ServiceBusClient _client;
        private readonly IOutboxRepository _outbox;
        private readonly ILogger<ServiceBusCanonicalPublisher> _logger;
        private readonly string _topicCustomer;
        private readonly string _topicVendor;
        private readonly string _topicAddress;

        public ServiceBusCanonicalPublisher(ServiceBusClient client, IOutboxRepository outbox, ILogger<ServiceBusCanonicalPublisher> logger)
        {
            _client = client;
            _outbox = outbox;
            _logger = logger;

            // Read topic names from environment and validate
            _topicCustomer = Environment.GetEnvironmentVariable("TOPIC_CUSTOMER");
            _topicVendor = Environment.GetEnvironmentVariable("TOPIC_VENDOR");
            _topicAddress = Environment.GetEnvironmentVariable("TOPIC_ADDRESS");

            if (string.IsNullOrWhiteSpace(_topicCustomer))
            {
                _topicCustomer = "CustomerChanged";
                _logger.LogWarning("Environment variable TOPIC_CUSTOMER is not set. Falling back to default '{topic}'", _topicCustomer);
            }

            if (string.IsNullOrWhiteSpace(_topicVendor))
            {
                _topicVendor = "VendorChanged";
                _logger.LogWarning("Environment variable TOPIC_VENDOR is not set. Falling back to default '{topic}'", _topicVendor);
            }

            if (string.IsNullOrWhiteSpace(_topicAddress))
            {
                _topicAddress = "BusinessAddressChanged";
                _logger.LogWarning("Environment variable TOPIC_ADDRESS is not set. Falling back to default '{topic}'", _topicAddress);
            }
        }

        public async Task PublishAsync(StandardEvent evt, string messageId)
        {
            // Choose topic based on eventType using configured values
            string topic = evt.eventType switch
            {
                "VendorChanged" => _topicVendor,
                "BusinessAddressChanged" => _topicAddress,
                _ => _topicCustomer
            };

            // Save to outbox first
            var payload = JsonSerializer.Serialize(evt);
            var outboxEntity = new Repositories.Entities.OutboxEntity
            {
                MessageId = messageId ?? evt.correlationId ?? Guid.NewGuid().ToString(),
                Destination = topic,
                Payload = payload
            };

            await _outbox.SaveAsync(outboxEntity);

            // Create ServiceBusMessage with properties
            var message = new ServiceBusMessage(Encoding.UTF8.GetBytes(payload))
            {
                ContentType = "application/json",
            };
            if (!string.IsNullOrEmpty(evt.correlationId))
                message.CorrelationId = evt.correlationId;
            message.ApplicationProperties["MessageId"] = outboxEntity.MessageId;

            ServiceBusSender sender = _client.CreateSender(topic);

            try
            {
                await sender.SendMessageAsync(message);
                await _outbox.MarkSentAsync(outboxEntity.Id);
                _logger.LogInformation("Published message {msg} to topic {topic}", outboxEntity.MessageId, topic);
            }
            catch (ServiceBusException sbex) when (sbex.IsTransient)
            {
                _logger.LogWarning(sbex, "Transient Service Bus error publishing to {topic}", topic);
                // let retry be handled by caller / hosting infra; outbox contains payload
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish to Service Bus topic {topic}", topic);
                await _outbox.MarkFailedAsync(outboxEntity.Id, ex.Message);
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _client.DisposeAsync();
        }
    }
}
