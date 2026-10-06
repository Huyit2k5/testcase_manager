using System.Reflection;
using System.Xml.Linq;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Acme.TestCaseManagement.Swagger;

/// <summary>
/// The controllers implement the application service interfaces and carry only <c>&lt;inheritdoc /&gt;</c>, which
/// Swashbuckle does not expand. This copies the summary of the interface method onto the operation instead.
/// </summary>
public class InterfaceDocumentationOperationFilter : IOperationFilter
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> Summaries = new(LoadSummaries);

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!string.IsNullOrEmpty(operation.Summary) || context.MethodInfo.DeclaringType is not { } controller)
        {
            return;
        }

        foreach (var contract in controller.GetInterfaces())
        {
            var map = controller.GetInterfaceMap(contract);
            var index = Array.FindIndex(map.TargetMethods, target => target == context.MethodInfo);
            if (index < 0)
            {
                continue;
            }

            if (Summaries.Value.TryGetValue(MemberName(map.InterfaceMethods[index]), out var summary))
            {
                operation.Summary = summary;
                return;
            }
        }
    }

    private static IReadOnlyDictionary<string, string> LoadSummaries()
    {
        var summaries = new Dictionary<string, string>();

        foreach (var file in Directory.EnumerateFiles(AppContext.BaseDirectory, "Acme.TestCaseManagement.*.xml"))
        {
            foreach (var member in XDocument.Load(file).Descendants("member"))
            {
                var name = (string?)member.Attribute("name");
                var summary = member.Element("summary")?.Value;
                if (name is not null && !string.IsNullOrWhiteSpace(summary))
                {
                    summaries[name] = string.Join(' ', summary.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                }
            }
        }

        return summaries;
    }

    /// <summary>The name an XML documentation file gives a method: M:Namespace.Type.Method(ParameterTypes).</summary>
    private static string MemberName(MethodInfo method)
    {
        var parameters = method.GetParameters();
        var parameterList = parameters.Length == 0
            ? string.Empty
            : "(" + string.Join(",", parameters.Select(p => p.ParameterType.FullName!.Replace('+', '.'))) + ")";

        return $"M:{method.DeclaringType!.FullName!.Replace('+', '.')}.{method.Name}{parameterList}";
    }
}
