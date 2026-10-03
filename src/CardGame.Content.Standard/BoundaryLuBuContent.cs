using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class BoundaryLuBuContent
{
    internal static void Register(IContentRegistryBuilder b)
    {
        b.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-lu-bu","boundary:liyu-current") with
        { ExecutionForms=SkillExecutionForm.Trigger, ActionForms=SkillActionForm.None });
        b.AddGeneral(new("boundary:lu-bu","界吕布","boundary_lu_bu","classic:wushuang","qun",5,["boundary:liyu-current"],GeneralGender.Male)
        { CharacterId="character:lu-bu",VariantId="boundary",RulesetId="sanguosha-ol" });
    }
}
