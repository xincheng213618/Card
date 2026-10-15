using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlSimaHuiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder,"ol-sima-hui",definition=>definition with
        {
            Tags=definition.Id=="ol:yinshi-sima-hui"?SkillTag.Locked:definition.Id=="ol:yeyan-jianjie"?SkillTag.Limited:SkillTag.None,
            ExecutionForms=definition.Id is "ol:huoji-jianjie" or "ol:lianhuan-jianjie"?SkillExecutionForm.State:SkillExecutionForm.Trigger,
            ActionForms=definition.Id is "ol:jianjie" or "ol:huoji-jianjie" or "ol:lianhuan-jianjie" or "ol:yeyan-jianjie"?SkillActionForm.Active:SkillActionForm.None
        });
        builder.AddGeneral(new("ol:sima-hui","司马徽·OL","ol-sima-hui","ol:jianjie","qun",3,["ol:chenghao","ol:yinshi-sima-hui"],GeneralGender.Male)
            {CharacterId="character:sima-hui",VariantId="ordinary",RulesetId="sanguosha-ol"});
    }
}
