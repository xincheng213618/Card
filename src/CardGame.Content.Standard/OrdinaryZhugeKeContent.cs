using CardGame.Core;
namespace CardGame.Content.Standard;

internal static class OrdinaryZhugeKeContent
{
    internal static void Register(IContentRegistryBuilder builder)
    {
        EmbeddedSkillProgramCatalog.RegisterBundle(builder,"ordinary-zhuge-ke",d=>d.Id=="ol:aocai"
            ? d with {ExecutionForms=SkillExecutionForm.State}
            : d with {ExecutionForms=SkillExecutionForm.None,ActionForms=SkillActionForm.Active});
        // The current API has no gender field; Male is the biography inference
        // “诸葛瑾长子”, not its placeholder initial_hp:0.
        builder.AddGeneral(new("ol:zhuge-ke","诸葛恪","ol-zhuge-ke","ol:aocai","wu",3,["ol:duwu"],GeneralGender.Male)
        {CharacterId="character:zhuge-ke",VariantId="ordinary",RulesetId="sanguosha-ol"});
    }
}
