using System.Threading.Tasks;

namespace LogicAppProcessor.Repositories
{
    public interface IInboxRepository
    {
        Task<bool> ExistsAsync(string messageId);
        Task SaveAsync(InboxRecord record);
    }

    public class InboxRecord
    {
        public string MessageId { get; set; }
        public string RawPayload { get; set; }
    }
}
