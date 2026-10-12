using Acme.TestCaseManagement.Enums;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace Acme.TestCaseManagement.TestCases;

/// <summary>Adds the lines of the history of a test case. The lines are saved with the unit of work of the change they describe.</summary>
public class TestCaseHistoryRecorder : DomainService
{
    private readonly IRepository<TestCaseHistoryEntry, Guid> _repository;

    public TestCaseHistoryRecorder(IRepository<TestCaseHistoryEntry, Guid> repository)
    {
        _repository = repository;
    }

    public virtual async Task RecordAsync(
        Guid testCaseId,
        TestCaseHistoryType type,
        string? fromValue = null,
        string? toValue = null,
        int? versionNumber = null,
        string? note = null)
    {
        await _repository.InsertAsync(new TestCaseHistoryEntry(
            GuidGenerator.Create(), CurrentTenant.Id, testCaseId, type, fromValue, toValue, versionNumber, note));
    }

    /// <summary>
    /// A change of content: the line for the change, and a line for the status when the edit sent an approved test case back to review.
    /// </summary>
    public virtual async Task RecordContentChangeAsync(TestCase testCase, TestCaseHistoryType type, string? note, bool sentBackForReview)
    {
        await RecordAsync(testCase.Id, type, note: note);
        if (sentBackForReview)
        {
            await RecordAsync(testCase.Id, TestCaseHistoryType.StatusChanged, nameof(TestCaseStatus.Approved), nameof(TestCaseStatus.UnderReview), note: "Edited after approval");
        }
    }

    /// <summary>A change of status, and the version that approving published.</summary>
    public virtual async Task RecordStatusChangeAsync(TestCase testCase, TestCaseStatus from, TestCaseVersion? published, string? changeSummary)
    {
        await RecordAsync(testCase.Id, TestCaseHistoryType.StatusChanged, from.ToString(), testCase.Status.ToString());
        if (published != null)
        {
            await RecordAsync(testCase.Id, TestCaseHistoryType.VersionPublished, versionNumber: published.VersionNumber, note: changeSummary);
        }
    }
}
