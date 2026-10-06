using System.Text;
using Acme.TestCaseManagement.Suites;

namespace Acme.TestCaseManagement.Transfer;

/// <summary>
/// A suite path is its names from the root joined by "/", for example <c>Payments/Cards/Refunds</c>. A "/" or a
/// backslash inside a name is written with a backslash in front of it.
/// </summary>
internal static class SuitePath
{
    public const char Separator = '/';

    public static string Format(IEnumerable<string> names)
    {
        return string.Join(Separator, names.Select(name => name.Replace("\\", "\\\\").Replace("/", "\\/")));
    }

    /// <summary>Splits a path into names. A name that is empty (for example in "A//B") makes the path invalid.</summary>
    public static bool TryParse(string path, out List<string> names)
    {
        names = new List<string>();
        var current = new StringBuilder();

        for (var i = 0; i < path.Length; i++)
        {
            var c = path[i];
            if (c == '\\' && i + 1 < path.Length && (path[i + 1] == '\\' || path[i + 1] == Separator))
            {
                current.Append(path[++i]);
            }
            else if (c == Separator)
            {
                names.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        names.Add(current.ToString().Trim());
        return names.All(name => name.Length > 0);
    }
}

/// <summary>The suite tree of the library as an import sees it, plus the suites that the import is going to create.</summary>
internal sealed class SuiteTree
{
    private readonly List<Node> _roots = new();
    private readonly Dictionary<Guid, Node> _byId = new();

    public SuiteTree(IEnumerable<TestSuite> suites)
    {
        var all = suites.OrderBy(s => s.Order).ThenBy(s => s.Name).ToList();
        foreach (var suite in all)
        {
            _byId[suite.Id] = new Node(suite.Id, suite.Name, null);
        }

        foreach (var suite in all)
        {
            var node = _byId[suite.Id];
            // A suite whose parent cannot be seen is shown at the root, as the library tree does.
            if (suite.ParentId.HasValue && _byId.TryGetValue(suite.ParentId.Value, out var parent))
            {
                node.Parent = parent;
                parent.Children.Add(node);
            }
            else
            {
                _roots.Add(node);
            }
        }
    }

    /// <summary>The suites to create, parents before children.</summary>
    public List<Node> Planned { get; } = new();

    public bool Contains(Guid id) => _byId.ContainsKey(id);

    /// <summary>The suite and all suites below it. An unknown id gives just that id, so that a filter on it matches nothing.</summary>
    public HashSet<Guid> SelfAndDescendantIds(Guid id)
    {
        var result = new HashSet<Guid> { id };
        if (_byId.TryGetValue(id, out var node))
        {
            var pending = new Stack<Node>();
            pending.Push(node);
            while (pending.Count > 0)
            {
                foreach (var child in pending.Pop().Children)
                {
                    if (child.Id.HasValue && result.Add(child.Id.Value))
                    {
                        pending.Push(child);
                    }
                }
            }
        }

        return result;
    }

    /// <summary>The path of an existing suite, for the Suite column of an export.</summary>
    public string PathOf(Guid id)
    {
        if (!_byId.TryGetValue(id, out var node))
        {
            return string.Empty;
        }

        var names = new List<string>();
        for (var current = node; current != null; current = current.Parent)
        {
            names.Add(current.Name);
        }

        names.Reverse();
        return SuitePath.Format(names);
    }

    /// <summary>Follows the names down the tree, matching case-insensitively.</summary>
    public Lookup Find(IReadOnlyList<string> names)
    {
        Node? current = null;
        var level = _roots;

        for (var depth = 0; depth < names.Count; depth++)
        {
            var matches = level.Where(n => string.Equals(n.Name, names[depth], StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count > 1)
            {
                return new Lookup(LookupKind.Ambiguous, null, current, depth);
            }

            if (matches.Count == 0)
            {
                return new Lookup(LookupKind.Missing, null, current, depth);
            }

            current = matches[0];
            level = current.Children;
        }

        return new Lookup(LookupKind.Found, current, current?.Parent, names.Count);
    }

    /// <summary>Plans the suites that <paramref name="names"/> still needs below <paramref name="lookup"/>, and returns the last one.</summary>
    public Node Plan(IReadOnlyList<string> names, Lookup lookup)
    {
        var parent = lookup.Parent;
        Node? node = null;

        for (var depth = lookup.MatchedDepth; depth < names.Count; depth++)
        {
            node = new Node(null, names[depth], parent);
            (parent?.Children ?? _roots).Add(node);
            Planned.Add(node);
            parent = node;
        }

        return node!;
    }

    internal sealed class Node
    {
        public Node(Guid? id, string name, Node? parent)
        {
            Id = id;
            Name = name;
            Parent = parent;
        }

        /// <summary>Null while the suite is only planned; set once the import has created it.</summary>
        public Guid? Id { get; set; }

        public string Name { get; }

        public Node? Parent { get; set; }

        public List<Node> Children { get; } = new();
    }

    internal enum LookupKind
    {
        Found,
        Missing,
        Ambiguous,
    }

    /// <summary>
    /// Where a path ended. <see cref="Parent"/> is the deepest suite that exists on the way (the parent of the first
    /// missing one), <see cref="MatchedDepth"/> the number of names that were found.
    /// </summary>
    internal readonly record struct Lookup(LookupKind Kind, Node? Node, Node? Parent, int MatchedDepth);
}
