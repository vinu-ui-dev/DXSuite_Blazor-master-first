namespace DXSuite.Connectors.Services.CoreApi.Models
{
    public partial class TblBaggageTerminal
    {
        public string DeviceId { get; set; } = null!;
        public string? Description { get; set; }
        public string? CarouselId { get; set; }
        public string Orientation { get; set; } = null!;
        public bool? ShowHelpMessaging { get; set; }
        public string HelpMessageStyle { get; set; } = null!;
        public bool? ShowMetrics { get; set; }
        public bool? ShowWeather { get; set; }
        public DateTime LastSync { get; set; }
        public bool PulseEnabled { get; set; }
        public DateTime CreateDate { get; set; }
        public virtual TblCarousel? Carousel { get; set; }
    }
}