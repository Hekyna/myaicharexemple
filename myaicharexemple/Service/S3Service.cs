using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Http;
using System.Text;
using System.Text.Json;
using MyHRExample.Models;

namespace MyHRExample.Services
{
    public class S3Service
    {
        private readonly AmazonS3Client _s3;
        private readonly string _bucketName = "mys3bucket12321341";

        public S3Service()
        {
            _s3 = new AmazonS3Client(
                "",
                "",
                RegionEndpoint.EUCentral1);
        }

        public async Task UploadRequirements(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return;

            using var stream = file.OpenReadStream();

            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = $"vacancies/{file.FileName}",
                InputStream = stream,
                ContentType = file.ContentType
            };

            await _s3.PutObjectAsync(request);
        }

        public async Task UploadText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var fileName = $"vacancy-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.txt";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));

            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = $"vacancies/{fileName}",
                InputStream = stream,
                ContentType = "text/plain"
            };

            await _s3.PutObjectAsync(request);
        }

        public async Task<List<string>> GetVacancies()
        {
            var vacancies = new List<string>();

            var request = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = "vacancies/"
            };

            var response = await _s3.ListObjectsV2Async(request);

            foreach (var item in response.S3Objects)
            {
                if (item.Key.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                {
                    vacancies.Add(item.Key);
                }
            }

            return vacancies;
        }

        public async Task<string> GetVacancyText(string key)
        {
            var request = new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = key
            };

            using var response = await _s3.GetObjectAsync(request);
            using var reader = new StreamReader(response.ResponseStream);

            return await reader.ReadToEndAsync();
        }

        public async Task<string> UploadResume(
            IFormFile resume,
            string candidateName,
            string email,
            string phone,
            string vacancyKey)
        {
            if (resume == null || resume.Length == 0)
                throw new Exception("Файл не вибрано.");

            var extension = Path.GetExtension(resume.FileName).ToLowerInvariant();

            if (extension != ".pdf" && extension != ".txt")
            {
                throw new Exception("Дозволені тільки PDF та TXT.");
            }

            var id = Guid.NewGuid().ToString("N");
            var fileName = $"{id}{extension}";
            var key = $"resumes/{fileName}";

            var contentType = extension == ".pdf" ? "application/pdf" : "text/plain";

            using var stream = resume.OpenReadStream();

            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = key,
                InputStream = stream,
                ContentType = contentType
            };

            await _s3.PutObjectAsync(request);

            var metadata = new ResumeViewModel
            {
                Id = id,
                CandidateName = candidateName,
                Email = email,
                Phone = phone,
                FileName = resume.FileName,
                UploadedAt = DateTime.UtcNow,
                VacancyKey = vacancyKey,
                S3Key = key
            };

            var json = JsonSerializer.Serialize(
                metadata,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

            using var metadataStream = new MemoryStream(Encoding.UTF8.GetBytes(json));

            var metadataRequest = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = $"resumes/{id}.json",
                InputStream = metadataStream,
                ContentType = "application/json"
            };

            await _s3.PutObjectAsync(metadataRequest);

            await AnalyzeAndSaveGeminiResultAsync(resume, candidateName, vacancyKey, id, key);

            return key;
        }

        private static readonly JsonSerializerOptions AnalysisJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly GeminiClient _gemini = new(
            Environment.GetEnvironmentVariable("") ?? "",
            Environment.GetEnvironmentVariable("gemini-3.5-flash") ?? "gemini-flash-latest,gemini-3.6-flash,gemini-3.5-flash,gemini-3.5-flash-lite");

        private async Task AnalyzeAndSaveGeminiResultAsync(
            IFormFile resume,
            string candidateName,
            string vacancyKey,
            string candidateId,
            string resumeKey)
        {
            var analysis = new AnalysisViewModel
            {
                ResumeKey = resumeKey,
                CandidateName = candidateName,
                AnalyzedAt = DateTime.UtcNow
            };

            try
            {
                var vacancyText = await GetVacancyText(vacancyKey);

                using var memory = new MemoryStream();
                await resume.CopyToAsync(memory);

                var mimeType = Path.GetExtension(resume.FileName).ToLowerInvariant() == ".pdf"
                    ? "application/pdf"
                    : "text/plain";

                var answer = await _gemini.AnalyzeAsync(memory.ToArray(), mimeType, BuildPrompt(vacancyText));
                var result = JsonSerializer.Deserialize<AnalysisViewModel>(answer, AnalysisJsonOptions) ?? new AnalysisViewModel();

                analysis.Skills = result.Skills ?? new();
                analysis.ConfirmedRequirements = result.ConfirmedRequirements ?? new();
                analysis.MissingRequirements = result.MissingRequirements ?? new();
                analysis.Summary = result.Summary;
                analysis.MatchPercent = result.MatchPercent;
                analysis.Status = "Completed";
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                analysis.Status = "Failed";
                analysis.Error = ex.Message;
            }

            try
            {
                await _s3.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = _bucketName,
                    Key = $"analysis/{candidateId}.json",
                    ContentBody = JsonSerializer.Serialize(analysis, AnalysisJsonOptions),
                    ContentType = "application/json"
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Не вдалося зберегти аналіз: {ex.Message}");
            }
        }

        private static string BuildPrompt(string vacancyText)
        {
            return
                "Ти HR-асистент. Проаналізуй резюме кандидата у доданому файлі і порівняй його з вакансією.\n\n" +
                "Вакансія та вимоги:\n" + vacancyText + "\n\n" +
                "Поверни лише JSON такого вигляду:\n" +
                "{\n" +
                "  \"skills\": [\"навички, знайдені в резюме\"],\n" +
                "  \"confirmedRequirements\": [\"вимоги вакансії, які підтверджені резюме\"],\n" +
                "  \"missingRequirements\": [\"вимоги вакансії, для яких у резюме не знайдено інформації\"],\n" +
                "  \"summary\": \"короткий висновок про кандидата, 2-3 речення українською\",\n" +
                "  \"matchPercent\": 0\n" +
                "}\n" +
                "matchPercent — ціле число від 0 до 100, наскільки кандидат відповідає вимогам. " +
                "Не вигадуй інформацію, якої немає в резюме.";
        }

        public async Task<AnalysisViewModel> GetAnalysisResultAsync(string candidateId)
        {
            try
            {
                var request = new GetObjectRequest
                {
                    BucketName = _bucketName,
                    Key = $"analysis/{candidateId}.json"
                };

                using var response = await _s3.GetObjectAsync(request);
                using var reader = new StreamReader(response.ResponseStream);
                var json = await reader.ReadToEndAsync();

                return JsonSerializer.Deserialize<AnalysisViewModel>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch
            {
                return null;
            }
        }

        public async Task<List<ResumeViewModel>> GetAllResumes()
        {
            var result = new List<ResumeViewModel>();

            var request = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = "resumes/"
            };

            var response = await _s3.ListObjectsV2Async(request);

            foreach (var item in response.S3Objects)
            {
                if (!item.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var getRequest = new GetObjectRequest
                    {
                        BucketName = _bucketName,
                        Key = item.Key
                    };

                    using var responseObject = await _s3.GetObjectAsync(getRequest);
                    using var reader = new StreamReader(responseObject.ResponseStream);

                    var json = await reader.ReadToEndAsync();
                    var resume = JsonSerializer.Deserialize<ResumeViewModel>(json);

                    if (resume != null)
                    {
                        result.Add(resume);
                    }
                }
                catch
                {
                }
            }

            return result.OrderByDescending(x => x.UploadedAt).ToList();
        }

        public string GetPresignedUrl(string key)
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = _bucketName,
                Key = key,
                Expires = DateTime.UtcNow.AddMinutes(10),
                Verb = HttpVerb.GET
            };

            return _s3.GetPreSignedURL(request);
        }

        public async Task DeleteResume(string resumeKey, string metadataKey)
        {
            if (!string.IsNullOrWhiteSpace(resumeKey))
            {
                await _s3.DeleteObjectAsync(new DeleteObjectRequest
                {
                    BucketName = _bucketName,
                    Key = resumeKey
                });
            }

            if (!string.IsNullOrWhiteSpace(metadataKey))
            {
                await _s3.DeleteObjectAsync(new DeleteObjectRequest
                {
                    BucketName = _bucketName,
                    Key = metadataKey
                });
            }
        }
    }
}