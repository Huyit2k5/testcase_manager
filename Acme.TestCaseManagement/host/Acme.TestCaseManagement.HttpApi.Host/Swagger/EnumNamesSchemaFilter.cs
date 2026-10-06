using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Acme.TestCaseManagement.Swagger;

/// <summary>
/// Enums travel as integers (the ABP default), which says nothing to a reader of the OpenAPI document. This adds the
/// member names to the description (shown by Swagger UI) and as <c>x-enum-varnames</c> (used by code generators).
/// </summary>
public class EnumNamesSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (!context.Type.IsEnum || schema is not OpenApiSchema enumSchema)
        {
            return;
        }

        var names = Enum.GetNames(context.Type);
        var members = names.Select(name => $"{Convert.ToInt64(Enum.Parse(context.Type, name))} = {name}");

        enumSchema.Description = string.IsNullOrEmpty(enumSchema.Description)
            ? $"Values: {string.Join(", ", members)}."
            : $"{enumSchema.Description} Values: {string.Join(", ", members)}.";

        enumSchema.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        enumSchema.Extensions["x-enum-varnames"] = new JsonNodeExtension(
            new JsonArray(names.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray()));
    }
}
