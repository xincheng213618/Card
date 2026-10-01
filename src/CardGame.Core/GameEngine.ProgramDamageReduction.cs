namespace CardGame.Core;

public sealed record ProgramDamageReducedEvent(long FrameId, string SkillId, int OwnerSeat,
    int SourceSeat, int TargetSeat, int AmountBefore, int AmountAfter) : IGameEvent;

public sealed partial class GameEngine
{
    private void ReduceProgramCurrentDamage(ProgramSkillFrame program, int amount)
    {
        if (program.WindowContext is not { Window: SkillProgramTriggerWindow.BeforeDamageApplied } context ||
            _resolutionStack.Count < 2 || _resolutionStack[^2] is not BeforeDamageProgramWindowFrame window ||
            window.Id != context.ParentFrameId || window.Prevented ||
            CurrentDamageAttempt is not { } attack || attack.SourceSeat != window.SourceSeat ||
            attack.TargetSeat != window.TargetSeat || amount <= 0)
            throw new InvalidOperationException("Damage reduction lost its active damage window.");
        var reduced = Math.Min(amount, window.Amount);
        attack.ReduceFinalizedDamageAmount(reduced);
        ReplaceRuntimeFrame(_resolutionStack[^2].Id, window with { Amount = window.Amount - reduced, Prevented = window.Amount == reduced });
        AdvanceEventRulesAndQueueFact(new ProgramDamageReducedEvent(program.Id, program.SkillId, program.OwnerSeat,
            window.SourceSeat, window.TargetSeat, window.Amount, window.Amount - reduced));
    }
}
