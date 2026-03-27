using System.Threading.Tasks;

namespace LogicAppProcessor.Repositories
{
    public class InboxRepository : IInboxRepository
    {
        // TODO: Implement EF Core DbContext injection and real persistence
        public Task<bool> ExistsAsync(string messageId)
        {
            // Placeholder: always return false
            return Task.FromResult(false);
        }

        public Task SaveAsync(InboxRecord record)
        {
            // Placeholder: TODO persist to Azure SQL via EF Core
            return Task.CompletedTask;
        }
    }
}
