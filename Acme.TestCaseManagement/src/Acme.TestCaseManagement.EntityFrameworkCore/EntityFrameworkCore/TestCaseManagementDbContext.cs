using Acme.TestCaseManagement.Attachments;
using Acme.TestCaseManagement.SharedSteps;
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
public class TestCaseManagementDbContext : AbpDbContext<TestCaseManagementDbContext>, ITestCaseManagementDbContext
{
    public DbSet<TestSuite> TestSuites { get; set; } = null!;

    public DbSet<TestCase> TestCases { get; set; } = null!;

    public DbSet<TestCaseVersion> TestCaseVersions { get; set; } = null!;

    public DbSet<TestPlan> TestPlans { get; set; } = null!;

    public DbSet<TestRun> TestRuns { get; set; } = null!;

    public DbSet<TestRunItem> TestRunItems { get; set; } = null!;

    public DbSet<TestExecution> TestExecutions { get; set; } = null!;

    public DbSet<Requirement> Requirements { get; set; } = null!;

    public DbSet<RequirementTestCase> RequirementTestCases { get; set; } = null!;

    public DbSet<DefectLink> DefectLinks { get; set; } = null!;

    public DbSet<QualityGate> QualityGates { get; set; } = null!;

    public DbSet<SignOffReport> SignOffReports { get; set; } = null!;

    public DbSet<ApiKey> ApiKeys { get; set; } = null!;

    public DbSet<Attachment> Attachments { get; set; } = null!;

    public DbSet<SharedStepGroup> SharedStepGroups { get; set; } = null!;

    public DbSet<AutomationPublication> AutomationPublications { get; set; } = null!;

    public TestCaseManagementDbContext(DbContextOptions<TestCaseManagementDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ConfigureTestCaseManagement();
    }
}
