using Acme.TestCaseManagement.Enums;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Acme.TestCaseManagement.TestCases;

/// <summary>
/// One line in the history of a test case: what happened, who did it and when (from the creation audit of the entity). It is only
/// ever added, so the history is not rewritten, and it stays when the test case is deleted.
/// </summary>
public class TestCaseHistoryEntry : CreationAuditedEntity<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    public virtual Guid TestCaseId { get; protected set; }

    public virtual TestCaseHistoryType Type { get; protected set; }

    /// <summary>The old value, for example the old status.</summary>
    public virtual string? FromValue { get; protected set; }

    /// <summary>The new value, for example the new status.</summary>
    public virtual string? ToValue { get; protected set; }

    /// <summary>The number of the version, when a version was published.</summary>
    public virtual int? VersionNumber { get; protected set; }

    /// <summary>Free text that completes the line: the fields that changed, the tags, the change summary.</summary>
    public virtual string? Note { get; protected set; }

    protected TestCaseHistoryEntry()
    {
    }

    public TestCaseHistoryEntry(
        Guid id,
        Guid? tenantId,
        Guid testCaseId,
        TestCaseHistoryType type,
        string? fromValue,
        string? toValue,
        int? versionNumber,
        string? note)
        : base(id)
    {
        TenantId = tenantId;
        TestCaseId = testCaseId;
        Type = type;
        FromValue = Check.Length(fromValue, nameof(fromValue), TestCaseHistoryConsts.MaxValueLength);
        ToValue = Check.Length(toValue, nameof(toValue), TestCaseHistoryConsts.MaxValueLength);
        VersionNumber = versionNumber;
        Note = Trim(note);
    }

    private static string? Trim(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        var trimmed = note.Trim();
        return trimmed.Length <= TestCaseHistoryConsts.MaxNoteLength ? trimmed : trimmed[..TestCaseHistoryConsts.MaxNoteLength];
    }
}
