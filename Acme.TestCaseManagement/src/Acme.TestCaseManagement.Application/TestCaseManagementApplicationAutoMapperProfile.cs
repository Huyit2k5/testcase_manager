using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Projects;
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

        // The counts are filled in by the application service, which counts what is in each project.
        CreateMap<Project, ProjectDto>()
            .ForMember(d => d.SuiteCount, o => o.Ignore())
            .ForMember(d => d.TestCaseCount, o => o.Ignore())
            .ForMember(d => d.PlanCount, o => o.Ignore())
            .ForMember(d => d.RequirementCount, o => o.Ignore())
            .ForMember(d => d.RunCount, o => o.Ignore());

        // The name of the group and whether the copy is behind are filled in by the application service, which looks the groups up.
        CreateMap<TestStep, TestStepDto>()
            .ForMember(d => d.SharedStepGroupName, o => o.Ignore())
            .ForMember(d => d.SharedStepOutdated, o => o.Ignore());

        CreateMap<TestCase, TestCaseDto>()
            .ForMember(d => d.Steps, o => o.MapFrom(s => s.Steps.OrderBy(x => x.StepOrder)))
            .ForMember(d => d.Tags, o => o.MapFrom(s => s.Tags.Select(t => t.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)));

        // A version holds the steps as they were tested; where they came from is not part of the snapshot.
        CreateMap<TestStepSnapshot, TestStepDto>()
            .ForMember(d => d.StepOrder, o => o.MapFrom(s => s.Order))
            .ForMember(d => d.SharedStepGroupId, o => o.Ignore())
            .ForMember(d => d.SharedStepRevision, o => o.Ignore())
            .ForMember(d => d.SharedStepGroupName, o => o.Ignore())
            .ForMember(d => d.SharedStepOutdated, o => o.Ignore());

        CreateMap<TestCaseVersion, TestCaseVersionDto>()
            .ForMember(d => d.Steps, o => o.MapFrom(s => TestStepSnapshot.Deserialize(s.StepsJson)));

        // The number of runs is counted by the application service for the plans it returns.
        CreateMap<TestPlan, TestPlanDto>().ForMember(d => d.RunCount, o => o.Ignore());

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
