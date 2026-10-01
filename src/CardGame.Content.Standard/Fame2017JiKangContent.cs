using CardGame.Core;
namespace CardGame.Content.Standard;
internal static class Fame2017JiKangContent
{
    internal static IReadOnlyList<string> AddedGeneralIds { get; } = ["classic:ji-kang"];
    internal static void Register(IContentRegistryBuilder builder)
    {
        foreach (var id in new[] { "classic:qingxian", "classic:juexiang", "classic:jixian", "classic:liexian", "classic:rouxian", "classic:hexian" })
        {
            var definition = EmbeddedSkillProgramCatalog.Definition("classic-ji-kang", id);
            builder.AddSkill(definition with { ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        }
        builder.AddGeneral(new ContentGeneralDefinition("classic:ji-kang", "嵇康", "ji_kang", "classic:qingxian", "wei", 3, ["classic:juexiang"], GeneralGender.Male)
        { CharacterId = "character:ji-kang", VariantId = "classic", RulesetId = "sanguosha-ol" });
    }
}
