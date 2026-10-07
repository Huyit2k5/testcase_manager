using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Plans.Dtos;
using Acme.TestCaseManagement.Quality;
using Acme.TestCaseManagement.QualityGates.Dtos;
using Acme.TestCaseManagement.SignOff.Dtos;
using Acme.TestCaseManagement.Requirements;
using Acme.TestCaseManagement.Requirements.Dtos;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.Runs.Dtos;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases;
using Acme.TestCaseManagement.TestCases.Dtos;
using AutoMapper;

namespace Acme.TestCaseManagement;

public class TestCaseManagementApplicationAutoMapperProfile : Profile
{
    public TestCaseManagementApplicationAutoMapperProfile()
    {
        CreateMap<TestSuite, TestSuiteDto>();

        CreateMap<TestStep, TestStepDto>();

        CreateMap<TestCase, TestCaseDto>()
            .ForMember(d => d.Steps, o => o.MapFrom(s => s.Steps.OrderBy(x => x.StepOrder)))
            .ForMember(d => d.Tags, o => o.MapFrom(s => s.Tags.Select(t => t.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)));

        CreateMap<TestStepSnapshot, TestStepDto>()
            .ForMember(d => d.StepOrder, o => o.MapFrom(s => s.Order));

        CreateMap<TestCaseVersion, TestCaseVersionDto>()
            .ForMember(d => d.Steps, o => o.MapFrom(s => TestStepSnapshot.Deserialize(s.StepsJson)));

        CreateMap<TestPlan, TestPlanDto>();

        // Items and the summary need data from other aggregates, so TestRunAppService fills them in.
        CreateMap<TestRun, TestRunDto>()
            .ForMember(d => d.Items, o => o.Ignore())
            .ForMember(d => d.Summary, o => o.Ignore());

        CreateMap<TestRunItem, TestRunItemDto>()
            .ForMember(d => d.VersionNumber, o => o.Ignore())
            .ForMember(d => d.TestCaseId, o => o.Ignore())
            .ForMember(d => d.TestCaseCode, o => o.Ignore())
            .ForMember(d => d.TestCaseTitle, o => o.Ignore())
            .ForMember(d => d.AttemptCount, o => o.Ignore());

        // Defect links are loaded separately by TestRunAppService.
        CreateMap<TestExecution, TestExecutionDto>()
            .ForMember(d => d.DefectLinks, o => o.Ignore());

        CreateMap<DefectLink, DefectLinkDto>();

        CreateMap<Requirement, RequirementDto>();

        CreateMap<QualityGate, QualityGateDto>();

        CreateMap<SignOffApproval, SignOffApprovalDto>();

        // The parsed summary and the integrity flag are filled in by SignOffAppService.
        CreateMap<SignOffReport, SignOffReportDto>()
            .ForMember(d => d.Approvals, o => o.MapFrom(s => s.Approvals.OrderBy(a => a.ApprovedTime)))
            .ForMember(d => d.Summary, o => o.Ignore())
            .ForMember(d => d.IntegrityVerified, o => o.Ignore());

        CreateMap<TestRunMetrics, TestRunSummaryDto>();
    }
}
