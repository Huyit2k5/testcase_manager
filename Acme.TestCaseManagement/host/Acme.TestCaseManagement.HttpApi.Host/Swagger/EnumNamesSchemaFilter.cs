#if NET10_0_OR_GREATER
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
#else
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
#endif
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Acme.TestCaseManagement.Swagger;

/// <summary>
/// Enums travel as integers (the ABP default), which says nothing to a reader of the OpenAPI document. This adds the
/// member names to the description (shown by Swagger UI) and as <c>x-enum-varnames</c> (used by code generators).
/// </summary>
public class EnumNamesSchemaFilter : ISchemaFilter
{
#if NET10_0_OR_GREATER
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
#else
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (!context.Type.IsEnum)
        {
            return;
        }

        var names = Enum.GetNames(context.Type);
        var members = names.Select(name => $"{Convert.ToInt64(Enum.Parse(context.Type, name))} = {name}");

        schema.Description = string.IsNullOrEmpty(schema.Description)
            ? $"Values: {string.Join(", ", members)}."
            : $"{schema.Description} Values: {string.Join(", ", members)}.";

        var varNames = new OpenApiArray();
        varNames.AddRange(names.Select(name => (IOpenApiAny)new OpenApiString(name)));
        schema.Extensions["x-enum-varnames"] = varNames;
    }
#endif
}
