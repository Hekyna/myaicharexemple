using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MyHRExample.Services;

namespace MyHRExample.Controllers
{
    public class HomeController : Controller
    {
        private readonly S3Service _s3Service;

        public HomeController()
        {
            _s3Service = new S3Service();
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> UploadRequirements(
            IFormFile file,
            string text)
        {
            try
            {
                // Загружаем текст вакансии
                if (!string.IsNullOrWhiteSpace(text))
                {
                    await _s3Service.UploadText(text);
                }

                // Загружаем выбранный файл
                if (file != null && file.Length > 0)
                {
                    await _s3Service.UploadRequirements(file);
                }

                TempData["Success"] =
                    "Вакансія успішно завантажена в AWS S3.";
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);

                TempData["Error"] =
                    "Помилка завантаження в AWS S3.";
            }

            return RedirectToAction("Index");
        }
    }
}