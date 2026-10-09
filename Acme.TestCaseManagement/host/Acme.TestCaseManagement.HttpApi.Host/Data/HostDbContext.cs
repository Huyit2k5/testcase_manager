using Acme.TestCaseManagement.Attachments;
using Acme.TestCaseManagement.SharedSteps;
using Acme.TestCaseManagement.Automation;
using Acme.TestCaseManagement.EntityFrameworkCore;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Projects;
using Acme.TestCaseManagement.Quality;
using Acme.TestCaseManagement.Requirements;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.TestCases;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Identity.EntityFrameworkCore;
using Volo.Abp.PermissionManagement;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;

namespace Acme.TestCaseManagement.Data;

/// <summary>
/// The host owns one database: users and roles (ABP Identity), permission grants (ABP Permission Management) and the
/// tables of this module, mapped through the module's <c>ConfigureTestCaseManagement</c> extension.
/// </summary>
[ReplaceDbContext(typeof(ITestCaseManagementDbContext))]
[ReplaceDbContext(typeof(IIdentityDbContext))]
[ReplaceDbContext(typeof(IPermissionManagementDbContext))]
[ConnectionStringName("Default")]
public class HostDbContext : AbpDbContext<HostDbContext>, ITestCaseManagementDbContext, IIdentityDbContext, IPermissionManagementDbContext
{
    // Test Case Management
    public DbSet<Project> Projects { get; set; } = null!;

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

    // Identity
    public DbSet<IdentityUser> Users { get; set; } = null!;
    public DbSet<IdentityRole> Roles { get; set; } = null!;
    public DbSet<IdentityClaimType> ClaimTypes { get; set; } = null!;
    public DbSet<OrganizationUnit> OrganizationUnits { get; set; } = null!;
    public DbSet<IdentitySecurityLog> SecurityLogs { get; set; } = null!;
    public DbSet<IdentityLinkUser> LinkUsers { get; set; } = null!;
    public DbSet<IdentityUserDelegation> UserDelegations { get; set; } = null!;
    public DbSet<IdentitySession> Sessions { get; set; } = null!;

    // Permission management
    public DbSet<PermissionGroupDefinitionRecord> PermissionGroups { get; set; } = null!;
    public DbSet<PermissionDefinitionRecord> Permissions { get; set; } = null!;
    public DbSet<PermissionGrant> PermissionGrants { get; set; } = null!;
    public DbSet<ResourcePermissionGrant> ResourcePermissionGrants { get; set; } = null!;

    public HostDbContext(DbContextOptions<HostDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ConfigureIdentity();
        builder.ConfigurePermissionManagement();
        builder.ConfigureTestCaseManagement();
    }
}
