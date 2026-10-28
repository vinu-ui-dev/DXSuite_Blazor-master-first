namespace DXSuite.Director.Models
{
    /// <summary>
    /// Config section "WebPreview". Metadata-based thumbnails (og:image/favicon) are always attempted
    /// for web assets; full-page capture via a headless Chromium/Edge is opt-in per deployment.
    /// </summary>
    public class WebPreviewOptions
    {
        /// <summary>Enables headless-browser page capture. Requires Edge/Chromium on the host.</summary>
        public bool CaptureEnabled { get; set; }

        /// <summary>Browser executable path. Null auto-detects a system Edge or Chrome installation.</summary>
        public string? BrowserPath { get; set; }

        public int CaptureTimeoutSeconds { get; set; } = 30;
        public int ViewportWidth { get; set; } = 1280;
        public int ViewportHeight { get; set; } = 720;

        /// <summary>
        /// Disables the outbound SSRF guard entirely, allowing previews of loopback, link-local,
        /// and private-range hosts. Intended for single-tenant on-prem installs whose signage
        /// content lives on the intranet; must stay false in shared/cloud deployments, where
        /// setting it disables headless capture rather than widening it.
        /// </summary>
        public bool AllowPrivateNetworks { get; set; }

        /// <summary>
        /// Narrow exemptions from the SSRF guard: exact hostnames ("dash.corp.local"),
        /// wildcard suffixes ("*.corp.local"), or CIDR ranges ("10.20.0.0/16").
        /// </summary>
        public string[] OutboundAllowList { get; set; } = [];
    }
}
