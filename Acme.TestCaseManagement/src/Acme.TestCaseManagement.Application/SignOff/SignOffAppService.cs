using System.Text.Json;
using Acme.TestCaseManagement.Permissions;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Quality;
using Acme.TestCaseManagement.QualityGates;
using Acme.TestCaseManagement.SignOff.Dtos;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Acme.TestCaseManagement.SignOff;

[Authorize(TestCaseManagementPermissions.SignOff.Default)]
public class SignOffAppService : TestCaseManagementAppService, ISignOffAppService
{
    private readonly IRepository<SignOffReport, Guid> _reportRepository;
    private readonly SignOffManager _signOffManager;

    public SignOffAppService(IRepository<SignOffReport, Guid> reportRepository, SignOffManager signOffManager)
    {
        _reportRepository = reportRepository;
        _signOffManager = signOffManager;
    }

    public virtual async Task<SignOffReportDto> GetAsync(Guid id)
    {
        return ToDto(await _reportRepository.GetAsync(id));
    }

    public virtual async Task<PagedResultDto<SignOffReportDto>> GetListAsync(GetSignOffListInput input)
    {
        var query = (await _reportRepository.WithDetailsAsync())
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
            new QualityGateScope(input.TestPlanId, input.MilestoneId),
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
