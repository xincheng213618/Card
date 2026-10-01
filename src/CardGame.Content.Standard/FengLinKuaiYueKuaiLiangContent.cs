using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class FengLinKuaiYueKuaiLiangContent
{
    internal static void Register(IContentRegistryBuilder b)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(b,"classic-kuai-yue-kuai-liang",d=>d.Id=="classic:shenshi" ? d with{Tags=SkillTag.Conversion,ExecutionForms=SkillExecutionForm.Trigger|SkillExecutionForm.State}:d);
        b.AddGeneral(new ContentGeneralDefinition("classic:kuai-yue-kuai-liang","蒯越蒯良","kuai_yue_kuai_liang","classic:jianxiang","wei",3,["classic:shenshi"],GeneralGender.Male)
        {CharacterId="character:kuai-yue-kuai-liang",VariantId="classic",RulesetId="sanguosha-ol"});
    }
}
