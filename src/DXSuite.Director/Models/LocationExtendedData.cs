using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DXSuite.Director.Models
{
    public class LocationExtendedData
    {
        /// <summary>
        /// Captures any JSON properties not mapped to class fields (e.g. VideoWallConfiguration fields
        /// like "layout", "backgroundScaleFactor"). Without this, System.Text.Json silently drops unknown
        /// properties during deserialization, causing UpdateProperty round-trips to destroy them.
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }

        // Content tool availability for this location. Null defers to the system default
        // (media upload on, everything else off). Display-level settings win when set.
        public bool? EnableMediaUpload { get; set; }
        public bool? EnableWebContent { get; set; }
        public bool? EnableStream { get; set; }
        public bool? EnableCreateMedia { get; set; }
        public bool? EnableTemplateCreator { get; set; }

        /// <summary>
        /// Schedule surfaces: limit each schedule slot to one asset. Null defers to the system
        /// default (multiple). A display's own SingleAssetsOnly setting wins when set.
        /// </summary>
        public bool? SingleAssetsOnly { get; set; }

        // When the location runs an approval chain, replaced content re-enters approval and
        // the old file keeps playing until the replacement is approved
        public bool RequireApprovalOnOverwrite { get; set; } = false;

        // Playlist-only: controls whether Shuffle/Fade buttons are visible to regular users
        public bool ShowShuffleButton { get; set; } = true;
        public bool ShowFadeButton { get; set; } = true;
        // Opt-in: the Republish Content button only means something for Engage-player
        // locations, so deployments without Engage players never see it
        public bool ShowRepublishButton { get; set; } = false;
        // Opt-in: live player view is only meaningful for Engage players that report
        // playback (applet 1.2.0 or later)
        public bool ShowLiveView { get; set; } = false;
        // Playlist-only: how long each image displays (seconds); null means use the client default (7s)
        public int? ImageDurationSeconds { get; set; }
        public int? GlobalConstraintId { get; set; }
        public int? IconLibraryLocationId { get; set; }

        // VideoWall-only: how wall outputs apply content changes. Null/"standard" =
        // refresh on receipt of a change notice; "synchronized" = wait for the
        // announced activation instant so all outputs cut together (briefly holds
        // old and new content, so keep heavy browser-decoded video walls on standard).
        public string? WallTransitionMode { get; set; }

        // Asset Library only: generate a grayscale alpha mask alongside each uploaded image
        // for use as a Pixera luma key that floats content over underlying video layers
        public bool UseVideoMask { get; set; } = false;

        // Visual background mode: null/"image", "googlemap", "offlinemap", "3dmodel"
        public string? BackgroundMode { get; set; }

        // Map config (shared by Google Maps and Offline Map modes)
        public double? MapCenterLatitude { get; set; }
        public double? MapCenterLongitude { get; set; }
        public int? MapZoomLevel { get; set; }
        public string? MapStyle { get; set; }

        // Map bounds restriction (fencing)
        public double? MapBoundsNorth { get; set; }
        public double? MapBoundsSouth { get; set; }
        public double? MapBoundsEast { get; set; }
        public double? MapBoundsWest { get; set; }
        public int? MapMinZoom { get; set; }
        public int? MapMaxZoom { get; set; }

        // Offline map tile config
        public string? OfflineTilePath { get; set; }

        // 3D Model
        public string? ModelFilePath { get; set; }
        public string? ModelCameraJson { get; set; }
        public double? HotspotScale { get; set; }

        // Custom-bundle locations only: which uploaded location bundle this location renders
        public LocationBundles.LocationBundleAssignment? Bundle { get; set; }

        // Playlist video encoding
        // "off" (default) | "manual" | "lcd" | "best-effort"
        public string PlaylistEncodingMode { get; set; } = "off";
        // Profiles used when PlaylistEncodingMode == "manual"
        public List<EncodingProfile>? EncodingProfiles { get; set; }
        // Fallback bitrate (Kbps) for "lcd" and "best-effort" when no device info is available
        public int? EncodingFallbackBitrateKbps { get; set; }
    }

    public class ConstraintExtendedData
    {
        public bool? EnableMediaUpload { get; set; }
        public bool? EnableWebContent { get; set; }
        public bool? EnableStream { get; set; }
        public bool? EnableCreateMedia { get; set; }
        public bool? EnableTemplateCreator { get; set; }
    }
}
