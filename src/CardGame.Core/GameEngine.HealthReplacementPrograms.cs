namespace CardGame.Core;

public sealed record DamageReplacedWithHpLossEvent(long ParentResolutionId, int PolicyOwnerSeat,
    int TargetSeat, int Amount, int RemainingHp) : IGameEvent;

public sealed partial class GameEngine
{
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 160)]
    private void ObserveProgramHealthChange(IGameEvent payload)
    {
        if (payload is DamageAppliedEvent paidDamage)
            ObserveDongBaiHpDecreased(paidDamage.TargetSeat,
                paidDamage.ArmorPayment?.HpBefore ?? _players[paidDamage.TargetSeat].Hp + paidDamage.Amount,
                paidDamage.ArmorPayment?.HpAfter ?? _players[paidDamage.TargetSeat].Hp);
        else if (payload is MaximumHpChangedEvent { HpBeforeClamp: { } clampBefore } clamp)
            ObserveDongBaiHpDecreased(clamp.PlayerSeat,clampBefore,_players[clamp.PlayerSeat].Hp);
        var changed = payload switch
        {
            DamageAppliedEvent damage => (damage.TargetSeat, HpChangeKind.Damage, damage.ArmorPayment?.ActualHpLost ?? damage.Amount),
            MaximumHpChangedEvent maximum => (maximum.PlayerSeat, HpChangeKind.MaximumHp, Math.Abs(maximum.Delta)),
            _ => (-1, HpChangeKind.Damage, 0)
        };
        if (!_setupComplete || changed.Item1 < 0 || changed.Item3 <= 0 || _winner != Winner.None ||
            (CollectProgramTriggerCandidates(_players[changed.Item1], SkillProgramTriggerWindow.AfterHealthChanged).Count == 0 &&
             !HasQimeiHealthSubject(changed.Item1) && !HasWenyiHealthSubject(changed.Item1))) return;
        var owner = _players[changed.Item1];
        var healthOwner = GrantedEntityTrailerHealthOwner(payload) ?? _resolutionStack.LastOrDefault()?.Id;
        var before = payload is DamageAppliedEvent damagePaid ? damagePaid.ArmorPayment?.HpBefore ?? owner.Hp + changed.Item3 :
            payload is MaximumHpChangedEvent { HpBeforeClamp: { } beforeClamp } ? beforeClamp : owner.Hp;
        var after = payload is DamageAppliedEvent { ArmorPayment: { } payment } ? payment.HpAfter : owner.Hp;
        var change = new HpChangeContext(++_resolutionSequence, healthOwner, null, owner.Seat,
            changed.Item2, changed.Item3, before, after);
        _pendingHpChanges.Add(change);
        CaptureQimeiHpQualification(change);
        CaptureMouSunJianOlHp(change);
    }

    private bool ApplyAttackAsHpLoss(IDamageAttempt attack)
    {
        var target = _players[attack.TargetSeat];
        var amount = CalculateCardDamageLayer(attack);
        if (TryBeginIdentityAppearanceForDamage(attack, amount)) return true;
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
