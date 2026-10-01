namespace CardGame.Core;

public sealed record DamageReplacedWithHpLossEvent(long ParentResolutionId, int PolicyOwnerSeat,
    int TargetSeat, int Amount, int RemainingHp) : IGameEvent;

public sealed partial class GameEngine
{
    private void ObserveProgramHealthChange(IGameEvent payload)
    {
        var changed = payload switch
        {
            DamageAppliedEvent damage => (damage.TargetSeat, HpChangeKind.Damage, damage.Amount),
            MaximumHpChangedEvent maximum => (maximum.PlayerSeat, HpChangeKind.MaximumHp, Math.Abs(maximum.Delta)),
            _ => (-1, HpChangeKind.Damage, 0)
        };
        if (!_setupComplete || changed.Item1 < 0 || changed.Item3 <= 0 || _winner != Winner.None ||
            CollectProgramTriggerCandidates(_players[changed.Item1], SkillProgramTriggerWindow.AfterHealthChanged).Count == 0) return;
        var owner = _players[changed.Item1];
        _pendingHpChanges.Add(new(++_resolutionSequence, _resolutionStack.LastOrDefault()?.Id, null, owner.Seat,
            changed.Item2, changed.Item3, payload is DamageAppliedEvent ? owner.Hp + changed.Item3 : owner.Hp, owner.Hp));
    }

    private bool ApplyAttackAsHpLoss(IDamageAttempt attack)
    {
        var target = _players[attack.TargetSeat];
        var amount = attack.DamageAmount;
        var before = target.Hp;
        target.Hp -= amount;
        RecordHpChange(attack.ResolutionId, null, target.Seat, before, target.Hp, HpChangeKind.Loss);
        AdvanceEventRulesAndQueueFact(new DamageReplacedWithHpLossEvent(attack.ResolutionId, attack.SourceSeat, target.Seat, amount, target.Hp));
        AddLog("HpLost", $"{target.Name} 失去 {amount} 点体力，剩余 {target.Hp} 点体力。", target.Seat);
        if (target.Hp > 0) return false;
        if (ActiveDying is not null) throw new InvalidOperationException("An HP-loss attack cannot nest another dying victim.");
        var responders = Array.AsReadOnly(BuildDyingResponderSeats(target.Seat).ToArray());
        var id = ++_resolutionSequence;
        PushRuntimeFrame(new DyingFrame(id, attack.ResolutionId, target.Seat, null, responders, 0,
            DyingContinuationKind.AttackHpLoss));
        AdvanceEventRulesAndQueueFact(new PlayerDyingEvent(id, target.Seat, null));
        _status = EngineStatus.Running;
        if (!TryBeginMandatorySelfDyingProgram(ActiveDying!) && !TryBeginDyingEntryProgramWindow(ActiveDying!)) ExposeHumanDyingPrompt();
        return true;
    }
}
