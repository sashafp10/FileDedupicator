namespace FilesDeduplicator.Utils;

/// <summary>
/// Writes a throttled "X / Y [pct%]" progress line to stdout using \r so it stays on
/// the same console line between ticks. File-move events are printed on permanent
/// new lines so they are not erased.
///
/// Update cadence: the progress counter is redrawn at most once per second.
/// File-move lines are always printed immediately.
///
/// On redirected output (pipe / file) the \r trick is skipped and plain lines are written.
/// </summary>
public sealed class ProgressReporter : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly int    _total;
    private readonly string _label;
    private int             _current;
    private DateTime        _lastPrint = DateTime.MinValue;
    private bool            _progressLineActive;

    public ProgressReporter(int total, string label = "Processing")
    {
        _total = total;
        _label = label;
    }

    /// <summary>
    /// Advance the counter by one.
    /// <paramref name="movedFile"/> – when provided, prints a permanent line describing
    /// the file that was just acted upon, then immediately redraws the progress counter.
    /// Without it the counter is refreshed only once the throttle interval elapses.
    /// </summary>
    public void Advance(string? movedFile = null)
    {
        _current++;

        if (movedFile != null)
        {
            ClearProgressLine();
            WritePermanent($"  ➜  {movedFile}");
            _lastPrint = DateTime.MinValue; // force immediate progress redraw
        }

        if (DateTime.UtcNow - _lastPrint >= Interval)
            PrintProgress();
    }

    /// <summary>Clears the transient progress line when the reporter is disposed.</summary>
    public void Dispose() => ClearProgressLine();

    // ── internals ────────────────────────────────────────────────────────────

    private void PrintProgress()
    {
        ClearProgressLine();
        var pct = _total > 0 ? (int)((double)_current / _total * 100) : 0;
        var line = $"  {_label}: {_current} / {_total}  [{pct}%]";

        if (Console.IsOutputRedirected)
        {
            Console.WriteLine(line);
        }
        else
        {
            Console.Write(line);
            _progressLineActive = true;
        }

        _lastPrint = DateTime.UtcNow;
    }

    private void ClearProgressLine()
    {
        if (!_progressLineActive) return;

        if (!Console.IsOutputRedirected)
        {
            try
            {
                int w = Console.WindowWidth;
                Console.Write("\r" + new string(' ', Math.Max(w - 1, 0)) + "\r");
            }
            catch
            {
                Console.Write("\r");
            }
        }

        _progressLineActive = false;
    }

    private static void WritePermanent(string text)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(text);
        Console.ResetColor();
    }
}
