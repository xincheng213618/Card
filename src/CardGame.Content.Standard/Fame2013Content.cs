using CardGame.Core;

namespace CardGame.Content.Standard;

// Current OL rules and official portraits: docs/content/sources/fame-2013-2026-09-30.json.
internal static class Fame2013Content
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        RegisterBundle("classic-li-ru", ("classic:fencheng", SkillTag.Limited));
        RegisterBundle("classic-liu-feng");
        RegisterBundle("classic-jian-yong");
        RegisterBundle("classic-yu-fan");
        RegisterBundle("classic-zhu-ran");
        Add("li-ru", "李儒", "qun", 3, ["classic:mieji", "classic:juece", "classic:fencheng"]);
        Add("liu-feng", "刘封", "shu", 4, ["classic:xiansi"]);
        Add("jian-yong", "简雍", "shu", 3, ["classic:qiaoshui", "classic:zongshi-pindian"]);
        Add("yu-fan", "虞翻", "wu", 3, ["classic:zongxuan", "classic:zhiyan"]);
        Add("zhu-ran", "朱然", "wu", 4, ["classic:danshou"]);

        void Add(string key, string name, string faction, int hp, string[] skills) =>
            builder.AddGeneral(new ContentGeneralDefinition("classic:" + key, name, key.Replace('-', '_'),
                skills[0], faction, hp, skills.Skip(1).ToArray(), GeneralGender.Male)
            { CharacterId = "character:" + key, VariantId = "classic", RulesetId = "sanguosha-ol" });

        void RegisterBundle(string bundle, params (string Id, SkillTag Tags)[] tagOverrides)
        {
            EmbeddedSkillProgramCatalog.RegisterBundle(builder, bundle, definition =>
            {
                var program = definition.Program!;
                return definition with
                {
                    ExecutionForms = (program.Triggers.Count > 0 ? SkillExecutionForm.Trigger : SkillExecutionForm.None) |
                        (program.Modifiers.Count + program.CardPolicies.Count + program.CardIdentities.Count > 0
                            ? SkillExecutionForm.State : SkillExecutionForm.None),
                    ActionForms = program.Activations.Count > 0 || program.ViewAs.Any(rule => rule.ForPlay) ||
                        program.CardPolicies.Any(policy => policy.Kind == SkillProgramCardPolicyKind.ForeignPublicPileSlash)
                        ? SkillActionForm.Active : SkillActionForm.None
                };
            }, tagOverrides.ToDictionary(skill => skill.Id, skill => skill.Tags, StringComparer.Ordinal));
        }
    }
}
