using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlXuanGongZhuContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder,"ol-xuan-gong-zhu",d=>d with{ExecutionForms=SkillExecutionForm.Trigger});
        builder.AddGeneral(new("ol:xuan-gong-zhu","宣公主·OL","ol-xuan-gong-zhu","ol:gaoling","jin",3,["ol:qimei","ol:zhuiji-tj"],GeneralGender.Female)
        {CharacterId="character:xuan-gong-zhu",VariantId="ol",RulesetId="sanguosha-ol"});
    }
}
