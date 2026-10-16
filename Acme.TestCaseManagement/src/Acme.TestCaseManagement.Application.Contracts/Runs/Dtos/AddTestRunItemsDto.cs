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

public class AssignTestRunItemsDto
{
    /// <summary>The items of the run to assign. A repeated id counts once.</summary>
    [Required]
    [MinLength(1)]
    [MaxLength(500)]
    public List<Guid> ItemIds { get; set; } = new();

    /// <summary>Null clears the assignment of every item.</summary>
    public Guid? AssignedUserId { get; set; }
}
