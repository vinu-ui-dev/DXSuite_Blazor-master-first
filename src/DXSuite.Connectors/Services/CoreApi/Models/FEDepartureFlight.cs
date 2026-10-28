using System.ComponentModel.DataAnnotations;
namespace DXSuite.Connectors.Services.CoreApi.Models
{
    public class FEDepartureFlight : IRollFlags
    {
        public int RecordId { get; set; }
        public string? Type { get; set; }
        public List<FECodeShare> CodeShares { get; set; } = new();
        public string? FLT { get; set; }
        [Required(ErrorMessage = "This field is required.")]
        public string? FLC { get; set; }
        [Required(ErrorMessage = "This field is required.")]
        [RegularExpression(@"^\d+$", ErrorMessage = "Invalid value only Numbers allowed.")]
        public string? FLN { get; set; }
        public string? Carrier { get; set; }
        [Required(ErrorMessage = "This field is required.")]
        public string? DES { get; set; }
        public string? CTY { get; set; }
        public string? GAT { get; set; }
        [Required(ErrorMessage = "This field is required.")]
        public DateTime? ScheduledTime { get; set; }
        [Required(ErrorMessage = "This field is required.")]
        public DateTime? EstimatedTime { get; set; }
        [Required(ErrorMessage = "This field is required.")]
        public DateTime? ActualTime { get; set; }
        [Required(ErrorMessage = "This field is required.")]
        public string? RCP { get; set; }
        public string? ROP { get; set; }
        public string? ROB { get; set; }
        public string? ROS { get; set; }
        public string? ROT { get; set; }
        public OverrideDepartureFlight? overrideFlightData { get; set; }
        public bool? preserveOverride { get; set; }

    }

}
