namespace ServiceLib.ViewModels;

// PattN: fork-only file, the preset fields of PattNG's editors (ui/server/PresetFields.kt), shared by the views of both UIs.

/// <summary>
/// PattN: a text field with an arrow that lists ready-made values by name (design 3.3, PattNG PresetField), for a
/// setting whose value can be too long to list, such as a finalMask or cipherSuites. A pick puts its value in the field,
/// which stays free to edit, so what is saved is the pick as edited, and the name of the value the field holds shows
/// under it (<see cref="HeldName" />). Only the arrow opens the list, so a click in the text only moves the caret, and a
/// pick asks first, through <see cref="ReplaceInteraction" />, before it takes the place of a value of the user's own.
/// The first pick, "default", blanks the field, which leaves the setting at its default; a blank field shows it as its
/// pick.
/// </summary>
public partial class PresetFieldViewModel : ReactiveObject
{
    private readonly FieldPresets _presets;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;

    /// <summary>The last match: the text matched and the pick it holds, as <see cref="FieldPresets.HeldOf" /> gives it.</summary>
    private (string Text, int Held)? _held;

    /// <summary>Raised with every change of the text, so that a match a later text overtook is dropped.</summary>
    private int _generation;

    public PresetFieldViewModel(FieldPresets presets, string? text)
    {
        _presets = presets;
        PickNames = [ResUI.TbPresetDefault, .. presets.Names];
        Text = text ?? string.Empty;
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Text))
            {
                Match();
            }
        };
        Match();
    }

    /// <summary>What the field holds, as typed or picked; what a save stores.</summary>
    [Reactive]
    public partial string Text { get; set; }

    /// <summary>
    /// The name of the preset the field holds, shown under it: "default" for a blank field at once, a preset's name, or
    /// null for a value of the user's own. JSON is matched off the UI thread, so the name can trail the text by a keystroke.
    /// </summary>
    [Reactive]
    public partial string? HeldName { get; set; }

    /// <summary>The names the arrow lists: "default" first, then the presets, each pick at its menu index.</summary>
    public IReadOnlyList<string> PickNames { get; }

    /// <summary>The name of what the field sets, as the arrow and the question name it (finalMask, cipherSuites).</summary>
    public string Setting => _presets.Setting;

    /// <summary>The tooltip and the accessible name of the arrow.</summary>
    public string ChooseTip => string.Format(ResUI.TbPresetChoose, Setting);

    /// <summary>
    /// Asked before a pick takes the place of a value of the user's own: the setting and the name of the pick, answered
    /// true to replace it. Unanswered, nothing is replaced.
    /// </summary>
    public Interaction<(string Setting, string Pick), bool> ReplaceInteraction { get; } = new();

    /// <summary>The match of the text the field holds now; the tests wait for it.</summary>
    public Task Matched { get; private set; } = Task.CompletedTask;

    /// <summary>The pick of the menu entry at <paramref name="index" /> of <see cref="PickNames" />.</summary>
    public static int PickOf(int index)
    {
        return index == 0 ? FieldPresets.Default : index - 1;
    }

    /// <summary>Picks the menu entry at <paramref name="index" /> of <see cref="PickNames" />, see <see cref="ChooseAsync" />.</summary>
    public Task ChooseAtAsync(int index)
    {
        return ChooseAsync(PickOf(index));
    }

    /// <summary>
    /// Puts the value of <paramref name="pick" />, <see cref="FieldPresets.Default" /> or the index of a preset, in the
    /// field. Until the text the field holds now is matched, a pick asks rather than lose a value of the user's own.
    /// </summary>
    public async Task ChooseAsync(int pick)
    {
        var value = _presets.TextOf(pick);
        if (value == null)
        {
            return;
        }
        var text = Text;
        int? held = _held is { } last && last.Text == text ? last.Held : null;
        if (FieldPresets.AsksBeforeReplacing(text, held))
        {
            var replace = await ReplaceInteraction.HandleSafe((Setting, _presets.NameOf(pick, ResUI.TbPresetDefault)), false);
            if (!replace)
            {
                return;
            }
        }
        Text = value;
    }

    private void Match()
    {
        var text = Text;
        var generation = Interlocked.Increment(ref _generation);
        // The name of the last match stands until this one is in; a blank field holds the default pick at once.
        HeldName = _presets.HeldNameOf(text, _held?.Held, ResUI.TbPresetDefault);
        if (KotlinText.IsBlank(text))
        {
            _held = (text, FieldPresets.Default);
            Matched = Task.CompletedTask;
            return;
        }
        Matched = MatchAsync(text, generation);
    }

    private async Task MatchAsync(string text, int generation)
    {
        var held = await Task.Run(() => _presets.HeldOf(text)).ConfigureAwait(false);
        OnUi(() =>
        {
            if (Volatile.Read(ref _generation) != generation)
            {
                return;
            }
            _held = (text, held);
            HeldName = _presets.HeldNameOf(text, held, ResUI.TbPresetDefault);
        });
    }

    /// <summary>Runs <paramref name="action" /> on the thread the field was made on, the UI's; at once where there is none, as in the tests.</summary>
    private void OnUi(Action action)
    {
        if (_ui == null || SynchronizationContext.Current == _ui)
        {
            action();
        }
        else
        {
            _ui.Post(_ => action(), null);
        }
    }
}
