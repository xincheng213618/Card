using System.Collections.ObjectModel;
using System.Reflection;

namespace CardGame.Core;

// Each point keeps the native caller's boundary. In particular, the declared
// global target list is deliberately narrower than directed target legality.
internal enum ProgramTargetRulePoint
{
    DirectedProhibition,
    DeclaredGlobalProhibition,
    SlashQualification,
    NativeUseMaterialQualification,
    MaterialPrivilegeAllowance,
    TargetCountContributions,
    ExtraSlashTargets,
    ExtraSlashTargetSource,
    QuotaAllowance,
    SlashQuotaBeforeLimit,
    SlashQuotaAfterLimit
}

internal readonly record struct ProgramDirectedTargetQuery(
    int ActorSeat, int TargetSeat, CardKind Kind, long? ExistingUseFrameId = null);
internal readonly record struct ProgramSlashTargetQuery(
    CharacterState Actor, CharacterState Target, CardKind Kind,
    CardConversionSource? Source, IReadOnlyList<int>? PhysicalCardIds);
internal readonly record struct ProgramTargetQuotaQuery(int ActorSeat, int TargetSeat);
internal readonly record struct ProgramNativeUseMaterialQuery(
    int ActorSeat, CardKind Kind, IReadOnlyList<int> PhysicalCardIds,
    CardConversionSource? Source = null, long? ExistingUseFrameId = null);
internal readonly record struct ProgramTargetCountQuery(CharacterState Actor, CardKind Kind);
internal readonly record struct ProgramSlashTargetExpansionQuery(
    int ActorSeat, CardKind Kind, Suit EffectiveSuit, IReadOnlyList<int> LegalTargetSeats, bool? EffectiveIsRed = null);
internal readonly record struct ProgramSlashTargetSourceQuery(CardUseEffectSource Source, string GameplayHash);
public sealed record ProgramSlashTargetExpansion(CardUseEffectSource Source, string GameplayHash)
{
    private readonly IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> TargetSeats
    { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
}
internal readonly record struct ProgramSlashQuotaQuery(
    CharacterState Actor, CharacterState Target, CardKind Kind, Card? PhysicalCard,
    int? EffectiveRank, int? ExcludedEquipmentId, IReadOnlyList<int>? PhysicalCardIds,
    EquipmentSlot? ExcludedEquipmentSlot, bool IgnoreMoQuanyu);

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
internal sealed class ProgramTargetRuleAttribute(ProgramTargetRulePoint point, int order) : Attribute
{
    internal ProgramTargetRulePoint Point { get; } = point;
    internal int Order { get; } = order;
}

internal sealed record ProgramTargetRuleRegistration(int Order, MethodInfo Method, Type DelegateType);

internal sealed class ProgramTargetRuleCatalog
{
    internal IReadOnlyDictionary<ProgramTargetRulePoint, IReadOnlyList<ProgramTargetRuleRegistration>> Registrations { get; }

    private ProgramTargetRuleCatalog(Dictionary<ProgramTargetRulePoint, SortedDictionary<int, ProgramTargetRuleRegistration>> entries) =>
        Registrations = new ReadOnlyDictionary<ProgramTargetRulePoint, IReadOnlyList<ProgramTargetRuleRegistration>>(
            entries.ToDictionary(pair => pair.Key,
                pair => (IReadOnlyList<ProgramTargetRuleRegistration>)Array.AsReadOnly(pair.Value.Values.ToArray())));

    internal static ProgramTargetRuleCatalog Discover(IEnumerable<MethodInfo> methods)
    {
        ArgumentNullException.ThrowIfNull(methods);
        var entries = Enum.GetValues<ProgramTargetRulePoint>().ToDictionary(point => point,
            _ => new SortedDictionary<int, ProgramTargetRuleRegistration>());
        foreach (var method in methods)
        foreach (var attribute in method.GetCustomAttributes<ProgramTargetRuleAttribute>())
        {
            if (!Enum.IsDefined(attribute.Point) || attribute.Order <= 0)
                throw new InvalidOperationException($"Target rule '{method.Name}' requires a valid point and positive order.");
            var delegateType = DelegateType(attribute.Point);
            var signature = delegateType.GetMethod("Invoke")!;
            if (method.IsAbstract || method.IsGenericMethod || method.ContainsGenericParameters ||
                method.ReturnType != signature.ReturnType ||
                !method.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual(signature.GetParameters().Select(parameter => parameter.ParameterType)) ||
                method.GetParameters().Any(parameter => parameter.IsOptional || parameter.IsOut || parameter.ParameterType.IsByRef))
                throw new InvalidOperationException($"Target rule '{method.Name}' has an invalid handler signature.");
            if (!entries[attribute.Point].TryAdd(attribute.Order, new(attribute.Order, method, delegateType)))
                throw new InvalidOperationException($"Duplicate target rule order '{attribute.Point}:{attribute.Order}'.");
        }
        return new(entries);
    }

    private static Type DelegateType(ProgramTargetRulePoint point) => point switch
    {
        ProgramTargetRulePoint.DirectedProhibition or ProgramTargetRulePoint.DeclaredGlobalProhibition =>
            typeof(Func<ProgramDirectedTargetQuery, bool>),
        ProgramTargetRulePoint.SlashQualification => typeof(Func<ProgramSlashTargetQuery, bool>),
        ProgramTargetRulePoint.NativeUseMaterialQualification or ProgramTargetRulePoint.MaterialPrivilegeAllowance => typeof(Func<ProgramNativeUseMaterialQuery, bool>),
        ProgramTargetRulePoint.TargetCountContributions => typeof(Func<ProgramTargetCountQuery, RuleQueryContribution?>),
        ProgramTargetRulePoint.ExtraSlashTargets => typeof(Func<ProgramSlashTargetExpansionQuery, ProgramSlashTargetExpansion>),
        ProgramTargetRulePoint.ExtraSlashTargetSource => typeof(Func<ProgramSlashTargetSourceQuery, bool>),
        ProgramTargetRulePoint.QuotaAllowance => typeof(Func<ProgramTargetQuotaQuery, bool>),
        ProgramTargetRulePoint.SlashQuotaBeforeLimit or ProgramTargetRulePoint.SlashQuotaAfterLimit =>
            typeof(Func<ProgramSlashQuotaQuery, bool>),
        _ => throw new ArgumentOutOfRangeException(nameof(point))
    };
}

internal sealed class ProgramTargetRules
{
    private readonly Func<ProgramDirectedTargetQuery, bool>[] _directedProhibitions;
    private readonly Func<ProgramDirectedTargetQuery, bool>[] _declaredGlobalProhibitions;
    private readonly Func<ProgramSlashTargetQuery, bool>[] _slashQualifications;
    private readonly Func<ProgramNativeUseMaterialQuery, bool>[] _nativeUseMaterialQualifications;
    private readonly Func<ProgramNativeUseMaterialQuery, bool>[] _materialPrivilegeAllowances;
    private readonly Func<ProgramTargetCountQuery, RuleQueryContribution?>[] _targetCountContributions;
    private readonly Func<ProgramSlashTargetExpansionQuery, ProgramSlashTargetExpansion?>[] _extraSlashTargets;
    private readonly Func<ProgramSlashTargetSourceQuery, bool>[] _extraSlashTargetSources;
    private readonly Func<ProgramTargetQuotaQuery, bool>[] _quotaAllowances;
    private readonly Func<ProgramSlashQuotaQuery, bool>[] _slashQuotaBeforeLimit;
    private readonly Func<ProgramSlashQuotaQuery, bool>[] _slashQuotaAfterLimit;

    internal ProgramTargetRules(ProgramTargetRuleCatalog catalog, Func<MethodInfo, Type, Delegate> bind)
    {
        var bound = new Dictionary<MethodInfo, Delegate>();
        TDelegate[] Bind<TDelegate>(ProgramTargetRulePoint point) where TDelegate : Delegate => catalog.Registrations[point].Select(registration =>
        {
            if (!bound.TryGetValue(registration.Method, out var callback))
                bound.Add(registration.Method, callback = bind(registration.Method, registration.DelegateType));
            return (TDelegate)callback;
        }).ToArray();
        _directedProhibitions = Bind<Func<ProgramDirectedTargetQuery, bool>>(ProgramTargetRulePoint.DirectedProhibition);
        _declaredGlobalProhibitions = Bind<Func<ProgramDirectedTargetQuery, bool>>(ProgramTargetRulePoint.DeclaredGlobalProhibition);
        _slashQualifications = Bind<Func<ProgramSlashTargetQuery, bool>>(ProgramTargetRulePoint.SlashQualification);
        _nativeUseMaterialQualifications = Bind<Func<ProgramNativeUseMaterialQuery, bool>>(ProgramTargetRulePoint.NativeUseMaterialQualification);
        _materialPrivilegeAllowances = Bind<Func<ProgramNativeUseMaterialQuery, bool>>(ProgramTargetRulePoint.MaterialPrivilegeAllowance);
        _targetCountContributions = Bind<Func<ProgramTargetCountQuery, RuleQueryContribution?>>(ProgramTargetRulePoint.TargetCountContributions);
        _extraSlashTargets = Bind<Func<ProgramSlashTargetExpansionQuery, ProgramSlashTargetExpansion?>>(ProgramTargetRulePoint.ExtraSlashTargets);
        _extraSlashTargetSources = Bind<Func<ProgramSlashTargetSourceQuery, bool>>(ProgramTargetRulePoint.ExtraSlashTargetSource);
        _quotaAllowances = Bind<Func<ProgramTargetQuotaQuery, bool>>(ProgramTargetRulePoint.QuotaAllowance);
        _slashQuotaBeforeLimit = Bind<Func<ProgramSlashQuotaQuery, bool>>(ProgramTargetRulePoint.SlashQuotaBeforeLimit);
        _slashQuotaAfterLimit = Bind<Func<ProgramSlashQuotaQuery, bool>>(ProgramTargetRulePoint.SlashQuotaAfterLimit);
    }

    internal bool IsDirectedProhibited(ProgramDirectedTargetQuery query) => Any(_directedProhibitions, query);
    internal bool IsDeclaredGlobalProhibited(ProgramDirectedTargetQuery query) => Any(_declaredGlobalProhibitions, query);
    internal bool HasQuotaAllowance(ProgramTargetQuotaQuery query) => Any(_quotaAllowances, query);
    internal bool HasSlashQuotaBeforeLimit(ProgramSlashQuotaQuery query) => Any(_slashQuotaBeforeLimit, query);
    internal bool HasSlashQuotaAfterLimit(ProgramSlashQuotaQuery query) => Any(_slashQuotaAfterLimit, query);

    internal bool AllowsSlash(ProgramSlashTargetQuery query)
    {
        foreach (var qualification in _slashQualifications) if (!qualification(query)) return false;
        return true;
    }

    internal bool HasMaterialPrivilege(ProgramNativeUseMaterialQuery query) => Any(_materialPrivilegeAllowances, query);

    internal bool AllowsNativeUseMaterial(ProgramNativeUseMaterialQuery query)
    {
        foreach (var qualification in _nativeUseMaterialQualifications) if (!qualification(query)) return false;
        return true;
    }

    internal RuleQueryContribution[] AppendTargetCount(ProgramTargetCountQuery query, RuleQueryContribution[] contributions)
    {
        foreach (var contribute in _targetCountContributions)
            if (contribute(query) is { } contribution) contributions = [.. contributions, contribution];
        return contributions;
    }

    internal IReadOnlyList<ProgramSlashTargetExpansion> ExtraSlashTargetClaims(ProgramSlashTargetExpansionQuery query)
    {
        var claims = new List<ProgramSlashTargetExpansion>();
        foreach (var rule in _extraSlashTargets)
        {
            if (rule(query) is not { } claim) continue;
            if (claim.Source.OwnerSeat != query.ActorSeat || !IsExtraSlashTargetSource(claim.Source, claim.GameplayHash) ||
                claim.TargetSeats.Distinct().Count() != claim.TargetSeats.Count ||
                claim.TargetSeats.Any(seat => !query.LegalTargetSeats.Contains(seat)))
                throw new InvalidOperationException("A target expansion lost its declared source or legal target domain.");
            if (claim.TargetSeats.Count != 0) claims.Add(claim);
        }
        return claims.AsReadOnly();
    }

    internal IReadOnlyList<int> ExtraSlashTargets(ProgramSlashTargetExpansionQuery query) =>
        Array.AsReadOnly(ExtraSlashTargetClaims(query).SelectMany(claim => claim.TargetSeats).Distinct().ToArray());

    internal bool IsExtraSlashTargetSource(CardUseEffectSource source, string gameplayHash) =>
        Any(_extraSlashTargetSources, new ProgramSlashTargetSourceQuery(source, gameplayHash));

    private static bool Any<TQuery>(Func<TQuery, bool>[] queries, TQuery query)
    {
        foreach (var evaluate in queries) if (evaluate(query)) return true;
        return false;
    }
}

public sealed partial class GameEngine
{
    private static readonly Lazy<ProgramTargetRuleCatalog> TargetRuleMetadata = new(() =>
        ProgramTargetRuleCatalog.Discover(RuntimeModuleMethods), true);
    private ProgramTargetRules? _targetRules;
    private ProgramTargetRules TargetRules => _targetRules ??= new(TargetRuleMetadata.Value,
        BindRuntimeHandler);
}
