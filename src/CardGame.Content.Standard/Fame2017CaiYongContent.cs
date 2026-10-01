using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class Fame2017CaiYongContent
{
    internal static IReadOnlyList<string> AddedGeneralIds { get; } = ["classic:cai-yong"];
    internal static void Register(IContentRegistryBuilder builder)
    {
        foreach (var id in new[] { "classic:bizhuan", "classic:tongbo" })
        {
            var definition = EmbeddedSkillProgramCatalog.Definition("classic-cai-yong", id);
            builder.AddSkill(definition with { ExecutionForms = SkillExecutionForm.Trigger | (id == "classic:bizhuan" ? SkillExecutionForm.State : SkillExecutionForm.None), ActionForms = SkillActionForm.None });
        }
        builder.AddGeneral(new ContentGeneralDefinition("classic:cai-yong", "蔡邕", "cai_yong", "classic:bizhuan", "qun", 3, ["classic:tongbo"], GeneralGender.Male)
        { CharacterId = "character:cai-yong", VariantId = "classic", RulesetId = "sanguosha-ol" });
    }
}
