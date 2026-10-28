namespace DXSuite.Connectors.Services.CoreApi.Models
{
    public class UserSettings
    {
        public bool? GATModify { get; set; } = false;
        public bool? ETModify { get; set; } = false;
        public bool? ATModify { get; set; } = false;
        public bool? CARModify { get; set; } = false;
        public bool? RCPModify { get; set; } = false;
        public bool? ROPModify { get; set; } = false;
        public bool? ROBModify { get; set; } = false;
        public bool? ROSModify { get; set; } = false;
        public bool? ROTModify { get; set; } = false;
        public bool? CodeSharesModify { get; set; } = false;
        public bool? PreserveAllOverridesOption { get; set; } = false;
    }
}
