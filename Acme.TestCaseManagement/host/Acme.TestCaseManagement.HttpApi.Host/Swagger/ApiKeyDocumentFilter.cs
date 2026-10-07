using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Acme.TestCaseManagement.Swagger;

/// <summary>
/// The document asks for a bearer token everywhere. The operation that publishes automation results also accepts an API
/// key, so its security lists both as alternatives.
/// </summary>
public class ApiKeyDocumentFilter : IDocumentFilter
{
    public const string PublishOperationId = "AutomationResults_Publish";

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        foreach (var operation in swaggerDoc.Paths.Values.SelectMany(path => path.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>()))
        {
            if (operation.OperationId != PublishOperationId)
            {
                continue;
            }

            operation.Security = new List<OpenApiSecurityRequirement>
            {
                new() { [new OpenApiSecuritySchemeReference("Bearer", swaggerDoc)] = new List<string>() },
                new() { [new OpenApiSecuritySchemeReference("ApiKey", swaggerDoc)] = new List<string>() },
            };
        }
    }
}
