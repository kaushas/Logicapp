using System.Threading.Tasks;
using LogicAppProcessor.Models;

namespace LogicAppProcessor.Services
{
    public interface IManhattanPublisher
    {
        Task PublishAsync(StandardEvent evt);
    }
}
