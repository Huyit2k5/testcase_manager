using System.Globalization;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Localization;
using Acme.TestCaseManagement.QualityGates.Dtos;
using Acme.TestCaseManagement.SignOff.Dtos;
using Microsoft.Extensions.Localization;
using Volo.Abp.DependencyInjection;

namespace Acme.TestCaseManagement.Reports;

/// <summary>
/// Turns a sign-off report (as it was frozen) into the text of a file, in the language of the caller. What the report says comes from
/// the frozen evaluation and not from the library as it is now, so the file of a sign-off is the same whenever it is made.
/// </summary>
public class SignOffReportBuilder : ITransientDependency
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private readonly IStringLocalizer<TestCaseManagementResource> _l;

    public SignOffReportBuilder(IStringLocalizer<TestCaseManagementResource> localizer)
    {
        _l = localizer;
    }

    public virtual SignOffReportDocument Build(SignOffReportDto report, string projectLabel)
    {
        var summary = report.Summary;
        var metrics = summary?.Metrics;

        var general = new List<(string, string)>
        {
            (Text("Title"), report.Title),
            (Text("Project"), projectLabel),
            (Text("Scope"), ScopeText(report, summary)),
            (Text("Gate"), report.QualityGateName),
            (Text("MinPassRate"), Percent(report.MinPassRate)),
            (Text("RequiredApprovals"), report.RequiredApprovals.ToString(Invariant)),
            (Text("Status"), _l["SignOffReport:Status:" + report.Status].Value),
            (Text("Created"), Time(report.CreationTime)),
            (Text("Completed"), report.ApprovedTime.HasValue ? Time(report.ApprovedTime.Value) : "-"),
        };
        if (summary != null)
        {
            general.Add((Text("Evaluated"), Time(summary.EvaluatedTime)));
            general.Add((Text("GateResult"), summary.Passed ? Text("Passed") : Text("Failed")));
        }

        general.Add((Text("Hash"), report.SnapshotHash));
        general.Add((Text("Integrity"), report.IntegrityVerified ? Text("IntegrityOk") : Text("IntegrityBroken")));

        var results = new List<(string, string)>();
        if (metrics != null)
        {
            results.Add((Text("Runs"), metrics.RunCount.ToString(Invariant)));
            results.Add((Text("Items"), metrics.TotalItems.ToString(Invariant)));
            results.Add((Text("ItemsPassed"), metrics.Passed.ToString(Invariant)));
            results.Add((Text("ItemsFailed"), metrics.Failed.ToString(Invariant)));
            results.Add((Text("ItemsBlocked"), metrics.Blocked.ToString(Invariant)));
            results.Add((Text("ItemsSkipped"), metrics.Skipped.ToString(Invariant)));
            results.Add((Text("ItemsUntested"), metrics.Untested.ToString(Invariant)));
            results.Add((Text("Completion"), Percent(metrics.CompletionPercentage)));
            results.Add((Text("PassRate"), metrics.PassRate.HasValue ? Percent(metrics.PassRate.Value) : "-"));
            results.Add((Text("FirstTimePassRate"), metrics.FirstTimePassRate.HasValue ? Percent(metrics.FirstTimePassRate.Value) : "-"));
            results.Add((Text("P1"), $"{metrics.P1Executed}/{metrics.P1Total} ({Percent(metrics.P1ExecutionRate)})"));
        }

        var criteria = (summary?.Criteria ?? new List<GateCriterionResultDto>())
            .Select(c => new ReportCriterion(
                CriterionLabel(c),
                c.Operator + " " + Number(c.Threshold) + Suffix(c.Code),
                c.Actual.HasValue ? Number(c.Actual.Value) + Suffix(c.Code) : "-",
                c.Passed ? Text("Passed") : Text("Failed"),
                c.Passed))
            .ToList();

        var counts = new List<(string, string)>();
        var defects = new List<ReportDefect>();
        if (metrics != null)
        {
            counts.Add((Text("DefectCritical"), metrics.OpenDefects.Critical.ToString(Invariant)));
            counts.Add((Text("DefectHigh"), metrics.OpenDefects.High.ToString(Invariant)));
            counts.Add((Text("DefectMedium"), metrics.OpenDefects.Medium.ToString(Invariant)));
            counts.Add((Text("DefectLow"), metrics.OpenDefects.Low.ToString(Invariant)));
            counts.Add((Text("DefectTotal"), metrics.OpenDefects.Total.ToString(Invariant)));
            defects.AddRange(metrics.OpenDefectIssues.Select(d => new ReportDefect(d.ExternalSystem, d.IssueKey, SeverityText(d.Severity), d.IssueUrl ?? string.Empty)));
        }

        var approvals = report.Approvals
            .OrderBy(a => a.ApprovedTime)
            .Select(a => new ReportApproval(a.ApproverName, a.ApproverRole ?? string.Empty, Time(a.ApprovedTime), a.Comment ?? string.Empty, a.Signature))
            .ToList();

        return new SignOffReportDocument(
            report.Title, Text("Heading"), general, results, criteria, counts, defects, approvals, Texts());
    }

    private string Text(string key) => _l["SignOffReport:" + key].Value;

    private ReportTexts Texts() => new(
        Text("SectionGeneral"), Text("SectionResults"), Text("SectionCriteria"), Text("SectionDefects"), Text("SectionApprovals"), Text("SectionSignatures"),
        Text("ColumnCriterion"), Text("ColumnRequired"), Text("ColumnActual"), Text("ColumnResult"),
        Text("ColumnSystem"), Text("ColumnKey"), Text("ColumnSeverity"), Text("ColumnLink"),
        Text("ColumnName"), Text("ColumnRole"), Text("ColumnTime"), Text("ColumnComment"), Text("ColumnSignature"),
        Text("ColumnField"), Text("ColumnValue"),
        Text("NoDefects"), Text("NoApprovals"), Text("PreparedBy"), Text("ApprovedBy"), Text("FullName"), Text("SignatureLine"), Text("DateLine"),
        Text("Page"), Text("GeneratedNote"));

    private string CriterionLabel(GateCriterionResultDto criterion)
    {
        var localized = _l["SignOffReport:Criterion:" + criterion.Code];
        return localized.ResourceNotFound ? criterion.Label : localized.Value;
    }

    private string SeverityText(SeverityLevel severity) => Text("Defect" + severity);

    private string ScopeText(SignOffReportDto report, QualityGateEvaluationDto? summary)
    {
        var plans = summary?.Scope.Plans.Select(p => p.Name).ToList() ?? new List<string>();
        if (plans.Count > 0)
        {
            return string.Join(", ", plans);
        }

        return report.MilestoneId.HasValue ? Text("Milestone") + " " + report.MilestoneId : "-";
    }

    /// <summary>Times are written in UTC with that said, so that a file does not mean one thing in Hanoi and another in Berlin.</summary>
    private static string Time(DateTime time) => time.ToString("yyyy-MM-dd HH:mm", Invariant) + " UTC";

    private static string Percent(decimal value) => Number(value) + "%";

    private static string Number(decimal value) => value.ToString("0.##", Invariant);

    /// <summary>The criteria of rates are in percent; the others count defects.</summary>
    private static string Suffix(string code) => code is "PassRate" or "P1Executed" ? "%" : string.Empty;
}
