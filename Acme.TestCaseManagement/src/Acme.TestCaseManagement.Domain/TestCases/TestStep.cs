using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace Acme.TestCaseManagement.TestCases;

/// <summary>One ordered action / expected-result pair. Owned by its <see cref="TestCase"/>.</summary>
public class TestStep : Entity<Guid>
{
    public virtual Guid TestCaseId { get; protected set; }

    /// <summary>One-based position within the test case.</summary>
    public virtual int StepOrder { get; protected set; }

    public virtual string Action { get; protected set; }

    public virtual string ExpectedResult { get; protected set; }

    public virtual string? TestData { get; protected set; }

    protected TestStep()
    {
        Action = default!;
        ExpectedResult = default!;
    }

    internal TestStep(Guid id, Guid testCaseId, int stepOrder, string action, string expectedResult, string? testData)
        : base(id)
    {
        TestCaseId = testCaseId;
        StepOrder = stepOrder;
        Action = ValidateText(action, nameof(action));
        ExpectedResult = ValidateText(expectedResult, nameof(expectedResult));
        TestData = Check.Length(testData, nameof(testData), TestStepConsts.MaxTextLength);
    }

    internal void Update(string action, string expectedResult, string? testData)
    {
        Action = ValidateText(action, nameof(action));
        ExpectedResult = ValidateText(expectedResult, nameof(expectedResult));
        TestData = Check.Length(testData, nameof(testData), TestStepConsts.MaxTextLength);
    }

    private static string ValidateText(string value, string parameterName)
    {
        return Check.NotNullOrWhiteSpace(value, parameterName, TestStepConsts.MaxTextLength);
    }

    internal void SetOrder(int stepOrder)
    {
        StepOrder = stepOrder;
    }
}
