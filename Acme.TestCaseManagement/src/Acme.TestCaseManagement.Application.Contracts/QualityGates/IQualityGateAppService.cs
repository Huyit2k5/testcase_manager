using Acme.TestCaseManagement.QualityGates.Dtos;
using Volo.Abp.Application.Services;

namespace Acme.TestCaseManagement.QualityGates;

public interface IQualityGateAppService : IApplicationService
{
    Task<QualityGateDto> GetAsync(Guid id);

    /// <summary>All gates of the tenant, ordered by name. There are only a handful, so the list is not paged.</summary>
    Task<List<QualityGateDto>> GetListAsync();

    Task<QualityGateDto> CreateAsync(CreateUpdateQualityGateDto input);

    Task<QualityGateDto> UpdateAsync(Guid id, CreateUpdateQualityGateDto input);

    /// <summary>
    /// Soft-deletes a gate. Sign-off reports keep the thresholds they were signed against, so they are not affected.
    /// </summary>
    Task DeleteAsync(Guid id);

    /// <summary>
    /// Evaluates the gate for a test plan or milestone without creating anything, and returns the metrics and the
    /// result of every criterion.
    /// </summary>
    Task<QualityGateEvaluationDto> EvaluateAsync(EvaluateQualityGateInput input);
}
