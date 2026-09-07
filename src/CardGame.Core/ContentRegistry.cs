using System.Collections.ObjectModel;

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
    SkillKind? LegacyKind = null);

public sealed record ContentGeneralDefinition(
    string Id,
    string Name,
    string PortraitKey,
    string SkillId,
    string? FactionId = null);

public sealed record ContentDeckCardCount(string CardDefinitionId, int Count);

public sealed record ContentDeckRecipe(
    string Id,
    string Name,
    int InitialHandSize,
    int DrawPerTurn,
    IReadOnlyList<ContentDeckCardCount> Cards);

public sealed record ContentModeDefinition(
    string Id,
    string Name,
    int MinPlayers,
    int MaxPlayers,
    IReadOnlyDictionary<string, int> RoleCounts,
    string? DeckId = null,
    int GeneralCandidateCount = 3,
    IReadOnlyList<string>? GeneralPoolIds = null);

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
    }

    public IReadOnlyList<PackageManifest> Packages { get; }

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
        return builder.Freeze(ordered);
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
            foreach (var general in _generals.Values)
            {
                if (!_skills.ContainsKey(general.SkillId))
                {
                    throw new InvalidOperationException(
                        $"General '{general.Id}' references unknown skill '{general.SkillId}'.");
                }
            }

            foreach (var deck in _decks.Values)
            {
                if (deck.InitialHandSize < 0 || deck.DrawPerTurn < 0 || deck.Cards.Count == 0)
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
            }

            foreach (var mode in _modes.Values)
            {
                if (mode.MinPlayers <= 0 || mode.MaxPlayers < mode.MinPlayers)
                {
                    throw new InvalidOperationException(
                        $"Mode '{mode.Id}' has an invalid player range.");
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

                if (mode.GeneralPoolIds is not null)
                {
                    if (mode.GeneralPoolIds.Count < mode.MaxPlayers ||
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
            return definition;
        }

        private static ContentGeneralDefinition NormalizeGeneral(ContentGeneralDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            return definition;
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
                Cards = Array.AsReadOnly(definition.Cards.Select(card => card with { }).ToArray())
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
                    : Array.AsReadOnly(definition.GeneralPoolIds.ToArray())
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
