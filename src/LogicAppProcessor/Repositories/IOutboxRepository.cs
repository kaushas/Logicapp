using System.Threading.Tasks;
using LogicAppProcessor.Repositories.Entities;

namespace LogicAppProcessor.Repositories
{
    public interface IOutboxRepository
    {
        Task SaveAsync(OutboxEntity entity);
        Task MarkSentAsync(long id);
        Task MarkFailedAsync(long id, string error);
    }
}
