using System.ComponentModel.DataAnnotations;
using Acme.TestCaseManagement.Enums;

namespace Acme.TestCaseManagement.SharedSteps.Dtos;

public class SharedStepDto
{
    /// <summary>Null when adding a step; set to keep the identity of an existing one.</summary>
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
}

public class SharedStepGroupDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Goes up by one whenever the steps of the group change. A test case that copied an older one is behind.</summary>
    public int Revision { get; set; }

    public List<SharedStepDto> Steps { get; set; } = new();

    public DateTime CreationTime { get; set; }

    public DateTime? LastModificationTime { get; set; }
}

public class SharedStepGroupSummaryDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int Revision { get; set; }

    public int StepCount { get; set; }

    /// <summary>The number of test cases that have steps copied from the group.</summary>
    public int UsedByCount { get; set; }
}

public class GetSharedStepGroupsInput
{
    /// <summary>Part of the name or the description.</summary>
    public string? Filter { get; set; }
}

public class CreateUpdateSharedStepGroupDto
{
    [Required]
    [StringLength(SharedStepGroupConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(SharedStepGroupConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>The complete ordered list, at least one and at most 50. On update, steps that are left out are removed.</summary>
    [Required]
    [MinLength(1)]
    [MaxLength(SharedStepGroupConsts.MaxSteps)]
    public List<SharedStepDto> Steps { get; set; } = new();
}

public class SharedStepUsageDto
{
    public Guid TestCaseId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public TestCaseStatus Status { get; set; }

    /// <summary>The oldest revision among the steps of the test case that came from the group.</summary>
    public int LinkedRevision { get; set; }

    public int LinkedStepCount { get; set; }

    /// <summary>True when the group has a newer revision than the one the test case copied.</summary>
    public bool IsOutdated { get; set; }
}

public class UpdateSharedStepUsersInput
{
    /// <summary>The test cases to bring up to date. Leave it out to update every test case that is behind.</summary>
    public List<Guid>? TestCaseIds { get; set; }

    /// <summary>Not used any more: an approved test case goes back to review, and the note is given when it is approved again.</summary>
    [StringLength(TestCaseConsts.MaxChangeSummaryLength)]
    public string? ChangeSummary { get; set; }
}

public class UpdateSharedStepUsersResultDto
{
    public int Updated { get; set; }

    public List<string> Codes { get; set; } = new();

    /// <summary>How many of them were approved and so went back to Under review.</summary>
    public int SentToReview { get; set; }
}
