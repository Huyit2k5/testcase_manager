namespace Acme.TestCaseManagement.Quality;

/// <summary>
/// The thresholds a gate evaluation uses. Kept separate from the <see cref="QualityGate"/> entity so that an
/// evaluation (and a sign-off report) can carry the exact values it was run with, even if the gate is edited or deleted later.
/// </summary>
/// <param name="Id">Id of the configured gate; null for the built-in baseline.</param>
/// <param name="Name">Display name of the gate.</param>
/// <param name="MinPassRate">Minimum pass rate in percent (0.01 to 100).</param>
/// <param name="RequiredApprovals">Distinct approvers a sign-off needs.</param>
/// <param name="IsBuiltIn">True for the baseline used when a tenant has no default gate.</param>
public record QualityGateSettings(Guid? Id, string Name, decimal MinPassRate, int RequiredApprovals, bool IsBuiltIn)
{
    /// <summary>Constitution V defaults: pass rate of 95% and QA Lead plus Product Owner approving.</summary>
    public static QualityGateSettings Baseline { get; } = new(
        null,
        "Baseline",
        QualityGateConsts.DefaultMinPassRate,
        QualityGateConsts.DefaultRequiredApprovals,
        true);
}
