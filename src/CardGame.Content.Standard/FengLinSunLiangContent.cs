using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class FengLinSunLiangContent
{
    internal static IReadOnlyList<string> AddedGeneralIds { get; }=["classic:sun-liang"];
    internal static void Register(IContentRegistryBuilder builder)
    {
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("classic-sun-liang","classic:kuizhu") with { ExecutionForms=SkillExecutionForm.Trigger });
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("classic-sun-liang","classic:chezheng") with { Tags=SkillTag.Locked,ExecutionForms=SkillExecutionForm.Trigger });
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("classic-sun-liang","classic:lijun") with { Tags=SkillTag.Lord,ExecutionForms=SkillExecutionForm.Trigger });
        builder.AddGeneral(new ContentGeneralDefinition("classic:sun-liang","孙亮","sun_liang","classic:kuizhu","wu",3,["classic:chezheng","classic:lijun"],GeneralGender.Male)
        { CharacterId="character:sun-liang",VariantId="classic",RulesetId="sanguosha-ol" });
    }
}
