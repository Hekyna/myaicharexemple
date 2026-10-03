using Microsoft.AspNetCore.Mvc;
using MyHRExample.Services;

namespace MyHRExample.Controllers
{
    public class ResumesController : Controller
    {
        private readonly S3Service _s3Service;

        public ResumesController(S3Service s3Service)
        {
            _s3Service = s3Service;
        }

        public async Task<IActionResult> Index()
        {
            var resumes =
                await _s3Service.GetAllResumes();

            return View(resumes);
        }

        [HttpGet]
        public IActionResult Open(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return RedirectToAction(nameof(Index));
            }

            var url =
                _s3Service.GetPresignedUrl(key);

            return Redirect(url);
        }

        [HttpGet]
        public async Task<IActionResult> Analysis(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return RedirectToAction(nameof(Index));
            }

            var analysis = await _s3Service.GetAnalysisResultAsync(id);
            if (analysis == null)
            {
                TempData["ErrorMessage"] = "Результат аналізу ще не готовий або не знайдений.";
                return RedirectToAction(nameof(Index));
            }

            return View(analysis);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(
            string key,
            string metadataKey)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return RedirectToAction(nameof(Index));
            }

            await _s3Service.DeleteResume(
                key,
                metadataKey);

            TempData["SuccessMessage"] =
                "Резюме видалено.";

            return RedirectToAction(nameof(Index));
        }
    }
}