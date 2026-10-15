using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class OlWuYanContent
{
    internal static void Register(IContentRegistryBuilder b)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(b,"ol-wu-yan",d=>d with{ExecutionForms=SkillExecutionForm.Trigger});
        b.AddGeneral(new("ol:wu-yan","吾彦·OL","ol-wu-yan","ol:lanjiang","wu",4,[],GeneralGender.Male)
        {CharacterId="character:wu-yan",VariantId="ordinary",RulesetId="sanguosha-ol"});
    }
}
