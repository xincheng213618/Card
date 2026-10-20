namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_ProgramChoices
{
    internal static int ResolveProgramEffectTarget(ProgramSkillFrame frame, SkillProgramEffectTarget target) => target switch
    {
        SkillProgramEffectTarget.Owner => frame.OwnerSeat,
        SkillProgramEffectTarget.Activator => frame.ActivatingSeat ?? frame.OwnerSeat,
        SkillProgramEffectTarget.SelectedTarget => frame.SelectedTargetSeats.Single(),
        SkillProgramEffectTarget.Actor => frame.WindowContext?.CardUse?.ActorSeat ??
            throw new InvalidOperationException("A program option lost its card-action actor."),
        _ => throw new InvalidOperationException("Unsupported program option chooser.")
    };
}
