using System;

namespace LogicAppProcessor.Repositories.Entities
{
    public class OutboxEntity
    {
        public long Id { get; set; }
        public string MessageId { get; set; }
        public string Destination { get; set; }
        public string Payload { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool Sent { get; set; }
        public DateTime? SentAt { get; set; }
        public string Error { get; set; }
    }
}
