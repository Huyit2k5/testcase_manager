using Acme.TestCaseManagement.Attachments;
using Acme.TestCaseManagement.SharedSteps;
using Acme.TestCaseManagement.Automation;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Projects;
using Acme.TestCaseManagement.Quality;
using Acme.TestCaseManagement.Requirements;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.TestCases;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;

namespace Acme.TestCaseManagement.EntityFrameworkCore;

[ConnectionStringName(TestCaseManagementDbProperties.ConnectionStringName)]
public interface ITestCaseManagementDbContext : IEfCoreDbContext
{
    /// <summary>
    /// True when every project is in reach of the code that is running (see <see cref="ProjectAccessScope"/>). Read by the query filters
    /// for every query. A host that embeds the model needs to write nothing: this has a default.
    /// </summary>
    bool IsAllProjectsInReach => ProjectAccessScope.Unrestricted;

    /// <summary>The projects in reach when <see cref="IsAllProjectsInReach"/> is false. Read by the query filters for every query.</summary>
    List<Guid> ProjectsInReach => ProjectAccessScope.VisibleProjectIds;

    DbSet<Project> Projects { get; }

    DbSet<ProjectMember> ProjectMembers { get; }

    // Master library (design time)
    DbSet<TestSuite> TestSuites { get; }

    DbSet<TestCase> TestCases { get; }

    DbSet<TestCaseVersion> TestCaseVersions { get; }

    DbSet<TestCaseHistoryEntry> TestCaseHistory { get; }

    // Execution cycle (runtime)
    DbSet<TestPlan> TestPlans { get; }

    DbSet<TestRun> TestRuns { get; }

    DbSet<TestRunItem> TestRunItems { get; }

    DbSet<TestExecution> TestExecutions { get; }

    DbSet<TestStepResult> TestStepResults { get; }

    // Traceability
    DbSet<Requirement> Requirements { get; }

    DbSet<RequirementTestCase> RequirementTestCases { get; }

    // Quality
    DbSet<DefectLink> DefectLinks { get; }

    DbSet<QualityGate> QualityGates { get; }

    DbSet<SignOffReport> SignOffReports { get; }

    // Automation (CI/CD)
    DbSet<ApiKey> ApiKeys { get; }

    DbSet<Attachment> Attachments { get; }

    DbSet<SharedStepGroup> SharedStepGroups { get; }

    DbSet<AutomationPublication> AutomationPublications { get; }
}
