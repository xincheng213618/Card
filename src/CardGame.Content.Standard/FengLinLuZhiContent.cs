using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class FengLinLuZhiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder,"classic-lu-zhi",d=>d with{Tags=d.Id=="classic:zhenliang"?SkillTag.Conversion:SkillTag.None,ExecutionForms=d.Id=="classic:zhenliang"?SkillExecutionForm.State|SkillExecutionForm.Trigger:SkillExecutionForm.Trigger});
        builder.AddGeneral(new ContentGeneralDefinition("classic:lu-zhi","卢植","lu_zhi","classic:mingren","qun",3,["classic:zhenliang"],GeneralGender.Male){CharacterId="character:lu-zhi",VariantId="classic",RulesetId="sanguosha-ol"});
    }
}
