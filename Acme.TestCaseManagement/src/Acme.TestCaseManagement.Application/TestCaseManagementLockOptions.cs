namespace Acme.TestCaseManagement;

/// <summary>
/// How long a change waits for another change of the same thing to finish (two approvals of one sign-off, two saves of the default quality
/// gate). The lock is in this process unless the host registers a distributed lock provider.
/// </summary>
public class TestCaseManagementLockOptions
{
    public TimeSpan Wait { get; set; } = TimeSpan.FromSeconds(30);
}
