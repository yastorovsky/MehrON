namespace ServiceLib.Aether;

/// <summary>
/// PattN: what the exit-node lists of the Aether editor and the WARP keys window show (PattNG ui/server/ExitNodes.kt).
/// </summary>
public static class AetherExitNodes
{
    /// <summary>
    /// The labels a pick list shows for <paramref name="names" />, in their order, as the exit-node lists show theirs:
    /// each name, made unique by a number after it, which <paramref name="numbered" /> writes, where it is one of
    /// <paramref name="reserved" />, the labels of the list's own entries, or came up before. The list hands back the
    /// label picked, so no two may be alike.
    /// </summary>
    public static List<string> DistinctLabels(IReadOnlyList<string> names, IEnumerable<string> reserved, Func<string, int, string> numbered)
    {
        var taken = new HashSet<string>(reserved, StringComparer.Ordinal);
        var labels = new List<string>(names.Count);
        foreach (var name in names)
        {
            var label = name;
            var number = 2;
            while (taken.Contains(label))
            {
                label = numbered(name, number++);
            }
            taken.Add(label);
            labels.Add(label);
        }
        return labels;
    }

    /// <summary>
    /// Why the exit-node named <paramref name="name" /> cannot be used, as far as <paramref name="nodes" />, the names of
    /// the profiles that can be one, tell: no profile has the name any more, or several have it. Null when one has it,
    /// when no name is set, and until the nodes are read.
    /// </summary>
    public static ExitNodeLookup.Problem? ProblemOf(string? name, IReadOnlyList<AetherExitNode>? nodes)
    {
        var wanted = KotlinText.TrimOrNull(name) ?? string.Empty;
        if (wanted.Length == 0 || nodes == null)
        {
            return null;
        }
        var profiles = nodes.FirstOrDefault(node => node.Name == wanted)?.Profiles ?? 0;
        return profiles switch
        {
            0 => ExitNodeLookup.NotFound.Instance,
            > 1 => ExitNodeLookup.SameName.Instance,
            _ => null,
        };
    }

    /// <summary>The localized message of <paramref name="problem" /> for the exit-node <paramref name="name" />.</summary>
    public static string MessageOf(ExitNodeLookup.Problem problem, string name)
    {
        var format = ResUI.ResourceManager.GetString(problem.MessageKey, ResUI.Culture) ?? problem.MessageKey;
        return string.Format(format, name);
    }
}
