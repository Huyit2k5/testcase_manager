using Acme.TestCaseManagement.Suites.Dtos;
using Volo.Abp.Application.Services;

namespace Acme.TestCaseManagement.Suites;

public interface ITestSuiteAppService : IApplicationService
{
    Task<TestSuiteDto> GetAsync(Guid id);

    /// <summary>Returns the whole library as a forest of root suites, each with nested children.</summary>
    Task<List<TestSuiteTreeDto>> GetTreeAsync();

    Task<TestSuiteDto> CreateAsync(CreateTestSuiteDto input);

    Task<TestSuiteDto> UpdateAsync(Guid id, UpdateTestSuiteDto input);

    /// <summary>Soft-deletes an empty suite. Suites with children or test cases are rejected.</summary>
    Task DeleteAsync(Guid id);

    Task<TestSuiteDto> MoveAsync(Guid id, MoveTestSuiteDto input);
}
