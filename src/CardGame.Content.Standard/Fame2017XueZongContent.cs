using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class Fame2017XueZongContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "classic-xue-zong", definition =>
            definition with { ExecutionForms = SkillExecutionForm.Trigger });
        builder.AddGeneral(new ContentGeneralDefinition("classic:xue-zong","薛综","xue_zong","classic:funan","wu",3,["classic:jiexun"],GeneralGender.Male)
        {CharacterId="character:xue-zong",VariantId="classic",RulesetId="sanguosha-ol"});
    }
}
