using System.ComponentModel.DataAnnotations;

namespace Acme.TestCaseManagement.Suites.Dtos;

/// <summary>Drag-and-drop result: the new parent (null for root level) and the zero-based position among its children.</summary>
public class MoveTestSuiteDto
{
    public Guid? NewParentId { get; set; }

    [Range(0, int.MaxValue)]
    public int NewOrder { get; set; }
}
