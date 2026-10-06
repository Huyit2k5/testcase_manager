namespace Acme.TestCaseManagement.TestCases;

/// <summary>Desired state of one step. A null <paramref name="Id"/> adds a new step; a known Id updates it in place.</summary>
public record TestStepInput(Guid? Id, string Action, string ExpectedResult, string? TestData);
