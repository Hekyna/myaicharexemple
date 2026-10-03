namespace MyHRExample.Models
{
    public class ResumeViewModel
    {
        public string Id { get; set; } = "";

        public string CandidateName { get; set; } = "";

        public string Email { get; set; } = "";

        public string Phone { get; set; } = "";

        public string FileName { get; set; } = "";

        public DateTime UploadedAt { get; set; }

        public string VacancyKey { get; set; } = "";

        public string S3Key { get; set; } = "";
    }
}