using LogicAppProcessor.Models;

namespace LogicAppProcessor.Services
{
    public interface ILiquidMapper
    {
        StandardEvent MapToCanonical(string templateName, string rawPayload);
    }
}
