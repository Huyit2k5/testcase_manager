using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace Acme.TestCaseManagement.SharedSteps;

/// <summary>One step of a <see cref="SharedStepGroup"/>. Owned by its group.</summary>
public class SharedStep : Entity<Guid>
{
    public virtual Guid SharedStepGroupId { get; protected set; }

    /// <summary>One-based position within the group.</summary>
    public virtual int StepOrder { get; protected set; }

    public virtual string Action { get; protected set; }

    public virtual string ExpectedResult { get; protected set; }

    public virtual string? TestData { get; protected set; }

    protected SharedStep()
    {
        Action = default!;
        ExpectedResult = default!;
    }

    internal SharedStep(Guid id, Guid groupId, int stepOrder, string action, string expectedResult, string? testData)
        : base(id)
    {
        SharedStepGroupId = groupId;
        StepOrder = stepOrder;
        Action = Text(action, nameof(action));
        ExpectedResult = Text(expectedResult, nameof(expectedResult));
        TestData = Check.Length(testData, nameof(testData), TestStepConsts.MaxTextLength);
    }

    internal void Update(string action, string expectedResult, string? testData)
    {
        Action = Text(action, nameof(action));
        ExpectedResult = Text(expectedResult, nameof(expectedResult));
        TestData = Check.Length(testData, nameof(testData), TestStepConsts.MaxTextLength);
    }

    internal void SetOrder(int stepOrder)
    {
        StepOrder = stepOrder;
    }

    internal bool ContentEquals(string action, string expectedResult, string? testData) =>
        string.Equals(Action, action, StringComparison.Ordinal)
        && string.Equals(ExpectedResult, expectedResult, StringComparison.Ordinal)
        && string.Equals(TestData ?? string.Empty, testData ?? string.Empty, StringComparison.Ordinal);

    private static string Text(string value, string parameterName) =>
        Check.NotNullOrWhiteSpace(value, parameterName, TestStepConsts.MaxTextLength);
}
