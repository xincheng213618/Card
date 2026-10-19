using System.Collections.ObjectModel;

using System.Collections.Frozen;

namespace CardGame.Core;

internal readonly record struct SkillBindingIndexStamp(
    long SkillRevision,
    int CurrentHp,
    bool PrimarySelected,
    bool PrimaryRevealed,
    bool HasSecondary,
    bool SecondarySelected,
    bool SecondaryRevealed, long ProjectionRevision = 0, Role? OwnerRole = null,
    long DerivedProjectionRevision = 0, bool OwnerAlive = true);

internal sealed record IndexedSkillProgramInstance(
    string SkillId,
    string SkillInstanceId,
    ContentSkillDefinition Definition,
    SkillProgram Program);

internal sealed record IndexedSkillProgramTrigger(
    string SkillId,
    string SkillInstanceId,
    SkillProgram Program,
    SkillProgramTrigger Trigger);

internal sealed record IndexedSkillProgramModifier(
    SkillProgramRuleSource Source,
    SkillProgramModifier Modifier);

/// <summary>
/// Immutable projection of one character's currently bound content skills.
/// It contains definitions and binding identities only; all mutable gameplay
/// conditions are deliberately evaluated by the caller at use time.
/// </summary>
internal sealed class SkillBindingShard
{
    private readonly IReadOnlySet<(string SkillId, string SkillInstanceId)> _activeInstances =
        FrozenSet<(string, string)>.Empty;
    private readonly IReadOnlyDictionary<SkillProgramCardPolicyKind,
        IReadOnlyList<(IndexedSkillProgramInstance Source, SkillProgramCardPolicy Policy)>> _cardPolicies =
        ReadOnlyDictionary<SkillProgramCardPolicyKind, IReadOnlyList<(IndexedSkillProgramInstance, SkillProgramCardPolicy)>>.Empty;
    private readonly IReadOnlyDictionary<CardKind,
        IReadOnlyList<(IndexedSkillProgramInstance Source, SkillProgramViewAs Rule)>> _singleCardConversions =
        ReadOnlyDictionary<CardKind, IReadOnlyList<(IndexedSkillProgramInstance, SkillProgramViewAs)>>.Empty;
    private readonly IReadOnlyDictionary<SkillProgramTriggerWindow, IReadOnlyList<ProgramTriggerCandidate>> _triggerCandidates =
        ReadOnlyDictionary<SkillProgramTriggerWindow, IReadOnlyList<ProgramTriggerCandidate>>.Empty;
    private readonly IReadOnlySet<(string SkillId, string BindingId)> _sharedUsageTriggerBindings =
        FrozenSet<(string, string)>.Empty;
    private static readonly IReadOnlyList<SkillGrant> EmptyGrants = Array.Empty<SkillGrant>();
    private static readonly IReadOnlyList<SkillProgram> EmptyPrograms = Array.Empty<SkillProgram>();
    private static readonly IReadOnlyList<IndexedSkillProgramInstance> EmptyInstances =
        Array.Empty<IndexedSkillProgramInstance>();
    private static readonly IReadOnlyList<IndexedSkillProgramTrigger> EmptyTriggers =
        Array.Empty<IndexedSkillProgramTrigger>();
    private static readonly IReadOnlyList<ProgramTriggerCandidate> EmptyTriggerCandidates =
        Array.Empty<ProgramTriggerCandidate>();
    private static readonly IReadOnlyList<IndexedSkillProgramModifier> EmptyModifiers =
        Array.Empty<IndexedSkillProgramModifier>();

    internal SkillBindingShard(
        int ownerSeat,
        SkillBindingIndexStamp stamp,
        IReadOnlyList<SkillGrant> activeGrants,
        IReadOnlyDictionary<string, ContentSkillDefinition> definitions,
        IReadOnlyList<IndexedSkillProgramInstance> programInstances,
        IReadOnlyList<SkillProgram> programs,
        IReadOnlyDictionary<SkillProgramTriggerWindow, IReadOnlyList<IndexedSkillProgramTrigger>>
            instanceTriggers,
        IReadOnlyDictionary<SkillProgramTriggerWindow, IReadOnlyList<IndexedSkillProgramTrigger>>
            uniqueTriggers,
        IReadOnlyDictionary<SkillRuleQuery, IReadOnlyList<IndexedSkillProgramModifier>> numericModifiers,
        IReadOnlyList<SkillProgram> activationPrograms,
        IReadOnlyList<SkillProgram> contributionPrograms,
        IReadOnlyList<SkillProgram> viewAsPrograms,
        IReadOnlyList<SkillProgram> cardIdentityPrograms,
        IReadOnlyList<SkillProgram> passiveRulePrograms)
    {
        Stamp = stamp;
        ActiveGrants = activeGrants;
        Definitions = definitions;
        ProgramInstances = programInstances;
        Programs = programs;
        InstanceTriggers = instanceTriggers;
        UniqueTriggers = uniqueTriggers;
        NumericModifiers = numericModifiers;
        ActivationPrograms = activationPrograms;
        ContributionPrograms = contributionPrograms;
        ViewAsPrograms = viewAsPrograms;
        CardIdentityPrograms = cardIdentityPrograms;
        PassiveRulePrograms = passiveRulePrograms;
        var sharedUsageTriggerBindings = new HashSet<(string SkillId, string BindingId)>();
        _sharedUsageTriggerBindings = sharedUsageTriggerBindings;
        var candidates = new Dictionary<SkillProgramTriggerWindow, IReadOnlyList<ProgramTriggerCandidate>>();
        foreach (var (window, bindings) in instanceTriggers)
        {
            foreach (var binding in bindings)
                if (binding.Trigger.NamedUsageGroup is not null || binding.Trigger.DynamicUsageLimit is not null ||
                    binding.Trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.DiscardHandToNamedTurnCount))
                    sharedUsageTriggerBindings.Add((binding.SkillId, binding.Trigger.Id));
            candidates.Add(window, Array.AsReadOnly(bindings
                .OrderByDescending(binding => binding.Trigger.Priority)
                .ThenBy(binding => binding.SkillId, StringComparer.Ordinal)
                .ThenBy(binding => binding.SkillInstanceId, StringComparer.Ordinal)
                .ThenBy(binding => binding.Trigger.ChoiceGroup ?? binding.Trigger.Id, StringComparer.Ordinal)
                .ThenBy(binding => binding.Trigger.Id, StringComparer.Ordinal)
                .DistinctBy(binding => (binding.SkillId, binding.Trigger.Id,
                    _sharedUsageTriggerBindings.Contains((binding.SkillId, binding.Trigger.Id)) ? "" : binding.SkillInstanceId))
                .Select(binding => new ProgramTriggerCandidate(ownerSeat, binding.SkillId, binding.Trigger.Id,
                    binding.SkillInstanceId, binding.Program.GameplayHash, binding.Trigger.Priority))
                .ToArray()));
        }
        _triggerCandidates = new ReadOnlyDictionary<SkillProgramTriggerWindow, IReadOnlyList<ProgramTriggerCandidate>>(candidates);
        _activeInstances = activeGrants
            .Select(grant => (grant.SkillId, grant.SkillInstanceId))
            .ToHashSet();
        _cardPolicies = programInstances.SelectMany(instance => instance.Program.CardPolicies
                .SelectMany(policy => policy.Kind == SkillProgramCardPolicyKind.IgnoreUseDistanceBeforeDealingDamage
                    ? new[] { (Kind: policy.Kind, Source: instance, Policy: policy),
                        (Kind: SkillProgramCardPolicyKind.IgnoreUseDistance, Source: instance, Policy: policy) }
                    : new[] { (Kind: policy.Kind, Source: instance, Policy: policy) }))
            .GroupBy(item => item.Kind).ToDictionary(group => group.Key,
                group => (IReadOnlyList<(IndexedSkillProgramInstance, SkillProgramCardPolicy)>)Array.AsReadOnly(
                    group.Select(item => (item.Source, item.Policy)).ToArray()));
        var conversions = new Dictionary<CardKind, List<(IndexedSkillProgramInstance, SkillProgramViewAs)>>();
        foreach (var instance in programInstances)
        foreach (var rule in instance.Program.ViewAs)
        {
            if (rule.InputCount != 1) continue;
            AddConversion(rule.OutputKind, instance, rule);
            if (rule.OutputKind == CardKind.FireSlash && rule.ExtendedUse ||
                rule.OutputKind is CardKind.FireSlash or CardKind.ThunderSlash && (rule.DeclarationValidation is not null || rule.EqualNameLengthUse || rule.RankTwoDamageUse || instance.SkillId == "le:fule"))
                AddConversion(CardKind.Slash, instance, rule);
        }
        _singleCardConversions = conversions.ToDictionary(item => item.Key,
            item => (IReadOnlyList<(IndexedSkillProgramInstance, SkillProgramViewAs)>)Array.AsReadOnly(item.Value.ToArray()));

        void AddConversion(CardKind kind, IndexedSkillProgramInstance source, SkillProgramViewAs rule)
        {
            if (!conversions.TryGetValue(kind, out var bucket)) conversions[kind] = bucket = [];
            bucket.Add((source, rule));
        }
    }

    internal SkillBindingIndexStamp Stamp { get; }
    internal SkillBindingShard(SkillBindingIndexStamp stamp) => Stamp = stamp;

    internal IReadOnlyList<SkillGrant> ActiveGrants { get; } = EmptyGrants;
    internal IReadOnlyDictionary<string, ContentSkillDefinition> Definitions { get; } =
        ReadOnlyDictionary<string, ContentSkillDefinition>.Empty;
    internal IReadOnlyList<IndexedSkillProgramInstance> ProgramInstances { get; } = EmptyInstances;
    internal IReadOnlyList<SkillProgram> Programs { get; } = EmptyPrograms;
    internal IReadOnlyDictionary<SkillProgramTriggerWindow, IReadOnlyList<IndexedSkillProgramTrigger>>
        InstanceTriggers { get; } = ReadOnlyDictionary<SkillProgramTriggerWindow, IReadOnlyList<IndexedSkillProgramTrigger>>.Empty;
    internal IReadOnlyDictionary<SkillProgramTriggerWindow, IReadOnlyList<IndexedSkillProgramTrigger>>
        UniqueTriggers { get; } = ReadOnlyDictionary<SkillProgramTriggerWindow, IReadOnlyList<IndexedSkillProgramTrigger>>.Empty;
    internal IReadOnlyDictionary<SkillRuleQuery, IReadOnlyList<IndexedSkillProgramModifier>> NumericModifiers { get; } =
        ReadOnlyDictionary<SkillRuleQuery, IReadOnlyList<IndexedSkillProgramModifier>>.Empty;
    internal IReadOnlyList<SkillProgram> ActivationPrograms { get; } = EmptyPrograms;
    internal IReadOnlyList<SkillProgram> ContributionPrograms { get; } = EmptyPrograms;
    internal IReadOnlyList<SkillProgram> ViewAsPrograms { get; } = EmptyPrograms;
    internal IReadOnlyList<SkillProgram> CardIdentityPrograms { get; } = EmptyPrograms;
    internal IReadOnlyList<SkillProgram> PassiveRulePrograms { get; } = EmptyPrograms;

    internal IReadOnlyList<IndexedSkillProgramTrigger> GetInstanceTriggers(SkillProgramTriggerWindow window) =>
        InstanceTriggers.GetValueOrDefault(window, EmptyTriggers);

    internal IReadOnlyList<IndexedSkillProgramTrigger> GetUniqueTriggers(SkillProgramTriggerWindow window) =>
        UniqueTriggers.GetValueOrDefault(window, EmptyTriggers);

    internal IReadOnlyList<ProgramTriggerCandidate> GetTriggerCandidates(SkillProgramTriggerWindow window, int occurrenceIndex = 0)
    {
        var candidates = _triggerCandidates.GetValueOrDefault(window, EmptyTriggerCandidates);
        if (occurrenceIndex == 0 || candidates.Count == 0) return candidates;
        var occurrences = new ProgramTriggerCandidate[candidates.Count];
        for (var i = 0; i < candidates.Count; i++) occurrences[i] = candidates[i] with { OccurrenceIndex = occurrenceIndex };
        return Array.AsReadOnly(occurrences);
    }

    internal (string SkillId, string BindingId, string SkillInstanceId) TriggerCandidateKey(ProgramTriggerCandidate candidate) =>
        (candidate.SkillId, candidate.BindingId,
            _sharedUsageTriggerBindings.Contains((candidate.SkillId, candidate.BindingId)) ? "" : candidate.SkillInstanceId);

    internal IReadOnlyList<IndexedSkillProgramModifier> GetNumericModifiers(SkillRuleQuery query) =>
        NumericModifiers.GetValueOrDefault(query, EmptyModifiers);

    internal bool HasSkill(string skillId) => Definitions.ContainsKey(skillId);

    internal bool HasInstance(string skillId, string skillInstanceId) =>
        _activeInstances.Contains((skillId, skillInstanceId));

    internal bool HasProgram(string skillId, string? gameplayHash = null) =>
        Definitions.TryGetValue(skillId, out var definition) && definition.Program is { } program &&
        (gameplayHash is null || string.Equals(program.GameplayHash, gameplayHash, StringComparison.Ordinal));

    internal SkillProgram? GetProgram(string skillId) =>
        Definitions.GetValueOrDefault(skillId)?.Program;

    internal IReadOnlyList<(IndexedSkillProgramInstance Source, SkillProgramCardPolicy Policy)>
        GetCardPolicies(SkillProgramCardPolicyKind kind) => _cardPolicies.GetValueOrDefault(kind) ??
            Array.Empty<(IndexedSkillProgramInstance, SkillProgramCardPolicy)>();

    internal IReadOnlyList<(IndexedSkillProgramInstance Source, SkillProgramViewAs Rule)>
        GetSingleCardConversions(CardKind kind) => _singleCardConversions.GetValueOrDefault(kind) ??
            Array.Empty<(IndexedSkillProgramInstance, SkillProgramViewAs)>();

}

/// <summary>
/// Match-local, lazily rebuilt index for content skill bindings. Registry and
/// mode capabilities are fixed for the lifetime of one engine; only ownership
/// revision and national-war reveal state participate in shard invalidation.
/// </summary>
internal sealed class MatchSkillBindingIndex
{
    private readonly Func<string, ContentSkillDefinition> _resolveDefinition;
    private readonly bool _isNationalWarMode;
    private readonly bool _allowsSecondaryGeneral;
    private readonly Func<CharacterState, bool>? _hasHpSensitiveSuppression;
    private readonly Func<CharacterState, SkillGrant, bool>? _grantQualification;
    private readonly Func<CharacterState,IReadOnlySet<string>>? _suppressionInputs;
    private readonly Func<long>? _projectionStamp;
    private readonly Func<CharacterState, long>? _derivedProjectionStamp;
    private readonly Dictionary<int, (SkillBindingShard Shard, int RebuildCount)> _shards = [];

    internal MatchSkillBindingIndex(
        Func<string, ContentSkillDefinition> resolveDefinition,
        bool isNationalWarMode,
        Func<CharacterState, bool>? hasHpSensitiveSuppression = null,
        Func<CharacterState, SkillGrant, bool>? grantQualification = null, Func<long>? projectionStamp = null, Func<CharacterState,IReadOnlySet<string>>? suppressionInputs = null,
        Func<CharacterState, long>? derivedProjectionStamp = null, bool dualGenerals = false)
    {
        ArgumentNullException.ThrowIfNull(resolveDefinition);
        _resolveDefinition = resolveDefinition;
        _isNationalWarMode = isNationalWarMode;
        _allowsSecondaryGeneral = isNationalWarMode || dualGenerals;
        _hasHpSensitiveSuppression = hasHpSensitiveSuppression;
        _grantQualification = grantQualification; _projectionStamp = projectionStamp; _suppressionInputs = suppressionInputs;
        _derivedProjectionStamp = derivedProjectionStamp;
    }

    internal int CachedSeatCount => _shards.Count;
    internal int TotalRebuildCount => _shards.Values.Sum(static entry => entry.RebuildCount);
    internal int GetRebuildCount(int seat) => _shards.GetValueOrDefault(seat).RebuildCount;
    internal void Invalidate(int seat) => _shards.Remove(seat);

    internal SkillBindingShard GetShard(CharacterState player)
    {
        ArgumentNullException.ThrowIfNull(player);
        var stamp = CaptureStamp(player) with
        {
            CurrentHp = _hasHpSensitiveSuppression?.Invoke(player) == true ? player.Hp : 0,
            ProjectionRevision = _projectionStamp?.Invoke() ?? 0,
            DerivedProjectionRevision = _derivedProjectionStamp?.Invoke(player) ?? 0,
            OwnerRole = player.Role,
            OwnerAlive = player.IsAlive
        };
        if (_shards.TryGetValue(player.Seat, out var existing) && existing.Shard.Stamp == stamp)
            return existing.Shard;

        var rebuilt = BuildShard(player, stamp);
        _shards[player.Seat] = (rebuilt, GetRebuildCount(player.Seat) + 1);
        return rebuilt;
    }

    internal static SkillBindingIndexStamp CaptureStamp(CharacterState player) => new(
        player.SkillGrants.Revision,
        0,
        player.GeneralSelected,
        player.GeneralRevealed,
        player.SecondaryGeneral is not null,
        player.SecondaryGeneralSelected,
        player.SecondaryGeneralRevealed);

    private SkillBindingShard BuildShard(CharacterState player, SkillBindingIndexStamp stamp)
    {
        var activeGrants = new List<SkillGrant>();
        var definitions = new Dictionary<string, ContentSkillDefinition>(StringComparer.Ordinal);
        var resolvedDefinitions = new Dictionary<string, ContentSkillDefinition>(StringComparer.Ordinal);
        foreach (var grant in player.SkillGrants.Grants)
        {
            if (!grant.IsEnabled || !CanUseSource(player, grant.SourceId) || _grantQualification?.Invoke(player, grant) == false) continue;
            if (!resolvedDefinitions.TryGetValue(grant.SkillId, out var definition))
            {
                definition = _resolveDefinition(grant.SkillId);
                resolvedDefinitions.Add(grant.SkillId, definition);
            }
            var isTemplate = grant.SourceId is CharacterState.PrimarySkillSource or
                CharacterState.SecondarySkillSource;
            if (isTemplate && definition.Tags.HasFlag(SkillTag.Lord) &&
                player.Role != Role.Lord && !PrintedLordSkillQualifications.Matches(player, grant))
                continue;
            activeGrants.Add(grant);
            definitions.TryAdd(grant.SkillId, definition);
        }

        var suppressors = _suppressionInputs?.Invoke(player) ?? activeGrants.Where(grant =>
                definitions[grant.SkillId].SuppressionRule is { } rule &&
                player.Hp == rule.OwnerHpEquals)
            .Select(grant => grant.SkillId).ToHashSet(StringComparer.Ordinal);
        if (suppressors.Count > 0)
        {
            activeGrants.RemoveAll(grant =>
                !grant.IsModeRule &&
                !suppressors.Contains(grant.SkillId) &&
                !grant.SourceId.StartsWith("equipment:", StringComparison.Ordinal));
            var remainingSkillIds = activeGrants.Select(grant => grant.SkillId)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var skillId in definitions.Keys.Where(id => !remainingSkillIds.Contains(id)).ToArray())
                definitions.Remove(skillId);
        }

        if (activeGrants.Count == 0)
            return new SkillBindingShard(stamp);

        var instances = activeGrants
            .GroupBy(grant => (grant.SkillId, grant.SkillInstanceId))
            .Select(group => group.OrderBy(grant => grant.GrantId, StringComparer.Ordinal).First())
            .Select(grant => (Grant: grant, Definition: definitions[grant.SkillId]))
            .Where(item => item.Definition.Program is not null)
            .Select(item => new IndexedSkillProgramInstance(
                item.Grant.SkillId,
                item.Grant.SkillInstanceId,
                item.Definition,
                item.Definition.Program!))
            .OrderBy(instance => instance.SkillId, StringComparer.Ordinal)
            .ThenBy(instance => instance.SkillInstanceId, StringComparer.Ordinal)
            .ToArray();
        var programs = instances
            .GroupBy(instance => instance.SkillId, StringComparer.Ordinal)
            .Select(group => group.First().Program)
            .OrderBy(program => program.Id, StringComparer.Ordinal)
            .ToArray();

        return new SkillBindingShard(
            player.Seat,
            stamp,
            Array.AsReadOnly(activeGrants.ToArray()),
            new ReadOnlyDictionary<string, ContentSkillDefinition>(definitions),
            Array.AsReadOnly(instances),
            Array.AsReadOnly(programs),
            BuildTriggerBuckets(instances),
            BuildTriggerBuckets(instances.GroupBy(instance => instance.SkillId, StringComparer.Ordinal)
                .Select(group => group.First())),
            BuildNumericBuckets(instances),
            Array.AsReadOnly(programs.Where(program => program.Activations.Count != 0).ToArray()),
            Array.AsReadOnly(programs.Where(program => program.Contributions.Count != 0).ToArray()),
            Array.AsReadOnly(programs.Where(program => program.ViewAs.Count != 0).ToArray()),
            Array.AsReadOnly(programs.Where(program => program.CardIdentities.Count != 0).ToArray()),
            Array.AsReadOnly(programs.Where(program =>
                program.Modifiers.Count != 0 || program.ViewAs.Count != 0).ToArray()));
    }

    private bool CanUseSource(CharacterState player, string sourceId)
    {
        if (sourceId == CharacterState.PrimarySkillSource)
            return !_isNationalWarMode || player.GeneralSelected && player.GeneralRevealed;
        if (sourceId == CharacterState.SecondarySkillSource)
            return _allowsSecondaryGeneral && player.SecondaryGeneral is not null &&
                   player.SecondaryGeneralSelected && player.SecondaryGeneralRevealed;
        return true;
    }

    private static IReadOnlyDictionary<SkillProgramTriggerWindow, IReadOnlyList<IndexedSkillProgramTrigger>>
        BuildTriggerBuckets(IEnumerable<IndexedSkillProgramInstance> instances) =>
        FreezeBuckets(instances.SelectMany(instance => instance.Program.Triggers.Select(trigger =>
                new IndexedSkillProgramTrigger(instance.SkillId, instance.SkillInstanceId,
                    instance.Program, trigger))), binding => binding.Trigger.Window);

    private static IReadOnlyDictionary<SkillRuleQuery, IReadOnlyList<IndexedSkillProgramModifier>>
        BuildNumericBuckets(IReadOnlyList<IndexedSkillProgramInstance> instances) =>
        FreezeBuckets(instances
            .SelectMany(instance => instance.Program.Modifiers.Select(modifier =>
                new IndexedSkillProgramModifier(
                    new SkillProgramRuleSource(instance.SkillId, instance.SkillInstanceId, instance.Program),
                    modifier))), binding => binding.Modifier.Query);

    private static IReadOnlyDictionary<TKey, IReadOnlyList<TBinding>> FreezeBuckets<TKey, TBinding>(
        IEnumerable<TBinding> bindings, Func<TBinding, TKey> key) where TKey : notnull =>
        new ReadOnlyDictionary<TKey, IReadOnlyList<TBinding>>(bindings.GroupBy(key).ToDictionary(
            group => group.Key, group => (IReadOnlyList<TBinding>)Array.AsReadOnly(group.ToArray())));
}
