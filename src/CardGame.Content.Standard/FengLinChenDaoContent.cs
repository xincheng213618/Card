using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class FengLinChenDaoContent
{
    internal static IReadOnlyList<string> AddedGeneralIds {get;}=["classic:chen-dao"];
    internal static void Register(IContentRegistryBuilder builder)
    {
        var definition=EmbeddedSkillProgramCatalog.Definition("classic-chen-dao","classic:wanglie");
        builder.AddSkill(definition with{ExecutionForms=SkillExecutionForm.Trigger,ActionForms=SkillActionForm.None});
        builder.AddGeneral(new ContentGeneralDefinition("classic:chen-dao","陈到","chen_dao","classic:wanglie","shu",4,[],GeneralGender.Male){CharacterId="character:chen-dao",VariantId="classic",RulesetId="sanguosha-ol"});
    }
}
