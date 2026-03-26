using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace ServiceBusProxy;

public class ServiceBusProxyFunction
{
    private readonly ILogger<ServiceBusProxyFunction> _logger;
    private readonly ServiceBusClient _serviceBusClient;

    public ServiceBusProxyFunction(
        ILogger<ServiceBusProxyFunction> logger,
        ServiceBusClient serviceBusClient)
    {
        _logger = logger;
        _serviceBusClient = serviceBusClient;
    }

    /// <summary>
    /// Proxies Service Bus queue message retrieval for Logic Apps local development
    /// GET /apim/servicebus/{connectionId}/{queueName}/messages/head?queueType=Main
    /// </summary>
    [Function("GetQueueMessage")]
    public async Task<HttpResponseData> GetQueueMessage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "apim/servicebus/{connectionId}/{queueName}/messages/head")] 
        HttpRequestData req,
        string connectionId,
        string queueName)
    {
        _logger.LogInformation($"========================================");
        _logger.LogInformation($"[PROXY] HTTP Request Received");
        _logger.LogInformation($"[PROXY] URL: {req.Url}");
        _logger.LogInformation($"[PROXY] Queue Name: '{queueName}'");
        _logger.LogInformation($"[PROXY] Method: {req.Method}");
        _logger.LogInformation($"========================================");

        try
        {
            _logger.LogInformation($"[PROXY] Creating Service Bus receiver for queue: '{queueName}'");
            
            // Create receiver for the queue
            var receiver = _serviceBusClient.CreateReceiver(queueName, new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            });

            _logger.LogInformation($"[PROXY] Receiver created successfully, attempting to receive message (timeout: 2s)...");

            // Receive a single message (with short timeout)
            var message = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(2));

            if (message == null)
            {
                // No messages available - return empty response
                _logger.LogWarning($"[PROXY] No messages available in queue '{queueName}'");
                
                var emptyResponse = req.CreateResponse(HttpStatusCode.NoContent);
                await receiver.DisposeAsync();
                return emptyResponse;
            }

            _logger.LogInformation($"[PROXY] ✓ Message Retrieved!");
            _logger.LogInformation($"[PROXY]   MessageId: {message.MessageId}");
            _logger.LogInformation($"[PROXY]   CorrelationId: {message.CorrelationId}");
            _logger.LogInformation($"[PROXY]   Size: {message.Body.ToArray().Length} bytes");
            _logger.LogInformation($"[PROXY]   EnqueuedTime: {message.EnqueuedTime}");
            _logger.LogInformation($"[PROXY]   DeliveryCount: {message.DeliveryCount}");

            // Decode and log the actual message content
            var messageBytes = message.Body.ToArray();
            var messageContent = Encoding.UTF8.GetString(messageBytes);
            _logger.LogInformation($"[PROXY] ==== DECODED MESSAGE CONTENT ====");
            _logger.LogInformation(messageContent);
            _logger.LogInformation($"[PROXY] ==== END MESSAGE CONTENT ====");

            var contentType = message.ContentType ?? "application/json";
            
            // For JSON content types, parse the body and provide it as Content
            // Logic Apps expects JSON messages to be pre-parsed
            object? parsedContent = null;
            if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    parsedContent = JsonSerializer.Deserialize<object>(messageContent);
                    _logger.LogInformation($"[PROXY] Successfully parsed JSON content");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"[PROXY] Failed to parse JSON content: {ex.Message}");
                }
            }

            // Convert to Logic Apps Service Bus message format
            // Include both ContentData (base64) and ContentDataJson (parsed JSON) for compatibility
            var serviceBusMessage = new
            {
                ContentData = Convert.ToBase64String(messageBytes),
                ContentDataJson = parsedContent, // Parsed JSON for application/json content types
                ContentType = contentType,
                MessageId = message.MessageId,
                SequenceNumber = message.SequenceNumber,
                Properties = message.ApplicationProperties ?? new Dictionary<string, object>(),
                CorrelationId = message.CorrelationId,
                DeliveryCount = message.DeliveryCount,
                EnqueuedTime = message.EnqueuedTime.ToString("o"), // ISO 8601 format
                ExpiresAt = message.ExpiresAt.ToString("o"),
                LockedUntil = message.LockedUntil.ToString("o"),
                LockToken = message.LockToken,
                PartitionKey = message.PartitionKey,
                ReplyTo = message.ReplyTo,
                Subject = message.Subject,
                TimeToLive = message.TimeToLive.TotalSeconds, // Convert to seconds
                To = message.To,
                SessionId = message.SessionId,
                ReplyToSessionId = message.ReplyToSessionId
            };

            // DO NOT complete message here - let Logic App process it first
            // The lock will expire if processing fails, and message will return to queue (at-least-once delivery)
            // Logic App handles idempotency via SHA256 hash in Inbox table
            _logger.LogInformation($"[PROXY] Message lock will expire at: {message.LockedUntil}");
            _logger.LogInformation($"[PROXY] Returning message to Logic App WITHOUT completing (Logic App will process then message lock expires)");

            // Close receiver (does not complete message)
            await receiver.DisposeAsync();

            _logger.LogInformation($"[PROXY] Returning message to Logic App (200 OK, {serviceBusMessage.ContentData.Length} chars base64)");

            // Return message in Service Bus format
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            
            // Use default serialization (PascalCase) to match Logic Apps expectation
            var json = JsonSerializer.Serialize(serviceBusMessage, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            _logger.LogInformation($"[PROXY] ==== FULL RESPONSE JSON ====");
            _logger.LogInformation(json);
            _logger.LogInformation($"[PROXY] ==== END RESPONSE JSON ====");
            await response.WriteStringAsync(json);

            _logger.LogInformation($"[PROXY] Response sent successfully");

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError($"========================================");
            _logger.LogError($"[PROXY] ❌ ERROR retrieving message from queue '{queueName}'");
            _logger.LogError($"[PROXY] Exception Type: {ex.GetType().Name}");
            _logger.LogError($"[PROXY] Exception Message: {ex.Message}");
            _logger.LogError($"[PROXY] Stack Trace: {ex.StackTrace}");
            _logger.LogError($"========================================");
            
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                error = ex.Message,
                exceptionType = ex.GetType().Name,
                queue = queueName
            }));
            return errorResponse;
        }
    }

    /// <summary>
    /// Health check endpoint
    /// </summary>
    [Function("HealthCheck")]
    public HttpResponseData HealthCheck(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] 
        HttpRequestData req)
    {
        _logger.LogInformation($"[PROXY] Health check request received from {req.Url}");
        
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.WriteString("ServiceBusProxy is running and healthy");
        return response;
    }
}
