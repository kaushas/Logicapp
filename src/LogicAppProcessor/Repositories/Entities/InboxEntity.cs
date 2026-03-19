using System;

namespace LogicAppProcessor.Repositories.Entities
{
    public class InboxEntity
    {
        public long Id { get; set; }
        public string MessageId { get; set; }
        public string SourceTopic { get; set; }
        public string CorrelationId { get; set; }
        public string RawPayload { get; set; }
        public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    }
}
