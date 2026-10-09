using Acme.TestCaseManagement.Enums;
using Acme.TestCaseManagement.Permissions;
using Acme.TestCaseManagement.Repositories;
using Acme.TestCaseManagement.Suites;
using Acme.TestCaseManagement.Suites.Dtos;
using Acme.TestCaseManagement.TestCases;
using Acme.TestCaseManagement.TestCases.Dtos;
using Acme.TestCaseManagement.Transfer.Dtos;
using Acme.TestCaseManagement.Transfer.Tabular;
using Acme.TestCaseManagement.Projects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;

namespace Acme.TestCaseManagement.Transfer;

[Authorize(TestCaseManagementPermissions.TestCases.Default)]
public class TestCaseTransferAppService : TestCaseManagementAppService, ITestCaseTransferAppService
{
    private readonly ITestCaseRepository _testCaseRepository;
    private readonly IRepository<TestSuite, Guid> _suiteRepository;
    private readonly ITestCaseAppService _testCaseService;
    private readonly ITestSuiteAppService _suiteService;
    private readonly TestCaseManagementTransferOptions _options;
    private readonly ProjectManager _projectManager;

    public TestCaseTransferAppService(
        ITestCaseRepository testCaseRepository,
        IRepository<TestSuite, Guid> suiteRepository,
        ITestCaseAppService testCaseService,
        ITestSuiteAppService suiteService,
        IOptions<TestCaseManagementTransferOptions> options,
        ProjectManager projectManager)
    {
        _projectManager = projectManager;
        _testCaseRepository = testCaseRepository;
        _suiteRepository = suiteRepository;
        _testCaseService = testCaseService;
        _suiteService = suiteService;
        _options = options.Value;
    }

    public virtual async Task<IRemoteStreamContent> ExportAsync(ExportTestCasesInput input)
    {
        var projectId = input.ProjectId;
        var suites = await _suiteRepository.GetListAsync(x => projectId == null || x.ProjectId == projectId);
        var tree = new SuiteTree(suites);

        var filter = new TestCaseFilter
        {
            SearchText = input.Filter,
            SuiteIds = input.SuiteId.HasValue
                ? (input.IncludeDescendantSuites ? tree.SelfAndDescendantIds(input.SuiteId.Value) : new HashSet<Guid> { input.SuiteId.Value })
                : (projectId.HasValue ? suites.Select(s => s.Id).ToHashSet() : null),
            Status = input.Status,
            Priority = input.Priority,
            Severity = input.Severity,
            ExecutionType = input.ExecutionType,
            Kind = input.Kind,
            Layer = input.Layer,
            Tags = input.Tags,
            HasAutomationId = input.HasAutomationId,
        };

        var count = await _testCaseRepository.GetFilteredCountAsync(filter);
        if (count > _options.MaxExportTestCases)
        {
            throw new BusinessException(TestCaseManagementErrorCodes.ExportTooLarge)
                .WithData("Count", count)
                .WithData("Limit", _options.MaxExportTestCases);
        }

        var testCases = await _testCaseRepository.GetFilteredListAsync(
            filter, nameof(TestCase.Code), 0, _options.MaxExportTestCases, includeDetails: true);

        var table = TestCaseSheet.Export(testCases, tree.PathOf);
        var bytes = TableFile.Write(table, input.Format, "Test cases");

        return new RemoteStreamContent(
            new MemoryStream(bytes),
            $"test-cases-{Clock.Now:yyyyMMdd-HHmmss}{TableFile.Extension(input.Format)}",
            TableFile.ContentType(input.Format),
            bytes.Length);
    }

    [Authorize(TestCaseManagementPermissions.TestCases.Create)]
    public virtual async Task<ImportReportDto> ImportAsync(ImportTestCasesInput input)
    {
        var messages = new TransferMessages(L);
        var report = new ImportReportDto { DryRun = input.DryRun };

        ParsedSheet sheet;
        try
        {
            var table = TableFile.Read(await UploadReader.ReadAsync(input.File, _options.MaxFileSizeBytes), _options);
            sheet = TestCaseSheet.Parse(table, messages);
        }
        catch (TableException exception)
        {
            report.FileErrors.Add(messages.Describe(exception.Problem, _options));
            return report;
        }

        report.FileErrors.AddRange(sheet.FileErrors);
        report.IgnoredColumns.AddRange(sheet.IgnoredColumns);

        var project = await _projectManager.ResolveForNewAsync(input.ProjectId);
        var tree = new SuiteTree(await _suiteRepository.GetListAsync(x => x.ProjectId == project.Id));
        if (input.DefaultSuiteId.HasValue && !tree.Contains(input.DefaultSuiteId.Value))
        {
            report.FileErrors.Add(messages.Get("Import:DefaultSuiteNotFound"));
        }

        if (report.FileErrors.Count > 0)
        {
            return report;
        }

        // Pass 1: check every test case against the library. Nothing is written.
        var plans = await PlanAsync(sheet, tree, input, messages, SafeFileName(Path.GetFileName(input.File.FileName ?? string.Empty)));

        foreach (var plan in plans)
        {
            report.Items.Add(new ImportItemResultDto
            {
                Row = plan.Draft.Row,
                Code = plan.Draft.Code.Length == 0 ? null : plan.Draft.Code,
                Outcome = plan.Outcome,
                Messages = plan.Messages,
            });
        }

        report.Total = plans.Count;
        report.Created = plans.Count(p => p.Outcome == ImportOutcome.Created);
        report.Updated = plans.Count(p => p.Outcome == ImportOutcome.Updated);
        report.Skipped = plans.Count(p => p.Outcome == ImportOutcome.Skipped);
        report.Invalid = plans.Count(p => p.Outcome == ImportOutcome.Invalid);

        if (report.Invalid > 0)
        {
            return report;
        }

        report.CreatedSuites = tree.Planned.Count;
        if (input.DryRun)
        {
            return report;
        }

        // Pass 2: write everything. This runs in one unit of work, so an unexpected failure leaves nothing behind.
        foreach (var suite in tree.Planned)
        {
            var created = await _suiteService.CreateAsync(new CreateTestSuiteDto { Name = suite.Name, ParentId = suite.Parent?.Id, ProjectId = project.Id });
            suite.Id = created.Id;
        }

        // A test case that takes an Automation ID waits for the one that gives it up (the rule of uniqueness holds at every step).
        var pending = plans.Where(p => p.Outcome is ImportOutcome.Created or ImportOutcome.Updated).ToList();
        var written = new HashSet<Guid>();
        while (pending.Count > 0)
        {
            var ready = pending.Where(p => p.Releasing == null || written.Contains(p.Releasing.Id)).ToList();
            if (ready.Count == 0)
            {
                // Planning refuses a cycle, so this cannot happen; whatever is left is written as it comes.
                ready = pending;
            }

            foreach (var plan in ready)
            {
                plan.Dto!.SuiteId = plan.Suite?.Id ?? plan.SuiteId ?? plan.Dto.SuiteId;

                if (plan.Outcome == ImportOutcome.Created)
                {
                    await _testCaseService.CreateAsync(plan.Dto);
                }
                else
                {
                    await _testCaseService.UpdateAsync(plan.Existing!.Id, plan.Dto);
                    written.Add(plan.Existing.Id);
                }

                pending.Remove(plan);
            }
        }

        report.Imported = true;
        return report;
    }

    /// <summary>Decides, for every test case of the file, what the import would do and why it cannot.</summary>
    private async Task<List<CasePlan>> PlanAsync(
        ParsedSheet sheet, SuiteTree tree, ImportTestCasesInput input, TransferMessages messages, string fileName)
    {
        var canCreateSuites = await AuthorizationService.IsGrantedAsync(TestCaseManagementPermissions.TestSuites.Manage);
        var canUpdate = await AuthorizationService.IsGrantedAsync(TestCaseManagementPermissions.TestCases.Update);

        var codes = sheet.Drafts.Select(d => d.Code).Where(c => c.Length > 0).Distinct().ToList();
        var existing = (await _testCaseRepository.GetListAsync(x => codes.Contains(x.Code), includeDetails: true))
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var plans = new List<CasePlan>();
        var automationIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var finalIds = FinalAutomationIds(sheet, input, existing);

        foreach (var draft in sheet.Drafts)
        {
            var plan = new CasePlan(draft);
            plans.Add(plan);
            plan.Messages.AddRange(draft.Errors);

            if (draft.Code.Length == 0)
            {
                plan.Outcome = ImportOutcome.Invalid;
                continue;
            }

            existing.TryGetValue(draft.Code, out var current);
            plan.Existing = current;

            if (current == null || input.OnExisting == ImportConflictMode.Update)
            {
                if (current != null && !canUpdate)
                {
                    plan.Messages.Add(messages.Get("Import:UpdateNotAllowed"));
                }
                else
                {
                    ResolveSuite(plan, sheet, tree, input, messages, canCreateSuites);
                    CheckTitle(plan, sheet, messages);
                    await CheckAutomationIdAsync(plan, sheet, automationIds, finalIds, existing.Values, messages);
                }
            }

            if (plan.Messages.Count > 0)
            {
                plan.Outcome = ImportOutcome.Invalid;
                continue;
            }

            if (current != null && input.OnExisting == ImportConflictMode.Skip)
            {
                plan.Outcome = ImportOutcome.Skipped;
                plan.Messages.Add(messages.Get("Import:AlreadyExists"));
                continue;
            }

            plan.Dto = current == null ? NewDto(draft) : MergedDto(current, draft, sheet);
            plan.Dto.ChangeSummary = messages.Get("Import:ChangeSummary", ("FileName", fileName));

            if (current == null)
            {
                plan.Outcome = ImportOutcome.Created;
                continue;
            }

            // The suite of an existing test case is only known after the planned suites are created, so compare the rest.
            plan.Dto.SuiteId = plan.SuiteId ?? current.SuiteId;
            var unchanged = plan.Suite == null && IsSame(ToDto(current), plan.Dto);
            plan.Outcome = unchanged ? ImportOutcome.Skipped : ImportOutcome.Updated;
            if (unchanged)
            {
                plan.Messages.Add(messages.Get("Import:Unchanged"));
            }
        }

        return plans;
    }

    private static void ResolveSuite(
        CasePlan plan, ParsedSheet sheet, SuiteTree tree, ImportTestCasesInput input, TransferMessages messages, bool canCreateSuites)
    {
        var path = plan.Draft.SuitePath;

        if (!sheet.Columns.Contains(TestCaseSheet.Suite) || path.Length == 0)
        {
            // The default suite is for new test cases; an existing one stays where it is.
            if (plan.Existing != null)
            {
                return;
            }

            if (input.DefaultSuiteId.HasValue)
            {
                plan.SuiteId = input.DefaultSuiteId;
            }
            else
            {
                plan.Messages.Add(messages.Get("Import:SuiteMissing"));
            }

            return;
        }

        if (!SuitePath.TryParse(path, out var names) || names.Any(name => name.Length > TestSuiteConsts.MaxNameLength))
        {
            plan.Messages.Add(messages.Get("Import:SuiteInvalid", ("Path", path), ("Max", TestSuiteConsts.MaxNameLength)));
            return;
        }

        var lookup = tree.Find(names);
        switch (lookup.Kind)
        {
            case SuiteTree.LookupKind.Ambiguous:
                plan.Messages.Add(messages.Get("Import:SuiteAmbiguous", ("Path", path)));
                break;

            case SuiteTree.LookupKind.Missing when !canCreateSuites:
                plan.Messages.Add(messages.Get("Import:SuiteNotFound", ("Path", path)));
                break;

            case SuiteTree.LookupKind.Missing:
                plan.Suite = tree.Plan(names, lookup);
                break;

            default:
                // A suite planned by an earlier row has no id yet; one that exists is used as it is.
                plan.Suite = lookup.Node!.Id.HasValue ? null : lookup.Node;
                plan.SuiteId = lookup.Node.Id;
                break;
        }
    }

    /// <summary>
    /// An Automation ID names one test case (FR-019). Refused here, so that it shows in the report with its row, and not as an
    /// error half way through the import: the ID may not belong to another test case of the library or of the file.
    /// </summary>
    /// <summary>The Automation ID each existing test case of the file will have afterwards (null: none), when the file sets it.</summary>
    private static Dictionary<Guid, string?> FinalAutomationIds(ParsedSheet sheet, ImportTestCasesInput input, Dictionary<string, TestCase> existing)
    {
        var finalIds = new Dictionary<Guid, string?>();
        if (input.OnExisting != ImportConflictMode.Update || !sheet.Columns.Contains(TestCaseSheet.AutomationId))
        {
            return finalIds;
        }

        foreach (var draft in sheet.Drafts)
        {
            if (draft.Code.Length > 0 && existing.TryGetValue(draft.Code, out var current))
            {
                finalIds[current.Id] = TestCaseManager.NormalizeAutomationId(draft.AutomationId);
            }
        }

        return finalIds;
    }

    private async Task CheckAutomationIdAsync(
        CasePlan plan, ParsedSheet sheet, Dictionary<string, string> seen, Dictionary<Guid, string?> finalIds,
        IEnumerable<TestCase> existing, TransferMessages messages)
    {
        // A column that the file does not have leaves the ID of an existing test case as it is.
        if (plan.Existing != null && !sheet.Columns.Contains(TestCaseSheet.AutomationId))
        {
            return;
        }

        var automationId = TestCaseManager.NormalizeAutomationId(plan.Draft.AutomationId);
        if (automationId == null)
        {
            return;
        }

        var unchanged = plan.Existing != null && string.Equals(plan.Existing.AutomationId, automationId, StringComparison.Ordinal);
        var owner = unchanged ? null : await _testCaseRepository.FindByAutomationIdAsync(automationId);
        var takenInFile = seen.TryGetValue(automationId, out var otherCode) && !string.Equals(otherCode, plan.Draft.Code, StringComparison.OrdinalIgnoreCase);

        var taken = owner != null && owner.Id != plan.Existing?.Id;
        if (taken && GivenUpByTheFile(owner!, automationId, plan.Existing, finalIds, existing))
        {
            // The owner is in the file and gets another ID (or none): the ID is free once the owner is written.
            plan.Releasing = owner;
            taken = false;
        }

        if (taken || takenInFile)
        {
            plan.Messages.Add(messages.Get("Import:DuplicateAutomationId", ("AutomationId", automationId)));
        }

        seen.TryAdd(automationId, plan.Draft.Code);
    }

    /// <summary>
    /// True when the file moves the owner of an ID to another ID or clears it, and so the ID can be taken. Following the chain
    /// (A takes what B has, B takes what C has...) back to the test case itself is a swap or a cycle, which has no safe order.
    /// </summary>
    private static bool GivenUpByTheFile(
        TestCase owner, string automationId, TestCase? taker, Dictionary<Guid, string?> finalIds, IEnumerable<TestCase> existing)
    {
        var holders = existing
            .Where(x => !string.IsNullOrEmpty(x.AutomationId))
            .GroupBy(x => x.AutomationId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var current = owner;
        for (var steps = 0; steps <= finalIds.Count; steps++)
        {
            if (!finalIds.TryGetValue(current.Id, out var next) || string.Equals(next, automationId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (next == null || !holders.TryGetValue(next, out var holder) || holder.Id == current.Id)
            {
                return true;
            }

            if (taker != null && holder.Id == taker.Id)
            {
                return false;
            }

            current = holder;
        }

        return false;
    }

    /// <summary>A title is required for a new test case, and may not be blanked on an existing one.</summary>
    private static void CheckTitle(CasePlan plan, ParsedSheet sheet, TransferMessages messages)
    {
        var needed = plan.Existing == null || sheet.Columns.Contains(TestCaseSheet.Title);
        if (needed && plan.Draft.Title.Length == 0)
        {
            plan.Messages.Add(messages.Get("Import:ValueRequired", ("Column", TestCaseSheet.Title)));
        }
    }

    private static CreateUpdateTestCaseDto NewDto(TestCaseDraft draft)
    {
        return new CreateUpdateTestCaseDto
        {
            Code = draft.Code,
            Title = draft.Title,
            Description = draft.Description,
            Preconditions = draft.Preconditions,
            Postconditions = draft.Postconditions,
            Priority = draft.Priority,
            Severity = draft.Severity,
            Kind = draft.Kind,
            Layer = draft.Layer,
            ExecutionType = draft.ExecutionType,
            AutomationId = draft.AutomationId,
            IsFlaky = draft.IsFlaky,
            Tags = draft.Tags,
            Steps = draft.Steps.Select(s => new TestStepDto { Action = s.Action, ExpectedResult = s.ExpectedResult, TestData = s.TestData }).ToList(),
        };
    }

    /// <summary>The existing test case with the columns that the file has laid over it. A column that is absent changes nothing.</summary>
    private static CreateUpdateTestCaseDto MergedDto(TestCase current, TestCaseDraft draft, ParsedSheet sheet)
    {
        var dto = ToDto(current);
        var has = sheet.Columns;

        if (has.Contains(TestCaseSheet.Title)) { dto.Title = draft.Title; }
        if (has.Contains(TestCaseSheet.Description)) { dto.Description = draft.Description; }
        if (has.Contains(TestCaseSheet.Preconditions)) { dto.Preconditions = draft.Preconditions; }
        if (has.Contains(TestCaseSheet.Postconditions)) { dto.Postconditions = draft.Postconditions; }
        if (has.Contains(TestCaseSheet.Priority)) { dto.Priority = draft.Priority; }
        if (has.Contains(TestCaseSheet.Severity)) { dto.Severity = draft.Severity; }
        if (has.Contains(TestCaseSheet.Kind)) { dto.Kind = draft.Kind; }
        if (has.Contains(TestCaseSheet.Layer)) { dto.Layer = draft.Layer; }
        if (has.Contains(TestCaseSheet.ExecutionType)) { dto.ExecutionType = draft.ExecutionType; }
        if (has.Contains(TestCaseSheet.AutomationId)) { dto.AutomationId = draft.AutomationId; }
        if (has.Contains(TestCaseSheet.Flaky)) { dto.IsFlaky = draft.IsFlaky; }
        if (has.Contains(TestCaseSheet.Tags)) { dto.Tags = draft.Tags; }

        if (new[] { TestCaseSheet.Action, TestCaseSheet.ExpectedResult, TestCaseSheet.TestData }.Any(has.Contains))
        {
            // The step at the same position keeps its identity; extra ones are removed or added.
            dto.Steps = draft.Steps
                .Select((s, i) => new TestStepDto
                {
                    Id = i < current.Steps.Count ? current.Steps.OrderBy(x => x.StepOrder).ElementAt(i).Id : null,
                    Action = s.Action,
                    ExpectedResult = s.ExpectedResult,
                    TestData = s.TestData,
                })
                .ToList();
        }

        return dto;
    }

    private static CreateUpdateTestCaseDto ToDto(TestCase testCase)
    {
        return new CreateUpdateTestCaseDto
        {
            SuiteId = testCase.SuiteId,
            Code = testCase.Code,
            Title = testCase.Title,
            Description = testCase.Description,
            Preconditions = testCase.Preconditions,
            Postconditions = testCase.Postconditions,
            Priority = testCase.Priority,
            Severity = testCase.Severity,
            Kind = testCase.Kind,
            Layer = testCase.Layer,
            ExecutionType = testCase.ExecutionType,
            AutomationId = testCase.AutomationId,
            IsFlaky = testCase.IsFlaky,
            Tags = testCase.Tags.Select(t => t.Name).ToList(),
            Steps = testCase.Steps
                .OrderBy(s => s.StepOrder)
                .Select(s => new TestStepDto { Id = s.Id, StepOrder = s.StepOrder, Action = s.Action, ExpectedResult = s.ExpectedResult, TestData = s.TestData })
                .ToList(),
        };
    }

    private static bool SameTags(IEnumerable<string>? a, IEnumerable<string>? b) =>
        (a ?? Enumerable.Empty<string>()).Select(TagNames.Normalize).Order().SequenceEqual((b ?? Enumerable.Empty<string>()).Select(TagNames.Normalize).Order());

    /// <summary>Whether two test cases have the same content. Blank and missing text are the same; step ids do not count.</summary>
    internal static bool IsSame(CreateUpdateTestCaseDto a, CreateUpdateTestCaseDto b)
    {
        static string Clean(string? value) => TestCaseSheet.Normalize(value ?? string.Empty);

        return a.SuiteId == b.SuiteId
               && Clean(a.Title) == Clean(b.Title)
               && Clean(a.Description) == Clean(b.Description)
               && Clean(a.Preconditions) == Clean(b.Preconditions)
               && Clean(a.Postconditions) == Clean(b.Postconditions)
               && Clean(a.AutomationId) == Clean(b.AutomationId)
               && a.Priority == b.Priority
               && a.Severity == b.Severity
               && a.Kind == b.Kind
               && a.Layer == b.Layer
               && a.ExecutionType == b.ExecutionType
               && a.IsFlaky == b.IsFlaky
               && SameTags(a.Tags, b.Tags)
               && a.Steps.Count == b.Steps.Count
               && a.Steps.Zip(b.Steps).All(pair =>
                   Clean(pair.First.Action) == Clean(pair.Second.Action)
                   && Clean(pair.First.ExpectedResult) == Clean(pair.Second.ExpectedResult)
                   && Clean(pair.First.TestData) == Clean(pair.Second.TestData));
    }

    /// <summary>What the import is going to do with one test case of the file.</summary>
    private sealed class CasePlan
    {
        public CasePlan(TestCaseDraft draft)
        {
            Draft = draft;
        }

        public TestCaseDraft Draft { get; }

        public ImportOutcome Outcome { get; set; }

        public List<string> Messages { get; } = new();

        public TestCase? Existing { get; set; }

        public CreateUpdateTestCaseDto? Dto { get; set; }

        /// <summary>The suite when it exists already, or the default suite.</summary>
        public Guid? SuiteId { get; set; }

        /// <summary>The test case of the file that has to give up the Automation ID this one takes, so it is written first.</summary>
        public TestCase? Releasing { get; set; }

        /// <summary>A suite that the import has yet to create: its id is known once pass 2 has made it.</summary>
        internal SuiteTree.Node? Suite { get; set; }
    }

    /// <summary>
    /// The file name goes into the change summary of every version the import creates, which is limited in length and shown in the history:
    /// control characters are dropped and a long name is cut, so that an odd file name cannot make the import fail after the check passed.
    /// </summary>
    internal static string SafeFileName(string? name)
    {
        var clean = new string((name ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length <= 100)
        {
            return clean;
        }

        var cut = char.IsHighSurrogate(clean[99]) ? 99 : 100;
        return clean[..cut] + "...";
    }
}
