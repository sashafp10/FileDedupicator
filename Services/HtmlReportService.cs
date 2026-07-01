using System.Text;
using System.Web;

namespace FilesDeduplicator.Services;

/// <summary>
/// Generates a self-contained static HTML report for a deduplication result.
/// Images/videos are embedded via file:// URIs — open locally in a browser.
/// No server required; no interactive file operations.
/// </summary>
public static class HtmlReportService
{
    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
    { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg", ".ico", ".tiff", ".tif", ".avif" };

    private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
    { ".mp4", ".webm", ".ogv" };

    private static readonly HashSet<string> VideoNoPreviewExts = new(StringComparer.OrdinalIgnoreCase)
    { ".mov", ".avi", ".mkv", ".wmv", ".flv", ".m4v", ".ts", ".3gp" };

    public static string Generate(DeduplicationResult result, string rootDirectory, string? outputPath = null)
    {
        rootDirectory = Path.GetFullPath(rootDirectory);
        outputPath ??= Path.Combine(
            Path.GetDirectoryName(rootDirectory) ?? rootDirectory,
            $"{Path.GetFileName(rootDirectory)}_duplicates_report.html");

        File.WriteAllText(outputPath, BuildHtml(result, rootDirectory), System.Text.Encoding.UTF8);
        return outputPath;
    }

    private static string BuildHtml(DeduplicationResult result, string rootDirectory)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.AppendLine($"<title>Duplicates \u2014 {HE(Path.GetFileName(rootDirectory))}</title>");
        sb.AppendLine(Css());
        sb.AppendLine("</head><body>");

        sb.AppendLine("<header>");
        sb.AppendLine("  <h1>Duplicates Report</h1>");
        sb.AppendLine("  <div class=\"meta\">");
        sb.AppendLine($"    <span>Root: <code>{HE(rootDirectory)}</code></span>");
        sb.AppendLine($"    <span>Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}</span>");
        sb.AppendLine($"    <span>Scanned: <b>{result.TotalScanned}</b> files</span>");
        sb.AppendLine($"    <span>Groups: <b>{result.Groups.Count}</b></span>");
        sb.AppendLine($"    <span>Duplicates: <b>{result.Moves.Count}</b></span>");
        sb.AppendLine("  </div>");
        sb.AppendLine("</header>");

        if (result.Groups.Count == 0)
        {
            sb.AppendLine("<div class=\"empty\">\u2705 No duplicates found.</div>");
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        int idx = 0;
        foreach (var group in result.Groups)
        {
            idx++;
            int total = 1 + group.DuplicatePaths.Count;
            sb.AppendLine("<section class=\"group\">");
            sb.AppendLine("  <div class=\"group-header\">");
            sb.AppendLine($"    <span class=\"gnum\">Group {idx}</span>");
            sb.AppendLine($"    <span class=\"gcount\">{total} identical file{(total > 1 ? "s" : "")}</span>");
            if (group.NamingWasAmbiguous)
                sb.AppendLine("    <span class=\"ambig\">\u26a0 kept by creation date</span>");
            sb.AppendLine("  </div>");
            sb.AppendLine("  <div class=\"cards\">");
            sb.Append(Card(group.KeptPath, rootDirectory, isKept: true));
            foreach (var d in group.DuplicatePaths)
                sb.Append(Card(d, rootDirectory, isKept: false));
            sb.AppendLine("  </div>");
            sb.AppendLine("</section>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string Card(string absPath, string rootDirectory, bool isKept)
    {
        var rel  = Path.GetRelativePath(rootDirectory, absPath);
        var ext  = Path.GetExtension(absPath);
        var info = File.Exists(absPath) ? new FileInfo(absPath) : null;
        var uri  = isKept 
            ? new Uri(absPath).AbsoluteUri
            : $"{rootDirectory}{Path.DirectorySeparatorChar}_duples{Path.DirectorySeparatorChar}{rel}";
            
        var name = Path.GetFileName(absPath);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"    <div class=\"card {(isKept ? "kept" : "dupe")}\">");
        sb.AppendLine($"      <div class=\"badge\">{(isKept ? "KEPT" : "DUPE")}</div>");

        sb.AppendLine("      <div class=\"preview\">");
        if (IsImage(ext))
        {
            sb.AppendLine($"        <img src=\"{HE(uri)}\" alt=\"{HE(name)}\" loading=\"lazy\"");
            sb.AppendLine("             onerror=\"this.replaceWith(Object.assign(document.createElement('div'),{className:'nopreview',textContent:'\ud83d\uddbc image not accessible'}))\">"); 
        }
        else if (IsVideoPreview(ext))
        {
            sb.AppendLine($"        <video src=\"{HE(uri)}\" controls muted preload=\"metadata\"></video>");
        }
        else if (IsVideoNoPreview(ext))
        {
            sb.AppendLine($"        <div class=\"nopreview\">\ud83c\udfa6 {HE(ext.TrimStart('.').ToUpperInvariant())} video</div>");
        }
        else
        {
            sb.AppendLine($"        <div class=\"nopreview\">\ud83d\udcc4 {HE(ext.TrimStart('.').ToUpperInvariant())} file</div>");
        }
        sb.AppendLine("      </div>");

        sb.AppendLine("      <div class=\"info\">");
        sb.AppendLine($"        <span class=\"fname\" title=\"{HE(absPath)}\">{HE(name)}</span>");
        sb.AppendLine($"        <span class=\"fpath\">{HE(rel)}</span>");
        if (info != null)
        {
            sb.AppendLine($"        <span class=\"fmeta\">{FormatSize(info.Length)}</span>");
            sb.AppendLine($"        <span class=\"fmeta\">Created {info.CreationTime:yyyy-MM-dd HH:mm}</span>");
            sb.AppendLine($"        <span class=\"fmeta\">Modified {info.LastWriteTime:yyyy-MM-dd HH:mm}</span>");
        }
        sb.AppendLine("      </div>");
        sb.AppendLine("    </div>");
        return sb.ToString();
    }

    private static string Css() => @"<style>
*,*::before,*::after{box-sizing:border-box;margin:0;padding:0}
body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;
  background:#0e0e14;color:#d4d4d8;padding:1.5rem 2rem;font-size:13px;line-height:1.5}
header{padding:1rem 1.25rem;background:#17172a;border-radius:8px;
  border-left:4px solid #7c3aed;margin-bottom:1.5rem}
header h1{font-size:1.5rem;color:#c4b5fd;margin-bottom:.4rem}
.meta{display:flex;flex-wrap:wrap;gap:.3rem 1.2rem;color:#888}
.meta b{color:#e4e4e7}
code{font-family:monospace;background:#1e1e30;padding:1px 5px;border-radius:3px;font-size:.85em}
.empty{padding:2rem;text-align:center;font-size:1.2rem;color:#4ade80;background:#0f1f0f;border-radius:8px}
.group{margin-bottom:2.5rem;border:1px solid #1e1e2e;border-radius:10px;overflow:hidden}
.group-header{display:flex;align-items:center;gap:.6rem 1.2rem;flex-wrap:wrap;
  padding:.55rem 1rem;background:#12122a;border-bottom:1px solid #1e1e2e}
.gnum{font-weight:700;color:#a78bfa}
.gcount{color:#666;font-size:.82rem}
.ambig{color:#fbbf24;font-size:.8rem;margin-left:auto}
.cards{display:flex;flex-wrap:wrap;gap:1px;background:#1a1a2e;padding:1px}
.card{flex:1 1 200px;max-width:300px;background:#111118;
  display:flex;flex-direction:column;gap:.5rem;padding:.75rem}
.card.kept{border-top:3px solid #22c55e}
.card.dupe{border-top:3px solid #ef4444}
.badge{display:inline-block;padding:.1rem .45rem;border-radius:4px;
  font-size:.7rem;font-weight:700;letter-spacing:.05em;width:fit-content;margin-bottom:.1rem}
.kept .badge{background:#14532d;color:#4ade80}
.dupe .badge{background:#450a0a;color:#f87171}
.preview{width:100%;min-height:110px;max-height:190px;background:#09090f;
  border-radius:5px;overflow:hidden;display:flex;align-items:center;justify-content:center}
.preview img{max-width:100%;max-height:190px;object-fit:contain;display:block}
.preview video{max-width:100%;max-height:190px;display:block}
.nopreview{color:#444;font-size:.82rem;text-align:center;padding:.8rem}
.info{display:flex;flex-direction:column;gap:.15rem;min-width:0}
.fname{font-weight:600;color:#e4e4e7;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.fpath{font-size:.78rem;color:#666;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.fmeta{font-size:.75rem;color:#555}
@media(max-width:520px){.card{flex:1 1 100%;max-width:100%}}
</style>";

    private static bool IsImage(string ext)          => ImageExts.Contains(ext);
    private static bool IsVideoPreview(string ext)   => VideoExts.Contains(ext);
    private static bool IsVideoNoPreview(string ext) => VideoNoPreviewExts.Contains(ext);
    private static string HE(string s)               => System.Web.HttpUtility.HtmlEncode(s);

    private static string FormatSize(long b) => b switch
    {
        < 1024                => $"{b} B",
        < 1024 * 1024         => $"{b / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{b / (1024.0 * 1024):F1} MB",
        _                     => $"{b / (1024.0 * 1024 * 1024):F2} GB"
    };
}
