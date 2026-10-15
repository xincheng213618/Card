using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlWangYunContent
{
    internal static void Register(IContentRegistryBuilder b)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(b,"ol-wang-yun",d=>d with{Tags=d.Id=="ol:moucheng"?SkillTag.Awakening:SkillTag.None,ExecutionForms=d.Id=="ol:moucheng"?SkillExecutionForm.Trigger:SkillExecutionForm.None,
            ActionForms=d.Id=="ol:moucheng"?SkillActionForm.None:SkillActionForm.Active});
        b.AddGeneral(new("ol:wang-yun","王允·OL","ol-wang-yun","ol:lianji","qun",4,["ol:moucheng"],GeneralGender.Male)
        {CharacterId="character:wang-yun",VariantId="ordinary",RulesetId="sanguosha-ol"});
    }
}
