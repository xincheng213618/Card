using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record WenyiDamageHealthReceipt(long DamageFrameId, long AttackFrameId, long HealthChangeId,
    int SourceSeat, int TargetSeat, int Amount, DamageNature Nature, bool SourceLess);
public sealed partial record DamageFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WenyiDamageHealthReceipt? WenyiHealth { get; init; }
}
public sealed record WenyiDamageHealthStartedEvent(WenyiDamageHealthReceipt Receipt) : IGameEvent;
public sealed record WenyiDamageHealthReturnedEvent(WenyiDamageHealthReceipt Receipt) : IGameEvent;

public sealed partial class GameEngine
{
    private bool TryBeginWenyiDamageHealth(IDamageAttempt attack, long damageFrameId)
    {
        if (!HasWenyiHealthSubject(attack.TargetSeat) ||
            _resolutionStack.LastOrDefault() is not DamageFrame damage || damage.Id != damageFrameId ||
            damage.ParentFrameId != attack.ResolutionId || damage.WenyiHealth is not null) return false;
        var change = _pendingHpChanges.FirstOrDefault(c => c.ParentFrameId == damage.Id &&
            c.TargetSeat == damage.TargetSeat && c.Kind == HpChangeKind.Damage && c.HpBefore != 1 && c.HpAfter == 1 &&
            CollectWenyiHpCandidates(c).Any());
        if (change is null) return false;
        var receipt = new WenyiDamageHealthReceipt(damage.Id, attack.ResolutionId, change.Id,
            damage.SourceSeat, damage.TargetSeat, damage.Amount, damage.Nature, attack.IsSourceLess);
        ReplaceRuntimeTop(damage with { WenyiHealth = receipt });
        AdvanceEventRulesAndQueueFact(new WenyiDamageHealthStartedEvent(receipt));
        if (!TryBeginHpChangedProgramWindow(damage.Id, PostEventContinuation.WenyiDamageHealth))
            throw new InvalidOperationException("Wenyi lost its committed damage health-change window before native damage observers.");
        return true;
    }

    private bool IsWenyiDamageHealthReturn(DamageFrame damage, HpChangedTriggerWindowFrame health) =>
        damage.WenyiHealth is { } r && r.DamageFrameId == damage.Id && r.AttackFrameId == damage.ParentFrameId &&
        r.SourceSeat == damage.SourceSeat && r.TargetSeat == damage.TargetSeat && r.Amount == damage.Amount &&
        r.Nature == damage.Nature && health.Continuation == PostEventContinuation.WenyiDamageHealth &&
        health.ResumeFrameId == damage.Id && health.Change.ParentFrameId == damage.Id &&
        health.Id == health.Change.Id && health.Id == r.HealthChangeId && health.Change.TargetSeat == r.TargetSeat &&
        health.Change.Kind == HpChangeKind.Damage &&
        health.Change.Amount == (damage.ArmorPayment?.ActualHpLost ?? r.Amount) &&
        health.Change.HpBefore == (damage.ArmorPayment?.HpBefore ?? r.Amount + 1) && health.Change.HpAfter == 1 &&
        (damage.ArmorPayment is null || damage.ArmorPayment.HpAfter == 1 && IsIssuedDamageArmorPayment(damage)) &&
        ProgramEventHistory<WenyiDamageHealthStartedEvent>().Count(e => e.Receipt == r) == 1 &&
        !ProgramEventHistory<WenyiDamageHealthReturnedEvent>().Any(e => e.Receipt == r);

    private bool HasWenyiDamageHealthContinuation(long attackFrameId)
    {
        for (var i = 0; i + 1 < _resolutionStack.Count; i++)
            if (_resolutionStack[i] is DamageFrame { WenyiHealth: not null } damage &&
                damage.ParentFrameId == attackFrameId && _resolutionStack[i + 1] is HpChangedTriggerWindowFrame health &&
                IsWenyiDamageHealthReturn(damage, health)) return true;
        return false;
    }

    private IReadOnlyList<Card> SeparateWenyiDamageHealthChildProcessing(
        CardAttackHandle attack, IReadOnlyList<Card> processing)
    {
        if (!HasHuanShuJunCapability || processing.Count == 0) return processing;
        for (var i = 0; i + 3 < _resolutionStack.Count; i++)
        {
            // Only the suspended original attack relinquishes its child's costs.
            // A nested attack must retain the complete ancestor set for its own
            // specialized, paid native-use matcher to verify.
            if (_resolutionStack[i] is not DamageFrame { WenyiHealth: not null } damage ||
                damage.ParentFrameId != attack.ResolutionId ||
                _resolutionStack[i + 1] is not HpChangedTriggerWindowFrame health ||
                !IsWenyiDamageHealthReturn(damage, health) ||
                _resolutionStack[i + 2] is not ProgramSkillFrame
                { HuanShuJun: { Op: SkillProgramEffectOp.ResolveWenyiHealthOneAid,
                    Paid: true, Stage: HuanShuJunStage.NativeUse, NativeReturned: false, Native: { } origin } receipt } program ||
                _resolutionStack[i + 3] is not CardUseFrame use ||
                use.Id != origin.UseId || origin.ProgramFrameId != program.Id ||
                !HuanShuJunPaidOrPreparing(program) || !WenyiHpRoot(program) ||
                !IsIssuedWenyiUse(use) || HuanShuJunObserverRoot()?.Id != program.Id ||
                use.Action is not { Type: CardActionType.Use } action ||
                action.ActionId != origin.ActionId || action.ActorSeat != origin.ActorSeat ||
                action.ProviderSeat != origin.ProviderSeat ||
                !action.PhysicalCards.Select(c => c.CardId).SequenceEqual(receipt.Materials.Select(c => c.CardId)) ||
                !action.ConversionChain.SequenceEqual(receipt.Sources)) continue;

            var reason = origin.Kind == CardKind.Peach ? CardMoveReasons.Use : CardMoveReasons.EquipmentUse;
            var childIds = new HashSet<int>();
            foreach (var cost in action.PhysicalCards)
            {
                if (_cardZones.GetLocation(cost.CardId) != CardLocation.Processing) continue;
                var payment = _cardMovements.LastOrDefault(m => m.CardId == cost.CardId);
                if (payment is null || payment.Sequence <= receipt.Before || payment.From != cost.From ||
                    payment.To != CardLocation.Processing || payment.Reason != reason ||
                    payment.CardKind != cost.CardKind || attack.PhysicalCards.Any(c => c.Id == cost.CardId))
                    return processing;
                childIds.Add(cost.CardId);
            }
            if (childIds.Count > 0)
                return processing.Where(card => !childIds.Contains(card.Id)).ToArray();
        }
        return processing;
    }

    private void AssertWenyiDamageHealth()
    {
        foreach (var damage in _resolutionStack.OfType<DamageFrame>().Where(d => d.WenyiHealth is not null))
        {
            var index = _resolutionStack.FindIndex(f => f.Id == damage.Id);
            if (index < 0 || index + 1 >= _resolutionStack.Count ||
                _resolutionStack[index + 1] is not HpChangedTriggerWindowFrame health ||
                !IsWenyiDamageHealthReturn(damage, health))
                throw new InvalidOperationException("Wenyi lost the original damage's paid health-change child.");
        }
    }

    private void ReturnWenyiDamageHealth(HpChangedTriggerWindowFrame health)
    {
        if (_resolutionStack.LastOrDefault() is not DamageFrame damage || !IsWenyiDamageHealthReturn(damage, health) ||
            CurrentDamageAttempt is not { } attack || attack.ResolutionId != damage.ParentFrameId ||
            attack.SourceSeat != damage.SourceSeat || attack.TargetSeat != damage.TargetSeat ||
            attack.IsSourceLess != damage.WenyiHealth!.SourceLess)
            throw new InvalidOperationException("Wenyi damage health children returned to a different actual damage attempt.");
        var receipt = damage.WenyiHealth!;
        ReplaceRuntimeTop(damage with { WenyiHealth = null });
        AdvanceEventRulesAndQueueFact(new WenyiDamageHealthReturnedEvent(receipt));
        if (_winner == Winner.None && TryBeginDamageTriggerWindow(attack, damage.Id,
                SkillProgramTriggerWindow.DamageAppliedBeforeDying)) return;
        var target = _players[damage.TargetSeat];
        if (_winner == Winner.None && target.IsAlive && target.Hp <= 0)
        { BeginDying(attack, damage.Id, target, _players[damage.SourceSeat]); return; }
        if (_winner == Winner.None && TryBeginDamageTriggerWindow(attack, damage.Id,
                SkillProgramTriggerWindow.AfterDamageApplied)) return;
        AdvanceEventRulesAndQueueFact(new AfterDamageEvent(damage.Id, damage.SourceSeat, damage.TargetSeat,
            damage.Amount, target.Hp, damage.Nature) { SourceLess = receipt.SourceLess });
        PopResolutionFrame(damage.Id, ResolutionFrameKind.Damage);
        CompleteDamageAttack(attack);
    }
}
