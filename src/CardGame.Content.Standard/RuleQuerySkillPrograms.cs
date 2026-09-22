using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class RuleQuerySkillPrograms
{
    private const string RulesResource =
        "CardGame.Content.Standard.SkillPrograms.rule-query-skills.rules.json";
    private const string PresentationResource =
        "CardGame.Content.Standard.SkillPrograms.rule-query-skills.presentation.json";

    private static readonly Lazy<SkillProgramCatalog> Catalog = new(() =>
        SkillProgramCatalog.Load(
            ReadEmbeddedText(RulesResource),
            ReadEmbeddedText(PresentationResource)));

    public static ContentSkillDefinition Definition(string id)
    {
        var program = Catalog.Value.Programs[id];
        var presentation = Catalog.Value.Presentations[id];
        return new ContentSkillDefinition(id, presentation.Name, presentation.Description)
        {
            Program = program
        };
    }

    private static string ReadEmbeddedText(string resourceName)
    {
        var assembly = typeof(RuleQuerySkillPrograms).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException($"Missing embedded resource '{resourceName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
