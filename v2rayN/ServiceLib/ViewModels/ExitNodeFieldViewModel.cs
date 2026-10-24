namespace ServiceLib.ViewModels;

// PattN: fork-only file, the exit-node field of the Aether editor and the WARP keys window (PattNG ui/server/ExitNodeField.kt).

/// <summary>
/// PattN: the exit-node an Aether core dials out through, in the Aether editor and the WARP keys window alike (design
/// 3.2.2 row 15, PattNG ExitNodeField): freedom, the default, with the finalMask and the dialMode set beside it, or a
/// profile Xray can build as a chain hop, chosen by its name, whose outbound then is the exit-node
/// (<see cref="AetherExit.Node" />). The list shows freedom first, then each name once
/// (<see cref="AetherExitNodes.DistinctLabels" />); a name no profile has any more, or several have, shows with the
/// message that says so under the field (<see cref="SupportingText" />), and is never replaced by a guess. The names are
/// read once the window opens (<see cref="SetNodes" />).
/// </summary>
public partial class ExitNodeFieldViewModel : ReactiveObject
{
    private IReadOnlyList<AetherExitNode>? _nodes;

    /// <summary>The name of each entry of <see cref="Labels" /> after freedom, in its order.</summary>
    private List<string> _names = [];

    public ExitNodeFieldViewModel(string? name)
    {
        Name = name ?? string.Empty;
        Labels = [ResUI.TbAetherExitNodeFreedom];
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Name))
            {
                Rebuild();
            }
        };
        Rebuild();
    }

    /// <summary>The name of the profile chosen, as stored; blank for freedom.</summary>
    [Reactive]
    public partial string Name { get; set; }

    /// <summary>What the list shows: freedom, then the names, then a name chosen that no profile has (any more).</summary>
    [Reactive]
    public partial IReadOnlyList<string> Labels { get; set; }

    /// <summary>
    /// The text under the field: nothing for freedom; for a name, why it cannot be used, or else that the finalMask and
    /// the dialMode are for freedom only.
    /// </summary>
    [Reactive]
    public partial string? SupportingText { get; set; }

    /// <summary>Whether <see cref="SupportingText" /> tells a problem, for the error colour.</summary>
    [Reactive]
    public partial bool HasProblem { get; set; }

    /// <summary>Whether the core dials out through freedom, which takes the finalMask and the dialMode beside the field.</summary>
    [Reactive]
    public partial bool IsFreedom { get; set; }

    /// <summary>The names read, with how many profiles have each; null until they are read.</summary>
    public IReadOnlyList<AetherExitNode>? Nodes => _nodes;

    /// <summary>
    /// The entry of <see cref="Labels" /> the field shows. A list that is replaced picks nothing for a moment, which
    /// changes nothing here, nor does the entry shown handed back.
    /// </summary>
    public int SelectedIndex
    {
        get => IndexOfName();
        set
        {
            if (value < 0 || value == IndexOfName())
            {
                return;
            }
            Name = value == 0 ? string.Empty : value - 1 < _names.Count ? _names[value - 1] : Name;
        }
    }

    /// <summary>The trimmed name, as it is looked up; empty for freedom.</summary>
    public string Wanted => KotlinText.TrimOrNull(Name) ?? string.Empty;

    /// <summary>Takes the names of the profiles that can be the exit-node, as they were read.</summary>
    public void SetNodes(IReadOnlyList<AetherExitNode>? nodes)
    {
        _nodes = nodes;
        Rebuild();
    }

    private int IndexOfName()
    {
        var wanted = Wanted;
        if (wanted.Length == 0)
        {
            return 0;
        }
        var index = _names.IndexOf(wanted);
        return index < 0 ? -1 : index + 1;
    }

    private void Rebuild()
    {
        var wanted = Wanted;
        var freedom = ResUI.TbAetherExitNodeFreedom;
        var names = _nodes?.Select(node => node.Name).ToList() ?? [];
        // A name no profile offered stays on show, so that the field tells what is stored and why it does not work.
        if (wanted.Length > 0 && !names.Contains(wanted))
        {
            names.Add(wanted);
        }
        var labels = AetherExitNodes.DistinctLabels(names, [freedom], (name, number) => string.Format(ResUI.TbLabelNumbered, name, number));
        _names = names;
        List<string> shown = [freedom, .. labels];
        if (!shown.SequenceEqual(Labels))
        {
            Labels = shown;
        }
        this.RaisePropertyChanged(nameof(SelectedIndex));
        var problem = AetherExitNodes.ProblemOf(wanted, _nodes);
        IsFreedom = wanted.Length == 0;
        HasProblem = problem != null;
        SupportingText = wanted.Length == 0
            ? null
            : problem != null ? AetherExitNodes.MessageOf(problem, wanted) : ResUI.TbAetherExitNodeHint;
    }
}
