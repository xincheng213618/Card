using System.Collections.ObjectModel;
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
    IReadOnlyList<string>? SoloFactionIds = null);

/// <summary>
/// Immutable, per-engine content catalogue. No static registration state is
/// shared between registries, so test packages and future modes remain isolated.
/// </summary>
public sealed class ContentRegistry
{
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
        SkillProgramRules.ValidateSetModifierConflicts(registry.Skills.Values
            .Select(skill => skill.Program)
            .OfType<SkillProgram>());
        ValidateExpandedProgramCardDomain(registry);
        return registry;
    }

    private static void ValidateExpandedProgramCardDomain(ContentRegistry registry)
    {
        var programs = registry.Skills.Values.Select(skill => skill.Program).OfType<SkillProgram>().ToArray();
        if (!registry.Cards.Values.Any(card => card.LegacyKind is { } kind && ProgramCompositionValidator.IsExpandedCardKind(kind)) &&
            !registry.Decks.Values.Any(deck => deck.PhysicalCards?.Any(card => card.Suit == Suit.None) == true) &&
            !programs.Any(program => program.Activations.Any(activation => ProgramCompositionValidator.RequiresExpandedCardDomain(activation.Effects)) ||
                program.Triggers.Any(trigger => ProgramCompositionValidator.RequiresExpandedCardDomain(trigger.Effects)))) return;

        // New physical cards can pass between skills. Recheck every program in
        // this distinct content combination, including programs loaded on their
        // historical ordinary-card domain before the generating package joined.
        foreach (var program in programs)
        {
            foreach (var activation in program.Activations)
                ProgramCompositionValidator.Validate($"registry.skills[{program.Id}].activations[{activation.Id}]", activation.Effects,
                    activation.MinTargets == 1 && activation.MaxTargets == 1, activation.MaxCards,
                    initialTargetSetCount: activation.MaxTargets > 1 ? activation.MinTargets : 0,
                    initialTargetSetMaximum: activation.MaxTargets > 1 ? activation.MaxTargets : 0,
                    expandedCardDomain: true);
            foreach (var trigger in program.Triggers)
                ProgramCompositionValidator.Validate($"registry.skills[{program.Id}].triggers[{trigger.Id}]", trigger.Effects,
                    window: trigger.Window, drawPhaseMode: trigger.DrawPhaseMode, expandedCardDomain: true);
        }
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
            Modes = modes.Values.OrderBy(mode => mode.Id, StringComparer.Ordinal).Select(mode => new
            {
                mode.Id, mode.Name, mode.MinPlayers, mode.MaxPlayers,
                Kind = mode.ModeKind.ToString(),
                mode.DeckId, mode.GeneralCandidateCount,
                GeneralPoolIds = mode.GeneralPoolIds?.ToArray(),
                RoleCounts = (mode.RoleCounts ?? new Dictionary<string, int>()).OrderBy(role => role.Key, StringComparer.Ordinal)
                    .Select(role => new { role.Key, role.Value }).ToArray(),
                TeamCounts = (mode.TeamCounts ?? new Dictionary<string, int>()).OrderBy(team => team.Key, StringComparer.Ordinal)
                    .Select(team => new { team.Key, team.Value }).ToArray(),
                FactionCounts = (mode.FactionCounts ?? new Dictionary<string, int>()).OrderBy(faction => faction.Key, StringComparer.Ordinal)
                    .Select(faction => new { faction.Key, faction.Value }).ToArray(),
                SoloFactionIds = (mode.SoloFactionIds ?? []).OrderBy(id => id, StringComparer.Ordinal).ToArray()
            }).ToArray()
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));

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
            foreach (var skill in _skills.Values.Where(item => item.Program is not null))
            {
                foreach (var grantedSkillId in skill.Program!.Triggers
                             .SelectMany(trigger => trigger.Effects)
                             .Where(effect => effect.Op == SkillProgramEffectOp.GrantSkills)
                             .SelectMany(effect => effect.SkillIds))
                {
                    if (!_skills.ContainsKey(grantedSkillId))
                    {
                        throw new InvalidOperationException(
                            $"Skill '{skill.Id}' grants unknown skill '{grantedSkillId}'.");
                    }
                }
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

                if (mode.ModeKind == ContentModeKind.NationalWarLite && mode.GeneralPoolIds is null)
                {
                    throw new InvalidOperationException(
                        $"National-war mode '{mode.Id}' must provide an explicit faction-tagged general pool.");
                }

                if (mode.GeneralPoolIds is not null)
                {
                    var minimumGeneralCount = mode.ModeKind == ContentModeKind.NationalWarLite
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

        private static ContentModeDefinition NormalizeMode(ContentModeDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            return definition with
            {
                RoleCounts = FreezeDictionary(definition.RoleCounts),
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
}
