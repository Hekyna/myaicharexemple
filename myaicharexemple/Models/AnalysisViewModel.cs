namespace MyHRExample.Models
{
    public class AnalysisViewModel
    {
        public string CandidateName { get; set; }
        public string CandidateSurname { get; set; }
        public string CandidateBirthDate { get; set; }
        public List<string> Skills { get; set; }
        public double CandidateMatchPercentage { get; set; }
        public string CandidateExperience { get; set; }
        public string CandidateDescription { get; set; }
    }
}