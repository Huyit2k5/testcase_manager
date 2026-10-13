using System.ComponentModel.DataAnnotations;

namespace Acme.TestCaseManagement.TestCases.Dtos;

public class DuplicateTestCaseDto
{
    /// <summary>The suite of the copy. Leave it out to keep the copy next to the original. It may be a suite of another project.</summary>
    public Guid? SuiteId { get; set; }

    /// <summary>The code of the copy. Leave it out to give the next code of the project ("EINV-008").</summary>
    [StringLength(TestCaseConsts.MaxCodeLength)]
    public string? Code { get; set; }

    /// <summary>The title of the copy. Leave it out to use the title of the original followed by "(copy)".</summary>
    [StringLength(TestCaseConsts.MaxTitleLength)]
    public string? Title { get; set; }
}
