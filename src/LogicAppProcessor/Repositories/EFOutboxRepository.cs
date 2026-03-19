using System;
using System.Threading.Tasks;
using LogicAppProcessor.Repositories.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LogicAppProcessor.Repositories
{
    public class EFOutboxRepository : IOutboxRepository
    {
        private readonly ProcessingDbContext _db;
        private readonly ILogger<EFOutboxRepository> _logger;

        public EFOutboxRepository(ProcessingDbContext db, ILogger<EFOutboxRepository> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task SaveAsync(OutboxEntity entity)
        {
            try
            {
                if (entity == null)
                {
                    throw new ArgumentNullException(nameof(entity), "OutboxEntity cannot be null");
                }

                _db.Outbox.Add(entity);
                await _db.SaveChangesAsync();
                _logger.LogInformation($"Saved message {entity.MessageId} to Outbox with ID {entity.Id}");
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, $"Database error saving message {entity?.MessageId} to Outbox");
                throw new InvalidOperationException("Failed to save message to Outbox", dbEx);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unexpected error saving message {entity?.MessageId} to Outbox");
                throw;
            }
        }

        public async Task MarkSentAsync(long id)
        {
            try
            {
                var e = await _db.Outbox.FindAsync(id);
                if (e == null)
                {
                    _logger.LogWarning($"Outbox record {id} not found when marking as sent");
                    return;
                }
                
                e.Sent = true;
                e.SentAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                _logger.LogInformation($"Marked Outbox message {e.MessageId} (ID: {id}) as sent");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error marking Outbox record {id} as sent");
                throw;
            }
        }

        public async Task MarkFailedAsync(long id, string error)
        {
            try
            {
                var e = await _db.Outbox.FindAsync(id);
                if (e == null)
                {
                    _logger.LogWarning($"Outbox record {id} not found when marking as failed");
                    return;
                }
                
                e.Error = error;
                await _db.SaveChangesAsync();
                _logger.LogWarning($"Marked Outbox message {e.MessageId} (ID: {id}) as failed: {error}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error marking Outbox record {id} as failed");
                throw;
            }
        }
    }
}
