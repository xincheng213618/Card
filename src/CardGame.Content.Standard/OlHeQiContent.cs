using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlHeQiContent
{
    internal static void Register(IContentRegistryBuilder b)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(b,"ol-he-qi",d=>d with{ExecutionForms=d.Id=="ol:qizhou"?SkillExecutionForm.State:SkillExecutionForm.None,ActionForms=d.Id=="ol:shanxi"?SkillActionForm.Active:SkillActionForm.None,Tags=d.Id=="ol:qizhou"?SkillTag.Locked:SkillTag.None});
        b.AddGeneral(new("ol:he-qi","贺齐·OL","ol-he-qi","ol:shanxi","wu",4,["ol:qizhou"],GeneralGender.Male){CharacterId="character:he-qi",VariantId="ordinary",RulesetId="sanguosha-ol"});
    }
}
