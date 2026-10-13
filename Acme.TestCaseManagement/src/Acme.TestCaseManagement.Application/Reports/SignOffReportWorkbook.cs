using Acme.TestCaseManagement.Transfer.Tabular;

namespace Acme.TestCaseManagement.Reports;

/// <summary>A sign-off report as a workbook: a sheet for the report itself, and one for the criteria, the open defects and the approvals.</summary>
internal static class SignOffReportWorkbook
{
    public static byte[] Write(SignOffReportDocument report)
    {
        var t = report.Texts;

        var overview = new List<IReadOnlyList<string>> { new[] { report.Heading, string.Empty } };
        overview.AddRange(report.General.Select(x => (IReadOnlyList<string>)new[] { x.Label, x.Value }));
        overview.Add(new[] { string.Empty, string.Empty });
        overview.Add(new[] { t.ResultsSection, string.Empty });
        overview.AddRange(report.Results.Select(x => (IReadOnlyList<string>)new[] { x.Label, x.Value }));

        var criteria = report.Criteria.Select(c => (IReadOnlyList<string>)new[] { c.Label, c.Required, c.Actual, c.Result }).ToList();

        var defects = report.DefectCounts.Select(x => (IReadOnlyList<string>)new[] { x.Label, x.Value, string.Empty, string.Empty }).ToList();
        defects.AddRange(report.Defects.Select(d => (IReadOnlyList<string>)new[] { d.System, d.Key, d.Severity, d.Link }));

        var approvals = report.Approvals.Select(a => (IReadOnlyList<string>)new[] { a.Name, a.Role, a.Time, a.Comment, a.Signature }).ToList();

        return XlsxTable.WriteWorkbook(new (string Name, Table Table)[]
        {
            (SheetName(t.GeneralSection), new Table(new[] { t.FieldColumn, t.ValueColumn }, overview)),
            (SheetName(t.CriteriaSection), new Table(new[] { t.CriterionColumn, t.RequiredColumn, t.ActualColumn, t.ResultColumn }, criteria)),
            (SheetName(t.DefectsSection), new Table(new[] { t.SystemColumn, t.KeyColumn, t.SeverityColumn, t.LinkColumn }, defects)),
            (SheetName(t.ApprovalsSection), new Table(new[] { t.NameColumn, t.RoleColumn, t.TimeColumn, t.CommentColumn, t.SignatureColumn }, approvals)),
        });
    }

    /// <summary>A sheet name has at most 31 characters and none of <c>\ / ? * [ ] :</c>.</summary>
    private static string SheetName(string text)
    {
        var cleaned = new string(text.Where(c => "\\/?*[]:".IndexOf(c) < 0).ToArray()).Trim();
        return cleaned.Length > 31 ? cleaned[..31].Trim() : cleaned;
    }
}
