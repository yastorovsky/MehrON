namespace ServiceLib.ViewModels;

// PattN: fork-only file, the log panel of the Aether pages (PattNG ServerAetherActivity AetherLogPanel, AetherLogEntry).

/// <summary>
/// A line of the log of an Aether page (PattNG AetherLogEntry): its <paramref name="Id" />, which only grows, its
/// <paramref name="Priority" />, its <paramref name="Text" />, and whether it is the page's <paramref name="Own" /> word
/// (a resource) rather than the core's output.
/// </summary>
public sealed record AetherLogEntry(long Id, AetherLogPriority Priority, string Text, bool Own)
{
    /// <summary>Shown in the theme's error colour.</summary>
    public bool IsError => Priority >= AetherLogPriority.Error;

    /// <summary>Shown in amber.</summary>
    public bool IsWarning => Priority == AetherLogPriority.Warn;

    /// <summary>The core's own output below a warning, shown dimmed beside the page's words.</summary>
    public bool IsOutput => !Own && Priority < AetherLogPriority.Warn;
}

/// <summary>
/// The log of an Aether page (design 3.2.2 row 43): the newest <see cref="Capacity" /> lines, the page's own words and
/// the core's output, which comes from the core's reader thread and is put in on the thread the log was made on, the
/// UI's, in the order it came.
/// </summary>
public class AetherLogViewModel(string emptyText)
{
    /// <summary>How many lines the log keeps (PattNG LOG_CAPACITY).</summary>
    public const int Capacity = 500;

    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private long _nextId;

    /// <summary>The lines, oldest first.</summary>
    public ObservableCollection<AetherLogEntry> Entries { get; } = [];

    /// <summary>What the panel shows while there are no lines.</summary>
    public string EmptyText { get; } = emptyText;

    /// <summary>The lines as text, one per line, for the Copy button.</summary>
    public string Text => string.Join(Environment.NewLine, Entries.Select(entry => entry.Text));

    /// <summary>Adds a word of the page's own, a resource already formatted.</summary>
    public void Append(AetherLogPriority priority, string text)
    {
        Add(priority, text, own: true);
    }

    /// <summary>
    /// Adds a line of the core's output, without its header and at its own level (PattNG appendOutput); a line with no
    /// message left is dropped.
    /// </summary>
    public void AppendOutput(string line)
    {
        var message = AetherLogLine.OutputMessage(line);
        if (message.Length > 0)
        {
            Add(AetherLogLine.OutputPriority(line.Trim()), message, own: false);
        }
    }

    private void Add(AetherLogPriority priority, string text, bool own)
    {
        var entry = new AetherLogEntry(Interlocked.Increment(ref _nextId), priority, text, own);
        if (_ui == null || SynchronizationContext.Current == _ui)
        {
            Put(entry);
        }
        else
        {
            _ui.Post(_ => Put(entry), null);
        }
    }

    private void Put(AetherLogEntry entry)
    {
        lock (Entries)
        {
            Entries.Add(entry);
            while (Entries.Count > Capacity)
            {
                Entries.RemoveAt(0);
            }
        }
    }
}
