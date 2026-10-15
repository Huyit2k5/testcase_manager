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
    public static void ConfigureTestCaseManagement(this ModelBuilder builder, ITestCaseManagementDbContext context)
    {
        Check.NotNull(builder, nameof(builder));
        Check.NotNull(context, nameof(context));

        builder.Entity<Project>(b =>
        {
            b.ToTable(TableName("Projects"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            // Leaves out what is in a project that the caller may not see (see ProjectAccessScope); ABP joins it with its own filters.
            AddProjectFilter(b, context, x => x.Id);

            b.Property(x => x.Key).IsRequired().HasMaxLength(ProjectConsts.MaxKeyLength);
            b.Property(x => x.Name).IsRequired().HasMaxLength(ProjectConsts.MaxNameLength);
            b.Property(x => x.Description).HasMaxLength(ProjectConsts.MaxDescriptionLength);

            // Not unique: a soft-deleted project keeps its key. ProjectManager enforces uniqueness.
            b.HasIndex(x => new { x.TenantId, x.Key });
        });

        builder.Entity<ProjectMember>(b =>
        {
            b.ToTable(TableName("ProjectMembers"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            // No foreign keys: the user belongs to the host, and a project is only soft-deleted.
            b.HasIndex(x => new { x.ProjectId, x.UserId }).IsUnique();
            b.HasIndex(x => x.UserId);
        });

        builder.Entity<TestSuite>(b =>
        {
            b.ToTable(TableName("Suites"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            // Leaves out what is in a project that the caller may not see (see ProjectAccessScope); ABP joins it with its own filters.
            AddProjectFilter(b, context, x => x.ProjectId);

            b.Property(x => x.Name).IsRequired().HasMaxLength(TestSuiteConsts.MaxNameLength);
            b.Property(x => x.Description).HasMaxLength(TestSuiteConsts.MaxDescriptionLength);

            // Self-reference; Restrict because suites are soft-deleted and a parent must not vanish under live children.
            b.HasOne<TestSuite>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(x => new { x.TenantId, x.ParentId, x.Order });
            // The project is a plain reference (no foreign key): a project is archived, and deleted only when it is empty.
            b.HasIndex(x => x.ProjectId);
        });

        builder.Entity<TestCase>(b =>
        {
            b.ToTable(TableName("TestCases"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            // Leaves out what is in a project that the caller may not see (see ProjectAccessScope); ABP joins it with its own filters.
            AddProjectFilter(b, context, x => x.ProjectId);

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
            b.HasIndex(x => x.ProjectId);
            b.HasIndex(x => x.Status);
            b.HasIndex(x => x.AutomationId);

            b.HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.TestCaseId).IsRequired().OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Tags).WithOne().HasForeignKey(x => x.TestCaseId).IsRequired().OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TestCaseHistoryEntry>(b =>
        {
            b.ToTable(TableName("TestCaseHistory"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.FromValue).HasMaxLength(TestCaseHistoryConsts.MaxValueLength);
            b.Property(x => x.ToValue).HasMaxLength(TestCaseHistoryConsts.MaxValueLength);
            b.Property(x => x.Note).HasMaxLength(TestCaseHistoryConsts.MaxNoteLength);

            // No foreign key: the history stays when the test case is deleted. It is read by test case, newest first.
            b.HasIndex(x => new { x.TestCaseId, x.CreationTime });
        });

        builder.Entity<TestCaseTag>(b =>
        {
            b.ToTable(TableName("TestCaseTags"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(TagConsts.MaxLength);
            b.Property(x => x.NormalizedName).IsRequired().HasMaxLength(TagConsts.MaxLength);

            // A test case has a tag once; and "the test cases with this tag" is the question a filter asks.
            b.HasIndex(x => new { x.TestCaseId, x.NormalizedName }).IsUnique();
            b.HasIndex(x => x.NormalizedName);
        });

        builder.Entity<TestStep>(b =>
        {
            b.ToTable(TableName("TestSteps"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Action).IsRequired().HasMaxLength(TestStepConsts.MaxTextLength);
            b.Property(x => x.ExpectedResult).IsRequired().HasMaxLength(TestStepConsts.MaxTextLength);
            b.Property(x => x.TestData).HasMaxLength(TestStepConsts.MaxTextLength);

            b.HasIndex(x => new { x.TestCaseId, x.StepOrder });

            // The steps that came from a group of shared steps; "which test cases use this group" is asked from here.
            b.HasIndex(x => x.SharedStepGroupId);
        });

        builder.Entity<SharedStepGroup>(b =>
        {
            b.ToTable(TableName("SharedStepGroups"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(SharedStepGroupConsts.MaxNameLength);
            b.Property(x => x.Description).HasMaxLength(SharedStepGroupConsts.MaxDescriptionLength);

            // Not unique at database level: soft-deleted rows keep their name, so SharedStepGroupManager checks against the live rows.
            b.HasIndex(x => new { x.TenantId, x.Name });

            b.HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.SharedStepGroupId).IsRequired().OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<SharedStep>(b =>
        {
            b.ToTable(TableName("SharedSteps"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.Action).IsRequired().HasMaxLength(TestStepConsts.MaxTextLength);
            b.Property(x => x.ExpectedResult).IsRequired().HasMaxLength(TestStepConsts.MaxTextLength);
            b.Property(x => x.TestData).HasMaxLength(TestStepConsts.MaxTextLength);

            b.HasIndex(x => new { x.SharedStepGroupId, x.StepOrder });
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

        ConfigureExecutionCycle(builder, context);
        ConfigureTraceability(builder, context);
        ConfigureQuality(builder, context);
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

    private static void ConfigureTraceability(ModelBuilder builder, ITestCaseManagementDbContext context)
    {
        builder.Entity<Requirement>(b =>
        {
            b.ToTable(TableName("Requirements"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            // Leaves out what is in a project that the caller may not see (see ProjectAccessScope); ABP joins it with its own filters.
            AddProjectFilter(b, context, x => x.ProjectId);

            b.Property(x => x.Code).IsRequired().HasMaxLength(RequirementConsts.MaxCodeLength);
            b.Property(x => x.Title).IsRequired().HasMaxLength(RequirementConsts.MaxTitleLength);
            b.Property(x => x.Description).HasMaxLength(RequirementConsts.MaxTextLength);
            b.Property(x => x.AcceptanceCriteria).HasMaxLength(RequirementConsts.MaxTextLength);

            // Not unique: soft-deleted requirements keep their code. RequirementManager enforces uniqueness.
            b.HasIndex(x => new { x.TenantId, x.Code });
            b.HasIndex(x => x.MilestoneId);
            b.HasIndex(x => x.ProjectId);
        });

        builder.Entity<RequirementTestCase>(b =>
        {
            b.ToTable(TableName("RequirementTestCases"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            // One row per (requirement, test case) pair; unlinking soft-deletes it and linking again restores it.
            b.HasKey(x => new { x.RequirementId, x.TestCaseId });

            b.Property(x => x.SuspectReason).HasMaxLength(RequirementConsts.MaxSuspectReasonLength);

            b.HasOne<Requirement>().WithMany().HasForeignKey(x => x.RequirementId).OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne<TestCase>().WithMany().HasForeignKey(x => x.TestCaseId).OnDelete(DeleteBehavior.Restrict).IsRequired();

            b.HasIndex(x => x.TestCaseId);
        });
    }

    private static void ConfigureQuality(ModelBuilder builder, ITestCaseManagementDbContext context)
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

            // Leaves out what is in a project that the caller may not see (see ProjectAccessScope); ABP joins it with its own filters.
            AddProjectFilter(b, context, x => x.ProjectId);

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
            b.HasIndex(x => x.ProjectId);
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

    private static void ConfigureExecutionCycle(ModelBuilder builder, ITestCaseManagementDbContext context)
    {
        builder.Entity<TestPlan>(b =>
        {
            b.ToTable(TableName("TestPlans"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            // Leaves out what is in a project that the caller may not see (see ProjectAccessScope); ABP joins it with its own filters.
            AddProjectFilter(b, context, x => x.ProjectId);

            b.Property(x => x.Name).IsRequired().HasMaxLength(TestPlanConsts.MaxNameLength);
            b.Property(x => x.Description).HasMaxLength(TestPlanConsts.MaxDescriptionLength);

            b.HasIndex(x => new { x.TenantId, x.Status });
            b.HasIndex(x => x.MilestoneId);
            b.HasIndex(x => x.ProjectId);
        });

        builder.Entity<TestRun>(b =>
        {
            b.ToTable(TableName("TestRuns"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            // Leaves out what is in a project that the caller may not see (see ProjectAccessScope); ABP joins it with its own filters.
            AddProjectFilter(b, context, x => x.ProjectId);

            b.Property(x => x.Title).IsRequired().HasMaxLength(TestRunConsts.MaxTitleLength);
            b.Property(x => x.Environment).IsRequired().HasMaxLength(TestRunConsts.MaxEnvironmentLength);

            // Derived from the items; never persisted.
            b.Ignore(x => x.CompletionPercentage);

            b.HasOne<TestPlan>().WithMany().HasForeignKey(x => x.TestPlanId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.TestRunId).IsRequired().OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(x => x.TestPlanId);
            b.HasIndex(x => x.ProjectId);
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

        builder.Entity<TestStepResult>(b =>
        {
            b.ToTable(TableName("TestStepResults"), TestCaseManagementDbProperties.DbSchema);
            b.ConfigureByConvention();

            b.Property(x => x.ActualResult).HasMaxLength(TestExecutionConsts.MaxActualResultLength);

            // Results are append-only like the attempts they belong to, so nothing cascades into them.
            b.HasOne<TestExecution>().WithMany().HasForeignKey(x => x.TestExecutionId).OnDelete(DeleteBehavior.Restrict).IsRequired();

            // One result for a step in an attempt.
            b.HasIndex(x => new { x.TestExecutionId, x.StepId }).IsUnique();
        });
    }

    /// <summary>
    /// Adds the project filter to the filter that ABP already put on the entity (soft delete, tenant): EF takes one such filter per
    /// entity, so the two are joined with "and". ABP sets its filters when the context is built, before this runs.
    /// <para>
    /// The filter reads two members of the <paramref name="context"/> itself, because EF re-reads only the members of the context
    /// for every query (the way ABP's own filters read <c>CurrentTenantId</c>); a static member would be read once and kept.
    /// </para>
    /// </summary>
    private static void AddProjectFilter<T>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> builder,
        ITestCaseManagementDbContext context,
        System.Linq.Expressions.Expression<Func<T, Guid>> projectOf)
        where T : class
    {
        var parameter = projectOf.Parameters[0];
        var self = System.Linq.Expressions.Expression.Constant(context, context.GetType());
        var asInterface = System.Linq.Expressions.Expression.Convert(self, typeof(ITestCaseManagementDbContext));
        var unrestricted = System.Linq.Expressions.Expression.Property(asInterface, nameof(ITestCaseManagementDbContext.IsAllProjectsInReach));
        var visible = System.Linq.Expressions.Expression.Property(asInterface, nameof(ITestCaseManagementDbContext.ProjectsInReach));
        var contains = System.Linq.Expressions.Expression.Call(visible, typeof(List<Guid>).GetMethod(nameof(List<Guid>.Contains), new[] { typeof(Guid) })!, projectOf.Body);
        var projectFilter = System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(System.Linq.Expressions.Expression.OrElse(unrestricted, contains), parameter);

#if NET10_0_OR_GREATER
        var existing = builder.Metadata.GetDeclaredQueryFilters().FirstOrDefault(filter => filter.Key == null)?.Expression;
#else
        // EF Core 9 has a single, unnamed filter per entity.
        var existing = builder.Metadata.GetQueryFilter();
#endif
        if (existing == null)
        {
            builder.HasQueryFilter(projectFilter);
            return;
        }

        var shared = System.Linq.Expressions.Expression.Parameter(typeof(T), "x");
        var left = new ReplaceParameter(existing.Parameters[0], shared).Visit(existing.Body)!;
        var right = new ReplaceParameter(parameter, shared).Visit(projectFilter.Body)!;
        builder.HasQueryFilter(System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(System.Linq.Expressions.Expression.AndAlso(left, right), shared));
    }

    private sealed class ReplaceParameter : System.Linq.Expressions.ExpressionVisitor
    {
        private readonly System.Linq.Expressions.ParameterExpression _from;
        private readonly System.Linq.Expressions.ParameterExpression _to;

        public ReplaceParameter(System.Linq.Expressions.ParameterExpression from, System.Linq.Expressions.ParameterExpression to)
        {
            _from = from;
            _to = to;
        }

        protected override System.Linq.Expressions.Expression VisitParameter(System.Linq.Expressions.ParameterExpression node) => node == _from ? _to : node;
    }

    public static string TableName(string name) => TestCaseManagementDbProperties.DbTablePrefix + name;
}
