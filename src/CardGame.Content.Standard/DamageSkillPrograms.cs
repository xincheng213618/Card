using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class DamageSkillPrograms
{
    private const string RulesResource =
        "CardGame.Content.Standard.SkillPrograms.damage-skills.rules.json";
    private const string PresentationResource =
        "CardGame.Content.Standard.SkillPrograms.damage-skills.presentation.json";

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
            Program = program,
            ExecutionForms = SkillExecutionForm.Trigger
        };
    }

    private static string ReadEmbeddedText(string resourceName)
    {
        var assembly = typeof(DamageSkillPrograms).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName) ??
            throw new InvalidOperationException($"Missing embedded resource '{resourceName}'.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
