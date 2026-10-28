namespace DXSuite.Director.Models.Atrius
{
    public class ResyncResponse
    {
        public DateTime? LastSync { get; set; } = DateTime.Now;
        public string? Status { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
