using Acme.TestCaseManagement.QualityGates;
using Acme.TestCaseManagement.QualityGates.Dtos;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;

namespace Acme.TestCaseManagement.Controllers;

[RemoteService(Name = "TestCaseManagement")]
[Area("test-case-management")]
[Route("api/test-case-management/quality-gates")]
public class QualityGateController : TestCaseManagementController, IQualityGateAppService
{
    private readonly IQualityGateAppService _gateAppService;

    public QualityGateController(IQualityGateAppService gateAppService)
    {
        _gateAppService = gateAppService;
    }

    /// <inheritdoc />
    [HttpGet("{id:guid}")]
    public virtual Task<QualityGateDto> GetAsync(Guid id)
    {
        return _gateAppService.GetAsync(id);
    }

    /// <inheritdoc />
    [HttpGet]
    public virtual Task<List<QualityGateDto>> GetListAsync()
    {
        return _gateAppService.GetListAsync();
    }

    /// <inheritdoc />
    [HttpPost]
    public virtual Task<QualityGateDto> CreateAsync(CreateUpdateQualityGateDto input)
    {
        return _gateAppService.CreateAsync(input);
    }

    /// <inheritdoc />
    [HttpPut("{id:guid}")]
    public virtual Task<QualityGateDto> UpdateAsync(Guid id, CreateUpdateQualityGateDto input)
    {
        return _gateAppService.UpdateAsync(id, input);
    }

    /// <inheritdoc />
    [HttpDelete("{id:guid}")]
    public virtual Task DeleteAsync(Guid id)
    {
        return _gateAppService.DeleteAsync(id);
    }

    /// <inheritdoc />
    [HttpPost("evaluate")]
    public virtual Task<QualityGateEvaluationDto> EvaluateAsync(EvaluateQualityGateInput input)
    {
        // POST because the input is a body; nothing is created or changed.
        return _gateAppService.EvaluateAsync(input);
    }
}
