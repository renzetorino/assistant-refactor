using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using dataAccess.LLM;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace dataAccess.Reports
{
    // Dedicated runner for intent classification using the chat model
    public class YamlIntentRunner
    {
        private readonly GroqJsonClient _groq;
        private readonly string _intentYamlPath;

        public YamlIntentRunner(GroqJsonClient groq)
        {
            _groq = groq;
            // Path to router.intent.yaml (adjust if needed)
            _intentYamlPath = Path.Combine(AppContext.BaseDirectory, "Planning", "Prompts", "router.intent.yaml");
        }

        public async Task<JsonDocument> RunIntentAsync(string userText, CancellationToken ct)
        {
            var yaml = await File.ReadAllTextAsync(_intentYamlPath, ct);
            var des = new DeserializerBuilder().Build();
            var yamlObj = des.Deserialize<dynamic>(yaml);
            string systemPrompt = yamlObj["system"] ?? "You are a strict intent/domain classifier.";
            double temperature = 0.0;
            try
            {
                var tempObj = yamlObj["defaults"]?["model"]?["temperature"];
                if (tempObj != null)
                    temperature = Convert.ToDouble(tempObj);
            }
            catch { }

            using var doc = await _groq.CompleteJsonAsyncChat(systemPrompt, userText, null, temperature, ct);
            // Return the parsed JSON document
            return JsonDocument.Parse(doc.RootElement.GetRawText());
        }
    }
}
