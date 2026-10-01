using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class FengLinHaoZhaoContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "classic-hao-zhao", definition =>
            definition with { ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        builder.AddGeneral(new ContentGeneralDefinition("classic:hao-zhao","郝昭","hao_zhao","classic:zhengu","wei",4,[],GeneralGender.Male){CharacterId="character:hao-zhao",VariantId="classic",RulesetId="sanguosha-ol"});
    }
}
