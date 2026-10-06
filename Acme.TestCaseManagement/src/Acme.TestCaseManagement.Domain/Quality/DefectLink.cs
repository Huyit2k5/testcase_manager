using Acme.TestCaseManagement.Enums;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Acme.TestCaseManagement.Quality;

/// <summary>
/// Reference from a failed <see cref="Runs.TestExecution"/> to a ticket in an external tracker (Jira, GitHub, ...).
/// The execution itself is never modified; a wrongly entered link is soft-deleted and entered again, while
/// <see cref="Severity"/> and <see cref="IsResolved"/> follow the ticket and may change over time.
/// </summary>
public class DefectLink : FullAuditedEntity<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    public virtual Guid TestExecutionId { get; protected set; }

    /// <summary>Name of the tracker, for example "Jira" or "GitHub".</summary>
    public virtual string ExternalSystem { get; protected set; }

    /// <summary>Ticket identifier in that tracker, for example "BUG-88" or "#42".</summary>
    public virtual string IssueKey { get; protected set; }

    public virtual string? IssueUrl { get; protected set; }

    /// <summary>Impact of the defect. Quality gates count open Critical and High defects.</summary>
    public virtual SeverityLevel Severity { get; protected set; }

    /// <summary>True once the defect is fixed (or closed) in the tracker. Open defects have this set to false.</summary>
    public virtual bool IsResolved { get; protected set; }

    public virtual DateTime? ResolvedTime { get; protected set; }

    protected DefectLink()
    {
        ExternalSystem = default!;
        IssueKey = default!;
    }

    internal DefectLink(
        Guid id,
        Guid? tenantId,
        Guid testExecutionId,
        string externalSystem,
        string issueKey,
        string? issueUrl,
        SeverityLevel severity)
        : base(id)
    {
        TenantId = tenantId;
        TestExecutionId = testExecutionId;
        ExternalSystem = Check.NotNullOrWhiteSpace(externalSystem, nameof(externalSystem), DefectLinkConsts.MaxExternalSystemLength).Trim();
        IssueKey = Check.NotNullOrWhiteSpace(issueKey, nameof(issueKey), DefectLinkConsts.MaxIssueKeyLength).Trim();
        IssueUrl = NormalizeUrl(issueUrl);
        Severity = severity;
    }

    /// <summary>Changes the severity and the resolved state. <paramref name="now"/> becomes the resolved time on resolution.</summary>
    public virtual void Update(SeverityLevel severity, bool isResolved, DateTime now)
    {
        Severity = severity;

        if (isResolved && !IsResolved)
        {
            ResolvedTime = now;
        }
        else if (!isResolved)
        {
            ResolvedTime = null;
        }

        IsResolved = isResolved;
    }

    private static string? NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var trimmed = Check.Length(url.Trim(), nameof(url), DefectLinkConsts.MaxIssueUrlLength);

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new BusinessException(TestCaseManagementErrorCodes.InvalidDefectUrl);
        }

        return trimmed;
    }
}
