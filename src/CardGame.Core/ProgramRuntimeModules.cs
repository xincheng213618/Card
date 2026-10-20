using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace CardGame.Core;

// Explicit registration prevents unrelated classes from becoming runtime hosts.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class ProgramRuntimeModuleAttribute : Attribute { }

// Trusted, narrow engine operations may be injected alongside existing read-only
// selection queries. This marker grants no access to engine state by itself.
internal interface IProgramRuntimeOperations { }

internal sealed class ProgramRuntimeModuleCatalog
{
    private static readonly Lazy<ProgramRuntimeModuleCatalog> BuiltIn = new(() => new(
        typeof(GameEngine).Assembly.GetTypes(), typeof(GameEngine).GetMethods(BindingFlags.Instance |
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)), true);
    internal static ProgramRuntimeModuleCatalog Default => BuiltIn.Value;
    internal sealed record Registration(Type Type, ConstructorInfo? Constructor, IReadOnlyList<Type> Dependencies);
    private readonly IReadOnlyDictionary<Type, Registration> _registrations;
    internal IReadOnlyList<Registration> Registrations { get; }
    internal IReadOnlyList<MethodInfo> Methods { get; }

    internal ProgramRuntimeModuleCatalog(IEnumerable<Type> types, IEnumerable<MethodInfo>? legacyMethods = null)
    {
        ArgumentNullException.ThrowIfNull(types);
        var registrations = new List<Registration>();
        var methods = new List<MethodInfo>(legacyMethods ?? []);
        foreach (var type in types.Select(type => type ??
                     throw new InvalidOperationException("A runtime module type cannot be null."))
                     .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            if (!IsDeclared(type)) continue;
            if (!type.IsClass || !type.IsSealed || type.IsGenericType || type.ContainsGenericParameters ||
                type.IsNested && type.DeclaringType == typeof(GameEngine))
                throw Invalid(type, "requires an independent, sealed, non-generic type");
            if (type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly).Any(field => !field.IsLiteral))
                throw Invalid(type, "must not retain nonconstant static fields");
            ConstructorInfo? constructor = null;
            Type[] dependencies = [];
            if (type.IsAbstract)
            {
                // A static module may retain constants, but never match state.
            }
            else
            {
                var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (constructors is not [var only]) throw Invalid(type, "requires exactly one constructor");
                constructor = only;
                dependencies = only.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
                if (dependencies.Distinct().Count() != dependencies.Length || dependencies.Any(dependency =>
                        !dependency.IsInterface || dependency.ContainsGenericParameters ||
                        typeof(ISkillProgramExecutionHost).IsAssignableFrom(dependency) ||
                        typeof(ISkillProgramEffectHost).IsAssignableFrom(dependency) ||
                        dependency == typeof(IProgramSelectionQuery) || dependency == typeof(IProgramRuntimeOperations) ||
                        !typeof(IProgramSelectionQuery).IsAssignableFrom(dependency) &&
                        !typeof(IProgramRuntimeOperations).IsAssignableFrom(dependency)) ||
                    only.GetParameters().Any(parameter => parameter.IsOptional || parameter.IsOut))
                    throw Invalid(type, "constructor dependencies must be dedicated query or runtime-operation interfaces");
            }
            registrations.Add(new(type, constructor, Array.AsReadOnly(dependencies)));
            methods.AddRange(type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        }
        Registrations = Array.AsReadOnly(registrations.ToArray());
        _registrations = new ReadOnlyDictionary<Type, Registration>(registrations.ToDictionary(item => item.Type));
        Methods = Array.AsReadOnly(methods.ToArray());
    }

    internal static bool IsDeclared(Type? type) => type?.IsDefined(typeof(ProgramRuntimeModuleAttribute), false) == true;
    internal static bool IsDeclaredStaticHandler(MethodInfo method) => method.IsStatic && IsDeclared(method.DeclaringType);
    internal Registration Get(Type type) => _registrations.TryGetValue(type, out var registration) ? registration :
        throw Invalid(type, "is not registered as an independent runtime module");
    private static InvalidOperationException Invalid(Type type, string reason) =>
        new($"Program runtime module '{type.FullName}' {reason}.");
}

// One registry per game; neither these instances nor bound delegates enter a
// checkpoint. Pending state remains on existing resolution frames.
internal sealed class ProgramRuntimeModuleRegistry(ProgramRuntimeModuleCatalog catalog, Func<Type, object> resolve)
{
    private readonly Dictionary<Type, object> _instances = new();
    private readonly HashSet<Type> _constructing = new();
    private readonly Dictionary<(MethodInfo Method, Type DelegateType), Delegate> _handlers = new();

    internal object Get(Type type)
    {
        if (_instances.TryGetValue(type, out var instance)) return instance;
        var registration = catalog.Get(type);
        if (registration.Constructor is null) throw new InvalidOperationException("A static runtime module has no instance.");
        if (!_constructing.Add(type)) throw new InvalidOperationException($"Cyclic runtime module dependency '{type.FullName}'.");
        try
        {
            var arguments = registration.Dependencies.Select(dependency =>
            {
                var value = resolve(dependency);
                if (!dependency.IsInstanceOfType(value) || value is GameEngine)
                    throw new InvalidOperationException($"Runtime dependency '{dependency.FullName}' resolved an invalid host.");
                return value;
            }).ToArray();
            try { instance = registration.Constructor.Invoke(arguments); }
            catch (TargetInvocationException failure) when (failure.InnerException is { } cause)
            { ExceptionDispatchInfo.Capture(cause).Throw(); throw; }
            _instances.Add(type, instance);
            return instance;
        }
        finally { _constructing.Remove(type); }
    }

    internal Delegate Bind(MethodInfo method, Type delegateType, object legacyHost)
    {
        var key = (method, delegateType);
        if (_handlers.TryGetValue(key, out var handler)) return handler;
        handler = method.IsStatic ? method.CreateDelegate(delegateType) :
            method.CreateDelegate(delegateType, Target(method, legacyHost));
        _handlers.Add(key, handler);
        return handler;
    }

    internal object? Target(MethodInfo method, object legacyHost) => method.IsStatic ? null :
        method.DeclaringType == typeof(GameEngine) ? legacyHost : Get(method.DeclaringType!);
}

public sealed partial class GameEngine
{
    private static readonly Lazy<ProgramRuntimeModuleCatalog> RuntimeModuleMetadata = new(() => ProgramRuntimeModuleCatalog.Default, true);
    private static IReadOnlyList<MethodInfo> RuntimeModuleMethods => RuntimeModuleMetadata.Value.Methods;
    private ProgramRuntimeModuleRegistry? _runtimeModules;
    private ProgramRuntimeModuleRegistry RuntimeModules => _runtimeModules ??= new(RuntimeModuleMetadata.Value,
        contract => ProgramCapabilities.Get(contract));
    internal object GetRuntimeModuleInstance(Type type) => RuntimeModules.Get(type);
    internal T GetRuntimeModule<T>() where T : class => (T)GetRuntimeModuleInstance(typeof(T));
    internal Delegate BindRuntimeHandler(MethodInfo method, Type delegateType) => RuntimeModules.Bind(method, delegateType, this);
    private object? ResolveRuntimeHandlerTarget(MethodInfo method) => RuntimeModules.Target(method, this);
}
