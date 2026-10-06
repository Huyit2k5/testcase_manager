namespace Acme.TestCaseManagement.QualityGates.Dtos;

public class EvaluateQualityGateInput
{
    /// <summary>Evaluate one test plan. Give either this or <see cref="MilestoneId"/>, not both.</summary>
    public Guid? TestPlanId { get; set; }

    /// <summary>Evaluate every test plan that belongs to this milestone.</summary>
    public Guid? MilestoneId { get; set; }

    /// <summary>The gate to use. When omitted the default gate is used, else the built-in baseline.</summary>
    public Guid? QualityGateId { get; set; }
}
