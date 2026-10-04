using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class BoundaryWangYiContent
{
    internal static void Register(IContentRegistryBuilder b)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(b, "boundary-wang-yi", d => d with
        { ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        b.AddGeneral(new("boundary:wang-yi", "界王异", "boundary_wang_yi", "boundary:zhenlie-current", "wei", 3,
            ["boundary:miji-current"], GeneralGender.Female)
        { CharacterId = "character:wang-yi", VariantId = "boundary", RulesetId = "sanguosha-ol" });
    }
}
