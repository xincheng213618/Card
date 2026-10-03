using System.Collections.ObjectModel;

namespace CardGame.Core;

internal readonly record struct SkillBindingIndexStamp(
    long SkillRevision,
    int CurrentHp,
    bool PrimarySelected,
    bool PrimaryRevealed,
    bool HasSecondary,
    bool SecondarySelected,
    bool SecondaryRevealed, long ProjectionRevision = 0, Role? MarkerQualificationRole = null);

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
    private readonly HashSet<(string SkillId, string SkillInstanceId)> _activeInstances;
    private readonly IReadOnlyDictionary<string, SkillProgram> _programsById;
    private static readonly IReadOnlyList<SkillGrant> EmptyGrants = Array.Empty<SkillGrant>();
    private static readonly IReadOnlyList<SkillProgram> EmptyPrograms = Array.Empty<SkillProgram>();
    private static readonly IReadOnlyList<IndexedSkillProgramInstance> EmptyInstances =
        Array.Empty<IndexedSkillProgramInstance>();
    private static readonly IReadOnlyList<IndexedSkillProgramTrigger> EmptyTriggers =
        Array.Empty<IndexedSkillProgramTrigger>();
    private static readonly IReadOnlyList<IndexedSkillProgramModifier> EmptyModifiers =
        Array.Empty<IndexedSkillProgramModifier>();

    internal SkillBindingShard(
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
        _activeInstances = activeGrants
            .Select(grant => (grant.SkillId, grant.SkillInstanceId))
            .ToHashSet();
        _programsById = new ReadOnlyDictionary<string, SkillProgram>(programs
            .ToDictionary(program => program.Id, StringComparer.Ordinal));
    }

    internal SkillBindingIndexStamp Stamp { get; }
    internal IReadOnlyList<SkillGrant> ActiveGrants { get; }
    internal IReadOnlyDictionary<string, ContentSkillDefinition> Definitions { get; }
    internal IReadOnlyList<IndexedSkillProgramInstance> ProgramInstances { get; }
    internal IReadOnlyList<SkillProgram> Programs { get; }
    internal IReadOnlyDictionary<SkillProgramTriggerWindow, IReadOnlyList<IndexedSkillProgramTrigger>>
        InstanceTriggers { get; }
    internal IReadOnlyDictionary<SkillProgramTriggerWindow, IReadOnlyList<IndexedSkillProgramTrigger>>
        UniqueTriggers { get; }
    internal IReadOnlyDictionary<SkillRuleQuery, IReadOnlyList<IndexedSkillProgramModifier>> NumericModifiers { get; }
    internal IReadOnlyList<SkillProgram> ActivationPrograms { get; }
    internal IReadOnlyList<SkillProgram> ContributionPrograms { get; }
    internal IReadOnlyList<SkillProgram> ViewAsPrograms { get; }
    internal IReadOnlyList<SkillProgram> CardIdentityPrograms { get; }
    internal IReadOnlyList<SkillProgram> PassiveRulePrograms { get; }

    internal IReadOnlyList<IndexedSkillProgramTrigger> GetInstanceTriggers(SkillProgramTriggerWindow window) =>
        InstanceTriggers.GetValueOrDefault(window, EmptyTriggers);

    internal IReadOnlyList<IndexedSkillProgramTrigger> GetUniqueTriggers(SkillProgramTriggerWindow window) =>
        UniqueTriggers.GetValueOrDefault(window, EmptyTriggers);

    internal IReadOnlyList<IndexedSkillProgramModifier> GetNumericModifiers(SkillRuleQuery query) =>
        NumericModifiers.GetValueOrDefault(query, EmptyModifiers);

    internal bool HasSkill(string skillId) => Definitions.ContainsKey(skillId);

    internal bool HasInstance(string skillId, string skillInstanceId) =>
        _activeInstances.Contains((skillId, skillInstanceId));

    internal bool HasProgram(string skillId, string? gameplayHash = null) =>
        _programsById.TryGetValue(skillId, out var program) &&
        (gameplayHash is null || string.Equals(program.GameplayHash, gameplayHash, StringComparison.Ordinal));

    internal SkillProgram? GetProgram(string skillId) =>
        _programsById.GetValueOrDefault(skillId);

    internal static SkillBindingShard Empty(SkillBindingIndexStamp stamp) => new(
        stamp,
        EmptyGrants,
        new ReadOnlyDictionary<string, ContentSkillDefinition>(
            new Dictionary<string, ContentSkillDefinition>(StringComparer.Ordinal)),
        EmptyInstances,
        EmptyPrograms,
        EmptyTriggerMap(),
        EmptyTriggerMap(),
        new ReadOnlyDictionary<SkillRuleQuery, IReadOnlyList<IndexedSkillProgramModifier>>(
            new Dictionary<SkillRuleQuery, IReadOnlyList<IndexedSkillProgramModifier>>()),
        EmptyPrograms,
        EmptyPrograms,
        EmptyPrograms,
        EmptyPrograms,
        EmptyPrograms);

    private static IReadOnlyDictionary<SkillProgramTriggerWindow, IReadOnlyList<IndexedSkillProgramTrigger>>
        EmptyTriggerMap() =>
        new ReadOnlyDictionary<SkillProgramTriggerWindow, IReadOnlyList<IndexedSkillProgramTrigger>>(
            new Dictionary<SkillProgramTriggerWindow, IReadOnlyList<IndexedSkillProgramTrigger>>());
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
    private readonly Func<CharacterState, bool>? _hasHpSensitiveSuppression;
    private readonly Func<CharacterState, SkillGrant, bool>? _grantQualification;
    private readonly Func<CharacterState,IReadOnlySet<string>>? _suppressionInputs;
    private readonly Func<long>? _projectionStamp;
    private readonly bool _trackMarkerQualification;
    private readonly Dictionary<int, SkillBindingShard> _shards = [];
    private readonly Dictionary<int, int> _rebuildCounts = [];

    internal MatchSkillBindingIndex(
        Func<string, ContentSkillDefinition> resolveDefinition,
        bool isNationalWarMode,
        Func<CharacterState, bool>? hasHpSensitiveSuppression = null,
        Func<CharacterState, SkillGrant, bool>? grantQualification = null, Func<long>? projectionStamp = null, Func<CharacterState,IReadOnlySet<string>>? suppressionInputs = null, bool trackMarkerQualification = false)
    {
        ArgumentNullException.ThrowIfNull(resolveDefinition);
        _resolveDefinition = resolveDefinition;
        _isNationalWarMode = isNationalWarMode;
        _hasHpSensitiveSuppression = hasHpSensitiveSuppression;
        _grantQualification = grantQualification; _projectionStamp = projectionStamp; _suppressionInputs = suppressionInputs;
        _trackMarkerQualification = trackMarkerQualification;
    }

    internal int CachedSeatCount => _shards.Count;
    internal int TotalRebuildCount => _rebuildCounts.Values.Sum();
    internal int GetRebuildCount(int seat) => _rebuildCounts.GetValueOrDefault(seat);

    internal SkillBindingShard GetShard(CharacterState player)
    {
        ArgumentNullException.ThrowIfNull(player);
        var stamp = CaptureStamp(player) with
        {
            CurrentHp = _hasHpSensitiveSuppression?.Invoke(player) == true ? player.Hp : 0,
            ProjectionRevision = _projectionStamp?.Invoke() ?? 0,
            MarkerQualificationRole = _trackMarkerQualification ? player.Role : null
        };
        if (_shards.TryGetValue(player.Seat, out var existing) && existing.Stamp == stamp)
            return existing;

        var rebuilt = BuildShard(player, stamp);
        _shards[player.Seat] = rebuilt;
        _rebuildCounts[player.Seat] = GetRebuildCount(player.Seat) + 1;
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
                player.Role != Role.Lord)
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
                !suppressors.Contains(grant.SkillId) &&
                !grant.SourceId.StartsWith("equipment:", StringComparison.Ordinal));
            var remainingSkillIds = activeGrants.Select(grant => grant.SkillId)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var skillId in definitions.Keys.Where(id => !remainingSkillIds.Contains(id)).ToArray())
                definitions.Remove(skillId);
        }

        if (activeGrants.Count == 0)
            return SkillBindingShard.Empty(stamp);

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
            stamp,
            Array.AsReadOnly(activeGrants.ToArray()),
            new ReadOnlyDictionary<string, ContentSkillDefinition>(definitions),
            Array.AsReadOnly(instances),
            Array.AsReadOnly(programs),
            BuildInstanceTriggerBuckets(instances),
            BuildUniqueTriggerBuckets(instances),
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
            return _isNationalWarMode && player.SecondaryGeneral is not null &&
                   player.SecondaryGeneralSelected && player.SecondaryGeneralRevealed;
        return true;
    }

    private static IReadOnlyDictionary<SkillProgramTriggerWindow, IReadOnlyList<IndexedSkillProgramTrigger>>
        BuildInstanceTriggerBuckets(IReadOnlyList<IndexedSkillProgramInstance> instances) =>
        Freeze(instances.SelectMany(instance => instance.Program.Triggers.Select(trigger =>
                new IndexedSkillProgramTrigger(instance.SkillId, instance.SkillInstanceId,
                    instance.Program, trigger)))
            .GroupBy(binding => binding.Trigger.Window)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<IndexedSkillProgramTrigger>)Array.AsReadOnly(group.ToArray())));

    private static IReadOnlyDictionary<SkillProgramTriggerWindow, IReadOnlyList<IndexedSkillProgramTrigger>>
        BuildUniqueTriggerBuckets(IReadOnlyList<IndexedSkillProgramInstance> instances) =>
        Freeze(instances.GroupBy(instance => instance.SkillId, StringComparer.Ordinal)
            .Select(group => group.First())
            .SelectMany(instance => instance.Program.Triggers.Select(trigger =>
                new IndexedSkillProgramTrigger(instance.SkillId, instance.SkillInstanceId,
                    instance.Program, trigger)))
            .GroupBy(binding => binding.Trigger.Window)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<IndexedSkillProgramTrigger>)Array.AsReadOnly(group.ToArray())));

    private static IReadOnlyDictionary<SkillRuleQuery, IReadOnlyList<IndexedSkillProgramModifier>>
        BuildNumericBuckets(IReadOnlyList<IndexedSkillProgramInstance> instances) =>
        Freeze(instances
            .SelectMany(instance => instance.Program.Modifiers.Select(modifier =>
                new IndexedSkillProgramModifier(
                    new SkillProgramRuleSource(instance.SkillId, instance.SkillInstanceId, instance.Program),
                    modifier)))
            .GroupBy(binding => binding.Modifier.Query)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<IndexedSkillProgramModifier>)Array.AsReadOnly(group.ToArray())));

    private static IReadOnlyDictionary<TKey, TValue> Freeze<TKey, TValue>(Dictionary<TKey, TValue> source)
        where TKey : notnull => new ReadOnlyDictionary<TKey, TValue>(source);
}
