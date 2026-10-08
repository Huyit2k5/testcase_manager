using Acme.TestCaseManagement.Permissions;
using Acme.TestCaseManagement.Quality;
using Acme.TestCaseManagement.QualityGates.Dtos;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;

namespace Acme.TestCaseManagement.QualityGates;

[Authorize(TestCaseManagementPermissions.QualityGates.Default)]
public class QualityGateAppService : TestCaseManagementAppService, IQualityGateAppService
{
    private readonly IRepository<QualityGate, Guid> _gateRepository;
    private readonly QualityGateManager _gateManager;

    public QualityGateAppService(IRepository<QualityGate, Guid> gateRepository, QualityGateManager gateManager)
    {
        _gateRepository = gateRepository;
        _gateManager = gateManager;
    }

    public virtual async Task<QualityGateDto> GetAsync(Guid id)
    {
        return ObjectMapper.Map<QualityGate, QualityGateDto>(await _gateRepository.GetAsync(id));
    }

    public virtual async Task<List<QualityGateDto>> GetListAsync()
    {
        var gates = (await _gateRepository.GetListAsync()).OrderBy(g => g.Name).ToList();

        return ObjectMapper.Map<List<QualityGate>, List<QualityGateDto>>(gates);
    }

    [Authorize(TestCaseManagementPermissions.QualityGates.Manage)]
    public virtual async Task<QualityGateDto> CreateAsync(CreateUpdateQualityGateDto input)
    {
        // One default gate per tenant is kept by clearing the old one: two saves at once would both clear and both set.
        if (input.IsDefault)
        {
            await LockUntilTheRequestEndsAsync("qualitygate-default");
        }

        var gate = await _gateManager.CreateAsync(
            input.Name, input.MinPassRate, input.RequiredApprovals, input.Description, input.IsDefault);

        await _gateRepository.InsertAsync(gate, autoSave: true);

        return ObjectMapper.Map<QualityGate, QualityGateDto>(gate);
    }

    [Authorize(TestCaseManagementPermissions.QualityGates.Manage)]
    public virtual async Task<QualityGateDto> UpdateAsync(Guid id, CreateUpdateQualityGateDto input)
    {
        if (input.IsDefault)
        {
            await LockUntilTheRequestEndsAsync("qualitygate-default");
        }

        var gate = await _gateRepository.GetAsync(id);

        await _gateManager.UpdateAsync(
            gate, input.Name, input.MinPassRate, input.RequiredApprovals, input.Description, input.IsDefault);
        await _gateRepository.UpdateAsync(gate, autoSave: true);

        return ObjectMapper.Map<QualityGate, QualityGateDto>(gate);
    }

    [Authorize(TestCaseManagementPermissions.QualityGates.Manage)]
    public virtual async Task DeleteAsync(Guid id)
    {
        await _gateRepository.DeleteAsync(id);
    }

    public virtual async Task<QualityGateEvaluationDto> EvaluateAsync(EvaluateQualityGateInput input)
    {
        var evaluation = await _gateManager.EvaluateAsync(
            new QualityGateScope(input.TestPlanId, input.MilestoneId), input.QualityGateId);

        return QualityGateDtoFactory.ToDto(evaluation, CriterionLabel);
    }

    private string CriterionLabel(string code)
    {
        return L[$"QualityGate:Criterion:{code}"].Value;
    }
}
