using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Http;
using System.Text;
using System.Text.Json;
using MyHRExample.Models;
using Google.GenAI;

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

            // ЗАПУСК АНАЛИЗА GEMINI (gemini-3.5-flash) И СОХРАНЕНИЕ В S3
            await AnalyzeAndSaveGeminiResultAsync(resume, vacancyKey, id);

            return key;
        }

        private async Task AnalyzeAndSaveGeminiResultAsync(IFormFile resume, string vacancyKey, string candidateId)
        {
            try
            {
                // Читаем текст вакансии из S3
                string vacancyText = await GetVacancyText(vacancyKey);

                // Читаем текст резюме из потока загруженного файла
                string resumeText = string.Empty;
                using (var reader = new StreamReader(resume.OpenReadStream()))
                {
                    resumeText = await reader.ReadToEndAsync();
                }

                // Инициализируем клиент Gemini
                var client = new Client(null, null, "");

                // Отправляем запрос модели с использованием версии 3.5-flash
                var response = await client.Models.GenerateContentAsync(
                    model: "gemini-3.5-flash",
                    contents: $"Ви — досвідчений HR-спеціаліст. Проаналізуй кандидата з резюме під вимоги з вакансії та поверни результат СУВОРО у форматі JSON (без додаткового тексту і без markdown-розмітки, просто чистий JSON), який відповідає таким полям класу GeResponse:\n" +
                              $"CandidateName, CandidateSurname, CandidateBirthDate, Skills (масив рядків), CandidateMatchPercentage (число), CandidateExperience, CandidateDescription.\n\n" +
                              $"Вакансія:\n{vacancyText}\n\nРезюме:\n{resumeText}"
                );

                string rawText = response.Candidates[0].Content.Parts[0].Text.Trim();

                // Очистка от markdown-оберток ```json если они присутствуют в ответе
                if (rawText.StartsWith("```"))
                {
                    int firstNewline = rawText.IndexOf('\n');
                    int lastBackticks = rawText.LastIndexOf("```");
                    if (firstNewline != -1 && lastBackticks != -1 && lastBackticks > firstNewline)
                    {
                        rawText = rawText.Substring(firstNewline + 1, lastBackticks - firstNewline - 1).Trim();
                    }
                }

                // Сохранение готового JSON-результата анализа в S3 бакет в папку analysis/
                using var analysisStream = new MemoryStream(Encoding.UTF8.GetBytes(rawText));
                var analysisRequest = new PutObjectRequest
                {
                    BucketName = _bucketName,
                    Key = $"analysis/{candidateId}.json",
                    InputStream = analysisStream,
                    ContentType = "application/json"
                };

                await _s3.PutObjectAsync(analysisRequest);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка аналізу Gemini: {ex.Message}");
            }
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