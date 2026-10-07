using System.ComponentModel.DataAnnotations;
using Acme.TestCaseManagement.Enums;
using Volo.Abp.Content;

namespace Acme.TestCaseManagement.Transfer.Dtos;

/// <summary>Which test cases to export. The filters are those of the test case list, without paging.</summary>
public class ExportTestCasesInput
{
    public TransferFormat Format { get; set; } = TransferFormat.Xlsx;

    /// <summary>Matches Code, Title or Description.</summary>
    public string? Filter { get; set; }

    public Guid? SuiteId { get; set; }

    /// <summary>When a suite is selected, also export the test cases of its descendant suites.</summary>
    public bool IncludeDescendantSuites { get; set; } = true;

    public TestCaseStatus? Status { get; set; }

    public PriorityLevel? Priority { get; set; }

    public SeverityLevel? Severity { get; set; }

    public ExecutionType? ExecutionType { get; set; }

    public TestKind? Kind { get; set; }

    public TestLayer? Layer { get; set; }

    /// <summary>Only test cases that have all of these tags (compared ignoring case).</summary>
    public List<string>? Tags { get; set; }

    /// <summary>True: only test cases with an Automation ID; false: only those without one.</summary>
    public bool? HasAutomationId { get; set; }
}

/// <summary>A file of test cases to import (multipart form).</summary>
public class ImportTestCasesInput
{
    /// <summary>An Excel (.xlsx) or CSV file; the format is detected from the content.</summary>
    [Required]
    public IRemoteStreamContent File { get; set; } = default!;

    /// <summary>The suite for rows that name none, and for new test cases when the file has no Suite column.</summary>
    public Guid? DefaultSuiteId { get; set; }

    /// <summary>What to do with a code that already exists. Skip by default.</summary>
    public ImportConflictMode OnExisting { get; set; } = ImportConflictMode.Skip;

    /// <summary>When true the file is checked and the report is returned, but nothing is written.</summary>
    public bool DryRun { get; set; }
}

/// <summary>A file of execution results to import into one run (multipart form).</summary>
public class ImportTestResultsInput
{
    /// <summary>An Excel (.xlsx) or CSV file; the format is detected from the content.</summary>
    [Required]
    public IRemoteStreamContent File { get; set; } = default!;

    /// <summary>When true the file is checked and the report is returned, but nothing is recorded.</summary>
    public bool DryRun { get; set; }
}

/// <summary>
/// The outcome of an import. An import is all or nothing: while any row is invalid nothing is written, and
/// <see cref="Imported"/> is false.
/// </summary>
public class ImportReportDto
{
    public bool DryRun { get; set; }

    /// <summary>True when the changes were written. False for a dry run and for a file with errors.</summary>
    public bool Imported { get; set; }

    /// <summary>Test cases (or result rows) found in the file.</summary>
    public int Total { get; set; }

    public int Created { get; set; }

    public int Updated { get; set; }

    public int Skipped { get; set; }

    public int Recorded { get; set; }

    public int Invalid { get; set; }

    /// <summary>Suites that were (or, in a dry run, would be) created from the Suite column.</summary>
    public int CreatedSuites { get; set; }

    /// <summary>Problems with the file as a whole, such as an unknown format or a missing column.</summary>
    public List<string> FileErrors { get; set; } = new();

    /// <summary>Columns of the file that the import does not use.</summary>
    public List<string> IgnoredColumns { get; set; } = new();

    /// <summary>One entry per test case (or result row), in file order.</summary>
    public List<ImportItemResultDto> Items { get; set; } = new();
}

public class ImportItemResultDto
{
    /// <summary>Spreadsheet row number: the header is row 1. For a test case with several steps, its first row.</summary>
    public int Row { get; set; }

    public string? Code { get; set; }

    public ImportOutcome Outcome { get; set; }

    /// <summary>Why the row is invalid, or a note such as "the test case already exists".</summary>
    public List<string> Messages { get; set; } = new();
}
