using System.ComponentModel.DataAnnotations;

namespace Acme.TestCaseManagement.Automation.Dtos;

public class ApiKeyDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>The start of the key, to recognize it in a list. It is not enough to use the key.</summary>
    public string KeyPrefix { get; set; } = string.Empty;

    public DateTime? ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    /// <summary>When the key last authenticated a request; null if it never has. Updated at most once a minute.</summary>
    public DateTime? LastUsedAt { get; set; }

    public DateTime CreationTime { get; set; }

    public Guid? CreatorId { get; set; }

    /// <summary>False once the key is revoked or has expired.</summary>
    public bool IsActive { get; set; }
}

public class CreateApiKeyDto
{
    /// <summary>What the key is for, for example "GitHub Actions - main".</summary>
    [Required]
    [StringLength(ApiKeyConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>When the key stops working. Leave it out for a key that works until it is revoked.</summary>
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>The only answer that contains the secret of a key. It cannot be read again.</summary>
public class ApiKeyCreatedDto : ApiKeyDto
{
    /// <summary>The key to give to the pipeline, sent in the X-Api-Key header. It is shown once and is not stored.</summary>
    public string Key { get; set; } = string.Empty;
}
