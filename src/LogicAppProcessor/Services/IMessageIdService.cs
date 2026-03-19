namespace LogicAppProcessor.Services
{
    public interface IMessageIdService
    {
        string ComputeMessageId(string rawPayload);
    }
}
