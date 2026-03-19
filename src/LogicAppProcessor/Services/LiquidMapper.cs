using DotLiquid;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using LogicAppProcessor.Models;
using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace LogicAppProcessor.Services
{
    public class LiquidMapper : ILiquidMapper
    {
        private readonly string _templatesPath;
        private readonly ILogger<LiquidMapper> _logger;

        public LiquidMapper(ILogger<LiquidMapper> logger)
        {
            _logger = logger;
            // Externalize templates under logic app artifacts path
            _templatesPath = Path.Combine(Directory.GetCurrentDirectory(), "LogicApp", "sktestlogicapp", "Artifacts", "Maps");
            _logger.LogInformation($"LiquidMapper initialized with templates path: {_templatesPath}");
        }

        public StandardEvent MapToCanonical(string templateName, string rawPayload)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(templateName))
                {
                    _logger.LogWarning("Template name is empty, using default customerToCanonical.liquid");
                    templateName = "customerToCanonical.liquid";
                }

                string file = Path.Combine(_templatesPath, templateName);
                if (!File.Exists(file))
                {
                    _logger.LogWarning($"Template {templateName} not found at {file}, falling back to customerToCanonical.liquid");
                    file = Path.Combine(_templatesPath, "customerToCanonical.liquid");
                    
                    if (!File.Exists(file))
                    {
                        throw new FileNotFoundException($"Default template customerToCanonical.liquid not found at {file}");
                    }
                }

                _logger.LogInformation($"Mapping with template: {templateName}");

                var templateContent = File.ReadAllText(file);
                var template = Template.Parse(templateContent);

                var payload = JObject.Parse(rawPayload);
                var hash = Hash.FromDictionary(new System.Collections.Generic.Dictionary<string, object>
                {
                    { "content", payload }
                });

                var result = template.Render(hash);

                var standardEvent = JsonConvert.DeserializeObject<StandardEvent>(result);
                _logger.LogInformation($"Successfully mapped to StandardEvent: {standardEvent?.eventType}");

                return standardEvent;
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, $"Failed to parse JSON payload or template result for template {templateName}");
                throw new InvalidOperationException("Invalid JSON format in payload or mapping result", jsonEx);
            }
            catch (FileNotFoundException fnfEx)
            {
                _logger.LogError(fnfEx, $"Template file not found: {templateName}");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unexpected error mapping payload with template {templateName}");
                throw new InvalidOperationException($"Failed to map payload with template {templateName}", ex);
            }
        }
    }
}
