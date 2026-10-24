using System.Collections.Specialized;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

/// <summary>
/// PattN: the log panel of the Aether pages, for an <see cref="AetherLogViewModel" />: errors in the error colour,
/// warnings in amber, the core's own output dimmed; it follows the newest line, and Copy puts every line on the clipboard.
/// </summary>
public partial class AetherLogPanel : ReactiveUserControl<AetherLogViewModel>
{
    private AetherLogViewModel? _shown;
    private bool _scrollQueued;

    public AetherLogPanel()
    {
        InitializeComponent();

        btnCopy.Click += async (_, _) =>
        {
            if (ViewModel is { } log && log.Entries.Count > 0)
            {
                await AvaUtils.SetClipboardData(this, log.Text);
                NoticeManager.Instance.Enqueue(ResUI.OperationSuccess);
            }
        };

        this.WhenActivated(disposables =>
        {
            this.WhenAnyValue(v => v.ViewModel)
                .Subscribe(Show)
                .DisposeWith(disposables);
        });
    }

    private void Show(AetherLogViewModel? log)
    {
        if (_shown != null)
        {
            _shown.Entries.CollectionChanged -= OnEntriesChanged;
        }
        _shown = log;
        lstLog.ItemsSource = log?.Entries;
        txtEmpty.Text = log?.EmptyText ?? string.Empty;
        if (log != null)
        {
            log.Entries.CollectionChanged += OnEntriesChanged;
        }
        OnEntriesChanged(null, null);
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs? e)
    {
        var entries = _shown?.Entries;
        var any = entries is { Count: > 0 };
        txtEmpty.IsVisible = !any;
        btnCopy.IsEnabled = any;
        // The newest line once the list has laid it out: scrolled to as it is added, the list goes by its estimate of where
        // the line sits, and a burst of lines leaves the newest one cut off at the bottom.
        if (any && !_scrollQueued)
        {
            _scrollQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _scrollQueued = false;
                if (_shown?.Entries is { Count: > 0 } shown)
                {
                    lstLog.ScrollIntoView(shown.Count - 1);
                }
            }, DispatcherPriority.Background);
        }
    }
}
