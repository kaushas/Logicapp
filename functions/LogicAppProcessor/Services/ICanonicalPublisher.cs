using System.Threading.Tasks;
using LogicAppProcessor.Models;

namespace LogicAppProcessor.Services
{
    public interface ICanonicalPublisher
    {
        Task PublishAsync(StandardEvent evt, string messageId);
    }
}
