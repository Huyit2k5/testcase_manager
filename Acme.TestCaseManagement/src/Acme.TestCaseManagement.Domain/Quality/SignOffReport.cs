using Acme.TestCaseManagement.Enums;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Acme.TestCaseManagement.Quality;

/// <summary>
/// Formal sign-off of a test plan or milestone. The evaluated figures are frozen at creation
/// (<see cref="SummaryStatsJson"/> and its <see cref="SnapshotHash"/>) and never change; only approvals are appended
/// and the status moves Pending to Approved (or Superseded). See the plan (ADR 4.5).
/// </summary>
public class SignOffReport : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>Set when the sign-off covers one test plan; null for a milestone sign-off.</summary>
    public virtual Guid? TestPlanId { get; protected set; }

    /// <summary>Set when the sign-off covers every plan of a milestone; null for a plan sign-off.</summary>
    public virtual Guid? MilestoneId { get; protected set; }

    public virtual string Title { get; protected set; }

    /// <summary>The configured gate that was used; null when the built-in baseline was used.</summary>
    public virtual Guid? QualityGateId { get; protected set; }

    /// <summary>Frozen copy of the gate name, pass rate threshold and approvals required at sign-off time.</summary>
    public virtual string QualityGateName { get; protected set; }

    public virtual decimal MinPassRate { get; protected set; }

    public virtual int RequiredApprovals { get; protected set; }

    public virtual SignOffStatus Status { get; protected set; }

    /// <summary>When the last required approval was given; null while Pending or Superseded.</summary>
    public virtual DateTime? ApprovedTime { get; protected set; }

    /// <summary>JSON of <see cref="SignOffSnapshot"/>: scope, thresholds, metrics, criteria results and open defects.</summary>
    public virtual string SummaryStatsJson { get; protected set; }

    /// <summary>Lowercase hexadecimal SHA-256 of <see cref="SummaryStatsJson"/>.</summary>
    public virtual string SnapshotHash { get; protected set; }

    public virtual ICollection<SignOffApproval> Approvals { get; protected set; }

    protected SignOffReport()
    {
        Title = default!;
        QualityGateName = default!;
        SummaryStatsJson = default!;
        SnapshotHash = default!;
        Approvals = new List<SignOffApproval>();
    }

    internal SignOffReport(
        Guid id,
        Guid? tenantId,
        QualityGateScope scope,
        string title,
        QualityGateSettings gate,
        string summaryStatsJson)
        : base(id)
    {
        TenantId = tenantId;
        TestPlanId = scope.TestPlanId;
        MilestoneId = scope.MilestoneId;
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), SignOffConsts.MaxTitleLength);
        QualityGateId = gate.Id;
        QualityGateName = gate.Name;
        MinPassRate = gate.MinPassRate;
        RequiredApprovals = gate.RequiredApprovals;
        Status = SignOffStatus.Pending;
        SummaryStatsJson = Check.NotNullOrWhiteSpace(summaryStatsJson, nameof(summaryStatsJson));
        SnapshotHash = SignOffSnapshot.ComputeHash(summaryStatsJson);
        Approvals = new List<SignOffApproval>();
    }

    public virtual QualityGateScope ToScope()
    {
        return new QualityGateScope(TestPlanId, MilestoneId);
    }

    /// <summary>The thresholds frozen in this report, used to re-check the gate before a later approval.</summary>
    public virtual QualityGateSettings ToSettings()
    {
        return new QualityGateSettings(QualityGateId, QualityGateName, MinPassRate, RequiredApprovals, QualityGateId == null);
    }

    /// <summary>
    /// True when the stored JSON still matches its hash and every approval still matches its digest.
    /// A false result means the stored data was altered outside this module.
    /// </summary>
    public virtual bool VerifyIntegrity()
    {
        return string.Equals(SnapshotHash, SignOffSnapshot.ComputeHash(SummaryStatsJson), StringComparison.Ordinal)
               && Approvals.All(a => a.VerifySignature(SnapshotHash));
    }

    /// <summary>Throws unless the report is Pending and <paramref name="userId"/> has not approved it yet.</summary>
    internal void EnsureCanBeApprovedBy(Guid userId)
    {
        if (Status != SignOffStatus.Pending)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.SignOffNotPending)
                .WithData("Title", Title)
                .WithData("Status", Status);
        }

        if (Approvals.Any(a => a.ApproverUserId == userId))
        {
            throw new BusinessException(TestCaseManagementErrorCodes.DuplicateSignOffApproval).WithData("Title", Title);
        }
    }

    /// <summary>Appends an approval; the report becomes Approved when the required number of distinct users is reached.</summary>
    internal SignOffApproval AddApproval(
        Guid approvalId, Guid userId, string userName, string? role, string? comment, DateTime time)
    {
        EnsureCanBeApprovedBy(userId);

        var approval = new SignOffApproval(
            approvalId, TenantId, Id, SnapshotHash, userId, userName, role, comment, time);
        Approvals.Add(approval);

        if (Approvals.Count >= RequiredApprovals)
        {
            Status = SignOffStatus.Approved;
            ApprovedTime = approval.ApprovedTime;
        }

        return approval;
    }

    /// <summary>Marks a Pending report as replaced by a newer sign-off of the same scope.</summary>
    internal void Supersede()
    {
        if (Status != SignOffStatus.Pending)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.SignOffNotPending)
                .WithData("Title", Title)
                .WithData("Status", Status);
        }

        Status = SignOffStatus.Superseded;
    }
}
