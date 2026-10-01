using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class FengLinChenDaoContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "classic-chen-dao", definition =>
            definition with { ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        builder.AddGeneral(new ContentGeneralDefinition("classic:chen-dao","陈到","chen_dao","classic:wanglie","shu",4,[],GeneralGender.Male){CharacterId="character:chen-dao",VariantId="classic",RulesetId="sanguosha-ol"});
    }
}
