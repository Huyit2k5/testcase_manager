using Acme.TestCaseManagement.Enums;

namespace Acme.TestCaseManagement.Plans.Dtos;

public class ChangeTestPlanStatusDto
{
    public PlanStatus TargetStatus { get; set; }
}
