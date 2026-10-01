using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class Fame2017XuShiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "classic-xu-shi", definition =>
            definition with
            {
                ExecutionForms = definition.Id == "classic:wengua" ? SkillExecutionForm.State : SkillExecutionForm.Trigger,
                ActionForms = definition.Id == "classic:wengua" ? SkillActionForm.Active : SkillActionForm.None
            });
        builder.AddGeneral(new ContentGeneralDefinition("classic:xu-shi", "徐氏", "xu_shi", "classic:wengua", "wu", 3, ["classic:fuzhu"], GeneralGender.Female)
        { CharacterId = "character:xu-shi", VariantId = "classic", RulesetId = "sanguosha-ol" });
    }
}
