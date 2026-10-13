using System.Text.Json;
using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Permissions;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Projects;
using Acme.TestCaseManagement.Quality;
using Acme.TestCaseManagement.Reports;
using Acme.TestCaseManagement.QualityGates;
using Acme.TestCaseManagement.SignOff.Dtos;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;

namespace Acme.TestCaseManagement.SignOff;

[Authorize(TestCaseManagementPermissions.SignOff.Default)]
public class SignOffAppService : TestCaseManagementAppService, ISignOffAppService
{
    private readonly IRepository<SignOffReport, Guid> _reportRepository;
    private readonly SignOffManager _signOffManager;
    private readonly IRepository<Project, Guid> _projects;
    private readonly SignOffReportBuilder _reportBuilder;

    public SignOffAppService(
        IRepository<SignOffReport, Guid> reportRepository,
        SignOffManager signOffManager,
        IRepository<Project, Guid> projects,
        SignOffReportBuilder reportBuilder)
    {
        _reportRepository = reportRepository;
        _signOffManager = signOffManager;
        _projects = projects;
        _reportBuilder = reportBuilder;
    }

    public virtual async Task<SignOffReportDto> GetAsync(Guid id)
    {
        return ToDto(await _reportRepository.GetAsync(id));
    }

    public virtual async Task<IRemoteStreamContent> ExportAsync(Guid id, ReportFormat format)
    {
        var report = await GetAsync(id);
        var project = report.ProjectId == Guid.Empty ? null : await _projects.FindAsync(report.ProjectId);
        var projectLabel = project == null ? "-" : $"{project.Key} - {project.Name}";

        var document = _reportBuilder.Build(report, projectLabel);
        var bytes = format == ReportFormat.Pdf ? SignOffReportPdf.Write(document) : SignOffReportWorkbook.Write(document);

        var name = $"signoff-{FileSlug(report.Title)}-{report.CreationTime:yyyyMMdd}.{(format == ReportFormat.Pdf ? "pdf" : "xlsx")}";
        var contentType = format == ReportFormat.Pdf ? "application/pdf" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        return new RemoteStreamContent(new MemoryStream(bytes), name, contentType, bytes.Length, disposeStream: true);
    }

    /// <summary>The title as a part of a file name: letters and digits without accents, other characters as dashes.</summary>
    internal static string FileSlug(string title)
    {
        var decomposed = title.Replace('đ', 'd').Replace('Đ', 'D').Normalize(System.Text.NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder();
        foreach (var c in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        }

        var slug = string.Join('-', builder.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "report" : slug.Length > 60 ? slug[..60].TrimEnd('-') : slug;
    }

    public virtual async Task<PagedResultDto<SignOffReportDto>> GetListAsync(GetSignOffListInput input)
    {
        var query = (await _reportRepository.WithDetailsAsync())
            .WhereIf(input.ProjectId.HasValue, x => x.ProjectId == input.ProjectId)
            .WhereIf(input.TestPlanId.HasValue, x => x.TestPlanId == input.TestPlanId)
            .WhereIf(input.MilestoneId.HasValue, x => x.MilestoneId == input.MilestoneId)
            .WhereIf(input.Status.HasValue, x => x.Status == input.Status);

        var totalCount = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(
            Sort(query, input.Sorting).Skip(input.SkipCount).Take(input.MaxResultCount));

        return new PagedResultDto<SignOffReportDto>(totalCount, items.Select(ToDto).ToList());
    }

    [Authorize(TestCaseManagementPermissions.SignOff.Approve)]
    public virtual async Task<SignOffReportDto> SignOffAsync(StartSignOffDto input)
    {
        // Two people starting a sign-off for the same plan or milestone at once would each supersede nothing and leave two Pending reports.
        await LockUntilTheRequestEndsAsync($"signoff-start:{input.TestPlanId}:{input.MilestoneId}");

        var report = await _signOffManager.StartAsync(
            new QualityGateScope(input.TestPlanId, input.MilestoneId, input.ProjectId),
            input.QualityGateId,
            input.Title,
            input.ApproverRole,
            input.Comment);

        await _reportRepository.InsertAsync(report, autoSave: true);

        return ToDto(report);
    }

    [Authorize(TestCaseManagementPermissions.SignOff.Approve)]
    public virtual async Task<SignOffReportDto> ApproveAsync(Guid id, ApproveSignOffDto input)
    {
        // The report becomes Approved when the approvals that were read plus this one are enough; two approvals at once would each
        // count only themselves and leave an enough-signed report Pending.
        await LockUntilTheRequestEndsAsync($"signoff:{id}");

        var report = await _reportRepository.GetAsync(id);

        await _signOffManager.ApproveAsync(report, input.ApproverRole, input.Comment);
        await _reportRepository.UpdateAsync(report, autoSave: true);

        return ToDto(report);
    }

    private SignOffReportDto ToDto(SignOffReport report)
    {
        var dto = ObjectMapper.Map<SignOffReport, SignOffReportDto>(report);
        dto.IntegrityVerified = report.VerifyIntegrity();

        try
        {
            dto.Summary = QualityGateDtoFactory.ToDto(
                SignOffSnapshot.Deserialize(report.SummaryStatsJson).Evaluation,
                code => L[$"QualityGate:Criterion:{code}"].Value);
        }
        catch (JsonException)
        {
            // A snapshot that cannot be read is reported through IntegrityVerified and the raw JSON, not by failing the request.
            dto.Summary = null;
            dto.IntegrityVerified = false;
        }

        return dto;
    }

    private static IQueryable<SignOffReport> Sort(IQueryable<SignOffReport> query, string? sorting)
    {
        var (column, descending) = TestPlanAppService.ParseSorting(sorting);

        var ordered = (column, descending) switch
        {
            ("title", false) => query.OrderBy(x => x.Title),
            ("title", true) => query.OrderByDescending(x => x.Title),
            ("status", false) => query.OrderBy(x => x.Status).ThenByDescending(x => x.CreationTime),
            ("status", true) => query.OrderByDescending(x => x.Status).ThenByDescending(x => x.CreationTime),
            ("creationtime", false) => query.OrderBy(x => x.CreationTime),
            _ => query.OrderByDescending(x => x.CreationTime),
        };

        // The columns above are not unique. Without a last unique key a page boundary can repeat or skip rows: SQL Server and
        // PostgreSQL order equal values as they like, differently from one query to the next.
        return ordered.ThenBy(x => x.Id);
    }
}
