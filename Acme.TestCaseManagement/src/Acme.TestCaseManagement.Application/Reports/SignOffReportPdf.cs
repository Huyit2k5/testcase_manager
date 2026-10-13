using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace Acme.TestCaseManagement.Reports;

/// <summary>A sign-off report as an A4 document to read, print and sign.</summary>
internal static class SignOffReportPdf
{
    private static readonly Color Ink = new(31, 41, 55);
    private static readonly Color Muted = new(107, 114, 128);
    private static readonly Color Line = new(209, 213, 219);
    private static readonly Color HeaderFill = new(243, 244, 246);
    private static readonly Color Good = new(22, 101, 52);
    private static readonly Color Bad = new(185, 28, 28);

    public static byte[] Write(SignOffReportDocument report)
    {
        ReportFonts.Install();
        var t = report.Texts;

        var document = new Document { Info = { Title = report.Title } };
        DefineStyles(document);

        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.LeftMargin = Unit.FromCentimeter(2);
        section.PageSetup.RightMargin = Unit.FromCentimeter(2);
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(2);

        var footer = section.Footers.Primary.AddParagraph();
        footer.Style = "Small";
        footer.Format.Alignment = ParagraphAlignment.Center;
        footer.AddText(t.GeneratedNote + "   ·   " + t.Page + " ");
        footer.AddPageField();
        footer.AddText(" / ");
        footer.AddNumPagesField();

        var heading = section.AddParagraph(report.Heading);
        heading.Style = "Title";
        var title = section.AddParagraph(report.Title);
        title.Style = "Subtitle";

        AddHeading(section, t.GeneralSection);
        AddKeyValues(section, report.General);

        AddHeading(section, t.ResultsSection);
        AddKeyValues(section, report.Results);

        AddHeading(section, t.CriteriaSection);
        var criteria = NewTable(section, 6.2, 3.4, 3.4, 4.0);
        AddHeaderRow(criteria, t.CriterionColumn, t.RequiredColumn, t.ActualColumn, t.ResultColumn);
        foreach (var c in report.Criteria)
        {
            var row = AddRow(criteria, c.Label, c.Required, c.Actual, c.Result);
            row.Cells[3].Format.Font.Color = c.Passed ? Good : Bad;
            row.Cells[3].Format.Font.Bold = true;
        }

        AddHeading(section, t.DefectsSection);
        AddKeyValues(section, report.DefectCounts);
        if (report.Defects.Count == 0)
        {
            var none = section.AddParagraph(t.NoDefects);
            none.Style = "Small";
            none.Format.SpaceBefore = 4;
        }
        else
        {
            var defects = NewTable(section, 3.0, 4.0, 2.6, 7.4);
            AddHeaderRow(defects, t.SystemColumn, t.KeyColumn, t.SeverityColumn, t.LinkColumn);
            foreach (var d in report.Defects)
            {
                AddRow(defects, d.System, d.Key, d.Severity, d.Link);
            }
        }

        AddHeading(section, t.ApprovalsSection);
        if (report.Approvals.Count == 0)
        {
            var none = section.AddParagraph(t.NoApprovals);
            none.Style = "Small";
        }
        else
        {
            var approvals = NewTable(section, 3.4, 2.4, 3.2, 4.0, 4.0);
            AddHeaderRow(approvals, t.NameColumn, t.RoleColumn, t.TimeColumn, t.CommentColumn, t.SignatureColumn);
            foreach (var a in report.Approvals)
            {
                AddRow(approvals, a.Name, a.Role, a.Time, a.Comment, a.Signature.Length > 16 ? a.Signature[..16] + "..." : a.Signature);
            }
        }

        AddHeading(section, t.SignaturesSection);
        AddSignatureBlock(section, t);

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, closeStream: false);
        return stream.ToArray();
    }

    private static void DefineStyles(Document document)
    {
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = ReportFonts.Family;
        normal.Font.Size = 9.5;
        normal.Font.Color = Ink;
        normal.ParagraphFormat.SpaceAfter = 0;

        var small = document.Styles.AddStyle("Small", StyleNames.Normal);
        small.Font.Size = 8;
        small.Font.Color = Muted;

        var title = document.Styles.AddStyle("Title", StyleNames.Normal);
        title.Font.Size = 16;
        title.Font.Bold = true;
        title.ParagraphFormat.Alignment = ParagraphAlignment.Center;
        title.ParagraphFormat.SpaceAfter = 4;

        var subtitle = document.Styles.AddStyle("Subtitle", StyleNames.Normal);
        subtitle.Font.Size = 11;
        subtitle.Font.Color = Muted;
        subtitle.ParagraphFormat.Alignment = ParagraphAlignment.Center;
        subtitle.ParagraphFormat.SpaceAfter = 6;

        var section = document.Styles.AddStyle("Section", StyleNames.Normal);
        section.Font.Size = 11.5;
        section.Font.Bold = true;
        section.ParagraphFormat.SpaceBefore = 14;
        section.ParagraphFormat.SpaceAfter = 4;
        section.ParagraphFormat.KeepWithNext = true;
    }

    private static void AddHeading(Section section, string text)
    {
        section.AddParagraph(text).Style = "Section";
    }

    private static void AddKeyValues(Section section, IReadOnlyList<(string Label, string Value)> pairs)
    {
        var table = NewTable(section, 5.2, 11.8);
        var rows = new List<Row>();
        foreach (var (label, value) in pairs)
        {
            var row = AddRow(table, label, value);
            row.Cells[0].Format.Font.Color = Muted;
            rows.Add(row);
        }

        // A short table is kept on one page: each row stays with the ones after it.
        for (var i = 0; i < rows.Count - 1; i++)
        {
            rows[i].KeepWith = rows.Count - 1 - i;
        }
    }

    private static Table NewTable(Section section, params double[] centimeters)
    {
        var table = section.AddTable();
        table.Borders.Color = Line;
        table.Borders.Width = 0.5;
        table.Rows.LeftIndent = 0;
        foreach (var width in centimeters)
        {
            table.AddColumn(Unit.FromCentimeter(width));
        }

        return table;
    }

    private static void AddHeaderRow(Table table, params string[] cells)
    {
        var row = table.AddRow();
        row.HeadingFormat = true;
        row.Shading.Color = HeaderFill;
        row.Format.Font.Bold = true;
        FillRow(row, cells);
    }

    private static Row AddRow(Table table, params string[] cells)
    {
        var row = table.AddRow();
        FillRow(row, cells);
        return row;
    }

    private static void FillRow(Row row, string[] cells)
    {
        row.TopPadding = 2.5;
        row.BottomPadding = 2.5;
        row.VerticalAlignment = VerticalAlignment.Center;
        for (var i = 0; i < cells.Length; i++)
        {
            var paragraph = row.Cells[i].AddParagraph();
            var lines = Wrap(cells[i]).Split((char)10);
            for (var l = 0; l < lines.Length; l++)
            {
                if (l > 0)
                {
                    paragraph.AddLineBreak();
                }

                paragraph.AddText(lines[l]);
            }
        }
    }

    /// <summary>A long text without a space (a hash, a link) is cut into lines, since the cell would not break it.</summary>
    private static string Wrap(string text)
    {
        if (text.Length <= 36 || text.Contains(' '))
        {
            return text;
        }

        return string.Join((char)10, Enumerable.Range(0, (text.Length + 31) / 32).Select(i => text.Substring(i * 32, Math.Min(32, text.Length - i * 32))));
    }

    /// <summary>Two blank places to sign by hand: who made the report and who accepts it.</summary>
    private static void AddSignatureBlock(Section section, ReportTexts t)
    {
        var table = section.AddTable();
        table.Borders.Visible = false;
        table.AddColumn(Unit.FromCentimeter(8.5));
        table.AddColumn(Unit.FromCentimeter(8.5));

        var heads = table.AddRow();
        heads.KeepWith = 3;   // the block is not split over two pages
        heads.Cells[0].AddParagraph(t.PreparedBy).Format.Font.Bold = true;
        heads.Cells[1].AddParagraph(t.ApprovedBy).Format.Font.Bold = true;

        var blank = table.AddRow();
        blank.KeepWith = 2;
        blank.Height = Unit.FromCentimeter(2.2);
        blank.VerticalAlignment = VerticalAlignment.Bottom;
        blank.Cells[0].AddParagraph(t.SignatureLine).Style = "Small";
        blank.Cells[1].AddParagraph(t.SignatureLine).Style = "Small";

        var names = table.AddRow();
        names.KeepWith = 1;
        names.TopPadding = 6;
        names.Cells[0].AddParagraph(t.FullName + ": ........................................").Style = "Small";
        names.Cells[1].AddParagraph(t.FullName + ": ........................................").Style = "Small";

        var dates = table.AddRow();
        dates.TopPadding = 6;
        dates.Cells[0].AddParagraph(t.DateLine + ": ___ / ___ / ______").Style = "Small";
        dates.Cells[1].AddParagraph(t.DateLine + ": ___ / ___ / ______").Style = "Small";
    }
}
