using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Permissions;
using Acme.TestCaseManagement.Quality;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.Runs.Dtos;
using Acme.TestCaseManagement.Transfer.Dtos;
using Acme.TestCaseManagement.Transfer.Tabular;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;

namespace Acme.TestCaseManagement.Transfer;

[Authorize(TestCaseManagementPermissions.TestRuns.Default)]
public class TestResultTransferAppService : TestCaseManagementAppService, ITestResultTransferAppService
{
    private readonly ITestRunAppService _runService;
    private readonly IRepository<TestExecution, Guid> _executionRepository;
    private readonly IRepository<DefectLink, Guid> _defectRepository;
    private readonly TestCaseManagementTransferOptions _options;

    public TestResultTransferAppService(
        ITestRunAppService runService,
        IRepository<TestExecution, Guid> executionRepository,
        IRepository<DefectLink, Guid> defectRepository,
        IOptions<TestCaseManagementTransferOptions> options)
    {
        _runService = runService;
        _executionRepository = executionRepository;
        _defectRepository = defectRepository;
        _options = options.Value;
    }

    public virtual async Task<IRemoteStreamContent> ExportAsync(Guid runId, TransferFormat format)
    {
        var run = await _runService.GetAsync(runId);
        var items = run.Items.OrderBy(i => i.Sequence).ToList();
        var itemIds = items.Select(i => i.Id).ToList();

        var executions = itemIds.Count == 0
            ? new List<TestExecution>()
            : await _executionRepository.GetListAsync(x => itemIds.Contains(x.TestRunItemId));
        var executionIds = executions.Select(x => x.Id).ToList();
        var defects = executionIds.Count == 0
            ? new List<DefectLink>()
            : await _defectRepository.GetListAsync(x => executionIds.Contains(x.TestExecutionId));

        var defectsByExecution = defects.OrderBy(d => d.CreationTime).ToLookup(d => d.TestExecutionId);
        var executionsByItem = executions.ToLookup(e => e.TestRunItemId);

        var rows = new List<ResultRow>();
        foreach (var item in items)
        {
            var attempts = executionsByItem[item.Id].OrderBy(e => e.AttemptNumber).ToList();
            var code = item.TestCaseCode ?? string.Empty;

            if (attempts.Count == 0)
            {
                rows.Add(new ResultRow(code, item.TestCaseTitle, item.VersionNumber, null, TestResultStatus.Untested, null, 0, string.Empty, null, null));
                continue;
            }

            foreach (var attempt in attempts)
            {
                rows.Add(new ResultRow(
                    code,
                    item.TestCaseTitle,
                    item.VersionNumber,
                    attempt.AttemptNumber,
                    attempt.Status,
                    attempt.ActualResult,
                    attempt.DurationSeconds,
                    TestResultSheet.FormatDefects(defectsByExecution[attempt.Id].Select(d => (d.ExternalSystem, d.IssueKey))),
                    attempt.CreationTime,
                    attempt.CreatorId?.ToString()));
            }
        }

        var bytes = TableFile.Write(TestResultSheet.Export(rows), format, "Results");

        return new RemoteStreamContent(
            new MemoryStream(bytes),
            $"test-results-{Clock.Now:yyyyMMdd-HHmmss}{TableFile.Extension(format)}",
            TableFile.ContentType(format),
            bytes.Length);
    }

    [Authorize(TestCaseManagementPermissions.TestRuns.Execute)]
    public virtual async Task<ImportReportDto> ImportAsync(Guid runId, ImportTestResultsInput input)
    {
        var messages = new TransferMessages(L);
        var report = new ImportReportDto { DryRun = input.DryRun };

        var run = await _runService.GetAsync(runId);

        ParsedResults parsed;
        try
        {
            parsed = TestResultSheet.Parse(TableFile.Read(await UploadReader.ReadAsync(input.File, _options.MaxFileSizeBytes), _options), messages);
        }
        catch (TableException exception)
        {
            report.FileErrors.Add(messages.Describe(exception.Problem, _options));
            return report;
        }

        report.FileErrors.AddRange(parsed.FileErrors);
        report.IgnoredColumns.AddRange(parsed.IgnoredColumns);

        if (run.Status == RunStatus.Completed)
        {
            report.FileErrors.Add(messages.Get("Import:Results:RunCompleted"));
        }

        if (report.FileErrors.Count > 0)
        {
            return report;
        }

        // Pass 1: match every row to an item of the run. Nothing is recorded.
        var itemsByCode = run.Items.Where(i => i.TestCaseCode != null)
            .ToLookup(i => i.TestCaseCode!, StringComparer.OrdinalIgnoreCase);
        var batch = new BatchExecuteTestItemsDto();

        foreach (var entry in parsed.Entries)
        {
            var result = new ImportItemResultDto
            {
                Row = entry.Row,
                Code = entry.Code.Length == 0 ? null : entry.Code,
                Messages = entry.Errors.ToList(),
            };
            report.Items.Add(result);

            if (entry.Errors.Count == 0 && entry.Skip)
            {
                result.Outcome = ImportOutcome.Skipped;
                result.Messages.Add(messages.Get("Import:Results:NoResult"));
                continue;
            }

            var item = entry.Errors.Count == 0 ? Match(entry, itemsByCode, messages, result.Messages) : null;
            if (result.Messages.Count > 0 || item == null)
            {
                result.Outcome = ImportOutcome.Invalid;
                continue;
            }

            result.Outcome = ImportOutcome.Recorded;
            batch.Items.Add(new BatchExecuteTestItemDto
            {
                TestRunItemId = item.Id,
                Status = entry.Status,
                ActualResult = entry.ActualResult,
                DurationSeconds = entry.DurationSeconds,
                Defects = entry.Defects.Select(d => new AddDefectLinkDto { ExternalSystem = d.System, IssueKey = d.Key }).ToList(),
            });
        }

        report.Total = report.Items.Count;
        report.Recorded = report.Items.Count(i => i.Outcome == ImportOutcome.Recorded);
        report.Skipped = report.Items.Count(i => i.Outcome == ImportOutcome.Skipped);
        report.Invalid = report.Items.Count(i => i.Outcome == ImportOutcome.Invalid);

        if (report.Invalid > 0 || input.DryRun || batch.Items.Count == 0)
        {
            return report;
        }

        // Pass 2: the existing batch execution records all attempts in one transaction, or none.
        await _runService.BatchExecuteAsync(runId, batch);

        report.Imported = true;
        return report;
    }

    private static TestRunItemDto? Match(
        ResultEntry entry, ILookup<string, TestRunItemDto> itemsByCode, TransferMessages messages, List<string> problems)
    {
        var candidates = itemsByCode[entry.Code].ToList();

        if (candidates.Count == 0)
        {
            problems.Add(messages.Get("Import:Results:ItemNotFound", ("Code", entry.Code)));
            return null;
        }

        if (entry.Version.HasValue)
        {
            candidates = candidates.Where(i => i.VersionNumber == entry.Version.Value).ToList();
            if (candidates.Count == 0)
            {
                problems.Add(messages.Get("Import:Results:VersionNotFound", ("Code", entry.Code), ("Version", entry.Version.Value)));
                return null;
            }
        }

        if (candidates.Count > 1)
        {
            problems.Add(messages.Get("Import:Results:ItemAmbiguous", ("Code", entry.Code)));
            return null;
        }

        return candidates[0];
    }
}
