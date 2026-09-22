using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class ClassicCardActionSkillPrograms
{
    private const string BundleResourceName = "classic-card-action-skills";

    internal static ContentSkillDefinition Definition(string id)
    {
        var definition = EmbeddedSkillProgramCatalog.Definition(BundleResourceName, id);
        return id switch
        {
            "classic:longyin" => definition with
            {
                LegacyKind = SkillKind.Longyin,
                ExecutionForms = SkillExecutionForm.Trigger
            },
            "classic:juzhan" => definition with
            {
                Tags = SkillTag.Conversion,
                ExecutionForms = SkillExecutionForm.Trigger
            },
            "classic:xianzhen" => definition with
            {
                LegacyKind = SkillKind.Xianzhen,
                ExecutionForms = SkillExecutionForm.State,
                ActionForms = SkillActionForm.Active
            },
            _ => throw new InvalidOperationException(
                $"Unknown classic card-action skill program '{id}'.")
        };
    }
}
