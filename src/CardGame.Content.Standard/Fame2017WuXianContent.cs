using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class Fame2017WuXianContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "classic-wu-xian", definition =>
            definition with { ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        builder.AddGeneral(new ContentGeneralDefinition("classic:wu-xian","吴苋","wu_xian","classic:fumian","shu",3,["classic:daiyan"],GeneralGender.Female)
        {CharacterId="character:wu-xian",VariantId="classic",RulesetId="sanguosha-ol"});
    }
}
