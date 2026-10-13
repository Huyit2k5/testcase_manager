namespace Acme.TestCaseManagement.Reports;

/// <summary>
/// A sign-off report as it is written to a file: every text is already in the language of the reader, so the file writers
/// (PDF and workbook) only lay it out. It is built from the report as it was frozen when the sign-off was made.
/// </summary>
public record SignOffReportDocument(
    string Title,
    string Heading,
    IReadOnlyList<(string Label, string Value)> General,
    IReadOnlyList<(string Label, string Value)> Results,
    IReadOnlyList<ReportCriterion> Criteria,
    IReadOnlyList<(string Label, string Value)> DefectCounts,
    IReadOnlyList<ReportDefect> Defects,
    IReadOnlyList<ReportApproval> Approvals,
    ReportTexts Texts);

/// <summary>One criterion of the quality gate with what was required and what was found.</summary>
public record ReportCriterion(string Label, string Required, string Actual, string Result, bool Passed);

public record ReportDefect(string System, string Key, string Severity, string Link);

public record ReportApproval(string Name, string Role, string Time, string Comment, string Signature);

/// <summary>The headings and captions that are not data.</summary>
public record ReportTexts(
    string GeneralSection,
    string ResultsSection,
    string CriteriaSection,
    string DefectsSection,
    string ApprovalsSection,
    string SignaturesSection,
    string CriterionColumn,
    string RequiredColumn,
    string ActualColumn,
    string ResultColumn,
    string SystemColumn,
    string KeyColumn,
    string SeverityColumn,
    string LinkColumn,
    string NameColumn,
    string RoleColumn,
    string TimeColumn,
    string CommentColumn,
    string SignatureColumn,
    string FieldColumn,
    string ValueColumn,
    string NoDefects,
    string NoApprovals,
    string PreparedBy,
    string ApprovedBy,
    string FullName,
    string SignatureLine,
    string DateLine,
    string Page,
    string GeneratedNote);
