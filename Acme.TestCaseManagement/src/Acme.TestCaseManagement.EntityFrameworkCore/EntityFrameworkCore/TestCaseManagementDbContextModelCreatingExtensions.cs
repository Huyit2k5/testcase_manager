using Acme.TestCaseManagement.Attachments;
using Acme.TestCaseManagement.Automation;
using Acme.TestCaseManagement.Plans;
using Acme.TestCaseManagement.Quality;
using Acme.TestCaseManagement.Requirements;
using Acme.TestCaseManagement.Runs;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.TestCases;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace Acme.TestCaseManagement.EntityFrameworkCore;

public static class TestCaseManagementDbContextModelCreatingExtensions
{
    /// <summary>
    /// Entry point used by <see cref="TestCaseManagementDbContext"/> and by host applications that
    /// embed this module in their own DbContext.
    /// </summary>
    /// <remarks>
    /// Every entity mapped here MUST call <c>b.ConfigureByConvention()</c>. For <c>IMultiTenant</c>
    /// entities that is what registers the ABP tenant query filter, and for audited / soft-deleted
    /// entities it maps the audit and <c>IsDeleted</c> columns. Table names use
    /// <see cref="TestCaseManagementDbProperties.DbTablePrefix"/> (e.g. <c>TcmTestCases</c>).
    /// </remarks>
    public static void ConfigureTestCaseManagement(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<TestSuite>(b =>
        {
            b.ToTable(TableName("Suites"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(TestSuiteConsts.MaxNameLength);
            b.Property(x => x.Description).HasMaxLength(TestSuiteConsts.MaxDescriptionLength);

            // Self-reference; Restrict because suites are soft-deleted and a parent must not vanish under live children.
            b.HasOne<TestSuite>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(x => new { x.TenantId, x.ParentId, x.Order });
        });

        builder.Entity<TestCase>(b =>
        {
            b.ToTable(TableName("TestCases"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(TestCaseConsts.MaxCodeLength);
            b.Property(x => x.Title).IsRequired().HasMaxLength(TestCaseConsts.MaxTitleLength);
            b.Property(x => x.Description).HasMaxLength(TestCaseConsts.MaxTextLength);
            b.Property(x => x.Preconditions).HasMaxLength(TestCaseConsts.MaxTextLength);
            b.Property(x => x.Postconditions).HasMaxLength(TestCaseConsts.MaxTextLength);
            b.Property(x => x.AutomationId).HasMaxLength(TestCaseConsts.MaxAutomationIdLength);

            b.HasOne<TestSuite>().WithMany().HasForeignKey(x => x.SuiteId).OnDelete(DeleteBehavior.Restrict).IsRequired();

            // Not unique at database level: soft-deleted rows keep their code, so uniqueness is enforced
            // by TestCaseManager against non-deleted rows.
            b.HasIndex(x => new { x.TenantId, x.Code });
            b.HasIndex(x => x.SuiteId);
            b.HasIndex(x => x.Status);
            b.HasIndex(x => x.AutomationId);

            b.HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.TestCaseId).IsRequired().OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TestStep>(b =>
        {
            b.ToTable(TableName("TestSteps"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Action).IsRequired().HasMaxLength(TestStepConsts.MaxTextLength);
            b.Property(x => x.ExpectedResult).IsRequired().HasMaxLength(TestStepConsts.MaxTextLength);
            b.Property(x => x.TestData).HasMaxLength(TestStepConsts.MaxTextLength);

            b.HasIndex(x => new { x.TestCaseId, x.StepOrder });
        });

        builder.Entity<TestCaseVersion>(b =>
        {
            b.ToTable(TableName("TestCaseVersions"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Title).IsRequired().HasMaxLength(TestCaseConsts.MaxTitleLength);
            b.Property(x => x.Preconditions).HasMaxLength(TestCaseConsts.MaxTextLength);
            b.Property(x => x.Postconditions).HasMaxLength(TestCaseConsts.MaxTextLength);
            b.Property(x => x.ChangeSummary).HasMaxLength(TestCaseConsts.MaxChangeSummaryLength);
            b.Property(x => x.StepsJson).IsRequired(); // unbounded text / JSON

            b.HasOne<TestCase>().WithMany().HasForeignKey(x => x.TestCaseId).OnDelete(DeleteBehavior.Restrict).IsRequired();

            // A version number is allocated once per test case.
            b.HasIndex(x => new { x.TestCaseId, x.VersionNumber }).IsUnique();
        });

        ConfigureExecutionCycle(builder);
        ConfigureTraceability(builder);
        ConfigureQuality(builder);
        ConfigureAutomation(builder);
    }

    private static void ConfigureAutomation(ModelBuilder builder)
    {
        builder.Entity<ApiKey>(b =>
        {
            b.ToTable(TableName("ApiKeys"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(ApiKeyConsts.MaxNameLength);
            b.Property(x => x.KeyPrefix).IsRequired().HasMaxLength(ApiKeyConsts.KeyPrefixLength);
            b.Property(x => x.KeyHash).IsRequired().HasMaxLength(SignOffConsts.HashLength);

            // The lookup of a request: the prefix narrows the keys to compare to one or two.
            b.HasIndex(x => x.KeyPrefix);
        });

        builder.Entity<Attachment>(b =>
        {
            b.ToTable(TableName("Attachments"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.FileName).IsRequired().HasMaxLength(AttachmentConsts.MaxFileNameLength);
            b.Property(x => x.ContentType).IsRequired().HasMaxLength(AttachmentConsts.MaxContentTypeLength);
            b.Property(x => x.Sha256).IsRequired().HasMaxLength(SignOffConsts.HashLength);
            b.Property(x => x.Description).HasMaxLength(AttachmentConsts.MaxDescriptionLength);
            b.Ignore(x => x.BlobName);

            // Everything a screen asks is "the files of this owner".
            b.HasIndex(x => new { x.OwnerType, x.OwnerId });
        });

        builder.Entity<AutomationPublication>(b =>
        {
            b.ToTable(TableName("AutomationPublications"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(AutomationConsts.MaxIdempotencyKeyLength);
            b.Property(x => x.RequestHash).IsRequired().HasMaxLength(SignOffConsts.HashLength);
            b.Property(x => x.ResponseJson).IsRequired(); // unbounded text / JSON

            // Two requests with the same key cannot both be recorded. (A database that treats NULLs as distinct, such as
            // SQLite and PostgreSQL, does not apply this to a host without tenants; the service checks first anyway.)
            b.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
        });
    }

    private static void ConfigureTraceability(ModelBuilder builder)
    {
        builder.Entity<Requirement>(b =>
        {
            b.ToTable(TableName("Requirements"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Code).IsRequired().HasMaxLength(RequirementConsts.MaxCodeLength);
            b.Property(x => x.Title).IsRequired().HasMaxLength(RequirementConsts.MaxTitleLength);
            b.Property(x => x.Description).HasMaxLength(RequirementConsts.MaxTextLength);
            b.Property(x => x.AcceptanceCriteria).HasMaxLength(RequirementConsts.MaxTextLength);

            // Not unique: soft-deleted requirements keep their code. RequirementManager enforces uniqueness.
            b.HasIndex(x => new { x.TenantId, x.Code });
            b.HasIndex(x => x.MilestoneId);
        });

        builder.Entity<RequirementTestCase>(b =>
        {
            b.ToTable(TableName("RequirementTestCases"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            // One row per (requirement, test case) pair; unlinking soft-deletes it and linking again restores it.
            b.HasKey(x => new { x.RequirementId, x.TestCaseId });

            b.HasOne<Requirement>().WithMany().HasForeignKey(x => x.RequirementId).OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne<TestCase>().WithMany().HasForeignKey(x => x.TestCaseId).OnDelete(DeleteBehavior.Restrict).IsRequired();

            b.HasIndex(x => x.TestCaseId);
        });
    }

    private static void ConfigureQuality(ModelBuilder builder)
    {
        builder.Entity<DefectLink>(b =>
        {
            b.ToTable(TableName("DefectLinks"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.ExternalSystem).IsRequired().HasMaxLength(DefectLinkConsts.MaxExternalSystemLength);
            b.Property(x => x.IssueKey).IsRequired().HasMaxLength(DefectLinkConsts.MaxIssueKeyLength);
            b.Property(x => x.IssueUrl).HasMaxLength(DefectLinkConsts.MaxIssueUrlLength);
            b.Property(x => x.Severity).IsRequired();
            b.Property(x => x.IsResolved).IsRequired();

            // Executions are append-only and are never deleted, so a link must never cascade into them.
            b.HasOne<TestExecution>().WithMany().HasForeignKey(x => x.TestExecutionId).OnDelete(DeleteBehavior.Restrict).IsRequired();

            // Not unique: soft-deleted links keep their key. Duplicates are rejected by DefectLinkManager.
            b.HasIndex(x => x.TestExecutionId);
            b.HasIndex(x => new { x.ExternalSystem, x.IssueKey });
            b.HasIndex(x => new { x.IsResolved, x.Severity }); // the quality gate counts open defects by severity
        });

        builder.Entity<QualityGate>(b =>
        {
            b.ToTable(TableName("QualityGates"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(QualityGateConsts.MaxNameLength);
            b.Property(x => x.Description).HasMaxLength(QualityGateConsts.MaxDescriptionLength);
            b.Property(x => x.MinPassRate).HasPrecision(5, 2);

            // Not unique: soft-deleted gates keep their name. QualityGateManager enforces unique names and one default.
            b.HasIndex(x => new { x.TenantId, x.Name });
            b.HasIndex(x => new { x.TenantId, x.IsDefault });
        });

        builder.Entity<SignOffReport>(b =>
        {
            b.ToTable(TableName("SignOffReports"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Title).IsRequired().HasMaxLength(SignOffConsts.MaxTitleLength);
            b.Property(x => x.QualityGateName).IsRequired().HasMaxLength(QualityGateConsts.MaxNameLength);
            b.Property(x => x.MinPassRate).HasPrecision(5, 2);
            b.Property(x => x.SummaryStatsJson).IsRequired(); // unbounded text / JSON
            b.Property(x => x.SnapshotHash).IsRequired().HasMaxLength(SignOffConsts.HashLength);

            // A report must keep pointing at the plan and gate it was made for.
            b.HasOne<TestPlan>().WithMany().HasForeignKey(x => x.TestPlanId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<QualityGate>().WithMany().HasForeignKey(x => x.QualityGateId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Approvals).WithOne().HasForeignKey(a => a.SignOffReportId).IsRequired().OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(x => new { x.TenantId, x.TestPlanId, x.Status });
            b.HasIndex(x => new { x.TenantId, x.MilestoneId, x.Status });
        });

        builder.Entity<SignOffApproval>(b =>
        {
            b.ToTable(TableName("SignOffApprovals"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.ApproverName).IsRequired().HasMaxLength(SignOffConsts.MaxApproverNameLength);
            b.Property(x => x.ApproverRole).HasMaxLength(SignOffConsts.MaxRoleLength);
            b.Property(x => x.Comment).HasMaxLength(SignOffConsts.MaxCommentLength);
            b.Property(x => x.Signature).IsRequired().HasMaxLength(SignOffConsts.HashLength);

            // One approval per user and report, even if two requests race.
            b.HasIndex(x => new { x.SignOffReportId, x.ApproverUserId }).IsUnique();
        });
    }

    private static void ConfigureExecutionCycle(ModelBuilder builder)
    {
        builder.Entity<TestPlan>(b =>
        {
            b.ToTable(TableName("TestPlans"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(TestPlanConsts.MaxNameLength);
            b.Property(x => x.Description).HasMaxLength(TestPlanConsts.MaxDescriptionLength);

            b.HasIndex(x => new { x.TenantId, x.Status });
            b.HasIndex(x => x.MilestoneId);
        });

        builder.Entity<TestRun>(b =>
        {
            b.ToTable(TableName("TestRuns"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Title).IsRequired().HasMaxLength(TestRunConsts.MaxTitleLength);
            b.Property(x => x.Environment).IsRequired().HasMaxLength(TestRunConsts.MaxEnvironmentLength);

            // Derived from the items; never persisted.
            b.Ignore(x => x.CompletionPercentage);

            b.HasOne<TestPlan>().WithMany().HasForeignKey(x => x.TestPlanId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.TestRunId).IsRequired().OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(x => x.TestPlanId);
            b.HasIndex(x => new { x.TenantId, x.Status });
        });

        builder.Entity<TestRunItem>(b =>
        {
            b.ToTable(TableName("TestRunItems"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            // The version a run item points at must never disappear from under it.
            b.HasOne<TestCaseVersion>().WithMany().HasForeignKey(x => x.TestCaseVersionId).OnDelete(DeleteBehavior.Restrict).IsRequired();

            b.HasIndex(x => x.TestRunId);
            b.HasIndex(x => x.TestCaseVersionId);
            b.HasIndex(x => x.AssignedUserId);
            b.HasIndex(x => new { x.TestRunId, x.TestCaseVersionId }).IsUnique();
        });

        builder.Entity<TestExecution>(b =>
        {
            b.ToTable(TableName("TestExecutions"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.ActualResult).HasMaxLength(TestExecutionConsts.MaxActualResultLength);

            b.HasOne<TestRunItem>().WithMany().HasForeignKey(x => x.TestRunItemId).OnDelete(DeleteBehavior.Restrict).IsRequired();

            // Attempt numbers are allocated once per item, so concurrent testers cannot create a duplicate attempt.
            b.HasIndex(x => new { x.TestRunItemId, x.AttemptNumber }).IsUnique();
        });
    }

    public static string TableName(string name) => TestCaseManagementDbProperties.DbTablePrefix + name;
}
