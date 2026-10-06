namespace Acme.TestCaseManagement.Suites.Dtos;

/// <summary>A suite with its descendants, used to render the library tree.</summary>
public class TestSuiteTreeDto
{
    public Guid Id { get; set; }

    public Guid? ParentId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int Order { get; set; }

    /// <summary>Test cases directly in this suite (not including descendants).</summary>
    public int TestCaseCount { get; set; }

    public List<TestSuiteTreeDto> Children { get; set; } = new();
}
