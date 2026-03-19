using System;
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
    }
}
