using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundaryWolongZhugeLiangContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-wolong-zhuge-liang", definition => definition.Id switch
        {
            "boundary:cangzhuo" => definition with { Tags = SkillTag.Locked,
                ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None },
            _ => definition
        });
        builder.AddGeneral(new("boundary:wolong-zhuge-liang", "界卧龙诸葛亮", "boundary_wolong_zhuge_liang",
            "classic:bazhen", "shu", 3, ["boundary:huoji-current", "boundary:kanpo-current", "boundary:cangzhuo"], GeneralGender.Male)
        { CharacterId = "character:wolong-zhuge-liang", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
