using System.Collections.ObjectModel;
using System.Reflection;

namespace CardGame.Core;

// Load-time contracts belong to the module that owns the operation. They are
// separate from runtime frame/payment proofs and never run during continuation.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class ProgramCatalogValidationAttribute(int order) : Attribute
{
    internal int Order { get; } = order;
}

internal sealed record ProgramCatalogValidationRegistration(int Order, MethodInfo Method,
    Action<IReadOnlyDictionary<string, SkillProgram>> Validate);

internal sealed class ProgramCatalogValidations
{
    private static readonly Lazy<ProgramCatalogValidations> BuiltIn = new(() => Discover(
        typeof(ProgramCatalogValidations).Assembly.GetTypes().SelectMany(type => type.GetMethods(
            BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))), true);

    internal static ProgramCatalogValidations Default => BuiltIn.Value;
    internal IReadOnlyList<ProgramCatalogValidationRegistration> Registrations { get; }

    private ProgramCatalogValidations(IEnumerable<ProgramCatalogValidationRegistration> registrations) =>
        Registrations = Array.AsReadOnly(registrations.OrderBy(r => r.Order).ToArray());

    internal static ProgramCatalogValidations Discover(IEnumerable<MethodInfo> methods)
    {
        var registrations = new SortedDictionary<int, ProgramCatalogValidationRegistration>();
        foreach (var method in methods)
        foreach (var attribute in method.GetCustomAttributes<ProgramCatalogValidationAttribute>())
        {
            var parameters = method.GetParameters();
            if (attribute.Order <= 0 || !method.IsStatic || method.IsGenericMethod || method.ContainsGenericParameters ||
                method.ReturnType != typeof(void) || parameters.Any(p => p.IsOptional || p.IsOut || p.ParameterType.IsByRef))
                throw Invalid(method);
            Action<IReadOnlyDictionary<string, SkillProgram>> validate;
            if (parameters.Select(p => p.ParameterType).SequenceEqual([typeof(IReadOnlyDictionary<string, SkillProgram>)]))
                validate = method.CreateDelegate<Action<IReadOnlyDictionary<string, SkillProgram>>>();
            else if (parameters.Select(p => p.ParameterType).SequenceEqual([typeof(string), typeof(SkillProgram)]))
            {
                var validateProgram = method.CreateDelegate<Action<string, SkillProgram>>();
                validate = programs =>
                {
                    // Retain validator-first ordering and the catalog's original
                    // enumeration order, including its first reported failure.
                    foreach (var program in programs.Values) validateProgram(program.Id, program);
                };
            }
            else throw Invalid(method);
            if (!registrations.TryAdd(attribute.Order, new(attribute.Order, method, validate)))
                throw new InvalidOperationException($"Duplicate program catalog validation order '{attribute.Order}'.");
        }
        return new(registrations.Values);
    }

    internal void Validate(IReadOnlyDictionary<string, SkillProgram> programs)
    {
        foreach (var registration in Registrations) registration.Validate(programs);
    }

    private static InvalidOperationException Invalid(MethodInfo method) =>
        new($"Program catalog validator '{method.DeclaringType?.Name}.{method.Name}' requires a positive order and a static catalog/program validation signature.");
}
