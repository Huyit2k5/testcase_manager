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

    // Import and export
    public const string ExportTooLarge = "TestCaseManagement:ExportTooLarge";
}
