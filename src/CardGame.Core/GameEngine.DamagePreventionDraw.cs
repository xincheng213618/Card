namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome PreventDamageAndDrawMultiple(ProgramSkillFrame frame, int multiplier)
    {
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.BeforeDamageApplied, TargetSeat: { } target } context ||
            target != frame.OwnerSeat || _resolutionStack.Count < 2 ||
            _resolutionStack[^2] is not BeforeDamageProgramWindowFrame parent || parent.Id != context.ParentFrameId ||
            parent.TargetSeat != target || parent.Prevented || parent.Amount <= 0 || context.Amount != parent.Amount)
            throw new InvalidOperationException("Prevention draw requires the exact original own before-damage parent amount.");
        var drawCount = checked(parent.Amount * multiplier);
        var beforeSequence = _cardMovements.Count == 0 ? 0L : _cardMovements[^1].Sequence;
        // The one atomic instruction commits prevention before drawing. A draw
        // observer returns at the next instruction, never executing this twice.
        PreventProgramCurrentDamage(frame);
        DrawProgramCards(frame.Id, frame.OwnerSeat, drawCount, null, null, SkillProgramCardSetVisibility.Private,
            new CardMoveReason($"skill-program.{frame.SkillId}.prevented-damage-draw"));
        var afterSequence = _cardMovements.Count == 0 ? beforeSequence : _cardMovements[^1].Sequence;
        var actualCount = _cardMovements.Count(m => m.Sequence > beforeSequence && m.Sequence <= afterSequence &&
            m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(frame.OwnerSeat) &&
            m.Reason.Value == $"skill-program.{frame.SkillId}.prevented-damage-draw");
        var current = GetActiveProgramFrame(frame.Id);
        ReplaceRuntimeTop(current with { PreventionDrawReceipt = new(current.InstructionIndex - 1, parent.Id,
            parent.SourceSeat, parent.TargetSeat, parent.Amount, multiplier, beforeSequence, afterSequence, actualCount) });
        return AwaitProgramBoundCardMovements(frame.Id, frame.OwnerSeat);
    }

    private bool IsPreventionDrawMovement(ProgramSkillFrame frame, SkillProgramEffect? paidEffect,
        ProgramMovementContinuation movement) =>
        paidEffect?.Op == SkillProgramEffectOp.PreventCurrentDamageAndDrawMultiple &&
        movement.SubjectSeat == frame.OwnerSeat && movement.BeforeCount == 0 && movement.CoverageResultBind is null &&
        frame.WindowContext is { Window: SkillProgramTriggerWindow.BeforeDamageApplied, Amount: > 0 } context &&
        context.TargetSeat == frame.OwnerSeat &&
        _resolutionStack.OfType<BeforeDamageProgramWindowFrame>().Any(parent =>
            parent.Id == context.ParentFrameId && IsValidPreventionDrawReceipt(frame, parent));

    private sealed partial class ProgramSkillHost : IDamagePreventionDrawProgramHost
    {
        public SkillProgramStepOutcome PreventDamageAndDrawMultiple(ProgramSkillFrame frame, int multiplier) =>
            engine.PreventDamageAndDrawMultiple(frame, multiplier);
    }
}
