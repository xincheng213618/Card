using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class AwakeningSkillPrograms
{
    private const string RulesResource =
        "CardGame.Content.Standard.SkillPrograms.awakening-skills.rules.json";
    private const string PresentationResource =
        "CardGame.Content.Standard.SkillPrograms.awakening-skills.presentation.json";
    private const string StateRulesResource =
        "CardGame.Content.Standard.SkillPrograms.state-awakening-skills.rules.json";
    private const string StatePresentationResource =
        "CardGame.Content.Standard.SkillPrograms.state-awakening-skills.presentation.json";

    private static readonly Lazy<SkillProgramCatalog> Catalog = new(() =>
        SkillProgramCatalog.Load(
            ReadEmbeddedText(RulesResource),
            ReadEmbeddedText(PresentationResource)));
    private static readonly Lazy<SkillProgramCatalog> StateCatalog = new(() =>
        SkillProgramCatalog.Load(
            ReadEmbeddedText(StateRulesResource),
            ReadEmbeddedText(StatePresentationResource)));

    public static ContentSkillDefinition Definition(string id)
    {
        var catalog = Catalog.Value.Programs.ContainsKey(id) ? Catalog.Value : StateCatalog.Value;
        var program = catalog.Programs[id];
        var presentation = catalog.Presentations[id];
        return new ContentSkillDefinition(id, presentation.Name, presentation.Description)
        {
            Program = program,
            Tags = SkillTag.Awakening,
            ExecutionForms = SkillExecutionForm.Trigger
        };
    }

    private static string ReadEmbeddedText(string resourceName)
    {
        var assembly = typeof(AwakeningSkillPrograms).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException($"Missing embedded resource '{resourceName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
