using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class Fame2016ConversionContent
{
    internal static IReadOnlyList<string> AddedGeneralIds { get; } = ["classic:guo-huanghou"];
    internal static void Register(IContentRegistryBuilder builder)
    {
        foreach (var id in new[] { "classic:jiaozhao", "classic:danxin" })
        {
            var definition = EmbeddedSkillProgramCatalog.Definition("classic-guo-huanghou", id);
            builder.AddSkill(definition with { ExecutionForms = id == "classic:danxin" ? SkillExecutionForm.Trigger : SkillExecutionForm.State,
                ActionForms = id == "classic:jiaozhao" ? SkillActionForm.Active : SkillActionForm.None });
        }
        builder.AddGeneral(new ContentGeneralDefinition("classic:guo-huanghou", "郭皇后", "guo_huanghou",
            "classic:jiaozhao", "wei", 3, ["classic:danxin"], GeneralGender.Female)
        { CharacterId = "character:guo-huanghou", VariantId = "classic", RulesetId = "sanguosha-ol" });
    }
}
