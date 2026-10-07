using Acme.TestCaseManagement.Attachments;
using Acme.TestCaseManagement.Automation;
using Acme.TestCaseManagement.Plans;
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
    // Master library (design time)
    DbSet<TestSuite> TestSuites { get; }

    DbSet<TestCase> TestCases { get; }

    DbSet<TestCaseVersion> TestCaseVersions { get; }

    // Execution cycle (runtime)
    DbSet<TestPlan> TestPlans { get; }

    DbSet<TestRun> TestRuns { get; }

    DbSet<TestRunItem> TestRunItems { get; }

    DbSet<TestExecution> TestExecutions { get; }

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

    DbSet<AutomationPublication> AutomationPublications { get; }
}
