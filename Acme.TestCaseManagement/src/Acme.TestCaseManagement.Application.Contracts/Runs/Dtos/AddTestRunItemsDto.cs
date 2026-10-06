using System.ComponentModel.DataAnnotations;

namespace Acme.TestCaseManagement.Runs.Dtos;

public class AddTestRunItemsDto
{
    [Required]
    public List<Guid> TestCaseIds { get; set; } = new();

    /// <summary>Optional tester for all added items.</summary>
    public Guid? AssignedUserId { get; set; }
}

public class AssignTestRunItemDto
{
    /// <summary>Null clears the assignment.</summary>
    public Guid? AssignedUserId { get; set; }
}
