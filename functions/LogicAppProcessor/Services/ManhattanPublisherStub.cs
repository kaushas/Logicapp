using System.Threading.Tasks;
using LogicAppProcessor.Models;
using Microsoft.Extensions.Logging;

namespace LogicAppProcessor.Services
{
    public class ManhattanPublisherStub : IManhattanPublisher
    {
        private readonly ILogger<ManhattanPublisherStub> _logger;
        public ManhattanPublisherStub(ILogger<ManhattanPublisherStub> logger)
        {
            _logger = logger;
        }

        public Task PublishAsync(StandardEvent evt)
        {
            // Stub: in production this will post to canonical topic and/or call a delivery function
            _logger.LogInformation("[Stub] Publishing event of type {type} with correlation {corr}", evt.eventType, evt.correlationId);
            return Task.CompletedTask;
        }
    }
}
