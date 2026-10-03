using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyHRExample.Services;

namespace MyHRExample.Controllers
{
    public class ClientController : Controller
    {
        private readonly S3Service _s3Service;

        public ClientController(S3Service s3Service)
        {
            _s3Service = s3Service;
        }

        public async Task<IActionResult> Index()
        {
            var vacancies = await _s3Service.GetVacancies();

            return View(vacancies);
        }

        [HttpGet]
        public async Task<IActionResult> Details(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var content =
                    await _s3Service.GetVacancyText(key);

                ViewBag.VacancyKey = key;
                ViewBag.VacancyText = content;

                return View();
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] =
                    ex.Message;

                return RedirectToAction(nameof(Index));
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Apply(
            IFormFile resume,
            string name,
            string email,
            string phone,
            string vacancyKey)
        {
            if (string.IsNullOrWhiteSpace(vacancyKey))
            {
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(phone))
            {
                TempData["ErrorMessage"] =
                    "Заповніть усі поля.";

                return RedirectToAction(
                    nameof(Details),
                    new { key = vacancyKey });
            }

            if (resume == null || resume.Length == 0)
            {
                TempData["ErrorMessage"] =
                    "Файл не був отриманий сервером.";

                return RedirectToAction(
                    nameof(Details),
                    new { key = vacancyKey });
            }

            var extension =
                Path.GetExtension(resume.FileName)
                    .ToLowerInvariant();

            if (extension != ".pdf" &&
                extension != ".txt")
            {
                TempData["ErrorMessage"] =
                    "Дозволені тільки PDF та TXT файли.";

                return RedirectToAction(
                    nameof(Details),
                    new { key = vacancyKey });
            }

            try
            {
                await _s3Service.UploadResume(
                    resume,
                    name,
                    email,
                    phone,
                    vacancyKey);

                TempData["SuccessMessage"] =
                    "Резюме успішно відправлено!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] =
                    ex.Message;
            }

            return RedirectToAction(
                nameof(Details),
                new { key = vacancyKey });
        }
    }
}