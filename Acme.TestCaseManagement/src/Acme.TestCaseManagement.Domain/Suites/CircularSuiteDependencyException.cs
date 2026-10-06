using Volo.Abp;

namespace Acme.TestCaseManagement.Suites;

public class CircularSuiteDependencyException : BusinessException
{
    public CircularSuiteDependencyException(string suiteName)
        : base(TestCaseManagementErrorCodes.CircularSuiteDependency)
    {
        WithData("SuiteName", suiteName);
    }
}
