using DXSuite.Director.Entities;
using DXSuite.Director.Services;
using DXSuite.Shared.Authorization;

namespace DXSuite.Director.Models
{
    public class MediaUploadConfig
    {
        public string UploadFolder { get; set; }
        public int LocationId { get; set; }
        public int ConstraintId { get; set; }
        public double? ExpectedWidth { get; set; }
        public double? ExpectedHeight { get; set; }
        public string ResolutionMode { get; set; } = "aspect-ratio";
        public MediaFile? EditMediaFile { get; set; }
        public bool AddToSql { get; set; } = false;

        /// <summary>
        /// When true, the upload is written to the media store and staged only — no TblAsset row, no approval
        /// record. The caller (e.g. a Message's ASL video slot) owns deciding when, or whether, to promote it.
        /// </summary>
        public bool StageOnly { get; set; } = false;
        public bool IsSchedulable { get; set; } = false;
        public bool LanguageMetadata { get; set; } = false;
        public bool VideoMask { get; set; } = false;
        public TblEndpoint? Endpoint { get; set; }
        public MediaFileUploadMetadata? FormData { get; set; }

        // Playlist rotation preview inside the uploader: the caller supplies the current playlist
        // so the not-yet-submitted upload can be previewed in context
        public bool EnablePlaylistPreview { get; set; } = false;
        public List<MediaFile>? PlaylistFiles { get; set; }
        public int? ImageDurationSeconds { get; set; }
        public Dictionary<string, string> AdditionalMetadata { get; set; } = new();
        public string? TargetFileName { get; set; }
        /// <summary>
        /// When true, hotel user uploads will create approval records and require admin approval
        /// </summary>
        public bool EnableContentApproval { get; set; } = false;

        /// <summary>
        /// The current user's resolved access for this location (metadata-driven access control).
        /// When set, enables auto-tagging on upload and forces SQL storage for partial-access users.
        /// </summary>
        public ResolvedAccess? UserAccess { get; set; }

        /// <summary>
        /// The "Uploading as" pick, kept on the config so an upload resumed after navigating away comes back
        /// with it. Candidates are not kept: they are read again when the uploader opens, and the pick is
        /// re-checked at write time.
        /// </summary>
        public DXSuite.Director.Services.UploadCandidate? UploadingAs { get; set; }

        /// <summary>The participating-field values picked alongside <see cref="UploadingAs"/>.</summary>
        public Dictionary<string, List<string>>? UploadPicks { get; set; }

        /// <summary>
        /// When set, the uploader is re-submitting a declined asset. Shows comment thread
        /// and requires a resubmit comment instead of creating a new approval record.
        /// </summary>
        public TblAssetApproval? ExistingApproval { get; set; }

        /// <summary>
        /// When set, the uploader checks this display's encoding profiles against the uploaded
        /// video and offers re-encoding if a profile threshold is exceeded.
        /// </summary>
        public int? DisplayId { get; set; }
    }

}
