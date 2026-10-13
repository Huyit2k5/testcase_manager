namespace Acme.TestCaseManagement.Projects;

/// <summary>
/// Which projects the code that is running may see. The data of every other project is left out of the queries (a global query
/// filter reads this), so a test case, a run or a plan of a project that is out of reach is simply not found.
/// <para>
/// With no scope set the code is unrestricted: background work, the migrator, an API key. The application services of the module set a
/// scope for the person who called them (see <c>ProjectAccessInterceptor</c>); a scope that is already set is left as it is.
/// </para>
/// </summary>
public static class ProjectAccessScope
{
    private sealed record Data(bool Unrestricted, List<Guid> ProjectIds);

    private static readonly List<Guid> None = new();

    private static readonly AsyncLocal<Data?> Current = new();

    /// <summary>A scope was set by someone.</summary>
    public static bool IsActive => Current.Value != null;

    /// <summary>Read by the query filters: true when every project is in reach.</summary>
    public static bool Unrestricted => Current.Value?.Unrestricted ?? true;

    /// <summary>Read by the query filters (not to be changed): the projects in reach when it is not unrestricted. A list, not an array: the MySQL provider of Oracle fails on a parameterized array. <see cref="Guid.Empty"/> (data that has no project yet) is always there.</summary>
    public static List<Guid> VisibleProjectIds => Current.Value?.ProjectIds ?? None;

    /// <summary>Until the result is disposed, only these projects are in reach.</summary>
    public static IDisposable Restrict(IEnumerable<Guid> projectIds)
    {
        return Set(new Data(false, projectIds.Append(Guid.Empty).Distinct().ToList()));
    }

    /// <summary>Until the result is disposed, every project is in reach (for example while the access of a person is worked out).</summary>
    public static IDisposable Unrestrict()
    {
        return Set(new Data(true, new List<Guid>()));
    }

    private static IDisposable Set(Data data)
    {
        var previous = Current.Value;
        Current.Value = data;
        return new Restore(previous);
    }

    private sealed class Restore : IDisposable
    {
        private readonly Data? _previous;
        private bool _done;

        public Restore(Data? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_done)
            {
                return;
            }

            _done = true;
            Current.Value = _previous;
        }
    }
}
