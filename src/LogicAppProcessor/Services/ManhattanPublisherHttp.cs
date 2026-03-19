using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using LogicAppProcessor.Models;
using LogicAppProcessor.Repositories;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace LogicAppProcessor.Services
{
    public class ManhattanPublisherHttp : IManhattanPublisher
    {
        private readonly HttpClient _http;
        private readonly IOutboxRepository _outbox;
        private readonly ILogger<ManhattanPublisherHttp> _logger;

        public ManhattanPublisherHttp(HttpClient http, IOutboxRepository outbox, ILogger<ManhattanPublisherHttp> logger)
        {
            _http = http;
            _outbox = outbox;
            _logger = logger;
        }

        public async Task PublishAsync(StandardEvent evt)
        {
            // Serialize payload and save to outbox
            var payload = JsonConvert.SerializeObject(evt.data);
            var outboxEntity = new Repositories.Entities.OutboxEntity
            {
                MessageId = evt.correlationId ?? Guid.NewGuid().ToString(),
                Destination = "ManhattanPubSub",
                Payload = payload
            };

            await _outbox.SaveAsync(outboxEntity);

            try
            {
                // Acquire JWT from auth stub
                var tokenResp = await _http.GetAsync(Environment.GetEnvironmentVariable("JWT_STUB_URL") ?? "http://localhost:7071/api/generate-token");
                string token = null;
                if (tokenResp.IsSuccessStatusCode)
                {
                    var tokenObj = JsonConvert.DeserializeObject<dynamic>(await tokenResp.Content.ReadAsStringAsync());
                    token = (string)tokenObj.token;
                }

                var publishUrl = Environment.GetEnvironmentVariable("MANHATTAN_PUBLISH_URL") ?? "https://pubsub.googleapis.com/v1/projects/batf-masc-prod-01-ops/topics/HST_XNT_Facility_GCPQ:publish";

                var msg = new { messages = new[] { new { data = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)), attributes = new { MSG_ID_PK = outboxEntity.MessageId } } } };
                var content = new StringContent(JsonConvert.SerializeObject(msg), Encoding.UTF8, "application/json");

                if (!string.IsNullOrEmpty(token))
                    _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var resp = await _http.PostAsync(publishUrl, content);

                if ((int)resp.StatusCode == 400 || (int)resp.StatusCode == 500)
                {
                    // Non-retryable per requirement — mark failed and write to error handling upstream
                    await _outbox.MarkFailedAsync(outboxEntity.Id, $"HTTP {resp.StatusCode}: {await resp.Content.ReadAsStringAsync()}");
                    _logger.LogError("Non-retryable publish error: {code}", resp.StatusCode);
                    return;
                }

                resp.EnsureSuccessStatusCode();

                await _outbox.MarkSentAsync(outboxEntity.Id);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Transient HTTP error publishing to Manhattan");
                // Let caller handle transient retry semantics, outbox contains the payload
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error publishing to Manhattan");
                await _outbox.MarkFailedAsync(outboxEntity.Id, ex.Message);
                throw;
            }
        }
    }
}
