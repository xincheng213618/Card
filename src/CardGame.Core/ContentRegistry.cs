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

public sealed record ContentSkillDefinition(
    string Id,
    string Name,
    string Description,
    SkillKind? LegacyKind = null)
{
    public SkillProgram? Program { get; init; }
    public SkillPresentation? ProgramPresentation { get; init; }
    public IPhaseSkillModule? PhaseSkill { get; init; }
    public IPindianResultModule? PindianResultSkill { get; init; }
    public SkillTag Tags { get; init; }
    public SkillExecutionForm ExecutionForms { get; init; }
    public SkillActionForm ActionForms { get; init; }
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
    /// <summary>
    /// Ordered skills for new content. <see cref="SkillId"/> remains the
    /// compatibility primary skill used by v1-v9 checkpoints and older hosts.
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
    /// Optional ordered physical-card recipe. New packages can provide exact
    /// suit/rank data; legacy count recipes remain byte-for-byte hash compatible.
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
        return registry;
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
        var baseCanonical = JsonSerializer.Serialize(new
        {
            HashSchema = 1,
            Packages = packages
                .OrderBy(package => package.Id, StringComparer.Ordinal)
                .Select(package => new
                {
                    package.Id,
                    Version = package.Version.ToString(),
                    Dependencies = package.Dependencies
                        .OrderBy(dependency => dependency.Id, StringComparer.Ordinal)
                        .Select(dependency => new
                        {
                            dependency.Id,
                            MinimumVersion = dependency.MinimumVersion.ToString()
                        })
                        .ToArray()
                })
                .ToArray(),
            Cards = cards
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new
                {
                    entry.Value.Id,
                    entry.Value.DisplayName,
                    entry.Value.CategoryName,
                    entry.Value.Description,
                    LegacyKind = entry.Value.LegacyKind?.ToString(),
                    AiTags = (entry.Value.AiTags ?? new Dictionary<string, string>())
                        .OrderBy(tag => tag.Key, StringComparer.Ordinal)
                        .Select(tag => new { tag.Key, tag.Value })
                        .ToArray()
                })
                .ToArray(),
            Skills = skills
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new
                {
                    entry.Value.Id,
                    Name = entry.Value.Program is null ? entry.Value.Name : string.Empty,
                    Description = entry.Value.Program is null ? entry.Value.Description : string.Empty,
                    LegacyKind = entry.Value.LegacyKind?.ToString()
                })
                .ToArray(),
            Generals = generals
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new
                {
                    entry.Value.Id,
                    entry.Value.Name,
                    entry.Value.PortraitKey,
                    entry.Value.SkillId,
                    entry.Value.FactionId
                })
                .ToArray(),
            Decks = decks
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new
                {
                    entry.Value.Id,
                    entry.Value.Name,
                    entry.Value.InitialHandSize,
                    entry.Value.DrawPerTurn,
                    Cards = entry.Value.Cards
                        .Select(card => new { card.CardDefinitionId, card.Count })
                        .ToArray()
                })
                .ToArray(),
            Modes = modes
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new
                {
                    entry.Value.Id,
                    entry.Value.Name,
                    entry.Value.MinPlayers,
                    entry.Value.MaxPlayers,
                    RoleCounts = (entry.Value.RoleCounts ?? new Dictionary<string, int>())
                        .OrderBy(role => role.Key, StringComparer.Ordinal)
                        .Select(role => new { role.Key, role.Value })
                        .ToArray(),
                    entry.Value.DeckId,
                    entry.Value.GeneralCandidateCount,
                    GeneralPoolIds = entry.Value.GeneralPoolIds?.ToArray()
                })
                .ToArray()
        });

        // Keep the v1 byte representation stable for every existing identity
        // registry. Team-mode metadata is added only when a registry actually
        // contains a team mode, so old checkpoints and content hashes remain
        // compatible while new modes still participate in drift detection.
        var hasExtendedMode = modes.Values.Any(mode =>
            mode.ModeKind != ContentModeKind.Identity || mode.TeamCounts is { Count: > 0 });
        var nationalModes = modes.Values
            .Where(mode => mode.ModeKind == ContentModeKind.NationalWarLite)
            .OrderBy(mode => mode.Id, StringComparer.Ordinal)
            .ToArray();
        var factionModeExtensions = nationalModes
            .Select(mode => mode.SoloFactionIds is { Count: > 0 }
                ? (object)new
                {
                    mode.Id,
                    FactionCounts = (mode.FactionCounts ?? new Dictionary<string, int>())
                        .OrderBy(faction => faction.Key, StringComparer.Ordinal)
                        .Select(faction => new { faction.Key, faction.Value })
                        .ToArray(),
                    SoloFactionIds = mode.SoloFactionIds
                        .OrderBy(id => id, StringComparer.Ordinal)
                        .ToArray()
                }
                : new
                {
                    mode.Id,
                    FactionCounts = (mode.FactionCounts ?? new Dictionary<string, int>())
                        .OrderBy(faction => faction.Key, StringComparer.Ordinal)
                        .Select(faction => new { faction.Key, faction.Value })
                        .ToArray()
                })
            .ToArray();
        var canonical = hasExtendedMode
            ? nationalModes.Length == 0
                ? JsonSerializer.Serialize(new
                {
                    HashSchema = 2,
                    Base = JsonSerializer.Deserialize<JsonElement>(baseCanonical),
                    ModeExtensions = modes
                        .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                        .Where(entry => entry.Value.ModeKind != ContentModeKind.Identity ||
                                        entry.Value.TeamCounts is { Count: > 0 })
                        .Select(entry => new
                        {
                            entry.Value.Id,
                            ModeKind = entry.Value.ModeKind.ToString(),
                            TeamCounts = (entry.Value.TeamCounts ?? new Dictionary<string, int>())
                                .OrderBy(team => team.Key, StringComparer.Ordinal)
                                .Select(team => new { team.Key, team.Value })
                                .ToArray()
                        })
                        .ToArray()
                })
                : JsonSerializer.Serialize(new
                {
                    HashSchema = 3,
                    Base = JsonSerializer.Deserialize<JsonElement>(baseCanonical),
                    ModeExtensions = modes
                        .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                        .Where(entry => entry.Value.ModeKind != ContentModeKind.Identity ||
                                        entry.Value.TeamCounts is { Count: > 0 })
                        .Select(entry => new
                        {
                            entry.Value.Id,
                            ModeKind = entry.Value.ModeKind.ToString(),
                            TeamCounts = (entry.Value.TeamCounts ?? new Dictionary<string, int>())
                                .OrderBy(team => team.Key, StringComparer.Ordinal)
                                .Select(team => new { team.Key, team.Value })
                                .ToArray()
                        })
                        .ToArray(),
                    FactionModeExtensions = factionModeExtensions
                })
            : baseCanonical;

        var generalVitals = generals.Values.Where(general => general.BaseHp != 4)
            .OrderBy(general => general.Id, StringComparer.Ordinal)
            .Select(general => new { general.Id, general.BaseHp }).ToArray();
        if (generalVitals.Length > 0)
            canonical = JsonSerializer.Serialize(new { HashSchema = 4, Base = JsonSerializer.Deserialize<JsonElement>(canonical), GeneralVitals = generalVitals });
        var generalSkillExtensions = generals.Values
            .Where(general => general.AdditionalSkillIds is { Count: > 0 })
            .OrderBy(general => general.Id, StringComparer.Ordinal)
            .Select(general => new
            {
                general.Id,
                AdditionalSkillIds = general.AdditionalSkillIds!.ToArray()
            })
            .ToArray();
        if (generalSkillExtensions.Length > 0)
            canonical = JsonSerializer.Serialize(new
            {
                HashSchema = 5,
                Base = JsonSerializer.Deserialize<JsonElement>(canonical),
                GeneralSkillExtensions = generalSkillExtensions
            });
        var generalGenderExtensions = generals.Values
            .Where(general => general.Gender != GeneralGender.Male)
            .OrderBy(general => general.Id, StringComparer.Ordinal)
            .Select(general => new { general.Id, Gender = general.Gender.ToString() })
            .ToArray();
        if (generalGenderExtensions.Length > 0)
            canonical = JsonSerializer.Serialize(new
            {
                HashSchema = 6,
                Base = JsonSerializer.Deserialize<JsonElement>(canonical),
                GeneralGenderExtensions = generalGenderExtensions
            });
        var physicalDeckExtensions = decks.Values
            .Where(deck => deck.PhysicalCards is { Count: > 0 })
            .OrderBy(deck => deck.Id, StringComparer.Ordinal)
            .Select(deck => new
            {
                deck.Id,
                PhysicalCards = deck.PhysicalCards!.Select(card => new
                {
                    card.CardDefinitionId,
                    Suit = card.Suit.ToString(),
                    card.Rank
                }).ToArray()
            })
            .ToArray();
        if (physicalDeckExtensions.Length > 0)
            canonical = JsonSerializer.Serialize(new
            {
                HashSchema = 7,
                Base = JsonSerializer.Deserialize<JsonElement>(canonical),
                PhysicalDeckExtensions = physicalDeckExtensions
            });
        var programs = skills.Values
            .Where(skill => skill.Program is not null)
            .OrderBy(skill => skill.Id, StringComparer.Ordinal)
            .Select(skill => new { skill.Program!.Id, skill.Program.GameplayHash })
            .ToArray();
        if (programs.Length > 0)
            canonical = JsonSerializer.Serialize(new
            {
                HashSchema = 8,
                Base = JsonSerializer.Deserialize<JsonElement>(canonical),
                RuntimeVersion = string.Join("+", skills.Values
                    .Where(skill => skill.Program is not null)
                    .Select(skill => skill.Program!.RuntimeVersion)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(version => version, StringComparer.Ordinal)
                    .DefaultIfEmpty(SkillProgramCatalog.RuntimeVersion)),
                Programs = programs
            });
        var skillMetadata = skills.Values
            .Where(skill => skill.Tags != SkillTag.None || skill.ExecutionForms != SkillExecutionForm.None)
            .OrderBy(skill => skill.Id, StringComparer.Ordinal)
            .Select(skill => new
            {
                skill.Id,
                Tags = skill.Tags.ToString(),
                ExecutionForms = skill.ExecutionForms.ToString()
            })
            .ToArray();
        if (skillMetadata.Length > 0)
            canonical = JsonSerializer.Serialize(new
            {
                HashSchema = 9,
                Base = JsonSerializer.Deserialize<JsonElement>(canonical),
                SkillMetadata = skillMetadata
            });
        var skillActionForms = skills.Values
            .Where(skill => skill.ActionForms != SkillActionForm.None)
            .OrderBy(skill => skill.Id, StringComparer.Ordinal)
            .Select(skill => new
            {
                skill.Id,
                ActionForms = skill.ActionForms.ToString()
            })
            .ToArray();
        if (skillActionForms.Length > 0)
            canonical = JsonSerializer.Serialize(new
            {
                HashSchema = 10,
                Base = JsonSerializer.Deserialize<JsonElement>(canonical),
                SkillActionForms = skillActionForms
            });
        var phaseSkills = skills.Values
            .Where(skill => skill.PhaseSkill is not null)
            .OrderBy(skill => skill.Id, StringComparer.Ordinal)
            .Select(skill => new { skill.Id, skill.PhaseSkill!.Revision, Window = skill.PhaseSkill.Window.ToString() })
            .ToArray();
        if (phaseSkills.Length > 0)
            canonical = JsonSerializer.Serialize(new
            {
                HashSchema = 11,
                Base = JsonSerializer.Deserialize<JsonElement>(canonical),
                PhaseSkillRuntime = "phase-skills-v2",
                PhaseSkills = phaseSkills
            });
        var pindianSkills = skills.Values.Where(skill => skill.PindianResultSkill is not null)
            .OrderBy(skill => skill.Id, StringComparer.Ordinal)
            .Select(skill => new { skill.Id, skill.PindianResultSkill!.Revision }).ToArray();
        if (pindianSkills.Length > 0)
            canonical = JsonSerializer.Serialize(new
            {
                HashSchema = 12, Base = JsonSerializer.Deserialize<JsonElement>(canonical),
                PindianRuntime = "pindian-v1", PindianSkills = pindianSkills
            });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
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
                             .Where(effect => effect.Op == SkillProgramTriggerEffectOp.GrantSkills)
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
            if (normalized.PhaseSkill is { } phaseSkill &&
                (phaseSkill.SkillId != normalized.Id || phaseSkill.Revision < 1 ||
                 !Enum.IsDefined(phaseSkill.Window) || normalized.Program is not null))
                throw new InvalidOperationException($"Invalid phase skill binding for '{normalized.Id}'.");
            if (normalized.PindianResultSkill is { } pindianSkill &&
                (pindianSkill.SkillId != normalized.Id || pindianSkill.Revision < 1 || normalized.Program is not null))
                throw new InvalidOperationException($"Invalid Pindian result skill binding for '{normalized.Id}'.");
            if (normalized.Program is null)
                return normalized;

            if (!string.Equals(normalized.Program.Id, normalized.Id, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Skill '{normalized.Id}' is bound to program '{normalized.Program.Id}'. Program ids must match their content skill ids.");
            if (normalized.LegacyKind is not null and not SkillKind.None)
                throw new InvalidOperationException(
                    $"Skill '{normalized.Id}' cannot have both configured and legacy implementations.");
            return normalized;
        }

        private static ContentGeneralDefinition NormalizeGeneral(ContentGeneralDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            if (definition.BaseHp is < 1 or > 20)
                throw new ArgumentOutOfRangeException(nameof(definition), "General base HP must be between 1 and 20.");
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
