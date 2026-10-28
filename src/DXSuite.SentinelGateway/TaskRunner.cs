using System.Diagnostics;
using System.Text;

namespace DXSuite.SentinelGateway;

/// <summary>Result of running a Task script on the host.</summary>
public record TaskRunResult(bool Success, int? ExitCode, string Stdout, string Stderr, string? Error);

/// <summary>
/// Executes a Task script on the local host by writing it to a temp file and running the
/// matching interpreter (PowerShell / cmd on Windows, bash on Linux). Captures stdout/stderr
/// (size-capped) and the exit code, enforces a hard timeout (kills the process tree on overrun),
/// and never throws — failures come back as a <see cref="TaskRunResult"/>.
/// </summary>
public static class TaskRunner
{
    private const int MaxOutputChars = 64 * 1024;

    public static async Task<TaskRunResult> RunAsync(string scriptType, string scriptContent, int timeoutMs, CancellationToken cancellationToken)
    {
        string ext, fileName, argsFormat;
        switch (scriptType.ToLowerInvariant())
        {
            case "cmd":
                ext = ".cmd"; fileName = "cmd.exe"; argsFormat = "/c \"{0}\""; break;
            case "bash":
                ext = ".sh"; fileName = "bash"; argsFormat = "\"{0}\""; break;
            case "powershell":
            default:
                ext = ".ps1"; fileName = "powershell.exe";
                argsFormat = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{0}\""; break;
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"sentinel-task-{Guid.NewGuid():N}{ext}");
        try
        {
            await File.WriteAllTextAsync(tempPath, scriptContent, cancellationToken);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = string.Format(argsFormat, tempPath),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null && stdout.Length < MaxOutputChars) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null && stderr.Length < MaxOutputChars) stderr.AppendLine(e.Data); };

            try { process.Start(); }
            catch (Exception ex) { return new TaskRunResult(false, null, "", "", $"Failed to start {fileName}: {ex.Message}"); }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeoutMs);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
                return new TaskRunResult(false, null, stdout.ToString(), stderr.ToString(), $"Script timed out after {timeoutMs} ms");
            }

            var exit = process.ExitCode;
            return new TaskRunResult(exit == 0, exit, stdout.ToString(), stderr.ToString(),
                exit == 0 ? null : $"Exited with code {exit}");
        }
        catch (Exception ex)
        {
            return new TaskRunResult(false, null, "", "", ex.Message);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best effort cleanup */ }
        }
    }
}
