using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Validation;

namespace Acme.TestCaseManagement.Validation;

/// <summary>
/// Refuses an enum value that is not a member of the enum. JSON binding turns the number 99 into <c>(TestResultStatus)99</c> without
/// complaint, and the rest of the module would then store and count a state that does not exist. It looks at every enum property
/// (and nullable enum property) of the module's own input objects, and of the objects and lists inside them, so a new DTO is
/// covered without remembering an attribute.
/// </summary>
public class EnumRangeValidationContributor : IObjectValidationContributor, ITransientDependency
{
    private const int MaxDepth = 8;

    public Task AddErrorsAsync(ObjectValidationContext context)
    {
        Check(context.ValidatingObject, string.Empty, 0, context.Errors, new HashSet<object>(ReferenceEqualityComparer.Instance));
        return Task.CompletedTask;
    }

    private static void Check(object? instance, string path, int depth, List<ValidationResult> errors, HashSet<object> seen)
    {
        if (instance == null || depth > MaxDepth || !IsOwnType(instance.GetType()) || !seen.Add(instance))
        {
            return;
        }

        foreach (var property in instance.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var name = path + property.Name;
            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var value = property.GetValue(instance);

            if (propertyType.IsEnum)
            {
                if (value != null && !Enum.IsDefined(propertyType, value))
                {
                    errors.Add(new ValidationResult(
                        $"The value {Convert.ChangeType(value, Enum.GetUnderlyingType(propertyType))} is not a valid {propertyType.Name}.",
                        new[] { name }));
                }
            }
            else if (value is IEnumerable items and not string)
            {
                var index = 0;
                foreach (var item in items)
                {
                    Check(item, $"{name}[{index++}].", depth + 1, errors, seen);
                }
            }
            else
            {
                Check(value, name + ".", depth + 1, errors, seen);
            }
        }
    }

    /// <summary>Only this module's own contracts: the objects of other modules are validated by their own rules.</summary>
    private static bool IsOwnType(Type type) => type.Assembly == typeof(EnumRangeValidationContributor).Assembly;
}
