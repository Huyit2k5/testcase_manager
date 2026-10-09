namespace Acme.TestCaseManagement;

public static class TestCaseManagementErrorCodes
{
    // Suites
    public const string CircularSuiteDependency = "TestCaseManagement:CircularSuiteDependency";
    public const string SuiteNotFound = "TestCaseManagement:SuiteNotFound";
    public const string SuiteNotEmpty = "TestCaseManagement:SuiteNotEmpty";

    // Test cases
    public const string DuplicateTestCaseCode = "TestCaseManagement:DuplicateTestCaseCode";
    public const string InvalidTestCaseStatusTransition = "TestCaseManagement:InvalidTestCaseStatusTransition";
    public const string TestCaseHasNoSteps = "TestCaseManagement:TestCaseHasNoSteps";
    public const string TestCaseNotApproved = "TestCaseManagement:TestCaseNotApproved";
    public const string InvalidStepOrder = "TestCaseManagement:InvalidStepOrder";

    // Plans
    public const string TestPlanNotFound = "TestCaseManagement:TestPlanNotFound";
    public const string InvalidTestPlanStatusTransition = "TestCaseManagement:InvalidTestPlanStatusTransition";
    public const string InvalidTestPlanDates = "TestCaseManagement:InvalidTestPlanDates";
    public const string TestPlanHasRuns = "TestCaseManagement:TestPlanHasRuns";

    // Runs
    public const string InvalidExecutionStatus = "TestCaseManagement:InvalidExecutionStatus";
    public const string TestRunAlreadyCompleted = "TestCaseManagement:TestRunAlreadyCompleted";
    public const string TestCaseAlreadyInRun = "TestCaseManagement:TestCaseAlreadyInRun";
    public const string TestPlanArchived = "TestCaseManagement:TestPlanArchived";
    public const string TestRunItemNotFound = "TestCaseManagement:TestRunItemNotFound";
    public const string DuplicateTestRunItem = "TestCaseManagement:DuplicateTestRunItem";

    // Defects
    public const string DefectRequiresFailedExecution = "TestCaseManagement:DefectRequiresFailedExecution";
    public const string DuplicateDefectLink = "TestCaseManagement:DuplicateDefectLink";
    public const string InvalidDefectUrl = "TestCaseManagement:InvalidDefectUrl";

    // Requirements
    public const string DuplicateRequirementCode = "TestCaseManagement:DuplicateRequirementCode";

    // Quality gates and sign-off
    public const string QualityGateNotPassed = "TestCaseManagement:QualityGateNotPassed";
    public const string DuplicateQualityGateName = "TestCaseManagement:DuplicateQualityGateName";
    public const string InvalidSignOffScope = "TestCaseManagement:InvalidSignOffScope";
    public const string SignOffScopeEmpty = "TestCaseManagement:SignOffScopeEmpty";
    public const string SignOffRequiresUser = "TestCaseManagement:SignOffRequiresUser";
    public const string SignOffNotPending = "TestCaseManagement:SignOffNotPending";
    public const string DuplicateSignOffApproval = "TestCaseManagement:DuplicateSignOffApproval";

    // Automation (CI/CD)
    public const string DuplicateAutomationId = "TestCaseManagement:DuplicateAutomationId";
    public const string AutomationTooManyResults = "TestCaseManagement:AutomationTooManyResults";
    public const string IdempotencyKeyReused = "TestCaseManagement:IdempotencyKeyReused";
    public const string AutomationPublishInProgress = "TestCaseManagement:AutomationPublishInProgress";
    public const string InvalidApiKeyExpiry = "TestCaseManagement:InvalidApiKeyExpiry";
    public const string InvalidAutomationRun = "TestCaseManagement:InvalidAutomationRun";

    // Shared steps
    public const string DuplicateSharedStepGroupName = "TestCaseManagement:DuplicateSharedStepGroupName";
    public const string SharedStepGroupHasNoSteps = "TestCaseManagement:SharedStepGroupHasNoSteps";
    public const string SharedStepGroupTooLarge = "TestCaseManagement:SharedStepGroupTooLarge";
    public const string SharedStepGroupInUse = "TestCaseManagement:SharedStepGroupInUse";
    public const string SharedStepsNotLinked = "TestCaseManagement:SharedStepsNotLinked";
    public const string SharedStepGroupAlreadyUsed = "TestCaseManagement:SharedStepGroupAlreadyUsed";

    // Concurrency
    public const string OperationInProgress = "TestCaseManagement:OperationInProgress";

    // Step suggestions (AI hook)
    public const string StepSuggestionNotConfigured = "TestCaseManagement:StepSuggestionNotConfigured";
    public const string StepSuggestionFailed = "TestCaseManagement:StepSuggestionFailed";
    public const string StepSuggestionNoUsableSteps = "TestCaseManagement:StepSuggestionNoUsableSteps";

    // Tags
    public const string InvalidTag = "TestCaseManagement:InvalidTag";
    public const string TooManyTags = "TestCaseManagement:TooManyTags";

    // Attachments
    public const string AttachmentEmpty = "TestCaseManagement:AttachmentEmpty";
    public const string AttachmentTooLarge = "TestCaseManagement:AttachmentTooLarge";
    public const string AttachmentTypeNotAllowed = "TestCaseManagement:AttachmentTypeNotAllowed";
    public const string AttachmentTooMany = "TestCaseManagement:AttachmentTooMany";
    public const string AttachmentOwnerNotFound = "TestCaseManagement:AttachmentOwnerNotFound";
    public const string AttachmentFileMissing = "TestCaseManagement:AttachmentFileMissing";

    // Projects
    public const string ProjectNotFound = "TestCaseManagement:ProjectNotFound";
    public const string ProjectArchived = "TestCaseManagement:ProjectArchived";
    public const string InvalidProjectKey = "TestCaseManagement:InvalidProjectKey";
    public const string DuplicateProjectKey = "TestCaseManagement:DuplicateProjectKey";
    public const string ProjectNotEmpty = "TestCaseManagement:ProjectNotEmpty";
    public const string DifferentProject = "TestCaseManagement:DifferentProject";

    // Import and export
    public const string ExportTooLarge = "TestCaseManagement:ExportTooLarge";
}
