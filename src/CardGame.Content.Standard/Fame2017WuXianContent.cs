using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class Fame2017WuXianContent
{
    internal static IReadOnlyList<string> AddedGeneralIds {get;}=["classic:wu-xian"];
    internal static void Register(IContentRegistryBuilder builder)
    {
        foreach(var id in new[]{"classic:fumian","classic:daiyan"}) builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("classic-wu-xian",id) with {ExecutionForms=SkillExecutionForm.Trigger,ActionForms=SkillActionForm.None});
        builder.AddGeneral(new ContentGeneralDefinition("classic:wu-xian","吴苋","wu_xian","classic:fumian","shu",3,["classic:daiyan"],GeneralGender.Female)
        {CharacterId="character:wu-xian",VariantId="classic",RulesetId="sanguosha-ol"});
    }
}
