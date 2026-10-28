namespace DXSuite.Director.Models
{
    public class FfmpegOptions
    {
        // Folder containing ffmpeg.exe / ffprobe.exe. "Path" is canonical; "BinaryPath"
        // is accepted as a legacy alias (it is the key FFMpegCore's GlobalFFOptions uses).
        public string Path { get; set; } = "";
        public string BinaryPath { get; set; } = "";

        // Configured folder, preferring Path then BinaryPath. Empty => rely on system PATH.
        public string BinaryFolder =>
            !string.IsNullOrWhiteSpace(Path) ? Path : BinaryPath;

        // Absolute folder for the ffmpeg binaries. Relative values are resolved against the
        // app content root (a bare relative path would otherwise resolve against the process
        // working directory, which differs under IIS and breaks ffprobe). Empty => PATH lookup.
        public string ResolveFolder(string contentRoot)
        {
            var folder = BinaryFolder;
            if (string.IsNullOrWhiteSpace(folder))
                return "";
            return System.IO.Path.IsPathRooted(folder)
                ? folder
                : System.IO.Path.Combine(contentRoot, folder);
        }

        // Full path to a named binary (e.g. "ffprobe.exe"). Falls back to the bare name so
        // Process.Start / FFMpegCore can resolve it via PATH when no folder is configured.
        public string ResolveBinary(string exeName, string contentRoot)
        {
            var folder = ResolveFolder(contentRoot);
            return string.IsNullOrEmpty(folder)
                ? exeName
                : System.IO.Path.Combine(folder, exeName);
        }
    }
}
