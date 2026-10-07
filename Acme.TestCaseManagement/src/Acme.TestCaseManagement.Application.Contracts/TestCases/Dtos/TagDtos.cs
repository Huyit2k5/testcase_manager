using System.ComponentModel.DataAnnotations;

namespace Acme.TestCaseManagement.TestCases.Dtos;

public class SetTestCaseTagsDto
{
    /// <summary>The complete list of tags; an empty list removes them all.</summary>
    [Required]
    [MaxLength(200)]
    public List<string> Tags { get; set; } = new();
}

public class TagSummaryDto
{
    public string Name { get; set; } = string.Empty;

    /// <summary>The number of test cases that have the tag.</summary>
    public int Count { get; set; }
}
