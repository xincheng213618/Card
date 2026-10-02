namespace CardGame.Core;

public sealed record ProgramRecoveryReceipt(long RecoveryFrameId, int InstructionIndex, int TargetSeat, int Recovered, bool Drawn = false);
public sealed record NamedSlashCancellation(string SkillId, int OwnerSeat, int TargetSeat);
public sealed record NextSlashDamageReservedEvent(int OwnerSeat, string SkillId, int TurnNumber, int TurnSeat,
    long CardUseFrameId, int TargetSeat, int Amount) : IGameEvent;
public sealed record NextSlashDamageConsumedEvent(int OwnerSeat, int TurnNumber, int TurnSeat,
    long CardUseFrameId, int Amount) : IGameEvent;
public sealed record NextSlashDamageExpiredEvent(int OwnerSeat, string SkillId, int TurnNumber, int TurnSeat, int Amount) : IGameEvent;

public sealed partial class GameEngine
{
    private sealed record NamedNextSlashReserve(int OwnerSeat, string SkillId, int TurnNumber, int TurnSeat, int Amount);
    private readonly List<NamedNextSlashReserve> _namedNextSlashReserves = [];
    private bool? _hasNextSlashDamageCapability;
    private bool HasNextSlashDamageCapability => _hasNextSlashDamageCapability ??= _contentRegistry.Skills.Values.Any(skill =>
        skill.Program?.Triggers.Any(trigger => trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.ReserveNextSlashDamage)) == true);

    private sealed partial class ProgramSkillHost : INextSlashProgramHost
    {
        public void RecoverToMaximum(ProgramSkillFrame frame) => engine.RecoverProgramToMaximum(frame);
        public void DrawRecoveryReceipt(ProgramSkillFrame frame) => engine.DrawProgramRecoveryReceipt(frame);
        public void ReserveNextSlashDamage(ProgramSkillFrame frame) => engine.ReserveProgramNextSlashDamage(frame);
    }

    private void RecoverProgramToMaximum(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.WindowContext?.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || active.RecoveryReceipt is not null)
            throw new InvalidOperationException("Maximum recovery requires one fresh preparation receipt.");
        var target = _players[active.OwnerSeat];
        var recovered = target.IsAlive ? Math.Max(0, target.MaxHp - target.Hp) : 0;
        var recoveryId = 0L;
        if (recovered > 0)
        {
            recoveryId = BeginRecovery(active.Id, active.OwnerSeat, active.OwnerSeat, recovered);
            target.Hp += recovered;
            AdvanceEventRulesAndQueueFact(new RecoveryAppliedEvent(active.OwnerSeat, active.OwnerSeat, recovered, target.Hp));
            PopResolutionFrame(recoveryId, ResolutionFrameKind.Recovery);
        }
        ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
        { RecoveryReceipt = new(recoveryId, active.InstructionIndex, active.OwnerSeat, recovered) });
    }

    private void DrawProgramRecoveryReceipt(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var receipt = active.RecoveryReceipt ?? throw new InvalidOperationException("Recovery draw requires the owner's completed recovery receipt.");
        if (receipt.Drawn || receipt.TargetSeat != active.OwnerSeat || receipt.InstructionIndex != active.InstructionIndex - 1)
            throw new InvalidOperationException("Recovery receipt must be drawn once by its next instruction.");
        ReplaceRuntimeTop(active with { RecoveryReceipt = receipt with { Drawn = true } });
        if (receipt.Recovered > 0 && _players[active.OwnerSeat].IsAlive && _winner == Winner.None)
            DrawProgramCards(active.Id, active.OwnerSeat, receipt.Recovered, null, null,
                SkillProgramCardSetVisibility.Private, CardMoveReasons.Draw);
    }

    private void ReserveProgramNextSlashDamage(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.WindowContext is not { Window: SkillProgramTriggerWindow.SlashFullyDodged, CardUse: { } context } ||
            LifecycleCardUse(context.ParentCardUseFrameId) is not { Action: { Type: CardActionType.Use } action } use ||
            action.ActorSeat != active.OwnerSeat || use.CardAttack is not { } attack ||
            ActiveCardAttack is not { } actualAttack || actualAttack.ResolutionId != use.Id ||
            attack.SuccessfulDodgeResponses < attack.RequiredDodgeResponses)
            throw new InvalidOperationException("Next Slash damage requires the actual user's fully dodged Slash owner.");
        if (IsSlashDodgeCancellationPrevented(actualAttack)) return;
        var cancellation = new NamedSlashCancellation(active.SkillId, active.OwnerSeat, attack.TargetSeat);
        if (use.NamedSlashCancellations?.Contains(cancellation) == true) return;
        ReplaceRuntimeFrame(use.Id, use with
        { NamedSlashCancellations = Array.AsReadOnly((use.NamedSlashCancellations ?? []).Append(cancellation).ToArray()) });
        var index = _namedNextSlashReserves.FindIndex(item => item.OwnerSeat == active.OwnerSeat && item.SkillId == active.SkillId &&
            item.TurnNumber == _turnNumber && item.TurnSeat == _currentSeat);
        if (index < 0) _namedNextSlashReserves.Add(new(active.OwnerSeat, active.SkillId, _turnNumber, _currentSeat, 1));
        else _namedNextSlashReserves[index] = _namedNextSlashReserves[index] with { Amount = checked(_namedNextSlashReserves[index].Amount + 1) };
        AdvanceEventRulesAndQueueFact(new NextSlashDamageReservedEvent(active.OwnerSeat, active.SkillId, _turnNumber,
            _currentSeat, use.Id, attack.TargetSeat, 1));
    }

    private void FreezeNextSlashDamage(long frameId)
    {
        var use = LifecycleCardUse(frameId) ?? throw new InvalidOperationException("Next Slash damage lost its card owner.");
        if (use.NextSlashDamage is not null || use.Action is not { Type: CardActionType.Use } action || !IsSlashCard(action.EffectiveKind)) return;
        var matching = _namedNextSlashReserves.Where(item => item.OwnerSeat == action.ActorSeat && item.TurnNumber == _turnNumber && item.TurnSeat == _currentSeat).ToArray();
        var amount = matching.Sum(item => item.Amount);
        // Preserve old nullable/canonical output when this registry has never issued this capability.
        if (amount == 0 && !HasNextSlashDamageCapability) return;
        ReplaceRuntimeFrame(frameId, use with { NextSlashDamage = amount });
        if (amount == 0) return;
        foreach (var item in matching) _namedNextSlashReserves.Remove(item);
        AdvanceEventRulesAndQueueFact(new NextSlashDamageConsumedEvent(action.ActorSeat, _turnNumber, _currentSeat, frameId, amount));
    }

    private int FrozenNextSlashDamage(IDamageAttempt attack) => !attack.IsChainPropagation &&
        attack.EffectiveCardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash
            ? LifecycleCardUse(attack.ResolutionId)?.NextSlashDamage ?? 0 : 0;

    private void ExpireNextSlashDamage(int turnNumber, int turnSeat)
    {
        foreach (var item in _namedNextSlashReserves.Where(item => item.TurnNumber == turnNumber && item.TurnSeat == turnSeat).ToArray())
        {
            _namedNextSlashReserves.Remove(item);
            AdvanceEventRulesAndQueueFact(new NextSlashDamageExpiredEvent(item.OwnerSeat, item.SkillId, turnNumber, turnSeat, item.Amount));
        }
    }
}
