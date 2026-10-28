namespace DXSuite.Connectors.Services.CoreApi.Models
{
    public partial class TblCarousel
    {
        public string CarouselId { get; set; } = null!;
        public int Terminal { get; set; }
        public int? SortOrder { get; set; }
        public bool Output { get; set; }
        public bool HideCarouselId { get; set; }
        public bool BeltRelayEnabled { get; set; }
        public string BeltRelayIp { get; set; } = null!;
        public string? RelayInputState { get; set; }
        public DateTime? RelayInputLastSync { get; set; }
        public DateTime? CreateDate { get; set; }
        public virtual ICollection<TblBaggageTerminal> TblBaggageTerminals { get; set; } = new List<TblBaggageTerminal>();
        public virtual ICollection<TblFlight> TblFlights { get; set; } = new List<TblFlight>();
    }
}