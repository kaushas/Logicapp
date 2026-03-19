namespace LogicAppProcessor.Models
{
    public class StandardEvent
    {
        public string eventType { get; set; }
        public string subject { get; set; }
        public string source { get; set; }
        public string eventTime { get; set; }
        public string schemaVersion { get; set; }
        public string contentType { get; set; }
        public string correlationId { get; set; }
        public object data { get; set; }
    }
}
