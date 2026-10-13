using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace Acme.TestCaseManagement.Projects;

public class ProjectDto : AuditedEntityDto<Guid>
{
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsArchived { get; set; }

    /// <summary>True: only the members and the people who manage projects see this project.</summary>
    public bool IsRestricted { get; set; }

    /// <summary>The number of members; they count only while the project is restricted.</summary>
    public int MemberCount { get; set; }

    /// <summary>What is in the project, for the page that manages projects.</summary>
    public int SuiteCount { get; set; }

    public int TestCaseCount { get; set; }

    public int PlanCount { get; set; }

    public int RequirementCount { get; set; }

    public int RunCount { get; set; }
}

/// <summary>Who may see a project: everyone, or only the members.</summary>
public class ProjectAccessDto
{
    /// <summary>False: everyone who may use the module sees the project. True: only <see cref="UserIds"/> and the people who manage projects.</summary>
    public bool IsRestricted { get; set; }

    /// <summary>The members: the ids that the host knows its users by.</summary>
    [MaxLength(ProjectConsts.MaxMembers)]
    public List<Guid> UserIds { get; set; } = new();
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
