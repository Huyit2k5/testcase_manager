using System.ComponentModel.DataAnnotations;

namespace Acme.TestCaseManagement.TestCases.Dtos;

public class TestStepDto
{
    /// <summary>Null when adding a new step; set to keep the identity of an existing step.</summary>
    public Guid? Id { get; set; }

    /// <summary>One-based position. Ignored on input: the order of the list is authoritative.</summary>
    public int StepOrder { get; set; }

    [Required]
    [StringLength(TestStepConsts.MaxTextLength)]
    public string Action { get; set; } = string.Empty;

    [Required]
    [StringLength(TestStepConsts.MaxTextLength)]
    public string ExpectedResult { get; set; } = string.Empty;

    [StringLength(TestStepConsts.MaxTextLength)]
    public string? TestData { get; set; }

    /// <summary>
    /// The group of shared steps this step was copied from, or null. Read-only: the link is made by inserting a group into the
    /// test case, kept while the content is unchanged, and lost when the step is edited or the group is detached.
    /// </summary>
    public Guid? SharedStepGroupId { get; set; }

    /// <summary>The revision of the group when it was copied. Read-only.</summary>
    public int? SharedStepRevision { get; set; }

    /// <summary>The name of the group, for showing. Read-only; null when the group no longer exists.</summary>
    public string? SharedStepGroupName { get; set; }

    /// <summary>True when the group has a newer revision than the one copied. Read-only.</summary>
    public bool SharedStepOutdated { get; set; }
}
