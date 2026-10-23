using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace CardGame.Core;

internal readonly record struct ProgramTargetResolverContext(int OwnerSeat, SkillProgramTargetKind TargetKind);
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class ProgramTargetResolverAttribute(params SkillProgramTargetKind[] targetKinds) : Attribute
{
    internal IReadOnlyList<SkillProgramTargetKind> TargetKinds { get; } = Array.AsReadOnly(targetKinds);
}

internal interface IProgramTargetResolver
{
    int MinimumTargets => 1;
    string EmptySelectionLabel => "不获得手牌。";
    IReadOnlyList<int> Resolve(ProgramTargetResolverContext context, IProgramSelectionQueries queries);
}

internal sealed class ProgramTargetResolverCatalog
{
    internal sealed record Registration(Type Implementation, IReadOnlyList<SkillProgramTargetKind> TargetKinds, IProgramTargetResolver Resolver);
    private readonly IReadOnlyDictionary<SkillProgramTargetKind, IProgramTargetResolver> _resolvers;
    internal IReadOnlyList<Registration> Registrations { get; }

    internal ProgramTargetResolverCatalog(IEnumerable<Type> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        var resolvers = new Dictionary<SkillProgramTargetKind, IProgramTargetResolver>();
        var registrations = new List<Registration>();
        foreach (var type in types.Select(type => type ?? throw new InvalidOperationException("A target resolver type cannot be null."))
                     .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            if (type.GetCustomAttribute<ProgramTargetResolverAttribute>() is not { } declaration) continue;
            var constructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (!type.IsClass || !type.IsSealed || type.IsAbstract || type.IsGenericType || type.ContainsGenericParameters ||
                !typeof(IProgramTargetResolver).IsAssignableFrom(type) || declaration.TargetKinds.Count == 0 ||
                declaration.TargetKinds.Any(kind => !Enum.IsDefined(kind)) ||
                declaration.TargetKinds.Distinct().Count() != declaration.TargetKinds.Count ||
                constructors is not [var constructor] || constructor.GetParameters().Length != 0 ||
                type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length != 0)
                throw new InvalidOperationException($"Target resolver '{type.FullName}' has an invalid registration.");
            var resolver = Construct(constructor);
            if (resolver.MinimumTargets < 0 || string.IsNullOrWhiteSpace(resolver.EmptySelectionLabel))
                throw new InvalidOperationException($"Target resolver '{type.FullName}' has an invalid selection domain.");
            foreach (var kind in declaration.TargetKinds)
                if (!resolvers.TryAdd(kind, resolver))
                    throw new InvalidOperationException($"Duplicate program target resolver '{kind}'.");
            registrations.Add(new(type, Array.AsReadOnly(declaration.TargetKinds.ToArray()), resolver));
        }
        if (registrations.Count == 0) throw new InvalidOperationException("No program target resolvers were registered.");
        Registrations = Array.AsReadOnly(registrations.ToArray());
        _resolvers = new ReadOnlyDictionary<SkillProgramTargetKind, IProgramTargetResolver>(resolvers);
    }

    internal bool TryResolve(SkillProgramTargetKind kind, out IProgramTargetResolver? resolver) => _resolvers.TryGetValue(kind, out resolver);

    private static IProgramTargetResolver Construct(ConstructorInfo constructor)
    {
        try { return (IProgramTargetResolver)constructor.Invoke(null); }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
    }
}

internal sealed class ProgramTargetResolverRegistry(ProgramTargetResolverCatalog catalog, IProgramSelectionQueries queries)
{
    internal int MinimumTargets(SkillProgramTargetKind kind) => catalog.TryResolve(kind, out var resolver)
        ? resolver!.MinimumTargets : kind is SkillProgramTargetKind.OtherLiving or SkillProgramTargetKind.OtherLivingWithHand or SkillProgramTargetKind.AnyLivingChained ? 0 : 1;

    internal string EmptySelectionLabel(SkillProgramTargetKind kind) => catalog.TryResolve(kind, out var resolver)
        ? resolver!.EmptySelectionLabel : kind is SkillProgramTargetKind.OtherLiving or SkillProgramTargetKind.AnyLivingChained ? "不选择角色。" : "不获得手牌。";

    internal bool TryResolve(ProgramTargetResolverContext context, out IReadOnlyList<int> targets)
    {
        if (!catalog.TryResolve(context.TargetKind, out var resolver)) { targets = []; return false; }
        targets = resolver!.Resolve(context, queries);
        return true;
    }
}
