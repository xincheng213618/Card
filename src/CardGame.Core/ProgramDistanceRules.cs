using System.Collections.ObjectModel;
using System.Reflection;

namespace CardGame.Core;

// Stages name existing native positions. Physical distance arithmetic, attack
// range and permission to ignore a card's distance are separate contracts.
internal enum ProgramDistanceStage
{
    DistanceBaseTerms,
    IncomingContributions,
    DistanceProjection,
    AttackRangeBaseTerms,
    AttackRangeContributions,
    CardUseExemption,
    ConvertedSlashExemption,
    CardUseLimitExemption,
    PhysicalSlash,
    VirtualSlash,
    ProvidedSlash,
    BorrowedSlash,
    BorrowedPhysicalSlash,
    BorrowedPaidWeaponSlash,
    SpecificSlashExemption
}

internal readonly record struct ProgramDistanceQuery(CharacterState Source, CharacterState Target);
internal readonly record struct ProgramAttackRangeQuery(CharacterState Player, int? ExcludedEquipmentId, EquipmentSlot? ExcludedEquipmentSlot);
internal readonly record struct ProgramCardDistanceQuery(CharacterState Actor, CharacterState Target, CardKind Kind,
    long? CardUseFrameId, int? ExcludedEquipmentId, EquipmentSlot? ExcludedEquipmentSlot);
internal readonly record struct ProgramSlashConversionDistanceQuery(CharacterState Owner, CardConversionSource Source);
internal readonly record struct ProgramSlashDistanceQuery(CharacterState Actor, CharacterState Target, CardKind Kind,
    Card? PhysicalCard = null, CardConversionSource? Conversion = null, long? ExistingUseFrameId = null,
    IReadOnlyList<int>? PhysicalCardIds = null, Suit? PhysicalSuit = null, bool? EffectiveColor = null,
    int? EffectiveRank = null, bool NoEffectiveRank = false, int? SpecificEffectiveRank = null,
    int? ExcludedEquipmentId = null, EquipmentSlot? ExcludedEquipmentSlot = null, bool AllowAnyPhysicalSuit = false);

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
internal sealed class ProgramDistanceRuleAttribute(ProgramDistanceStage stage, int order) : Attribute
{
    internal ProgramDistanceStage Stage { get; } = stage;
    internal int Order { get; } = order;
}

internal sealed record ProgramDistanceRegistration(int Order, MethodInfo Method, Type DelegateType);

internal sealed class ProgramDistanceCatalog
{
    internal IReadOnlyDictionary<ProgramDistanceStage, IReadOnlyList<ProgramDistanceRegistration>> Registrations { get; }

    private ProgramDistanceCatalog(Dictionary<ProgramDistanceStage, SortedDictionary<int, ProgramDistanceRegistration>> entries) =>
        Registrations = new ReadOnlyDictionary<ProgramDistanceStage, IReadOnlyList<ProgramDistanceRegistration>>(
            entries.ToDictionary(pair => pair.Key,
                pair => (IReadOnlyList<ProgramDistanceRegistration>)Array.AsReadOnly(pair.Value.Values.ToArray())));

    internal static ProgramDistanceCatalog Discover(IEnumerable<MethodInfo> methods)
    {
        ArgumentNullException.ThrowIfNull(methods);
        var entries = Enum.GetValues<ProgramDistanceStage>().ToDictionary(stage => stage,
            _ => new SortedDictionary<int, ProgramDistanceRegistration>());
        foreach (var method in methods)
        foreach (var attribute in method.GetCustomAttributes<ProgramDistanceRuleAttribute>())
        {
            if (!Enum.IsDefined(attribute.Stage) || attribute.Order <= 0)
                throw new InvalidOperationException($"Distance rule '{method.Name}' requires a valid stage and positive order.");
            var type = DelegateType(attribute.Stage);
            var signature = type.GetMethod("Invoke")!;
            if (method.IsAbstract || method.IsGenericMethod || method.ContainsGenericParameters ||
                method.ReturnType != signature.ReturnType ||
                !method.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual(signature.GetParameters().Select(parameter => parameter.ParameterType)) ||
                method.GetParameters().Any(parameter => parameter.IsOptional || parameter.IsOut || parameter.ParameterType.IsByRef))
                throw new InvalidOperationException($"Distance rule '{method.Name}' has an invalid handler signature.");
            if (!entries[attribute.Stage].TryAdd(attribute.Order, new(attribute.Order, method, type)))
                throw new InvalidOperationException($"Duplicate distance rule order '{attribute.Stage}:{attribute.Order}'.");
        }
        return new(entries);
    }

    private static Type DelegateType(ProgramDistanceStage stage) => stage switch
    {
        ProgramDistanceStage.DistanceBaseTerms => typeof(Action<ProgramDistanceQuery, List<RuleQueryBaseTerm>>),
        ProgramDistanceStage.IncomingContributions => typeof(Func<int, IEnumerable<RuleQueryContribution>>),
        ProgramDistanceStage.DistanceProjection => typeof(Func<CharacterState, CharacterState, RuleQueryEvaluation, RuleQueryEvaluation>),
        ProgramDistanceStage.AttackRangeBaseTerms => typeof(Action<ProgramAttackRangeQuery, List<RuleQueryBaseTerm>>),
        ProgramDistanceStage.AttackRangeContributions => typeof(Action<CharacterState, List<RuleQueryContribution>>),
        ProgramDistanceStage.CardUseExemption => typeof(Func<ProgramCardDistanceQuery, bool>),
        ProgramDistanceStage.ConvertedSlashExemption => typeof(Func<ProgramSlashConversionDistanceQuery, bool>),
        ProgramDistanceStage.CardUseLimitExemption => typeof(Func<CharacterState, bool>),
        ProgramDistanceStage.PhysicalSlash or ProgramDistanceStage.VirtualSlash or ProgramDistanceStage.ProvidedSlash or
            ProgramDistanceStage.BorrowedSlash or ProgramDistanceStage.BorrowedPhysicalSlash or
            ProgramDistanceStage.BorrowedPaidWeaponSlash or ProgramDistanceStage.SpecificSlashExemption =>
                typeof(Func<ProgramSlashDistanceQuery, bool>),
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };
}

internal sealed class ProgramDistanceRules
{
    private readonly Dictionary<ProgramDistanceStage, Delegate[]> _callbacks;

    internal ProgramDistanceRules(ProgramDistanceCatalog catalog, Func<MethodInfo, Type, Delegate> bind)
    {
        var bound = new Dictionary<MethodInfo, Delegate>();
        _callbacks = catalog.Registrations.ToDictionary(pair => pair.Key, pair => pair.Value.Select(registration =>
        {
            if (!bound.TryGetValue(registration.Method, out var callback))
                bound.Add(registration.Method, callback = bind(registration.Method, registration.DelegateType));
            return callback;
        }).ToArray());
    }

    internal void AddDistanceBaseTerms(ProgramDistanceQuery query, List<RuleQueryBaseTerm> terms)
    {
        foreach (var callback in _callbacks[ProgramDistanceStage.DistanceBaseTerms])
            ((Action<ProgramDistanceQuery, List<RuleQueryBaseTerm>>)callback)(query, terms);
    }

    internal void AddIncomingContributions(int targetSeat, List<RuleQueryContribution> contributions)
    {
        foreach (var callback in _callbacks[ProgramDistanceStage.IncomingContributions])
            contributions.AddRange(((Func<int, IEnumerable<RuleQueryContribution>>)callback)(targetSeat));
    }

    internal RuleQueryEvaluation ProjectDistance(CharacterState source, CharacterState target, RuleQueryEvaluation evaluation)
    {
        foreach (var callback in _callbacks[ProgramDistanceStage.DistanceProjection])
            evaluation = ((Func<CharacterState, CharacterState, RuleQueryEvaluation, RuleQueryEvaluation>)callback)(source, target, evaluation);
        return evaluation;
    }

    internal void AddAttackRangeBaseTerms(ProgramAttackRangeQuery query, List<RuleQueryBaseTerm> terms)
    {
        foreach (var callback in _callbacks[ProgramDistanceStage.AttackRangeBaseTerms])
            ((Action<ProgramAttackRangeQuery, List<RuleQueryBaseTerm>>)callback)(query, terms);
    }

    internal void AddAttackRangeContributions(CharacterState player, List<RuleQueryContribution> contributions)
    {
        foreach (var callback in _callbacks[ProgramDistanceStage.AttackRangeContributions])
            ((Action<CharacterState, List<RuleQueryContribution>>)callback)(player, contributions);
    }

    internal bool HasCardUseExemption(ProgramCardDistanceQuery query)
    {
        foreach (var callback in _callbacks[ProgramDistanceStage.CardUseExemption])
            if (((Func<ProgramCardDistanceQuery, bool>)callback)(query)) return true;
        return false;
    }

    internal bool HasConvertedSlashExemption(ProgramSlashConversionDistanceQuery query)
    {
        foreach (var callback in _callbacks[ProgramDistanceStage.ConvertedSlashExemption])
            if (((Func<ProgramSlashConversionDistanceQuery, bool>)callback)(query)) return true;
        return false;
    }

    internal bool HasCardUseLimitExemption(CharacterState player)
    {
        foreach (var callback in _callbacks[ProgramDistanceStage.CardUseLimitExemption])
            if (((Func<CharacterState, bool>)callback)(player)) return true;
        return false;
    }

    internal bool AllowsSlashDistance(ProgramDistanceStage stage, ProgramSlashDistanceQuery query)
    {
        if (stage is not (ProgramDistanceStage.PhysicalSlash or ProgramDistanceStage.VirtualSlash or
            ProgramDistanceStage.ProvidedSlash or ProgramDistanceStage.BorrowedSlash or
            ProgramDistanceStage.BorrowedPhysicalSlash or ProgramDistanceStage.BorrowedPaidWeaponSlash or
            ProgramDistanceStage.SpecificSlashExemption)) throw new ArgumentOutOfRangeException(nameof(stage));
        foreach (var callback in _callbacks[stage])
            if (((Func<ProgramSlashDistanceQuery, bool>)callback)(query)) return true;
        return false;
    }
}

public sealed partial class GameEngine
{
    private static readonly Lazy<ProgramDistanceCatalog> DistanceRuleMetadata = new(() =>
        ProgramDistanceCatalog.Discover(RuntimeModuleMethods), true);
    private ProgramDistanceRules? _distanceRules;
    private ProgramDistanceRules DistanceRules => _distanceRules ??= new(DistanceRuleMetadata.Value,
        BindRuntimeHandler);
}
