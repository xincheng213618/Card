using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class BoundarySunJianContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-sun-jian", "boundary:yinghun"));
        builder.AddSkill(EmbeddedSkillProgramCatalog.Definition("boundary-sun-jian", "boundary:wulie") with
        { Tags = SkillTag.Limited, ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None });
        builder.AddGeneral(new("boundary:sun-jian", "界孙坚", "boundary_sun_jian", "boundary:yinghun", "wu", 5,
            ["boundary:wulie"], GeneralGender.Male)
        {
            CharacterId = "character:sun-jian", VariantId = "boundary", RulesetId = "sanguosha-ol",
            InitialHp = 4
        });
    }
}
