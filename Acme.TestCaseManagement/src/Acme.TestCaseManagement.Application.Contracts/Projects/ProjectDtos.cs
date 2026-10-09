using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace Acme.TestCaseManagement.Projects;

public class ProjectDto : AuditedEntityDto<Guid>
{
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsArchived { get; set; }

    /// <summary>What is in the project, for the page that manages projects.</summary>
    public int SuiteCount { get; set; }

    public int TestCaseCount { get; set; }

    public int PlanCount { get; set; }

    public int RequirementCount { get; set; }

    public int RunCount { get; set; }
}

public class GetProjectListInput
{
    /// <summary>Archived projects are left out unless this is true.</summary>
    public bool IncludeArchived { get; set; }
}

public class CreateProjectDto
{
    /// <summary>2 to 10 capital letters or digits, starting with a letter (for example EINV). It cannot be changed later.</summary>
    [Required]
    [StringLength(ProjectConsts.MaxKeyLength)]
    public string Key { get; set; } = string.Empty;

    [Required]
    [StringLength(ProjectConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(ProjectConsts.MaxDescriptionLength)]
    public string? Description { get; set; }
}

public class UpdateProjectDto
{
    [Required]
    [StringLength(ProjectConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [StringLength(ProjectConsts.MaxDescriptionLength)]
    public string? Description { get; set; }
}
