using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class Fame2017XueZongContent
{
    internal static IReadOnlyList<string> AddedGeneralIds { get; } = ["classic:xue-zong"];
    internal static void Register(IContentRegistryBuilder builder)
    {
        foreach(var id in new[]{"classic:funan","classic:jiexun"})
            builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("classic-xue-zong",id) with {ExecutionForms=SkillExecutionForm.Trigger});
        builder.AddGeneral(new ContentGeneralDefinition("classic:xue-zong","薛综","xue_zong","classic:funan","wu",3,["classic:jiexun"],GeneralGender.Male)
        {CharacterId="character:xue-zong",VariantId="classic",RulesetId="sanguosha-ol"});
    }
}
