namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void RestoreCompletedUseAttackAfterIssuedChild(ProgramSkillFrame program, ProgramCardTriggerWindowFrame window)
    {
        if (window.Continuation != ProgramCardContinuation.CompletedSlash ||
            program.WindowContext?.CardUse is not { } identity ||
            identity.ParentCardUseFrameId != window.ParentFrameId || identity.CardActionId != window.Action.ActionId)
            return;
        var heji = ProgramEventHistory<WuJingHejiNativeIssuedEvent>().Where(e => e.ProgramFrameId == program.Id &&
            e.IncomingUseId == window.ParentFrameId && e.IncomingActionId == window.Action.ActionId &&
            e.Source.SkillId == program.SkillId && e.Source.SkillInstanceId == program.SkillInstanceId &&
            e.GameplayHash == program.GameplayHash).ToArray();
        var counter = ProgramEventHistory<FulueCounterUseIssuedEvent>().Where(e => e.Invoice.ProgramFrameId == program.Id &&
            e.Invoice.OriginalUseId == window.ParentFrameId && e.Invoice.OriginalActionId == window.Action.ActionId &&
            e.Invoice.Source.SkillId == program.SkillId && e.Invoice.Source.SkillInstanceId == program.SkillInstanceId &&
            e.Invoice.GameplayHash == program.GameplayHash).ToArray();
        long? childId = heji is [var issued] && ProgramEventHistory<WuJingHejiReturnedEvent>()
            .Count(e => e.ProgramFrameId == program.Id && e.UseId == issued.UseId) == 1 ? issued.UseId :
            counter is [var reverse] ? reverse.UseId : null;
        if (childId is not { } id) return;
        if (LifecycleCardUse(id) is not null || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == id) != 1 ||
            LifecycleCardUse(window.ParentFrameId) is not { Action: { } action, CardAttack: not null } original ||
            original.Id != window.AttackOwnerFrameId || action.ActionId != window.Action.ActionId ||
            ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == original.Id) != 1 ||
            ActiveCardAttack is { } active && active.ResolutionId != original.Id)
            throw new InvalidOperationException("An issued completed-card child lost its finished native use or original completed attack.");
        // A native child deactivates the old attack. Only its exact completed
        // parent regains the handle needed to close the original trigger window.
        ActiveCardAttack = new(this, original.Id);
    }
}
