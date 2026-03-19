using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace LogicAppProcessor.Services
{
    public class MessageIdService : IMessageIdService
    {
        private readonly ILogger<MessageIdService> _logger;

        public MessageIdService(ILogger<MessageIdService> logger)
        {
            _logger = logger;
        }

        public string ComputeMessageId(string rawPayload)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(rawPayload))
                {
                    _logger.LogWarning("Empty payload provided for message ID computation");
                    throw new ArgumentException("Payload cannot be empty", nameof(rawPayload));
                }

                // Deterministic key: derive business key from payload (e.g., CustomerAccountNumber) when available.
                string businessKey = ExtractBusinessKey(rawPayload) ?? "unknown";

                // Use SHA1 of raw payload for etag/hash
                using (var sha1 = SHA1.Create())
                {
                    var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(rawPayload));
                    string hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    var messageId = businessKey + ":" + hex;
                    _logger.LogInformation($"Computed message ID: {messageId.Substring(0, Math.Min(50, messageId.Length))}...");
                    return messageId;
                }
            }
            catch (Exception ex) when (ex is not ArgumentException)
            {
                _logger.LogError(ex, "Error computing message ID");
                throw new InvalidOperationException("Failed to compute message ID", ex);
            }
        }

        private string ExtractBusinessKey(string raw)
        {
            try
            {
                var j = JObject.Parse(raw);
                if (j["CustomerAccountNumber"] != null) 
                {
                    var key = j["CustomerAccountNumber"].ToString();
                    _logger.LogDebug($"Extracted business key from CustomerAccountNumber: {key}");
                    return key;
                }
                if (j["VendorId"] != null) 
                {
                    var key = j["VendorId"].ToString();
                    _logger.LogDebug($"Extracted business key from VendorId: {key}");
                    return key;
                }
                
                _logger.LogDebug("No business key found in payload, using 'unknown'");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse JSON when extracting business key");
            }
            return null;
        }
    }
}
