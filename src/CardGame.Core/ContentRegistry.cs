using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CardGame.Core;

/// <summary>Stable namespaced id used by content packages and replay metadata.</summary>
public readonly record struct ContentId
{
    public ContentId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException("Content ids must be non-empty and namespaced, for example standard:slash.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value ?? string.Empty;
}

public sealed record PackageDependency(string Id, Version MinimumVersion);

public sealed record PackageManifest(
    string Id,
    Version Version,
    IReadOnlyList<PackageDependency> Dependencies)
{
    public PackageManifest(string id, Version version)
        : this(id, version, [])
    {
    }

    public PackageManifest(string id, string version, string description)
        : this(id, Version.Parse(version), [])
    {
    }
}

public interface IGameContentPackage
{
    PackageManifest Manifest { get; }

    void Register(IContentRegistryBuilder builder);
}

public interface IContentRegistryBuilder
{
    void AddCard(ContentCardDefinition definition);

    void AddSkill(ContentSkillDefinition definition);

    void AddGeneral(ContentGeneralDefinition definition);

    void AddDeck(ContentDeckRecipe definition);

    void AddMode(ContentModeDefinition definition);
}

public sealed record ContentCardDefinition(
    string Id,
    string DisplayName,
    string CategoryName,
    string Description,
    CardKind? LegacyKind = null,
    IReadOnlyDictionary<string, string>? AiTags = null);

public enum SkillImplementationStatus { Complete, Partial, Planned }

/// <summary>A passive rule that suspends the owner's other skills while its condition holds.</summary>
public sealed record SkillSuppressionRule(int OwnerHpEquals);

public sealed record ContentSkillDefinition(
    string Id,
    string Name,
    string Description)
{
    public SkillImplementationStatus ImplementationStatus { get; init; } = SkillImplementationStatus.Complete;
    public string? PendingImplementation { get; init; }
    public SkillProgram? Program { get; init; }
    public SkillPresentation? ProgramPresentation { get; init; }
    public SkillTag Tags { get; init; }
    public SkillExecutionForm ExecutionForms { get; init; }
    public SkillActionForm ActionForms { get; init; }
    public SkillSuppressionRule? SuppressionRule { get; init; }
    public IReadOnlyDictionary<Role, double>? SelectionWeights { get; init; }
    public SkillRevealWeights? RevealWeights { get; init; }
}

public sealed record ContentGeneralDefinition(
    string Id,
    string Name,
    string PortraitKey,
    string SkillId,
    string? FactionId = null,
    int BaseHp = 4,
    IReadOnlyList<string>? AdditionalSkillIds = null,
    GeneralGender Gender = GeneralGender.Male)
{
    /// <summary>Person, edition and selected rules module are separate catalogue identities.</summary>
    public string? CharacterId { get; init; }
    public string? VariantId { get; init; }
    public string? RulesetId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? InitialHp { get; init; }
    /// <summary>
    /// Complete ordered skill identities used by the current runtime.
    /// </summary>
    public IReadOnlyList<string> SkillIds => AdditionalSkillIds is { Count: > 0 }
        ? new[] { SkillId }.Concat(AdditionalSkillIds).ToArray()
        : [SkillId];
}

public sealed record ContentDeckCardCount(string CardDefinitionId, int Count);

public sealed record ContentDeckPhysicalCard(string CardDefinitionId, Suit Suit, int Rank);

public sealed record ContentDeckRecipe(
    string Id,
    string Name,
    int InitialHandSize,
    int DrawPerTurn,
    IReadOnlyList<ContentDeckCardCount> Cards)
{
    /// <summary>
    /// Optional ordered physical-card recipe with exact suit/rank data.
    /// Exactly one of Cards or PhysicalCards must contain entries.
    /// </summary>
    public IReadOnlyList<ContentDeckPhysicalCard>? PhysicalCards { get; init; }
}

public sealed record ContentModeDefinition(
    string Id,
    string Name,
    int MinPlayers,
    int MaxPlayers,
    IReadOnlyDictionary<string, int> RoleCounts,
    string? DeckId = null,
    int GeneralCandidateCount = 3,
    IReadOnlyList<string>? GeneralPoolIds = null,
    ContentModeKind ModeKind = ContentModeKind.Identity,
    IReadOnlyDictionary<string, int>? TeamCounts = null,
    IReadOnlyDictionary<string, int>? FactionCounts = null,
    IReadOnlyList<string>? SoloFactionIds = null)
{
    public TeamActivityDefinition? TeamActivity { get; init; }
    public BossActivityDefinition? BossActivity { get; init; }
    public HulaoActivityDefinition? HulaoActivity { get; init; }
    public ShenwuActivityDefinition? ShenwuActivity { get; init; }
    public NianActivityDefinition? NianActivity { get; init; }
    public HellActivityDefinition? HellActivity { get; init; }
    public WenheActivityDefinition? WenheActivity { get; init; }
    public DragonBoatActivityDefinition? DragonBoatActivity { get; init; }
    /// <summary>Eight-player identity variant with two generals per seat.</summary>
    public bool DualGenerals { get; init; }
    /// <summary>Optional identity-role overrides for the number of general choices.</summary>
    public IReadOnlyDictionary<string, int>? RoleGeneralCandidateCounts { get; init; }
    /// <summary>Optional identity candidate families; every listed version remains a distinct legal general.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? GeneralVariantGroups { get; init; }
    /// <summary>AI strength preferences only; these never affect candidate draw probability.</summary>
    public IReadOnlyDictionary<string, int>? GeneralSelectionWeights { get; init; }
    /// <summary>Exact editions AI may draw directly but may not obtain through another edition's swap.</summary>
    public IReadOnlyList<string>? AiGeneralSwapExcludedIds { get; init; }
}

/// <summary>
/// Immutable, per-engine content catalogue. No static registration state is
/// shared between registries, so test packages and future modes remain isolated.
/// </summary>
public sealed class ContentRegistry
{
    // Only the fixed expanded-domain proof is shared. Programs are immutable;
    // weak keys let a custom catalogue be collected without retaining its rules.
    private static readonly ConditionalWeakTable<SkillProgram, Lazy<bool>> ExpandedProgramProofs = new();

    private readonly IReadOnlyDictionary<string, ContentCardDefinition> _cards;
    private readonly IReadOnlyDictionary<string, ContentSkillDefinition> _skills;
    private readonly IReadOnlyDictionary<string, ContentGeneralDefinition> _generals;
    private readonly IReadOnlyDictionary<string, ContentDeckRecipe> _decks;
    private readonly IReadOnlyDictionary<string, ContentModeDefinition> _modes;

    private ContentRegistry(
        IReadOnlyList<PackageManifest> packages,
        IReadOnlyDictionary<string, ContentCardDefinition> cards,
        IReadOnlyDictionary<string, ContentSkillDefinition> skills,
        IReadOnlyDictionary<string, ContentGeneralDefinition> generals,
        IReadOnlyDictionary<string, ContentDeckRecipe> decks,
        IReadOnlyDictionary<string, ContentModeDefinition> modes)
    {
        Packages = packages;
        _cards = cards;
        _skills = skills;
        _generals = generals;
        _decks = decks;
        _modes = modes;
        ProgramDependencies = new SkillProgramDependencies(_skills.Values);
        ContentHash = ComputeContentHash(
            Packages,
            _cards,
            _skills,
            _generals,
            _decks,
            _modes);
    }

    public IReadOnlyList<PackageManifest> Packages { get; }

    /// <summary>
    /// Stable SHA-256 fingerprint of the normalized package manifests and all
    /// registered content definitions. It is independent of package input and
    /// dictionary insertion order, but intentionally does not fingerprint the
    /// executable rule implementation.
    /// </summary>
    public string ContentHash { get; }

    public IReadOnlyDictionary<string, ContentCardDefinition> Cards => _cards;

    public IReadOnlyDictionary<string, ContentSkillDefinition> Skills => _skills;

    public IReadOnlyDictionary<string, ContentGeneralDefinition> Generals => _generals;

    public IReadOnlyDictionary<string, ContentDeckRecipe> Decks => _decks;

    public IReadOnlyDictionary<string, ContentModeDefinition> Modes => _modes;
    internal SkillProgramDependencies ProgramDependencies { get; }

    public ContentCardDefinition GetCard(string id) =>
        _cards.TryGetValue(id, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown card content id '{id}'.");

    public ContentSkillDefinition GetSkill(string id) =>
        _skills.TryGetValue(id, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown skill content id '{id}'.");

    public bool IsGeneralPlayable(string id) => _generals.TryGetValue(id, out var general) &&
        general.SkillIds.All(skillId => _skills[skillId].ImplementationStatus == SkillImplementationStatus.Complete);

    public ContentDeckRecipe GetDeck(string id) =>
        _decks.TryGetValue(id, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Unknown deck content id '{id}'.");

    public static ContentRegistry Build(params IGameContentPackage[] packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        if (packages.Any(package => package is null))
        {
            throw new ArgumentException("A content package cannot be null.", nameof(packages));
        }

        var manifests = packages
            .Select(package => ValidateManifest(package.Manifest))
            .ToArray();
        if (manifests.Select(manifest => manifest.Id).Distinct(StringComparer.Ordinal).Count() != manifests.Length)
        {
            throw new InvalidOperationException("Content package ids must be unique.");
        }

        var packageById = manifests
            .ToDictionary(manifest => ValidatePackageId(manifest.Id), StringComparer.Ordinal);
        var ordered = TopologicallyOrderPackages(packageById);
        var builder = new RegistryBuilder();

        foreach (var manifest in ordered)
        {
            var package = packages.Single(candidate => candidate.Manifest.Id == manifest.Id);
            package.Register(builder.ForPackage(manifest.Id));
        }

        builder.ValidateReferences();
        var registry = builder.Freeze(ordered);
        var programs = registry.Skills.Values
            .Select(skill => skill.Program)
            .OfType<SkillProgram>().ToArray();
        SkillProgramRules.ValidateSetModifierConflicts(programs);
        ValidateExpandedProgramCardDomain(registry, programs);
        return registry;
    }

    private static void ValidateExpandedProgramCardDomain(ContentRegistry registry, IReadOnlyList<SkillProgram> programs)
    {
        if (!registry.Cards.Values.Any(card => card.LegacyKind is { } kind && ProgramCompositionValidator.IsExpandedCardKind(kind)) &&
            !registry.Decks.Values.Any(deck => deck.PhysicalCards?.Any(card => card.Suit == Suit.None) == true) &&
            !programs.Any(program => program.Activations.Any(activation => ProgramCompositionValidator.RequiresExpandedCardDomain(activation.Effects)) ||
                program.Triggers.Any(trigger => ProgramCompositionValidator.RequiresExpandedCardDomain(trigger.Effects)))) return;

        // New physical cards can pass between skills. Every program needs an
        // expanded-domain proof, even if it loaded on the historical ordinary
        // domain. This proof depends only on the immutable program, whereas
        // references, conflicts and fingerprints still belong to each registry.
        foreach (var program in programs)
        {
            try
            {
                _ = ExpandedProgramProofs.GetValue(program, static candidate =>
                    new Lazy<bool>(() => ValidateExpandedProgramCardDomain(candidate))).Value;
            }
            catch
            {
                ExpandedProgramProofs.Remove(program);
                throw;
            }
        }
    }

    private static bool ValidateExpandedProgramCardDomain(SkillProgram program)
    {
        foreach (var activation in program.Activations)
            ProgramCompositionValidator.Validate($"registry.skills[{program.Id}].activations[{activation.Id}]", activation.Effects,
                activation.MinTargets == 1 && activation.MaxTargets == 1, activation.MaxCards,
                initialTargetSetCount: activation.MaxTargets > 1 ? activation.MinTargets : 0,
                initialTargetSetMaximum: activation.MaxTargets > 1 ? activation.MaxTargets : 0,
                expandedCardDomain: true, activationTargetKind: activation.TargetKind, activationSourceZones: activation.SourceZones, activationMinimumCards: activation.MinCards, activationCardCategories: activation.CardCategories);
        foreach (var trigger in program.Triggers)
            ProgramCompositionValidator.Validate($"registry.skills[{program.Id}].triggers[{trigger.Id}]", trigger.Effects,
                initialSelectedTarget: trigger.DeferredTurnEndOnly, window: trigger.Window, drawPhaseMode: trigger.DrawPhaseMode, expandedCardDomain: true, cardActionRelation: trigger.OwnerRelation, cardKinds: trigger.CardKinds, turnOwnerScope: trigger.TurnOwnerScope);
        return true;
    }

    private static IReadOnlyList<PackageManifest> TopologicallyOrderPackages(
        IReadOnlyDictionary<string, PackageManifest> packages)
    {
        var ordered = new List<PackageManifest>(packages.Count);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        void Visit(string id)
        {
            if (visited.Contains(id))
            {
                return;
            }

            if (!visiting.Add(id))
            {
                throw new InvalidOperationException($"Content package dependency cycle detected at '{id}'.");
            }

            var manifest = packages[id];
            foreach (var dependency in manifest.Dependencies.OrderBy(item => item.Id, StringComparer.Ordinal))
            {
                if (!packages.TryGetValue(dependency.Id, out var dependencyManifest))
                {
                    throw new InvalidOperationException(
                        $"Package '{id}' depends on missing package '{dependency.Id}'.");
                }

                if (dependencyManifest.Version < dependency.MinimumVersion)
                {
                    throw new InvalidOperationException(
                        $"Package '{id}' requires '{dependency.Id}' version {dependency.MinimumVersion} or newer, " +
                        $"but {dependencyManifest.Version} was supplied.");
                }

                Visit(dependency.Id);
            }

            visiting.Remove(id);
            visited.Add(id);
            ordered.Add(manifest);
        }

        foreach (var id in packages.Keys.OrderBy(value => value, StringComparer.Ordinal))
        {
            Visit(id);
        }

        return ordered;
    }

    private static string ValidatePackageId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("A content package must have an id.");
        }

        return id;
    }

    private static string ComputeContentHash(
        IReadOnlyList<PackageManifest> packages,
        IReadOnlyDictionary<string, ContentCardDefinition> cards,
        IReadOnlyDictionary<string, ContentSkillDefinition> skills,
        IReadOnlyDictionary<string, ContentGeneralDefinition> generals,
        IReadOnlyDictionary<string, ContentDeckRecipe> decks,
        IReadOnlyDictionary<string, ContentModeDefinition> modes)
    {
        // One canonical representation of current content. No nested historical hash layouts.
        var canonical = JsonSerializer.Serialize(new
        {
            HashSchema = 14,
            Packages = packages.OrderBy(package => package.Id, StringComparer.Ordinal).Select(package => new
            {
                package.Id,
                Version = package.Version.ToString(),
                Dependencies = package.Dependencies.OrderBy(dependency => dependency.Id, StringComparer.Ordinal)
                    .Select(dependency => new { dependency.Id, MinimumVersion = dependency.MinimumVersion.ToString() })
                    .ToArray()
            }).ToArray(),
            Cards = cards.Values.OrderBy(card => card.Id, StringComparer.Ordinal).Select(card => new
            {
                card.Id, card.DisplayName, card.CategoryName, card.Description,
                Kind = card.LegacyKind?.ToString(),
                AiTags = (card.AiTags ?? new Dictionary<string, string>()).OrderBy(tag => tag.Key, StringComparer.Ordinal)
                    .Select(tag => new { tag.Key, tag.Value }).ToArray()
            }).ToArray(),
            Skills = skills.Values.OrderBy(skill => skill.Id, StringComparer.Ordinal).Select(skill => new
            {
                skill.Id,
                ImplementationStatus = skill.ImplementationStatus.ToString(),
                // Program presentation has its own hash and does not change gameplay identity.
                Name = skill.Program is null ? skill.Name : string.Empty,
                Description = skill.Program is null ? skill.Description : string.Empty,
                Tags = skill.Tags.ToString(),
                ExecutionForms = skill.ExecutionForms.ToString(),
                ActionForms = skill.ActionForms.ToString(),
                skill.RevealWeights,
                SelectionWeights = skill.SelectionWeights?.OrderBy(pair => pair.Key)
                    .Select(pair => new { Role = pair.Key.ToString(), pair.Value }).ToArray(),
                Program = skill.Program is {} program ? new
                {
                    program.Id, program.RuntimeVersion, program.MinimumRulesVersion, program.GameplayHash
                } : null
            }).ToArray(),
            Generals = generals.Values.OrderBy(general => general.Id, StringComparer.Ordinal).Select(GeneralFingerprint).ToArray(),
            Decks = decks.Values.OrderBy(deck => deck.Id, StringComparer.Ordinal).Select(deck => new
            {
                deck.Id, deck.Name, deck.InitialHandSize, deck.DrawPerTurn,
                Cards = deck.Cards.Select(card => new { card.CardDefinitionId, card.Count }).ToArray(),
                PhysicalCards = (deck.PhysicalCards ?? []).Select(card => new
                {
                    card.CardDefinitionId, Suit = card.Suit.ToString(), card.Rank
                }).ToArray()
            }).ToArray(),
            Modes = modes.Values.OrderBy(mode => mode.Id, StringComparer.Ordinal).Select(ModeFingerprint).ToArray()
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

        static object ModeFingerprint(ContentModeDefinition mode)
        {
            var fingerprint = new Dictionary<string, object?>
            {
                [nameof(mode.Id)] = mode.Id, [nameof(mode.Name)] = mode.Name,
                [nameof(mode.MinPlayers)] = mode.MinPlayers, [nameof(mode.MaxPlayers)] = mode.MaxPlayers,
                ["Kind"] = mode.ModeKind.ToString(), [nameof(mode.DeckId)] = mode.DeckId,
                [nameof(mode.GeneralCandidateCount)] = mode.GeneralCandidateCount,
                [nameof(mode.GeneralPoolIds)] = mode.GeneralPoolIds?.ToArray(),
                [nameof(mode.RoleCounts)] = mode.RoleCounts.OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => new { entry.Key, entry.Value }).ToArray(),
                [nameof(mode.TeamCounts)] = (mode.TeamCounts ?? new Dictionary<string, int>()).OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => new { entry.Key, entry.Value }).ToArray(),
                [nameof(mode.FactionCounts)] = (mode.FactionCounts ?? new Dictionary<string, int>()).OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => new { entry.Key, entry.Value }).ToArray(),
                [nameof(mode.SoloFactionIds)] = (mode.SoloFactionIds ?? []).OrderBy(id => id, StringComparer.Ordinal).ToArray()
            };
            // Modes without overrides keep their historical fingerprint representation.
            if (mode.DualGenerals) fingerprint[nameof(mode.DualGenerals)] = true;
            if (mode.TeamActivity is { } activity)
                fingerprint[nameof(mode.TeamActivity)] = new
                {
                    PlayerPools = OrderedPools(activity.PlayerPools),
                    SpiritPools = OrderedPools(activity.SpiritPools),
                    MachinePools = OrderedPools(activity.MachinePools),
                    activity.PlayerTier, activity.SpiritTierMinimum, activity.SpiritTierMaximum, activity.MachineTier,
                    PlayerSkillIds = OrderedPools(activity.PlayerSkillIds)
                };
            if (mode.BossActivity is { } boss)
                fingerprint[nameof(mode.BossActivity)] = boss;
            if (mode.HulaoActivity is { } hulao)
                fingerprint[nameof(mode.HulaoActivity)] = hulao;
            if (mode.ShenwuActivity is { } shenwu)
                fingerprint[nameof(mode.ShenwuActivity)] = shenwu;
            if (mode.NianActivity is { } nian)
                fingerprint[nameof(mode.NianActivity)] = nian;
            if (mode.HellActivity is { } hell)
                fingerprint[nameof(mode.HellActivity)] = hell;
            if (mode.WenheActivity is { } wenhe)
                fingerprint[nameof(mode.WenheActivity)] = wenhe;
            if (mode.DragonBoatActivity is { } dragonBoat)
                fingerprint[nameof(mode.DragonBoatActivity)] = new
                { PlayerPoolIds = OrderedPools(dragonBoat.PlayerPoolIds), dragonBoat.PlayerTier, dragonBoat.Rounds };
            if (mode.RoleGeneralCandidateCounts is { } candidates)
                fingerprint[nameof(mode.RoleGeneralCandidateCounts)] = candidates.OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => new { entry.Key, entry.Value }).ToArray();
            if (mode.GeneralVariantGroups is { } variants)
                fingerprint[nameof(mode.GeneralVariantGroups)] = variants.OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => new { entry.Key, Value = entry.Value.Order(StringComparer.Ordinal).ToArray() }).ToArray();
            if (mode.GeneralSelectionWeights is { } weights)
                fingerprint[nameof(mode.GeneralSelectionWeights)] = weights.OrderBy(entry => entry.Key, StringComparer.Ordinal)
                    .Select(entry => new { entry.Key, entry.Value }).ToArray();
            if (mode.AiGeneralSwapExcludedIds is { } excluded)
                fingerprint[nameof(mode.AiGeneralSwapExcludedIds)] = excluded.Order(StringComparer.Ordinal).ToArray();
            return fingerprint;
        }

        static object OrderedPools(IReadOnlyDictionary<string, IReadOnlyList<string>> pools) =>
            pools.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new { p.Key, p.Value }).ToArray();

        static object GeneralFingerprint(ContentGeneralDefinition general) => general.InitialHp is null
            ? new
            {
                general.CharacterId, general.VariantId, general.RulesetId,
                general.Id, general.Name, general.PortraitKey, general.FactionId, general.BaseHp,
                Gender = general.Gender.ToString(), Skills = general.SkillIds.ToArray()
            }
            : new
            {
                general.CharacterId, general.VariantId, general.RulesetId,
                general.Id, general.Name, general.PortraitKey, general.FactionId, general.BaseHp,
                Gender = general.Gender.ToString(), Skills = general.SkillIds.ToArray(), general.InitialHp
            };
    }
    private static PackageManifest ValidateManifest(PackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ValidatePackageId(manifest.Id);
        ArgumentNullException.ThrowIfNull(manifest.Version);
        if (manifest.Dependencies is null || manifest.Dependencies.Any(dependency => dependency is null))
        {
            throw new ArgumentException("A content package must provide non-null dependencies.", nameof(manifest));
        }

        if (manifest.Dependencies.Select(dependency => dependency.Id)
                .Distinct(StringComparer.Ordinal)
                .Count() != manifest.Dependencies.Count)
        {
            throw new ArgumentException("A content package cannot repeat a dependency.", nameof(manifest));
        }

        return manifest;
    }

    private sealed class RegistryBuilder : IContentRegistryBuilder
    {
        private readonly Dictionary<string, ContentCardDefinition> _cards = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ContentSkillDefinition> _skills = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ContentGeneralDefinition> _generals = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ContentDeckRecipe> _decks = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ContentModeDefinition> _modes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _owners = new(StringComparer.Ordinal);

        public IContentRegistryBuilder ForPackage(string packageId)
        {
            _ = packageId;
            return new PackageBuilder(this);
        }

        public void AddCard(ContentCardDefinition definition) => Add(
            _cards,
            NormalizeCard(definition),
            definition.Id,
            "card");

        public void AddSkill(ContentSkillDefinition definition) => Add(
            _skills,
            NormalizeSkill(definition),
            definition.Id,
            "skill");

        public void AddGeneral(ContentGeneralDefinition definition) => Add(
            _generals,
            NormalizeGeneral(definition),
            definition.Id,
            "general");

        public void AddDeck(ContentDeckRecipe definition) => Add(
            _decks,
            NormalizeDeck(definition),
            definition.Id,
            "deck");

        public void AddMode(ContentModeDefinition definition) => Add(
            _modes,
            NormalizeMode(definition),
            definition.Id,
            "mode");

        public void ValidateReferences()
        {
            RicePileContract.ValidatePrograms(_skills.Values.Where(s => s.Program is not null).ToDictionary(s => s.Id, s => s.Program!, StringComparer.Ordinal));
            foreach (var skill in _skills.Values.Where(item => item.Program is not null))
            {
                foreach (var restore in skill.Program!.Triggers.SelectMany(t => t.Effects)
                    .Where(e => e.Op == SkillProgramEffectOp.AddRaoshePindianRankAndRestoreGushe))
                    if (!_skills.TryGetValue(restore.SourceBind!, out var contest) || contest.Program is null ||
                        contest.Program.Activations.Where(a => a.Effects is [{ Op: SkillProgramEffectOp.StartSimultaneousPindian }]).ToArray() is not [var activation] ||
                        activation.UsesPerPhase != 1)
                        throw new InvalidOperationException("A Pindian rank restore requires its exact registered simultaneous contest phase activation.");
                foreach (var schedule in skill.Program!.Triggers.SelectMany(t => t.Effects)
                    .Where(e => e.Op == SkillProgramEffectOp.ScheduleEarnedActualEndingBenefit))
                {
                    if (!_skills.TryGetValue(schedule.SourceBind!, out var benefitSkill) || benefitSkill.Program is null ||
                        benefitSkill.Program.Triggers.SingleOrDefault(t => t.Id == schedule.StateId) is not
                        { Window: SkillProgramTriggerWindow.TurnEnding, Subject: SkillProgramTriggerSubject.Owner,
                            TurnOwnerScope: SkillProgramTurnOwnerScope.EarnedActualEnding, Optional: false } benefit ||
                        benefit.Effects is not [{ Op: SkillProgramEffectOp.DrawLostHpThenOfferOwnedCardsUpTo }])
                        throw new InvalidOperationException("An earned actual Ending schedule requires its exact mandatory lost-HP gift binding.");
                }
                foreach (var policy in skill.Program!.Triggers.SelectMany(t => t.Effects)
                    .Where(e => e.Op == SkillProgramEffectOp.DiscardOwnedCardToAdjustCurrentDamage).Select(e => e.PublicDeathDamageCost!))
                    if (!_skills.ContainsKey(policy.QualifierSkillId))
                        throw new InvalidOperationException("Signed damage cost references an unknown public-death qualifier skill.");
                foreach (var declaration in skill.Program!.ViewAs.Where(rule => rule.DeclarationValidation is not null))
                    if (!_skills.ContainsKey(declaration.DeclarationValidation!.ChallengeGrantSkillId))
                        throw new InvalidOperationException("A declaration validation references an unknown challenge grant skill.");
                foreach (var trigger in skill.Program!.Triggers.Where(t=>t.IncludeResponseUses && t.Window==SkillProgramTriggerWindow.CardUseCommitted || t.Effects.Any(e=>e.Op==SkillProgramEffectOp.IssueCardNoResponseAndPlayUseBan)))
                {
                    if (trigger.Window == SkillProgramTriggerWindow.CardUseCommitted &&
                        ProgramCommittedUseContracts.TryValidate(skill.Id, trigger)) continue;
                    if (trigger.Effects is [{ Op: SkillProgramEffectOp.HoldUsedSuitHandAndDraw }])
                    { FriendEventComposition.ValidateTrigger(skill.Id, trigger); continue; }
                    if (trigger.Effects is [{ Op: SkillProgramEffectOp.NullifyMatchingHandUse }])
                    { NamedAmbushContract.ValidateTrigger(skill.Id, trigger); continue; }
                    if (trigger.Window == SkillProgramTriggerWindow.CardUseCommitted &&
                        trigger.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && !trigger.Optional &&
                        trigger.Effects is [{ Op: SkillProgramEffectOp.RecordOpeningTurnUse }]) continue;
                    if (trigger.Window == SkillProgramTriggerWindow.CardUseCommitted &&
                        trigger.OwnerRelation == SkillProgramCardActionOwnerRelation.Actor && !trigger.Optional &&
                        trigger.Effects.Count == 1 && trigger.Effects[0] is
                        { Op: SkillProgramEffectOp.SetBooleanState, BooleanValue: true, Condition.Kind: SkillProgramConditionKind.Always } &&
                        trigger.Condition.Kind == SkillProgramTriggerConditionKind.CardActionSuitIs && trigger.Condition.Suits.Count == 1) continue;
                    if(trigger.Window!=SkillProgramTriggerWindow.CardUseCommitted || trigger.OwnerRelation!=SkillProgramCardActionOwnerRelation.Actor || trigger.Effects.Count!=1 || trigger.Effects[0].Op!=SkillProgramEffectOp.IssueCardNoResponseAndPlayUseBan ||
                       !HasRequiredPositiveCondition(trigger.Condition,SkillProgramTriggerConditionKind.CardActionActorIsCurrentTurn) || !HasRequiredPositiveCondition(trigger.Condition,SkillProgramTriggerConditionKind.CardActionPhaseIsPlay))
                        throw new InvalidOperationException("Issued card policy requires a single own Play actor use.");
                }
                foreach (var trigger in skill.Program!.Triggers.Where(t =>
                             t.Effects.Any(e => e.Op == SkillProgramEffectOp.ReplaceSkillsOnPreparation)))
                {
                    if (trigger.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow ||
                        trigger.Subject != SkillProgramTriggerSubject.Owner ||
                        trigger.TurnOwnerScope != SkillProgramTurnOwnerScope.Own || trigger.Optional ||
                        trigger.UsageScope != SkillUsageScope.Game || trigger.UsageLimit != 1 ||
                        trigger.Effects[^1].Op != SkillProgramEffectOp.ReplaceSkillsOnPreparation)
                        throw new InvalidOperationException(
                            "Preparation replacement requires its terminal forced once-game own preparation binding.");
                    foreach (var replacement in trigger.Effects.Where(e => e.Op == SkillProgramEffectOp.ReplaceSkillsOnPreparation))
                        foreach (var id in replacement.SkillIds.Append(replacement.SourceBind!))
                            if (!_skills.ContainsKey(id))
                                throw new InvalidOperationException("Preparation replacement references an unknown skill.");
                }
                foreach (var trigger in skill.Program!.Triggers.Where(trigger => trigger.DeferredTurnEndOnly ||
                             trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.ScheduleDeferredHandAlignment)))
                {
                    if (trigger.Window != SkillProgramTriggerWindow.TurnEnding ||
                        trigger.Subject != SkillProgramTriggerSubject.Owner ||
                        trigger.TurnOwnerScope != SkillProgramTurnOwnerScope.Own)
                        throw new InvalidOperationException("Deferred alignment requires its source owner's own ending boundary.");
                    foreach (var schedule in trigger.Effects.Where(effect => effect.Op == SkillProgramEffectOp.ScheduleDeferredHandAlignment))
                        if (skill.Program.Triggers.SingleOrDefault(item => item.Id == schedule.StateId)?.DeferredTurnEndOnly != true)
                            throw new InvalidOperationException("Deferred alignment must name its own exact opt-in continuation.");
                }
                foreach (var effect in skill.Program!.Triggers.SelectMany(trigger => trigger.Effects)
                             .Where(effect => effect.Op == SkillProgramEffectOp.DrawPublicSuitThenEscalatingDiscard))
                {
                    if (!_skills.TryGetValue(effect.SkillIds.Single(), out var upgraded) ||
                        upgraded.Program?.Triggers.SelectMany(trigger => trigger.Effects).Any(target =>
                            target.Op == SkillProgramEffectOp.ExchangeRespondedCardEntities && target.StateId == effect.StateId) != true)
                        throw new InvalidOperationException($"Skill '{skill.Id}' requires the upgraded skill's matching response-exchange state.");
                }
                foreach (var suitGrant in skill.Program!.EquipmentSuitSkillGrants)
                {
                    if (!_skills.TryGetValue(suitGrant.SkillId, out var leaf) || leaf.SuppressionRule is not null ||
                        leaf.Program is not { } target || target.EquipmentSuitSkillGrants.Count != 0 ||
                        target.HpSkillGrants.Count != 0 || target.LordSkillProjection ||
                        target.CardPolicies.Any(policy => policy.Kind == SkillProgramCardPolicyKind.RewriteSuit) ||
                        target.Triggers.Any(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.InitializePrivateGeneralLibrary)))
                        throw new InvalidOperationException($"Skill '{skill.Id}' must grant a known leaf without another projection or suppression capability.");
                }
                foreach (var hpGrant in skill.Program!.HpSkillGrants)
                {
                    if (!_skills.TryGetValue(hpGrant.SkillId, out var derived) ||
                        derived.Program?.HpSkillGrants.Count > 0 || derived.Program?.EquipmentSuitSkillGrants.Count > 0 || derived.SuppressionRule is not null ||
                        derived.Program?.LordSkillProjection == true ||
                        derived.Program?.Triggers.Any(trigger => trigger.Effects.Any(effect =>
                            effect.Op == SkillProgramEffectOp.InitializePrivateGeneralLibrary)) == true)
                        throw new InvalidOperationException($"Skill '{skill.Id}' must grant a known leaf skill without another projection or suppression capability.");
                }
                foreach (var phaseGrant in skill.Program!.Triggers.SelectMany(trigger => trigger.Effects)
                             .Where(effect => effect.Op == SkillProgramEffectOp.GrantOtherPlayUseRestriction))
                {
                    if (!_skills.TryGetValue(phaseGrant.SkillIds.Single(), out var derived) || derived.SuppressionRule is not null ||
                        derived.Program is not { CardPolicies: [{ Kind: SkillProgramCardPolicyKind.StopUsingAfterTrickOrHpCount }] } leaf ||
                        leaf.Activations.Count != 0 || leaf.Triggers.Count != 0 || leaf.ViewAs.Count != 0 ||
                        leaf.Modifiers.Count != 0 || leaf.Contributions.Count != 0 || leaf.CardIdentities.Count != 0 ||
                        leaf.BooleanStates.Count != 0 || leaf.DamageModifiers.Count != 0 || leaf.HpSkillGrants.Count != 0 || leaf.EquipmentSuitSkillGrants.Count != 0 ||
                        leaf.LordSkillProjection || leaf.CannotChallengeDeclarations)
                        throw new InvalidOperationException($"Skill '{skill.Id}' requires a known leaf with exactly its phase use restriction policy.");
                }
                Bao2025Contract.ValidateReferences(skill, _skills);
                foreach (var offeredSkill in skill.Program!.Triggers.SelectMany(trigger => trigger.Effects)
                             .Where(effect => effect.Op == SkillProgramEffectOp.OfferRandomPlayPhaseSkill).SelectMany(effect => effect.SkillIds))
                    if (!_skills.ContainsKey(offeredSkill))
                        throw new InvalidOperationException($"Skill '{skill.Id}' offers unknown skill '{offeredSkill}'.");
                CaoYingContract.ValidateReferences(skill, _skills);
                foreach (var grantedSkillId in skill.Program!.Triggers
                             .SelectMany(trigger => trigger.Effects)
                             .Where(effect => effect.Op is SkillProgramEffectOp.DyingRecipientChooseReplaceOrGrantThenRecover or SkillProgramEffectOp.DrawAfterOtherDeathThenChooseMissingSkill or SkillProgramEffectOp.DeathRecipientChooseSkill or SkillProgramEffectOp.GrantOtherPlayUseRestriction or SkillProgramEffectOp.AwakenAfterGameActivations or SkillProgramEffectOp.DiscardHandToNamedTurnCount or SkillProgramEffectOp.GrantSkills or SkillProgramEffectOp.GrantPlayPhaseSkills or SkillProgramEffectOp.GrantRandomSkillAndSuitShield or SkillProgramEffectOp.ExchangePublicPileHand or SkillProgramEffectOp.ObtainPublicPileCard or SkillProgramEffectOp.ExchangePublicPile or SkillProgramEffectOp.DistributePublicPileIfAllSuits or SkillProgramEffectOp.DrawPublicSuitThenEscalatingDiscard)
                             .SelectMany(effect => effect.SkillIds)
                             .Concat(skill.Program.Activations.SelectMany(activation => activation.Effects)
                                 .Where(effect => effect.Op is SkillProgramEffectOp.ObtainPublicPileCard or SkillProgramEffectOp.GrantPlayPhaseSkills or SkillProgramEffectOp.OfferRandomPlayPhaseSkill)
                                 .SelectMany(effect => effect.SkillIds)))
                {
                    if (!_skills.ContainsKey(grantedSkillId))
                    {
                        throw new InvalidOperationException(
                            $"Skill '{skill.Id}' grants unknown skill '{grantedSkillId}'.");
                    }
                }
                foreach (var awakening in skill.Program.Triggers.SelectMany(t => t.Effects)
                    .Where(e => e.Op == SkillProgramEffectOp.AwakenAfterGameActivations))
                    if (!_skills.TryGetValue(awakening.SourceBind!, out var counted) ||
                        counted.Program?.Activations.Any(a => a.Id == awakening.StateId) != true)
                        throw new InvalidOperationException($"Skill '{skill.Id}' counts an unknown activation for awakening.");
                foreach (var suppressedSkillId in skill.Program.Triggers.SelectMany(t => t.Effects)
                    .Where(e => e.Op == SkillProgramEffectOp.SuppressOwnSkillAfterAlcoholSlashDamage).SelectMany(e => e.SkillIds))
                    if (!_skills.ContainsKey(suppressedSkillId))
                        throw new InvalidOperationException($"Skill '{skill.Id}' suppresses unknown skill '{suppressedSkillId}'.");
                foreach (var countSource in skill.Program.Triggers.SelectMany(t => t.Effects)
                    .Where(e => e.Op == SkillProgramEffectOp.DiscardHandToNamedTurnCount).SelectMany(e => e.SkillIds))
                    if (_skills[countSource].Program?.Triggers.Any(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.DiscardNonFinalTargetCardThenDraw)) != true)
                        throw new InvalidOperationException($"Skill '{skill.Id}' requires a declared named-turn count producer '{countSource}'.");
            }

            foreach (var general in _generals.Values)
            {
                foreach (var skillId in general.SkillIds)
                {
                    if (!_skills.ContainsKey(skillId))
                    {
                        throw new InvalidOperationException(
                            $"General '{general.Id}' references unknown skill '{skillId}'.");
                    }
                }
            }

            foreach (var deck in _decks.Values)
            {
                var hasCounts = deck.Cards.Count > 0;
                var hasPhysicalCards = deck.PhysicalCards is { Count: > 0 };
                if (deck.InitialHandSize < 0 || deck.DrawPerTurn < 0 || hasCounts == hasPhysicalCards)
                {
                    throw new InvalidOperationException($"Deck '{deck.Id}' has invalid setup values.");
                }

                if (deck.Cards.Any(card => card.Count <= 0) ||
                    deck.Cards.Select(card => card.CardDefinitionId)
                        .Distinct(StringComparer.Ordinal)
                        .Count() != deck.Cards.Count)
                {
                    throw new InvalidOperationException($"Deck '{deck.Id}' has invalid or repeated card entries.");
                }

                foreach (var card in deck.Cards)
                {
                    if (!_cards.ContainsKey(card.CardDefinitionId))
                    {
                        throw new InvalidOperationException(
                            $"Deck '{deck.Id}' references unknown card '{card.CardDefinitionId}'.");
                    }
                }

                foreach (var card in deck.PhysicalCards ?? [])
                {
                    if (!_cards.ContainsKey(card.CardDefinitionId))
                    {
                        throw new InvalidOperationException(
                            $"Deck '{deck.Id}' references unknown card '{card.CardDefinitionId}'.");
                    }

                    if (!Enum.IsDefined(card.Suit) || card.Rank is < 1 or > 13)
                    {
                        throw new InvalidOperationException(
                            $"Deck '{deck.Id}' contains invalid physical card data.");
                    }
                }
            }

            foreach (var mode in _modes.Values)
            {
                ValidateTeamActivity(mode);
                ValidateBossActivity(mode);
                ValidateShenwuActivity(mode);
                ValidateNianActivity(mode);
                ValidateHellActivity(mode);
                ValidateWenheActivity(mode);
                ValidateDragonBoatActivity(mode);
                if (mode.MinPlayers <= 0 || mode.MaxPlayers < mode.MinPlayers)
                {
                    throw new InvalidOperationException(
                        $"Mode '{mode.Id}' has an invalid player range.");
                }

                if (mode.ModeKind == ContentModeKind.Team)
                {
                    if (mode.TeamCounts is null || mode.TeamCounts.Count < 2)
                    {
                        throw new InvalidOperationException(
                            $"Team mode '{mode.Id}' must define at least two public teams.");
                    }

                    if (mode.RoleCounts.Count != 0)
                    {
                        throw new InvalidOperationException(
                            $"Team mode '{mode.Id}' must not use identity role counts.");
                    }

                    if (mode.TeamCounts.Any(entry => string.IsNullOrWhiteSpace(entry.Key) || entry.Value <= 0))
                    {
                        throw new InvalidOperationException(
                            $"Team mode '{mode.Id}' must define non-empty teams with positive counts.");
                    }

                    var teamTotal = mode.TeamCounts.Values.Sum();
                    if (teamTotal < mode.MinPlayers || teamTotal > mode.MaxPlayers)
                    {
                        throw new InvalidOperationException(
                            $"Mode '{mode.Id}' team distribution totals {teamTotal}, outside its player range.");
                    }
                }
                else if (mode.ModeKind == ContentModeKind.NationalWarLite)
                {
                    if (mode.RoleCounts.Count != 0 || mode.TeamCounts is { Count: > 0 })
                    {
                        throw new InvalidOperationException(
                            $"National-war mode '{mode.Id}' must not use identity roles or public-team counts.");
                    }

                    if (mode.FactionCounts is null || mode.FactionCounts.Count < 2)
                    {
                        throw new InvalidOperationException(
                            $"National-war mode '{mode.Id}' must define at least two hidden factions.");
                    }

                    if (mode.FactionCounts.Any(entry => string.IsNullOrWhiteSpace(entry.Key) || entry.Value <= 0))
                    {
                        throw new InvalidOperationException(
                            $"National-war mode '{mode.Id}' must define positive faction counts.");
                    }

                    var factionTotal = mode.FactionCounts.Values.Sum();
                    if (factionTotal < mode.MinPlayers || factionTotal > mode.MaxPlayers)
                    {
                        throw new InvalidOperationException(
                            $"Mode '{mode.Id}' faction distribution totals {factionTotal}, outside its player range.");
                    }

                    if (mode.SoloFactionIds is { Count: > 0 })
                    {
                        if (mode.SoloFactionIds.Any(string.IsNullOrWhiteSpace) ||
                            mode.SoloFactionIds.Distinct(StringComparer.Ordinal).Count() != mode.SoloFactionIds.Count ||
                            mode.SoloFactionIds.Any(factionId => !mode.FactionCounts.ContainsKey(factionId)))
                        {
                            throw new InvalidOperationException(
                                $"National-war mode '{mode.Id}' has invalid solo faction ids.");
                        }

                        if (mode.SoloFactionIds.Any(factionId => mode.FactionCounts[factionId] != 1))
                        {
                            throw new InvalidOperationException(
                                $"National-war mode '{mode.Id}' must assign exactly one seat to each solo faction.");
                        }
                    }
                }
                else if (mode.ModeKind == ContentModeKind.Survival)
                {
                    if (mode.RoleCounts.Count != 0 || mode.TeamCounts is { Count: > 0 } ||
                        mode.FactionCounts is { Count: > 0 } || mode.SoloFactionIds is { Count: > 0 } ||
                        mode.GeneralPoolIds is null)
                        throw new InvalidOperationException($"Survival mode '{mode.Id}' requires an explicit individual general pool without identity, team or faction assignments.");
                }
                else if (mode.ModeKind == ContentModeKind.Identity)
                {
                    if (mode.TeamCounts is { Count: > 0 })
                    {
                        throw new InvalidOperationException(
                            $"Identity mode '{mode.Id}' must not define public team counts.");
                    }

                    if (mode.RoleCounts.Keys.Any(key =>
                            key is nameof(Role.TeamA) or nameof(Role.TeamB)))
                    {
                        throw new InvalidOperationException(
                            $"Identity mode '{mode.Id}' must not use public-team compatibility roles.");
                    }

                    if (mode.RoleCounts.Any(entry => entry.Value < 0))
                    {
                        throw new InvalidOperationException($"Mode '{mode.Id}' has a negative role count.");
                    }

                    var roleTotal = mode.RoleCounts.Values.Sum();
                    if (roleTotal < mode.MinPlayers || roleTotal > mode.MaxPlayers)
                    {
                        throw new InvalidOperationException(
                            $"Mode '{mode.Id}' role distribution totals {roleTotal}, outside its player range.");
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Mode '{mode.Id}' has an unknown mode kind.");
                }

                if (mode.GeneralCandidateCount <= 0)
                {
                    throw new InvalidOperationException(
                        $"Mode '{mode.Id}' must expose at least one general candidate.");
                }

                if (mode.DeckId is not null && !_decks.ContainsKey(mode.DeckId))
                {
                    throw new InvalidOperationException(
                        $"Mode '{mode.Id}' references unknown deck '{mode.DeckId}'.");
                }

                if (mode.RoleGeneralCandidateCounts is { } roleCandidates &&
                    (mode.ModeKind != ContentModeKind.Identity || roleCandidates.Any(entry =>
                        !mode.RoleCounts.ContainsKey(entry.Key) || entry.Value <= 0)))
                {
                    throw new InvalidOperationException(
                        $"Mode '{mode.Id}' has invalid identity-role general candidate counts.");
                }

                if (mode.ModeKind == ContentModeKind.NationalWarLite && mode.GeneralPoolIds is null)
                {
                    throw new InvalidOperationException(
                        $"National-war mode '{mode.Id}' must provide an explicit faction-tagged general pool.");
                }

                if (mode.GeneralVariantGroups is { } variants &&
                    (mode.ModeKind != ContentModeKind.Identity || mode.GeneralPoolIds is null ||
                     variants.Any(group => string.IsNullOrWhiteSpace(group.Key) || group.Value.Count < 2 ||
                         group.Value.Any(id => !mode.GeneralPoolIds.Contains(id, StringComparer.Ordinal))) ||
                     variants.Values.SelectMany(ids => ids).Distinct(StringComparer.Ordinal).Count() !=
                     variants.Values.Sum(ids => ids.Count)))
                    throw new InvalidOperationException($"Mode '{mode.Id}' has invalid general variant groups.");

                if (mode.AiGeneralSwapExcludedIds is { } excluded &&
                    (mode.GeneralVariantGroups is null || mode.GeneralPoolIds is null ||
                     excluded.Distinct(StringComparer.Ordinal).Count() != excluded.Count ||
                     excluded.Any(id => !mode.GeneralPoolIds.Contains(id, StringComparer.Ordinal))))
                    throw new InvalidOperationException($"Mode '{mode.Id}' has invalid AI general swap exclusions.");

                if (mode.GeneralSelectionWeights is { } weights)
                {
                    if (mode.ModeKind != ContentModeKind.Identity || mode.GeneralPoolIds is null ||
                        weights.Any(entry => entry.Value is < 1 or > 1000 ||
                            !mode.GeneralPoolIds.Contains(entry.Key, StringComparer.Ordinal)))
                        throw new InvalidOperationException($"Mode '{mode.Id}' has invalid general selection weights.");
                }
                if (mode.AiGeneralSwapExcludedIds is not null || mode.GeneralSelectionWeights is not null)
                {
                    var familyKeys = mode.GeneralVariantGroups?.SelectMany(group => group.Value.Select(id => (Id: id, Family: group.Key)))
                        .ToDictionary(entry => entry.Id, entry => entry.Family, StringComparer.Ordinal);
                    if (mode.GeneralPoolIds!.DistinctBy(id => familyKeys?.GetValueOrDefault(id, id) ?? id,
                            StringComparer.Ordinal).Count() < mode.MaxPlayers * (mode.DualGenerals ? 2 : 1))
                        throw new InvalidOperationException($"Mode '{mode.Id}' needs a distinct general family for every seat.");
                }

                if (mode.DualGenerals && (mode.ModeKind != ContentModeKind.Identity ||
                    mode.MinPlayers != 8 || mode.MaxPlayers != 8 || mode.GeneralPoolIds is null))
                    throw new InvalidOperationException($"Mode '{mode.Id}' must define an eight-player identity dual-general pool.");

                if (mode.GeneralPoolIds is not null)
                {
                    var minimumGeneralCount = mode.ModeKind == ContentModeKind.NationalWarLite || mode.DualGenerals
                        ? mode.MaxPlayers * 2
                        : mode.MaxPlayers;
                    if (mode.GeneralPoolIds.Count < minimumGeneralCount ||
                        mode.GeneralPoolIds.Distinct(StringComparer.Ordinal).Count() != mode.GeneralPoolIds.Count)
                    {
                        throw new InvalidOperationException(
                            $"Mode '{mode.Id}' must provide a unique general pool large enough for its table.");
                    }

                    foreach (var generalId in mode.GeneralPoolIds)
                    {
                        if (!_generals.ContainsKey(generalId))
                        {
                            throw new InvalidOperationException(
                                $"Mode '{mode.Id}' references unknown general '{generalId}'.");
                        }
                        if (_generals[generalId].SkillIds.Any(skillId =>
                                _skills[skillId].ImplementationStatus != SkillImplementationStatus.Complete))
                            throw new InvalidOperationException(
                                $"Mode '{mode.Id}' includes unfinished general '{generalId}'.");
                    }

                    if (mode.ModeKind == ContentModeKind.NationalWarLite)
                    {
                        var factionCounts = mode.GeneralPoolIds
                            .Select(generalId => _generals[generalId].FactionId)
                            .Where(factionId => !string.IsNullOrWhiteSpace(factionId))
                            .GroupBy(factionId => factionId!, StringComparer.Ordinal)
                            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
                        foreach (var faction in mode.FactionCounts!)
                        {
                            if (factionCounts.GetValueOrDefault(faction.Key) < faction.Value * 2)
                            {
                                throw new InvalidOperationException(
                                    $"National-war mode '{mode.Id}' needs at least {faction.Value * 2} generals for faction '{faction.Key}'.");
                            }
                        }

                        if (mode.GeneralPoolIds.Any(generalId =>
                                string.IsNullOrWhiteSpace(_generals[generalId].FactionId) ||
                                !mode.FactionCounts.ContainsKey(_generals[generalId].FactionId!)))
                        {
                            throw new InvalidOperationException(
                                $"National-war mode '{mode.Id}' references a general outside its faction distribution.");
                        }
                    }
                }
            }
        }

        public ContentRegistry Freeze(IReadOnlyList<PackageManifest> packages) => new(
            Array.AsReadOnly(packages.Select(NormalizeManifest).ToArray()),
            FreezeMutableDictionary(_cards),
            FreezeMutableDictionary(_skills),
            FreezeMutableDictionary(_generals),
            FreezeMutableDictionary(_decks),
            FreezeMutableDictionary(_modes));

        private void Add<T>(
            IDictionary<string, T> target,
            T definition,
            string id,
            string kind)
        {
            var contentId = new ContentId(id);
            if (_owners.TryGetValue(contentId.Value, out var owner))
            {
                throw new InvalidOperationException(
                    $"Duplicate content id '{contentId.Value}' ({owner} and {kind}).");
            }

            _owners.Add(contentId.Value, kind);
            target.Add(contentId.Value, definition);
        }

        private static ContentCardDefinition NormalizeCard(ContentCardDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            return definition with
            {
                AiTags = FreezeDictionary(definition.AiTags ?? new Dictionary<string, string>())
            };
        }

        private static ContentSkillDefinition NormalizeSkill(ContentSkillDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (!Enum.IsDefined(definition.ImplementationStatus))
                throw new InvalidOperationException($"Skill '{definition.Id}' has an invalid implementation status.");
            if (definition.ImplementationStatus != SkillImplementationStatus.Complete &&
                string.IsNullOrWhiteSpace(definition.PendingImplementation))
                throw new InvalidOperationException($"Unfinished skill '{definition.Id}' must describe its missing behavior.");
            if (definition.ImplementationStatus == SkillImplementationStatus.Planned && definition.Program is not null)
                throw new InvalidOperationException($"Planned skill '{definition.Id}' cannot expose an executable program.");
            const SkillTag allTags = SkillTag.Lord | SkillTag.Locked | SkillTag.Limited |
                                     SkillTag.Awakening | SkillTag.Conversion;
            const SkillExecutionForm allForms = SkillExecutionForm.State | SkillExecutionForm.Trigger;
            const SkillActionForm allActionForms = SkillActionForm.Active;
            if ((definition.Tags & ~allTags) != 0)
                throw new InvalidOperationException($"Skill '{definition.Id}' has unsupported structured tags.");
            if ((definition.ExecutionForms & ~allForms) != 0)
                throw new InvalidOperationException($"Skill '{definition.Id}' has unsupported execution forms.");
            if ((definition.ActionForms & ~allActionForms) != 0)
                throw new InvalidOperationException($"Skill '{definition.Id}' has unsupported action forms.");

            var normalized = definition.Tags.HasFlag(SkillTag.Awakening)
                ? definition with { Tags = definition.Tags | SkillTag.Locked | SkillTag.Limited }
                : definition;
            if (normalized.SelectionWeights is { } selectionWeights)
                normalized = normalized with
                {
                    SelectionWeights = new ReadOnlyDictionary<Role, double>(
                        new Dictionary<Role, double>(selectionWeights))
                };
            if (normalized.Program is null)
                return normalized;

            if (!string.Equals(normalized.Program.Id, normalized.Id, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Skill '{normalized.Id}' is bound to program '{normalized.Program.Id}'. Program ids must match their content skill ids.");
            return normalized;
        }

        private static ContentGeneralDefinition NormalizeGeneral(ContentGeneralDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (definition.BaseHp is < 1 or > 20)
                throw new ArgumentOutOfRangeException(nameof(definition), "General base HP must be between 1 and 20.");
            if (definition.InitialHp is { } initialHp && (initialHp < 1 || initialHp > definition.BaseHp))
                throw new ArgumentOutOfRangeException(nameof(definition), "Initial HP must be between 1 and base maximum HP.");
            var additionalSkillIds = definition.AdditionalSkillIds?.ToArray() ?? [];
            if (string.IsNullOrWhiteSpace(definition.SkillId) ||
                additionalSkillIds.Any(string.IsNullOrWhiteSpace) ||
                new[] { definition.SkillId }.Concat(additionalSkillIds)
                    .Distinct(StringComparer.Ordinal)
                    .Count() != additionalSkillIds.Length + 1)
            {
                throw new ArgumentException(
                    $"General '{definition.Id}' must reference distinct non-empty skills.",
                    nameof(definition));
            }

            return definition with
            {
                AdditionalSkillIds = additionalSkillIds.Length == 0
                    ? null
                    : Array.AsReadOnly(additionalSkillIds)
            };
        }

        private static ContentDeckRecipe NormalizeDeck(ContentDeckRecipe definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (definition.Cards is null)
            {
                throw new ArgumentException("A deck recipe must provide cards.", nameof(definition));
            }

            return definition with
            {
                Cards = Array.AsReadOnly(definition.Cards.Select(card => card with { }).ToArray()),
                PhysicalCards = definition.PhysicalCards is null
                    ? null
                    : Array.AsReadOnly(definition.PhysicalCards.Select(card => card with { }).ToArray())
            };
        }

        private void ValidateTeamActivity(ContentModeDefinition mode)
        {
            if (mode.TeamActivity is not { } a) return;
            if (mode.ModeKind != ContentModeKind.Team || mode.MinPlayers != 8 || mode.MaxPlayers != 8 ||
                mode.TeamCounts is not { Count: 2 } teams || teams.Values.Any(n => n != 4) ||
                mode.GeneralPoolIds is null || a.PlayerTier is < 1 or > 5 || a.SpiritTierMinimum is < 1 or > 5 ||
                a.SpiritTierMaximum < a.SpiritTierMinimum || a.SpiritTierMaximum > 5 || a.MachineTier is < 1 or > 5)
                throw new InvalidOperationException($"Activity '{mode.Id}' requires two four-seat teams and bounded bonuses.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pools in new[] { a.PlayerPools, a.SpiritPools, a.MachinePools })
            {
                if (pools.Count != 2 || teams.Keys.Any(team => !pools.ContainsKey(team)))
                    throw new InvalidOperationException($"Activity '{mode.Id}' has inconsistent team pools.");
                foreach (var pool in pools.Values)
                    if (pool.Count < 2 || pool.Distinct(StringComparer.Ordinal).Count() != pool.Count ||
                        pool.Any(id => !seen.Add(id) || !mode.GeneralPoolIds.Contains(id, StringComparer.Ordinal)))
                        throw new InvalidOperationException($"Activity '{mode.Id}' needs disjoint pools with at least two generals each.");
            }
            if (seen.Count != mode.GeneralPoolIds.Count ||
                a.PlayerSkillIds.Count != 2 || teams.Keys.Any(team => !a.PlayerSkillIds.ContainsKey(team)) ||
                a.PlayerSkillIds.Values.Any(ids => ids.Distinct(StringComparer.Ordinal).Count() != ids.Count || ids.Any(id => !_skills.ContainsKey(id))))
                throw new InvalidOperationException($"Activity '{mode.Id}' has invalid generals or player skills.");
        }

        private void ValidateBossActivity(ContentModeDefinition mode)
        {
            if (mode.HulaoActivity is { } h &&
                (h.TransformationHp <= 0 || h.ShuffleLimit < 2 || mode.BossActivity is not { Stages.Count: 2 }))
                throw new InvalidOperationException($"Hulao activity '{mode.Id}' requires two boss forms and valid transition rules.");
            if (mode.BossActivity is not { } a) return;
            var seats = mode.HellActivity is null ? 4 : 5;
            if (mode.TeamActivity is not null || mode.ModeKind != ContentModeKind.Team || mode.MinPlayers != seats || mode.MaxPlayers != seats ||
                mode.TeamCounts is not { Count: 2 } teams || teams.GetValueOrDefault(a.PlayerTeamId) != 3 ||
                teams.GetValueOrDefault(a.EnemyTeamId) != seats - 3 || a.PlayerTier is < 1 or > 5 || a.Stages.Count == 0 ||
                a.PlayerPoolIds.Count < 3 || a.PlayerPoolIds.Distinct(StringComparer.Ordinal).Count() != a.PlayerPoolIds.Count ||
                a.PlayerSkillIds.Distinct(StringComparer.Ordinal).Count() != a.PlayerSkillIds.Count ||
                a.PlayerSkillIds.Any(id => !_skills.ContainsKey(id)) || mode.GeneralPoolIds is null)
                throw new InvalidOperationException($"Boss activity '{mode.Id}' requires three allied players, one boss, and valid tier skills.");
            var ids = a.PlayerPoolIds.ToHashSet(StringComparer.Ordinal);
            if (mode.HellActivity is { } hell) ids.Add(hell.AuxiliaryGeneralId);
            foreach (var stage in a.Stages)
            {
                if (string.IsNullOrWhiteSpace(stage.Name) || stage.InitialHandSize < 0 || stage.GeneralPoolIds.Count == 0 ||
                    stage.GeneralPoolIds.Distinct(StringComparer.Ordinal).Count() != stage.GeneralPoolIds.Count ||
                    stage.GeneralPoolIds.Any(a.PlayerPoolIds.Contains))
                    throw new InvalidOperationException($"Boss activity '{mode.Id}' has an invalid stage.");
                ids.UnionWith(stage.GeneralPoolIds);
            }
            if (!ids.SetEquals(mode.GeneralPoolIds))
                throw new InvalidOperationException($"Boss activity '{mode.Id}' must declare exactly its player and stage generals.");
        }

        private void ValidateNianActivity(ContentModeDefinition mode)
        {
            if (mode.NianActivity is not { } nian) return;
            if (mode.BossActivity is not { Stages.Count: 1 } boss || mode.TeamActivity is not null ||
                mode.ShenwuActivity is not null || mode.HulaoActivity is not null || mode.HellActivity is not null || nian.SharedRevives != 3 ||
                nian.FormGeneralIds.Count != 5 || nian.FormGeneralIds.Distinct(StringComparer.Ordinal).Count() != 5 ||
                !nian.FormGeneralIds.SequenceEqual(boss.Stages[0].GeneralPoolIds) ||
                nian.FormGeneralIds.Any(id => _generals[id].BaseHp != 12))
                throw new InvalidOperationException($"Nian activity '{mode.Id}' requires five ordered twelve-health forms, one encounter and three shared revivals.");
        }

        private void ValidateWenheActivity(ContentModeDefinition mode)
        {
            if (mode.WenheActivity is not { } activity) return;
            if (mode.ModeKind != ContentModeKind.Survival || mode.MinPlayers != 8 || mode.MaxPlayers != 8 ||
                activity.PlayerTier is < 1 or > 5 || mode.TeamActivity is not null || mode.BossActivity is not null ||
                mode.HulaoActivity is not null || mode.ShenwuActivity is not null || mode.NianActivity is not null ||
                mode.HellActivity is not null || mode.DragonBoatActivity is not null || mode.DualGenerals)
                throw new InvalidOperationException($"Wenhe activity '{mode.Id}' requires eight independent contestants and its own bounded player tier.");
        }

        private void ValidateDragonBoatActivity(ContentModeDefinition mode)
        {
            if (mode.DragonBoatActivity is not { } a) return;
            var teams = new[] { "team:wei", "team:shu", "team:wu", "team:qun" };
            if (mode.ModeKind != ContentModeKind.Team || mode.MinPlayers != 8 || mode.MaxPlayers != 8 ||
                a.PlayerTier is < 1 or > 5 || a.Rounds != 2 || mode.GeneralCandidateCount != 6 ||
                mode.TeamCounts is not { Count: 4 } counts || teams.Any(id => counts.GetValueOrDefault(id) != 2) ||
                a.PlayerPoolIds.Count != 4 || teams.Any(id => !a.PlayerPoolIds.TryGetValue(id, out var pool) || pool.Count < 12 ||
                    pool.Any(general => !_generals.ContainsKey(general) || mode.GeneralPoolIds?.Contains(general) != true)) ||
                mode.TeamActivity is not null || mode.BossActivity is not null || mode.ShenwuActivity is not null ||
                mode.HulaoActivity is not null || mode.NianActivity is not null || mode.HellActivity is not null ||
                mode.WenheActivity is not null || mode.DualGenerals)
                throw new InvalidOperationException($"Dragon boat '{mode.Id}' requires four public two-seat faction pools, six candidates and two actual rounds.");
        }

        private void ValidateHellActivity(ContentModeDefinition mode)
        {
            if (mode.HellActivity is not { } hell) return;
            if (mode.BossActivity is not { Stages.Count: 4, PlayerTier: 5 } boss || hell.BossTier is < 1 or > 5 ||
                mode.TeamActivity is not null || mode.NianActivity is not null || mode.HulaoActivity is not null ||
                mode.ShenwuActivity is not null || !_generals.ContainsKey(hell.AuxiliaryGeneralId) ||
                boss.PlayerPoolIds.Contains(hell.AuxiliaryGeneralId) ||
                boss.Stages[3].GeneralPoolIds.Count != 2 ||
                boss.Stages.Any(stage => stage.GeneralPoolIds.Contains(hell.AuxiliaryGeneralId)) ||
                !boss.Stages[3].GeneralPoolIds.Contains(hell.HiddenGeneralId) ||
                boss.Stages.Take(3).Any(stage => stage.GeneralPoolIds.Contains(hell.HiddenGeneralId)))
                throw new InvalidOperationException($"Hell activity '{mode.Id}' requires four encounters, a separate auxiliary and a conditional final boss.");
        }

        private void ValidateShenwuActivity(ContentModeDefinition mode)
        {
            if (mode.ShenwuActivity is not { } activity) return;
            if (mode.TeamActivity is not null || mode.BossActivity is not null || mode.HulaoActivity is not null ||
                mode.ModeKind != ContentModeKind.Team || mode.MinPlayers != 6 || mode.MaxPlayers != 6 ||
                mode.TeamCounts is not { Count: 2 } teams || teams.GetValueOrDefault(activity.PlayerTeamId) != 3 ||
                teams.GetValueOrDefault(activity.EnemyTeamId) != 3 || activity.PlayerTeamId == activity.EnemyTeamId ||
                activity.PlayerTier is < 1 or > 5 || activity.PlayerSeatOffsets.Count != 3 ||
                activity.PlayerSeatOffsets[0] != 0 || activity.MinionSeatOffsets.Count != 2 ||
                !activity.PlayerSeatOffsets.Concat(activity.MinionSeatOffsets).Append(activity.LeaderSeatOffset)
                    .Order().SequenceEqual(Enumerable.Range(0, 6)) || activity.PlayerPoolIds.Count < 15 ||
                activity.GodPoolIds.Count < 3 || activity.LeaderPoolIds.Count == 0 || activity.MinionGeneralIds.Count != 2 ||
                activity.PlayerSkillIds.Distinct(StringComparer.Ordinal).Count() != activity.PlayerSkillIds.Count ||
                activity.PlayerSkillIds.Any(id => !_skills.ContainsKey(id)) || mode.GeneralPoolIds is null)
                throw new InvalidOperationException($"Shenwu activity '{mode.Id}' requires three allied players, three beasts and valid draft slots.");
            var pools = new[] { activity.PlayerPoolIds, activity.GodPoolIds, activity.LeaderPoolIds, activity.MinionGeneralIds };
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (pools.Any(pool => pool.Any(id => !ids.Add(id))) || !ids.SetEquals(mode.GeneralPoolIds) ||
                activity.PlayerPoolIds.Any(id => _generals[id].FactionId == "god") ||
                activity.GodPoolIds.Any(id => _generals[id].FactionId != "god") ||
                activity.HalfHpSkillIds.Count != activity.LeaderPoolIds.Count ||
                activity.LeaderPoolIds.Any(id => !activity.HalfHpSkillIds.TryGetValue(id, out var skill) ||
                    !_skills.ContainsKey(skill) || _generals[id].SkillIds.Contains(skill, StringComparer.Ordinal)))
                throw new InvalidOperationException($"Shenwu activity '{mode.Id}' has overlapping pools or invalid permanent half-health skills.");
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<string>> FreezeActivityPools(
            IReadOnlyDictionary<string, IReadOnlyList<string>> pools) =>
            new ReadOnlyDictionary<string, IReadOnlyList<string>>(pools.ToDictionary(p => p.Key,
                p => (IReadOnlyList<string>)Array.AsReadOnly(p.Value.ToArray()), StringComparer.Ordinal));

        private static ContentModeDefinition NormalizeMode(ContentModeDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            return definition with
            {
                RoleCounts = FreezeDictionary(definition.RoleCounts),
                RoleGeneralCandidateCounts = definition.RoleGeneralCandidateCounts is null
                    ? null
                    : FreezeDictionary(definition.RoleGeneralCandidateCounts),
                GeneralSelectionWeights = definition.GeneralSelectionWeights is null
                    ? null
                    : FreezeDictionary(definition.GeneralSelectionWeights),
                AiGeneralSwapExcludedIds = definition.AiGeneralSwapExcludedIds is null ? null
                    : Array.AsReadOnly(definition.AiGeneralSwapExcludedIds.ToArray()),
                GeneralVariantGroups = definition.GeneralVariantGroups is null ? null : FreezeDictionary(
                    definition.GeneralVariantGroups.ToDictionary(entry => entry.Key,
                        entry => (IReadOnlyList<string>)Array.AsReadOnly(entry.Value.ToArray()), StringComparer.Ordinal)),
                TeamActivity = definition.TeamActivity is not { } activity ? null : activity with
                {
                    PlayerPools = FreezeActivityPools(activity.PlayerPools),
                    SpiritPools = FreezeActivityPools(activity.SpiritPools),
                    MachinePools = FreezeActivityPools(activity.MachinePools),
                    PlayerSkillIds = FreezeActivityPools(activity.PlayerSkillIds)
                },
                DragonBoatActivity = definition.DragonBoatActivity is not { } dragonBoat ? null : dragonBoat with
                { PlayerPoolIds = FreezeActivityPools(dragonBoat.PlayerPoolIds) },
                BossActivity = definition.BossActivity is not { } boss ? null : boss with
                {
                    PlayerPoolIds = Array.AsReadOnly(boss.PlayerPoolIds.ToArray()),
                    PlayerSkillIds = Array.AsReadOnly(boss.PlayerSkillIds.ToArray()),
                    Stages = Array.AsReadOnly(boss.Stages.Select(stage => stage with
                    { GeneralPoolIds = Array.AsReadOnly(stage.GeneralPoolIds.ToArray()) }).ToArray())
                },
                NianActivity = definition.NianActivity is not { } nian ? null : nian with
                {
                    FormGeneralIds = Array.AsReadOnly(nian.FormGeneralIds.ToArray())
                },
                ShenwuActivity = definition.ShenwuActivity is not { } shenwu ? null : shenwu with
                {
                    PlayerPoolIds = Array.AsReadOnly(shenwu.PlayerPoolIds.ToArray()),
                    GodPoolIds = Array.AsReadOnly(shenwu.GodPoolIds.ToArray()),
                    LeaderPoolIds = Array.AsReadOnly(shenwu.LeaderPoolIds.ToArray()),
                    MinionGeneralIds = Array.AsReadOnly(shenwu.MinionGeneralIds.ToArray()),
                    PlayerSeatOffsets = Array.AsReadOnly(shenwu.PlayerSeatOffsets.ToArray()),
                    MinionSeatOffsets = Array.AsReadOnly(shenwu.MinionSeatOffsets.ToArray()),
                    PlayerSkillIds = Array.AsReadOnly(shenwu.PlayerSkillIds.ToArray()),
                    HalfHpSkillIds = FreezeDictionary(shenwu.HalfHpSkillIds)
                },
                GeneralPoolIds = definition.GeneralPoolIds is null
                    ? null
                    : Array.AsReadOnly(definition.GeneralPoolIds.ToArray()),
                TeamCounts = definition.TeamCounts is null
                    ? null
                    : FreezeDictionary(definition.TeamCounts),
                FactionCounts = definition.FactionCounts is null
                    ? null
                    : FreezeDictionary(definition.FactionCounts),
                SoloFactionIds = definition.SoloFactionIds is null
                    ? null
                    : Array.AsReadOnly(definition.SoloFactionIds.ToArray())
            };
        }

        private static PackageManifest NormalizeManifest(PackageManifest manifest) => manifest with
        {
            Dependencies = Array.AsReadOnly(manifest.Dependencies
                .Select(dependency => dependency with { })
                .ToArray())
        };

        private static IReadOnlyDictionary<string, T> FreezeDictionary<T>(
            IReadOnlyDictionary<string, T>? source)
        {
            var copy = source is null
                ? new Dictionary<string, T>(StringComparer.Ordinal)
                : new Dictionary<string, T>(source, StringComparer.Ordinal);
            return new ReadOnlyDictionary<string, T>(copy);
        }

        private static IReadOnlyDictionary<string, T> FreezeMutableDictionary<T>(
            IDictionary<string, T> source) =>
            new ReadOnlyDictionary<string, T>(
                new Dictionary<string, T>(source, StringComparer.Ordinal));

        private sealed class PackageBuilder(RegistryBuilder owner) : IContentRegistryBuilder
        {
            public void AddCard(ContentCardDefinition definition) => owner.AddCard(definition);

            public void AddSkill(ContentSkillDefinition definition) => owner.AddSkill(definition);

            public void AddGeneral(ContentGeneralDefinition definition) => owner.AddGeneral(definition);

            public void AddDeck(ContentDeckRecipe definition) => owner.AddDeck(definition);

            public void AddMode(ContentModeDefinition definition) => owner.AddMode(definition);
        }
    }
    private static bool HasRequiredPositiveCondition(SkillProgramTriggerCondition c, SkillProgramTriggerConditionKind kind) => c.Kind == kind || c.Kind == SkillProgramTriggerConditionKind.All && c.Children.Any(child => HasRequiredPositiveCondition(child, kind));
}
