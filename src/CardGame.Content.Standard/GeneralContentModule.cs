using CardGame.Core;

namespace CardGame.Content.Standard;

/// <summary>
/// One explicit module declaration supplies both registration and its ordered roster.
/// The declarations are compiled once; each registry still validates and freezes them.
/// </summary>
internal sealed class GeneralContentModule
{
    private readonly Lazy<DeclarationBuilder> _declaration;
    private readonly bool _registerGeneralSkillsFirst;
    private readonly HashSet<string> _registerSkillsLast;

    internal GeneralContentModule(
        Action<IContentRegistryBuilder> declare,
        bool appendPoolLast = false,
        bool registerGeneralSkillsFirst = false,
        IReadOnlyCollection<string>? registerSkillsLast = null)
    {
        ArgumentNullException.ThrowIfNull(declare);
        AppendPoolLast = appendPoolLast;
        _registerGeneralSkillsFirst = registerGeneralSkillsFirst;
        _registerSkillsLast = new HashSet<string>(registerSkillsLast ?? [], StringComparer.Ordinal);
        _declaration = new Lazy<DeclarationBuilder>(() =>
        {
            var declaration = new DeclarationBuilder();
            declare(declaration);
            return declaration;
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    // Fame 2015 historically registers earlier than its position at the pool's tail.
    internal bool AppendPoolLast { get; }

    internal IEnumerable<string> GeneralIds => _declaration.Value.Generals.Select(general => general.Id);

    internal void Register(IContentRegistryBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var declaration = _declaration.Value;
        IEnumerable<ContentSkillDefinition> skills = declaration.Skills;
        if (_registerGeneralSkillsFirst)
        {
            var generalSkillIds = declaration.Generals.SelectMany(general => general.SkillIds)
                .Distinct(StringComparer.Ordinal).ToArray();
            var generalSkillSet = generalSkillIds.ToHashSet(StringComparer.Ordinal);
            skills = generalSkillIds.SelectMany(id => declaration.Skills.Where(skill => skill.Id == id))
                .Concat(declaration.Skills.Where(skill => !generalSkillSet.Contains(skill.Id)));
        }
        foreach (var id in _registerSkillsLast)
            if (!declaration.Skills.Any(skill => skill.Id == id))
                throw new InvalidOperationException($"A module cannot defer undeclared skill '{id}'.");
        foreach (var skill in skills.Where(skill => !_registerSkillsLast.Contains(skill.Id))) builder.AddSkill(skill);
        foreach (var skill in skills.Where(skill => _registerSkillsLast.Contains(skill.Id))) builder.AddSkill(skill);
        foreach (var general in declaration.Generals) builder.AddGeneral(general);
    }

    private sealed class DeclarationBuilder : IContentRegistryBuilder
    {
        internal List<ContentSkillDefinition> Skills { get; } = [];
        internal List<ContentGeneralDefinition> Generals { get; } = [];

        public void AddSkill(ContentSkillDefinition definition) => Skills.Add(definition);
        public void AddGeneral(ContentGeneralDefinition definition) => Generals.Add(definition);
        public void AddCard(ContentCardDefinition definition) => throw UnsupportedContent();
        public void AddDeck(ContentDeckRecipe definition) => throw UnsupportedContent();
        public void AddMode(ContentModeDefinition definition) => throw UnsupportedContent();

        private static InvalidOperationException UnsupportedContent() =>
            new("A general module may declare only skills and generals.");
    }
}
