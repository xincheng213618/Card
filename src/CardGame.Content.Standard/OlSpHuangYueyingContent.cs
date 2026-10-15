using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlSpHuangYueyingContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("ol-sp-huang-yueying","ol:jiqiao") with{ExecutionForms=SkillExecutionForm.Trigger});
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("ol-sp-huang-yueying","ol:linglong") with{Tags=SkillTag.Locked,ExecutionForms=SkillExecutionForm.State,ActionForms=SkillActionForm.None});
        builder.AddGeneral(new("ol:huang-yueying-sp","SP黄月英·OL","ol-sp-huang-yueying","ol:jiqiao","qun",3,["ol:linglong"],GeneralGender.Female)
        {CharacterId="character:huang-yueying",VariantId="sp",RulesetId="sanguosha-ol"});
    }
}
