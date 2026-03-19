using System;
using System.Threading.Tasks;
using LogicAppProcessor.Repositories.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LogicAppProcessor.Repositories
{
    public class EFInboxRepository : IInboxRepository
    {
        private readonly ProcessingDbContext _db;
        private readonly ILogger<EFInboxRepository> _logger;

        public EFInboxRepository(ProcessingDbContext db, ILogger<EFInboxRepository> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<bool> ExistsAsync(string messageId)
        {
            try
            {
                if (string.IsNullOrEmpty(messageId))
                {
                    _logger.LogWarning("ExistsAsync called with empty messageId");
                    return false;
                }

                var exists = await _db.Inbox.AsNoTracking().AnyAsync(x => x.MessageId == messageId);
                if (exists)
                {
                    _logger.LogInformation($"Message {messageId} already exists in Inbox");
                }
                return exists;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking if message {messageId} exists in Inbox");
                throw;
            }
        }

        public async Task SaveAsync(InboxRecord record)
        {
            try
            {
                if (record == null)
                {
                    throw new ArgumentNullException(nameof(record), "InboxRecord cannot be null");
                }

                if (string.IsNullOrEmpty(record.MessageId))
                {
                    throw new ArgumentException("MessageId cannot be empty", nameof(record));
                }

                var entity = new InboxEntity
                {
                    MessageId = record.MessageId,
                    RawPayload = record.RawPayload
                };

                _db.Inbox.Add(entity);
                await _db.SaveChangesAsync();
                _logger.LogInformation($"Saved message {record.MessageId} to Inbox");
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, $"Database error saving message {record?.MessageId} to Inbox");
                throw new InvalidOperationException("Failed to save message to Inbox", dbEx);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unexpected error saving message {record?.MessageId} to Inbox");
                throw;
            }
        }
    }
}
