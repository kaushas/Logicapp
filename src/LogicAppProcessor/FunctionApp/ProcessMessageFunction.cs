using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using LogicAppProcessor.Services;
using LogicAppProcessor.Models;
using LogicAppProcessor.Repositories;
using LogicAppProcessor.Repositories.Entities;

namespace LogicAppProcessor.FunctionApp
{
    public class ProcessMessageFunction
    {
        // Static in-process cache for idempotency key tracking (survives across function invocations)
        private static readonly ConcurrentDictionary<string, DateTime> _idempotencyCache = 
            new ConcurrentDictionary<string, DateTime>();
        
        // TTL for cached idempotency keys (5 minutes)
        private static readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);

        private readonly IMessageIdService _messageIdService;
        private readonly ILiquidMapper _liquidMapper;
        private readonly IInboxRepository _inboxRepository;
        private readonly IOutboxRepository _outboxRepository;
        private readonly IManhattanPublisher _manhattanPublisher;
        private readonly ICanonicalPublisher _canonicalPublisher;
        private readonly ProcessingDbContext _dbContext;

        public ProcessMessageFunction(
            IMessageIdService messageIdService,
            ILiquidMapper liquidMapper,
            IInboxRepository inboxRepository,
            IOutboxRepository outboxRepository,
            IManhattanPublisher manhattanPublisher,
            ICanonicalPublisher canonicalPublisher,
            ProcessingDbContext dbContext)
        {
            _messageIdService = messageIdService;
            _liquidMapper = liquidMapper;
            _inboxRepository = inboxRepository;
            _outboxRepository = outboxRepository;
            _manhattanPublisher = manhattanPublisher;
            _canonicalPublisher = canonicalPublisher;
            _dbContext = dbContext;
        }

        [FunctionName("ProcessMessage")]
        public async Task<IActionResult> Run([
            HttpTrigger(AuthorizationLevel.Function, "post", Route = "process-message")] HttpRequest req,
            ILogger log)
        {
            string messageId = null;

            try
            {
                // Read raw body
                string raw = await new System.IO.StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrWhiteSpace(raw))
                {
                    log.LogWarning("Received empty payload");
                    return new BadRequestObjectResult(new { error = "Empty payload" });
                }

                // Compute message id for idempotency
                messageId = _messageIdService.ComputeMessageId(raw);
                log.LogInformation($"Processing message: {messageId}");

                // Check for duplicates (Inbox pattern - idempotency)
                if (await _inboxRepository.ExistsAsync(messageId))
                {
                    log.LogInformation($"Duplicate message detected: {messageId}");
                    return new OkObjectResult(new { status = "Duplicate", messageId });
                }

                // Determine mapping template based on header or payload
                string sourceTopic = req.Headers.ContainsKey("x-source-topic") 
                    ? req.Headers["x-source-topic"].ToString() 
                    : "customerToCanonical";
                
                string templateName = $"{sourceTopic}.liquid";

                // Begin transaction for Inbox + Outbox pattern (ACID guarantee)
                using (var transaction = await _dbContext.Database.BeginTransactionAsync())
                {
                    try
                    {
                        // 1. Save to Inbox (deduplication + audit trail)
                        await _inboxRepository.SaveAsync(new InboxRecord 
                        { 
                            MessageId = messageId, 
                            RawPayload = raw 
                        });

                        // 2. Map to canonical using Liquid templates
                        StandardEvent canonical = _liquidMapper.MapToCanonical(templateName, raw);

                        // 3. Save to Outbox (guaranteed delivery pattern)
                        var outboxEntity = new OutboxEntity
                        {
                            MessageId = messageId,
                            Destination = "ServiceBusCanonical",
                            Payload = System.Text.Json.JsonSerializer.Serialize(canonical),
                            Sent = false
                        };
                        await _outboxRepository.SaveAsync(outboxEntity);

                        // Commit transaction - message is now safely persisted
                        await transaction.CommitAsync();
                        log.LogInformation($"Message persisted successfully: {messageId}");

                        // 4. Attempt to publish (outside transaction)
                        // If this fails, a separate process can retry from Outbox
                        try
                        {
                            await _canonicalPublisher.PublishAsync(canonical, messageId);
                            await _outboxRepository.MarkSentAsync(outboxEntity.Id);
                            log.LogInformation($"Message published to Service Bus: {messageId}");
                        }
                        catch (Exception pubEx)
                        {
                            log.LogError(pubEx, $"Failed to publish message {messageId} - will retry from Outbox");
                            await _outboxRepository.MarkFailedAsync(outboxEntity.Id, pubEx.Message);
                            // Don't fail the request - message is safely persisted in Outbox for retry
                        }

                        return new OkObjectResult(new { status = "Processed", messageId });
                    }
                    catch
                    {
                        await transaction.RollbackAsync();
                        throw;
                    }
                }
            }
            catch (InvalidOperationException invEx)
            {
                log.LogError(invEx, $"Invalid operation for message {messageId}: {invEx.Message}");
                return new BadRequestObjectResult(new 
                { 
                    error = "Invalid operation", 
                    message = invEx.Message,
                    messageId 
                });
            }
            catch (ArgumentException argEx)
            {
                log.LogError(argEx, $"Invalid argument for message {messageId}: {argEx.Message}");
                return new BadRequestObjectResult(new 
                { 
                    error = "Invalid input", 
                    message = argEx.Message,
                    messageId 
                });
            }
            catch (Exception ex)
            {
                log.LogError(ex, $"Unhandled error processing message {messageId}: {ex.Message}");
                return new ObjectResult(new 
                { 
                    error = "Internal server error", 
                    message = ex.Message,
                    messageId 
                })
                {
                    StatusCode = 500
                };
            }
        }

        /// <summary>
        /// CheckDuplicate - Called from Logic Apps to check if message has been processed recently
        /// Uses in-process cache (5-min TTL) to avoid redundant processing without DB calls
        /// </summary>
        [FunctionName("CheckDuplicate")]
        public IActionResult CheckDuplicate(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "check-duplicate")] HttpRequest req,
            ILogger log)
        {
            try
            {
                // Extract idempotency key from request body or query parameter
                string idempotencyKey = null;

                if (req.Query.ContainsKey("key"))
                {
                    idempotencyKey = req.Query["key"].ToString();
                }
                else
                {
                    // Try reading from JSON body
                    string body = new System.IO.StreamReader(req.Body).ReadToEndAsync().Result;
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        var json = System.Text.Json.JsonDocument.Parse(body);
                        if (json.RootElement.TryGetProperty("idempotencyKey", out var keyElement))
                        {
                            idempotencyKey = keyElement.GetString();
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(idempotencyKey))
                {
                    return new BadRequestObjectResult(new { error = "Missing idempotencyKey" });
                }

                // Clean expired entries from cache
                CleanExpiredCacheEntries(log);

                // Check if key exists in cache
                bool isDuplicate = _idempotencyCache.ContainsKey(idempotencyKey);

                if (isDuplicate)
                {
                    log.LogInformation($"Duplicate detected in cache: {idempotencyKey}");
                    return new OkObjectResult(new 
                    { 
                        isDuplicate = true, 
                        message = "Message already processed in last 5 minutes",
                        idempotencyKey 
                    });
                }

                // Not a duplicate - add to cache
                _idempotencyCache.TryAdd(idempotencyKey, DateTime.UtcNow);
                log.LogInformation($"New message cached: {idempotencyKey}");

                return new OkObjectResult(new 
                { 
                    isDuplicate = false,
                    message = "Message is new, proceed with processing",
                    idempotencyKey 
                });
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error in CheckDuplicate function");
                return new ObjectResult(new 
                { 
                    error = "Internal server error", 
                    message = ex.Message 
                })
                {
                    StatusCode = 500
                };
            }
        }

        /// <summary>
        /// Cleans expired entries from the idempotency cache
        /// Removes keys that are older than _cacheExpiry (5 minutes)
        /// </summary>
        private static void CleanExpiredCacheEntries(ILogger log)
        {
            var expiredKeys = _idempotencyCache
                .Where(kvp => DateTime.UtcNow - kvp.Value > _cacheExpiry)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                if (_idempotencyCache.TryRemove(key, out _))
                {
                    log.LogDebug($"Removed expired cache entry: {key}");
                }
            }
        }
    }
}
