using Google.GenAI;
using Google.GenAI.Types;

namespace MyHRExample.Services
{
    public class GeminiClient
    {
        private readonly Client _client;
        private readonly string[] _models;

        public GeminiClient(string apiKey, string models)
        {
            _client = new Client(null, null, apiKey);
            _models = models.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        public async Task<string> AnalyzeAsync(byte[] file, string mimeType, string prompt)
        {
            var contents = new Content
            {
                Role = "user",
                Parts = new List<Part>
                {
                    new Part { InlineData = new Blob { MimeType = mimeType, Data = file } },
                    new Part { Text = prompt }
                }
            };

            var config = new GenerateContentConfig
            {
                Temperature = 0.2,
                ResponseMimeType = "application/json"
            };

            Exception? lastError = null;

            foreach (var model in _models)
            {
                try
                {
                    var response = await _client.Models.GenerateContentAsync(
                        model: model,
                        contents: contents,
                        config
                    );

                    Console.WriteLine($"Gemini: відповідь від моделі {model}");
                    return response.Candidates[0].Content.Parts[0].Text;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Gemini ({model}): {ex.Message}");
                    lastError = ex;
                }
            }

            throw new Exception("Gemini API: " + lastError?.Message);
        }
    }
}