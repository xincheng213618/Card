using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class BoundaryZhenJiContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder, "boundary-zhen-ji", definition => definition with
        { ExecutionForms = SkillExecutionForm.None, ActionForms = SkillActionForm.None });
        builder.AddGeneral(new("boundary:zhen-ji", "界甄姬", "boundary_zhen_ji", "boundary:luoshen", "wei", 3,
            ["classic:qingguo"], GeneralGender.Female)
        { CharacterId = "character:zhen-ji", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
