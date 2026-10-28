namespace DXSuite.Director.Models
{
    /// <summary>
    /// Effective content-tool availability for a schedule surface. Display-level settings win when
    /// set, unset values fall back to the location, and the system default is media upload only.
    /// </summary>
    public class UploadTypeSettings
    {
        public bool MediaUpload { get; init; } = true;
        public bool WebContent { get; init; }
        public bool Stream { get; init; }
        public bool CreateMedia { get; init; }
        public bool TemplateCreator { get; init; }

        public bool AnyEnabled => MediaUpload || WebContent || Stream || CreateMedia || TemplateCreator;

        public static UploadTypeSettings Resolve(DisplayExtendedData? display, LocationExtendedData? location) => new()
        {
            MediaUpload = display?.EnableMediaUpload ?? location?.EnableMediaUpload ?? true,
            WebContent = display?.EnableWebContent ?? location?.EnableWebContent ?? false,
            Stream = display?.EnableStream ?? location?.EnableStream ?? false,
            CreateMedia = display?.EnableCreateMedia ?? location?.EnableCreateMedia ?? false,
            TemplateCreator = display?.EnableTemplateCreator ?? location?.EnableTemplateCreator ?? false,
        };

        public static bool ResolveSingleAssetsOnly(DisplayExtendedData? display, LocationExtendedData? location) =>
            display?.SingleAssetsOnly ?? location?.SingleAssetsOnly ?? false;
    }
}
