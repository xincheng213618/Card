using System.Text.Json.Serialization;

namespace CardGame.Core;

/// <summary>The payment of one real damage segment, before any damage children run.</summary>
public sealed record DamageArmorPaymentReceipt(long DamageFrameId, long AttackFrameId,
    int SourceSeat, int TargetSeat, int DamageAmount, DamageNature Nature, bool SourceLess,
    int ArmorBefore, int ArmorAbsorbed, int ArmorAfter, int HpBefore, int HpLossAmount,
    int UnclampedHpAfter, int HpAfter)
{
    public int ActualHpLost => Math.Max(0, HpBefore - HpAfter);
}

public sealed partial record DamageFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DamageArmorPaymentReceipt? ArmorPayment { get; init; }
}

public sealed partial class GameEngine
{
    private DamageArmorPaymentReceipt? PrepareDamageArmorPayment(long frameId, IDamageAttempt attack)
    {
        var target = _players[attack.TargetSeat];
        if (target.Armor == 0) return null;
        if (target.Armor < 0 || _resolutionStack.LastOrDefault() is not DamageFrame damage ||
            damage.Id != frameId || damage.ParentFrameId != attack.ResolutionId ||
            damage.SourceSeat != attack.SourceSeat || damage.TargetSeat != attack.TargetSeat ||
            damage.Amount != attack.DamageAmount || damage.Amount < 0 || !attack.DamageAmountFinalized || damage.ArmorPayment is not null ||
            !IsExactArmorDamageRequest(damage, attack, requireUnpaid: true))
            throw new InvalidOperationException("Armor payment requires the exact unpaid native damage segment.");
        var absorbed = Math.Min(target.Armor, damage.Amount);
        var hpLoss = damage.Amount - absorbed;
        var unclamped = checked(target.Hp - hpLoss);
        var receipt = new DamageArmorPaymentReceipt(damage.Id, damage.ParentFrameId,
            damage.SourceSeat, damage.TargetSeat, damage.Amount, damage.Nature, attack.IsSourceLess,
            target.Armor, absorbed, target.Armor - absorbed, target.Hp, hpLoss, unclamped, Math.Max(0, unclamped));
        ReplaceRuntimeTop(damage with { ArmorPayment = receipt });
        return receipt;
    }

    private IDamageAttempt? DamageArmorPaymentOwner(DamageFrame damage) =>
        _resolutionStack.FirstOrDefault(frame => frame.Id == damage.ParentFrameId) switch
        {
            ProgramSkillFrame { AttackAttempt: not null } program => new ProgramAttackHandle(this, program.Id),
            CardUseFrame { CardAttack: not null } use => new CardAttackHandle(this, use.Id),
            ProgramSkillFrame { CardAttack: not null } program => new CardAttackHandle(this, program.Id),
            JudgmentFrame { CardAttack: not null } judgment => new CardAttackHandle(this, judgment.Id),
            _ => null
        };

    private bool IsExactArmorDamageRequest(DamageFrame damage, IDamageAttempt attack, bool requireUnpaid = false)
    {
        if (ProgramEventHistory<DamageRequestedEvent>().Where(request => request.ResolutionId == damage.Id).ToArray()
                is not [var request] || request.SourceSeat != damage.SourceSeat || request.TargetSeat != damage.TargetSeat ||
            request.Amount != damage.Amount || request.SourceCard != attack.EffectiveCardKind ||
            request.Nature != damage.Nature || request.SourceLess != attack.IsSourceLess)
            return false;
        // DamageWasApplied belongs to the whole attack/use and remains true
        // after the first chained recipient. The new segment's own request,
        // empty payment slot and unpaid event suffix are the payment boundary.
        return !requireUnpaid || !CompleteProgramEventHistory()
            .SkipWhile(fact => fact is not DamageRequestedEvent issued || issued.ResolutionId != damage.Id)
            .Skip(1).Any(fact => fact is DamageAppliedEvent);
    }

    private bool IsValidDamageArmorPayment(DamageFrame damage, bool requireApplied)
    {
        if (damage.ArmorPayment is not { } payment || DamageArmorPaymentOwner(damage) is not { } attack ||
            payment.DamageFrameId != damage.Id || payment.AttackFrameId != damage.ParentFrameId ||
            payment.SourceSeat != damage.SourceSeat || payment.TargetSeat != damage.TargetSeat ||
            payment.DamageAmount != damage.Amount || payment.DamageAmount < 0 || payment.Nature != damage.Nature ||
            payment.ArmorBefore <= 0 || payment.ArmorAbsorbed != Math.Min(payment.ArmorBefore, payment.DamageAmount) ||
            payment.ArmorAfter != payment.ArmorBefore - payment.ArmorAbsorbed ||
            payment.HpLossAmount != payment.DamageAmount - payment.ArmorAbsorbed ||
            (long)payment.UnclampedHpAfter != (long)payment.HpBefore - payment.HpLossAmount ||
            payment.HpAfter != Math.Max(0, payment.UnclampedHpAfter) ||
            attack.SourceSeat != payment.SourceSeat || attack.TargetSeat != payment.TargetSeat ||
            attack.DamageAmount != payment.DamageAmount || !attack.DamageAmountFinalized || attack.IsSourceLess != payment.SourceLess ||
            requireApplied && !attack.DamageWasApplied)
            return false;
        return IsExactArmorDamageRequest(damage, attack);
    }

    private static bool DamageAppliedMatchesArmorPayment(DamageAppliedEvent applied,
        DamageArmorPaymentReceipt payment) =>
        applied.ArmorPayment == payment && applied.SourceSeat == payment.SourceSeat &&
        applied.TargetSeat == payment.TargetSeat && applied.Amount == payment.DamageAmount &&
        applied.RemainingHp == payment.HpAfter && applied.Nature == payment.Nature &&
        applied.SourceLess == payment.SourceLess;

    private bool IsIssuedDamageArmorPayment(DamageFrame damage) =>
        IsValidDamageArmorPayment(damage, requireApplied: true) &&
        ProgramEventHistory<DamageAppliedEvent>().Where(applied => applied.ArmorPayment == damage.ArmorPayment).ToArray()
            is [var applied] && DamageAppliedMatchesArmorPayment(applied, damage.ArmorPayment!);

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 101)]
    private void RequireExactDamageArmorPayment(IGameEvent payload)
    {
        if (payload is not DamageAppliedEvent applied) return;
        if (applied.ArmorPayment is null &&
            _resolutionStack.LastOrDefault() is not DamageFrame { ArmorPayment: not null }) return;
        if (_resolutionStack.LastOrDefault() is not DamageFrame damage || damage.ArmorPayment is not { } payment ||
            !IsValidDamageArmorPayment(damage, requireApplied: true) ||
            !DamageAppliedMatchesArmorPayment(applied, payment) ||
            ProgramEventHistory<DamageAppliedEvent>().Any(previous => previous.ArmorPayment == payment))
            throw new InvalidOperationException("Damage lost its exact once-only Armor and HP payment.");
    }

    private void AssertDamageArmorPayments()
    {
        foreach (var damage in _resolutionStack.OfType<DamageFrame>().Where(frame => frame.ArmorPayment is not null))
            if (!IsIssuedDamageArmorPayment(damage))
                throw new InvalidOperationException("An Armor-paid damage segment lost its issued native payment receipt.");
    }
}
