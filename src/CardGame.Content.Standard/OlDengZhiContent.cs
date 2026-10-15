using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlDengZhiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder,"ol-deng-zhi",d=>d with
        { Tags=d.Id=="ol:sujian" ? SkillTag.Locked : SkillTag.None, ExecutionForms=SkillExecutionForm.Trigger });
        builder.AddGeneral(new("ol:deng-zhi","邓芝·OL","ol-deng-zhi","ol:xiuhao","shu",3,["ol:sujian"],GeneralGender.Male)
        {CharacterId="character:deng-zhi",VariantId="ordinary",RulesetId="sanguosha-ol"});
    }
}
