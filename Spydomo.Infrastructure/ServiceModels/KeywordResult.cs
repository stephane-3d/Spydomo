namespace Spydomo.Infrastructure.ServiceModels
{
    public class KeywordResult
    {
        public string Keyword { get; set; } = "";
        public string Reason { get; set; } = "";
        public double Confidence { get; set; } = 10.0;
    }

}
