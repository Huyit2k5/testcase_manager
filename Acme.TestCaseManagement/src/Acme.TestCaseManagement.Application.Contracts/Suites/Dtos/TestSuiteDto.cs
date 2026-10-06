using Volo.Abp.Application.Dtos;

namespace Acme.TestCaseManagement.Suites.Dtos;

public class TestSuiteDto : AuditedEntityDto<Guid>
{
    public Guid? ParentId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int Order { get; set; }
}
