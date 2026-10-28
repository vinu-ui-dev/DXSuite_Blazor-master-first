namespace DXSuite.Director.Models
{
    public static class Types
    {
        public enum AssetType
        {
            Image = 0,
            Video = 1,
            Web = 4,
            Stream = 5
        }

        public enum DisplayMode
        {
            Schedule = 0,
            Override = 1,
            Off = 2,
            StatusSelector = 3
        }

        public enum DisplayType
        {
            StandardDisplay = 0,
            BackwallDisplay = 1,
            BackwallMask = 2
        }

        /// <summary>
        /// Represents the approval status of hotel-uploaded content
        /// </summary>
        public enum ApprovalStatus
        {
            /// <summary>Content is awaiting admin review</summary>
            Pending = 0,

            /// <summary>Content has been approved and can play on devices</summary>
            Approved = 1,

            /// <summary>Content has been declined and will not play on devices</summary>
            Declined = 2,

            /// <summary>Content was deleted while pending — removed from all approval queues</summary>
            Withdrawn = 3
        }
    }
}
