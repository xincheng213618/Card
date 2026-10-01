namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed record ProgramDamageTransferFollowup(
        string SkillId, int OwnerSeat, int TargetSeat, bool DrawLostHp);

    private void RedirectProgramCurrentDamage(ProgramSkillFrame frame, string sourceBind,
        bool drawLostHpAfterDamage)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.WindowContext is not
            { Window: SkillProgramTriggerWindow.BeforeDamageApplied, ParentFrameId: var parentId } ||
            _resolutionStack.Count < 2 ||
            _resolutionStack[^2] is not BeforeDamageProgramWindowFrame window ||
            window.Id != parentId || window.Prevented || window.RedirectedTargetSeat is not null ||
            frame.SelectedTargetSeats.Count != 1)
            throw new InvalidOperationException("Damage redirection lost its selected recipient or parent window.");

        var attack = CurrentDamageAttempt ??
            throw new InvalidOperationException("Damage redirection has no pending damage occurrence.");
        var recipientSeat = frame.SelectedTargetSeats[0];
        if (!IsValidPlayerSeat(recipientSeat) || !_players[recipientSeat].IsAlive ||
            recipientSeat == window.TargetSeat || attack.SourceSeat != window.SourceSeat ||
            attack.TargetSeat != window.TargetSeat || attack.DamageRedirected)
            throw new InvalidOperationException("The damage recipient or frozen participants changed.");
        var cost = GetProgramCardSet(active, sourceBind);
        if (cost.CardIds.Count != 1)
            throw new InvalidOperationException("Damage redirection requires exactly one bound cost card.");

        attack.RedirectFinalizedDamageTarget(recipientSeat,
            new ProgramDamageTransferFollowup(active.SkillId, active.OwnerSeat,
                recipientSeat, drawLostHpAfterDamage));
        ReplaceRuntimeFrame(_resolutionStack[^2].Id, window with { RedirectedTargetSeat = recipientSeat });
        AdvanceEventRulesAndQueueFact(new ProgramDamageTransferredEvent(
            attack.ResolutionId, active.SkillId, active.OwnerSeat, attack.SourceSeat,
            recipientSeat, cost.CardIds[0], window.Amount, window.Nature));
        AddLog("SkillTriggered",
            $"{_players[active.OwnerSeat].Name} 转移 {window.Amount} 点伤害给 {_players[recipientSeat].Name}。",
            active.OwnerSeat, recipientSeat);
    }
}
