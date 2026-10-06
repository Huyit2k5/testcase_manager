using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace Acme.TestCaseManagement.Quality;

/// <summary>
/// One user's approval of a sign-off report. Append-only: the type has no mutators and the module never changes or
/// deletes an approval. <see cref="Signature"/> is an integrity digest over the report, the frozen snapshot hash and
/// the approver's identity, role, comment and time; it is not an asymmetric digital signature.
/// </summary>
public class SignOffApproval : CreationAuditedEntity<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    public virtual Guid SignOffReportId { get; protected set; }

    public virtual Guid ApproverUserId { get; protected set; }

    /// <summary>User name at the time of approval, kept so the report stays readable if the account is renamed.</summary>
    public virtual string ApproverName { get; protected set; }

    /// <summary>Free text such as "QA Lead" or "Product Owner".</summary>
    public virtual string? ApproverRole { get; protected set; }

    public virtual string? Comment { get; protected set; }

    /// <summary>Truncated to milliseconds so that it survives a round trip through any database provider unchanged.</summary>
    public virtual DateTime ApprovedTime { get; protected set; }

    /// <summary>Lowercase hexadecimal SHA-256, see <see cref="ComputeSignature"/>.</summary>
    public virtual string Signature { get; protected set; }

    protected SignOffApproval()
    {
        ApproverName = default!;
        Signature = default!;
    }

    internal SignOffApproval(
        Guid id,
        Guid? tenantId,
        Guid signOffReportId,
        string snapshotHash,
        Guid approverUserId,
        string approverName,
        string? approverRole,
        string? comment,
        DateTime approvedTime)
        : base(id)
    {
        TenantId = tenantId;
        SignOffReportId = signOffReportId;
        ApproverUserId = approverUserId;
        ApproverName = Check.NotNullOrWhiteSpace(approverName, nameof(approverName), SignOffConsts.MaxApproverNameLength);
        ApproverRole = Normalize(approverRole, nameof(approverRole), SignOffConsts.MaxRoleLength);
        Comment = Normalize(comment, nameof(comment), SignOffConsts.MaxCommentLength);
        ApprovedTime = TruncateToMilliseconds(approvedTime);
        Signature = ComputeSignature(signOffReportId, snapshotHash, approverUserId, ApproverRole, Comment, ApprovedTime);
    }

    /// <summary>
    /// SHA-256 over report id, snapshot hash, approver id, role, comment and the approval time (as ticks, so it does
    /// not depend on the DateTime kind).
    /// </summary>
    public static string ComputeSignature(
        Guid signOffReportId,
        string snapshotHash,
        Guid approverUserId,
        string? approverRole,
        string? comment,
        DateTime approvedTime)
    {
        var canonical = string.Join(
            "\n",
            signOffReportId.ToString("D"),
            snapshotHash,
            approverUserId.ToString("D"),
            approverRole ?? string.Empty,
            comment ?? string.Empty,
            TruncateToMilliseconds(approvedTime).Ticks.ToString(CultureInfo.InvariantCulture));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    internal bool VerifySignature(string snapshotHash)
    {
        return string.Equals(
            Signature,
            ComputeSignature(SignOffReportId, snapshotHash, ApproverUserId, ApproverRole, Comment, ApprovedTime),
            StringComparison.Ordinal);
    }

    internal static DateTime TruncateToMilliseconds(DateTime value)
    {
        return new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond, value.Kind);
    }

    private static string? Normalize(string? value, string parameterName, int maxLength)
    {
        return string.IsNullOrWhiteSpace(value) ? null : Check.Length(value.Trim(), parameterName, maxLength);
    }
}
