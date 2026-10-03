namespace MyHRExample.Models
{
    public class AnalysisViewModel
    {
        public string ResumeKey { get; set; } = "";
        public string CandidateName { get; set; } = "";
        public string Status { get; set; } = "";
        public List<string> Skills { get; set; } = new();
        public List<string> ConfirmedRequirements { get; set; } = new();
        public List<string> MissingRequirements { get; set; } = new();
        public string Summary { get; set; } = "";
        public int MatchPercent { get; set; }
        public DateTime AnalyzedAt { get; set; }
        public string? Error { get; set; }
    }
}
