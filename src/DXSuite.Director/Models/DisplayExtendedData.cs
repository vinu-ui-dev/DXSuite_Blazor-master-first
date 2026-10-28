namespace DXSuite.Director.Models
{
    public class DisplayExtendedData
    {
        public List<EncodingProfile>? EncodingProfiles { get; set; }

        /// <summary>
        /// When true, each schedule slot accepts only one asset. Dragging a second asset
        /// replaces the current one rather than appending to the list. Duration editing is hidden.
        /// Intended for backgrounds and single-asset video wall zones.
        /// Null defers to the location setting, then the system default (multiple).
        /// </summary>
        public bool? SingleAssetsOnly { get; set; }

        // Whether schedule editors expose the fade toggle to users. Defaults off
        // for displays (unlike playlist locations, where it defaults on).
        public bool ShowFadeButton { get; set; }

        // Content tool availability for this display's schedule surface.
        // Null defers to the location setting, then the system default (media upload on, everything else off).
        public bool? EnableMediaUpload { get; set; }
        public bool? EnableWebContent { get; set; }
        public bool? EnableStream { get; set; }
        public bool? EnableCreateMedia { get; set; }
        public bool? EnableTemplateCreator { get; set; }
    }
}
