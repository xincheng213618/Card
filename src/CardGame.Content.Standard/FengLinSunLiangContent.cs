using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class FengLinSunLiangContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "classic-sun-liang", definition =>
            definition with
            {
                Tags = definition.Id switch
                {
                    "classic:chezheng" => SkillTag.Locked,
                    "classic:lijun" => SkillTag.Lord,
                    _ => SkillTag.None
                },
                ExecutionForms = SkillExecutionForm.Trigger
            });
        builder.AddGeneral(new ContentGeneralDefinition("classic:sun-liang","孙亮","sun_liang","classic:kuizhu","wu",3,["classic:chezheng","classic:lijun"],GeneralGender.Male)
        { CharacterId="character:sun-liang",VariantId="classic",RulesetId="sanguosha-ol" });
    }
}
