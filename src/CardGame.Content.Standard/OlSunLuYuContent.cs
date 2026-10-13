using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class OlSunLuYuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "ol-sun-lu-yu", definition => definition with
            { ExecutionForms = definition.Id == "ol:zhixi" ? SkillExecutionForm.State : SkillExecutionForm.Trigger },
            new Dictionary<string, SkillTag> { ["ol:zhixi"] = SkillTag.Locked });
        // Current ordinary client character 135 lists Mumu247 before Meibu246.
        builder.AddGeneral(new("ol:sun-lu-yu", "孙鲁育·OL", "ol-sun-lu-yu", "ol:mumu", "wu", 3,
            ["ol:meibu"], GeneralGender.Female)
        { CharacterId = "character:sun-lu-yu", VariantId = "ol", RulesetId = "sanguosha-ol" });
    }
}
