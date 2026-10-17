namespace CardGame.Core;

public sealed partial class GameEngine
{
    private YintianDamageReceipt? YintianDamageState(long id) =>
        _resolutionStack.SingleOrDefault(frame => frame.Id == id) switch
        {
            CardUseFrame frame => frame.YintianDamage,
            ProgramSkillFrame frame => frame.YintianDamage,
            JudgmentFrame frame => frame.YintianDamage,
            _ => null
        };
    private void SetYintianDamageState(long id, YintianDamageReceipt? receipt)
    {
        var frame = _resolutionStack.Single(f => f.Id == id);
        ReplaceRuntimeFrame(id, frame switch
        {
            CardUseFrame card => card with { YintianDamage = receipt },
            ProgramSkillFrame program => program with { YintianDamage = receipt },
            JudgmentFrame judgment => judgment with { YintianDamage = receipt },
            _ => throw new InvalidOperationException("Yintian requires its exact native damage owner.")
        });
    }
    private int YintianDamageCursor(IDamageAttempt attack) => !attack.IsChainPropagation ? -1 :
        attack is ProgramAttackHandle ? GetProgramAttackState(attack.ResolutionId).ChainedTargetIndex :
        GetCardAttackState(attack.ResolutionId).ChainedTargetIndex;
    private bool YintianGrantConsumed(long grantId) =>
        ProgramEventHistory<YintianChargeConsumedEvent>().Any(e => e.GrantId == grantId);

    private int YintianDamageBonus(IDamageAttempt attack)
    {
        if (!HasYintianCapability || attack.IsSourceLess || !IsValidPlayerSeat(attack.SourceSeat) || attack.DamageAmount <= 0) return 0;
        var cursor = YintianDamageCursor(attack);
        if (YintianDamageState(attack.ResolutionId) is { } previous)
        {
            if (previous.SourceSeat == attack.SourceSeat && previous.ChainCursor == cursor &&
                (previous.TransferredTargetSeat ?? previous.TargetSeat) == attack.TargetSeat && !YintianGrantConsumed(previous.GrantId)) return 1;
            // A new recipient/effect is a new native attempt. An unconsumed
            // charge remains available after a prevented previous attempt.
            SetYintianDamageState(attack.ResolutionId, null);
        }
        var charge = AvailableYintianCharge(attack.SourceSeat);
        if (charge is null || _resolutionStack.Any(frame => YintianDamageState(frame.Id) is { } r &&
                r.GrantId == charge.GrantId && !YintianGrantConsumed(r.GrantId))) return 0;
        var receipt = new YintianDamageReceipt(++_resolutionSequence, charge.GrantId, attack.ResolutionId,
            attack.SourceSeat, attack.TargetSeat, cursor, attack.DamageAmount);
        SetYintianDamageState(attack.ResolutionId, receipt);
        AdvanceEventRulesAndQueueFact(new YintianDamageIssuedEvent(receipt.InvoiceId, receipt.GrantId,
            receipt.AttackId, receipt.SourceSeat, receipt.TargetSeat, receipt.ChainCursor, receipt.InheritedAmount));
        return 1;
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 28113)]
    private void ObserveYintianNativeDamage(IGameEvent payload)
    {
        if (!HasYintianCapability) return;
        if (payload is ExtraCardEffectStartedEvent extra && LifecycleCardUse(extra.CardUseFrameId) is
            { ExtraCardEffect.WholeGroupRepeated: true, Action: { } action, YintianDamage: not null } restarted &&
            action.ActionId == extra.ActionId && _resolutionStack.LastOrDefault()?.Id == restarted.Id)
            SetYintianDamageState(restarted.Id, null);
        if (payload is ProgramDamageTransferredEvent transfer && CurrentDamageAttempt is { IsSourceLess: false } moved &&
            YintianDamageState(moved.ResolutionId) is { TransferredTargetSeat: null } original &&
            _resolutionStack.Count >= 2 && _resolutionStack[^1] is ProgramSkillFrame program &&
            _resolutionStack[^2] is BeforeDamageProgramWindowFrame window &&
            GetPausedPrivateOfferEffect(program) is { Op: SkillProgramEffectOp.RedirectCurrentDamage } &&
            program.WindowContext?.ParentFrameId == window.Id && transfer.ResolutionId == moved.ResolutionId &&
            original.AttackId == moved.ResolutionId && original.SourceSeat == moved.SourceSeat &&
            (window.ContinuationAttackResolutionId ?? window.ParentFrameId) == moved.ResolutionId &&
            original.TargetSeat == window.TargetSeat && original.ChainCursor == YintianDamageCursor(moved) &&
            window.RedirectedTargetSeat == moved.TargetSeat && transfer.TargetSeat == moved.TargetSeat &&
            transfer.SourceSeat == original.SourceSeat && transfer.DamageAmount == window.Amount &&
            transfer.SkillId == program.SkillId && transfer.OwnerSeat == program.OwnerSeat && moved.DamageRedirected)
        {
            SetYintianDamageState(moved.ResolutionId, original with
            { TransferredTargetSeat = moved.TargetSeat, TransferWindowId = window.Id });
            AdvanceEventRulesAndQueueFact(new YintianDamageTransferredEvent(original.InvoiceId, original.GrantId,
                original.AttackId, original.SourceSeat, original.TargetSeat, moved.TargetSeat,
                original.ChainCursor, window.Id, transfer.DamageAmount));
        }
        if (payload is not DamageAppliedEvent { Amount: > 0, SourceLess: false } applied ||
            _resolutionStack.LastOrDefault() is not DamageFrame damage || CurrentDamageAttempt is not { IsSourceLess: false } attack ||
            damage.ParentFrameId != attack.ResolutionId || damage.SourceSeat != applied.SourceSeat ||
            damage.TargetSeat != applied.TargetSeat || damage.Amount != applied.Amount ||
            attack.SourceSeat != applied.SourceSeat || attack.TargetSeat != applied.TargetSeat ||
            YintianDamageState(attack.ResolutionId) is not { } receipt || receipt.AttackId != attack.ResolutionId ||
            receipt.SourceSeat != applied.SourceSeat || (receipt.TransferredTargetSeat ?? receipt.TargetSeat) != applied.TargetSeat ||
            receipt.ChainCursor != YintianDamageCursor(attack)) return;
        if (!ValidYintianDamageReceipt(receipt) || YintianGrantConsumed(receipt.GrantId))
            throw new InvalidOperationException("Yintian cannot consume an unissued or already consumed damage charge.");
        if (receipt.TransferredTargetSeat is { } target && (receipt.TransferWindowId is not { } windowId || !attack.DamageRedirected ||
            ProgramEventHistory<YintianDamageTransferredEvent>().Count(e => e.InvoiceId == receipt.InvoiceId &&
                e.GrantId == receipt.GrantId && e.AttackId == receipt.AttackId && e.SourceSeat == receipt.SourceSeat &&
                e.OriginalTargetSeat == receipt.TargetSeat && e.TargetSeat == target && e.ChainCursor == receipt.ChainCursor &&
                e.WindowId == windowId && e.Amount == applied.Amount) != 1 ||
            !ProgramEventHistory<ProgramDamageTransferredEvent>().Any(e => e.ResolutionId == receipt.AttackId &&
                e.SourceSeat == receipt.SourceSeat && e.TargetSeat == target && e.DamageAmount == applied.Amount)))
            throw new InvalidOperationException("Yintian's transferred damage lost its exact original-source invoice.");
        AdvanceEventRulesAndQueueFact(new YintianChargeConsumedEvent(receipt.GrantId, receipt.InvoiceId,
            receipt.AttackId, damage.Id, receipt.SourceSeat, applied.TargetSeat, receipt.ChainCursor, applied.Amount));
    }

    private void SetYintianChainDamage(IDamageAttempt attack, int amount)
    {
        if (!SameAttackOwner(attack, CurrentDamageAttempt) || !attack.IsChainPropagation || amount < 0)
            throw new InvalidOperationException("Yintian may only rebase its exact native chained recipient.");
        if (attack is ProgramAttackHandle)
            UpdateProgramAttackState(attack.ResolutionId, state => state with { DamageAmount = amount });
        else UpdateCardAttackState(attack.ResolutionId, state => state! with { DamageAmount = amount });
    }
    private void PrepareYintianChainAdvance(IDamageAttempt attack)
    {
        if (!HasYintianCapability || YintianDamageState(attack.ResolutionId) is not { } r) return;
        var seats = attack is ProgramAttackHandle ? GetProgramAttackState(attack.ResolutionId).ChainedTargetSeats :
            GetCardAttackState(attack.ResolutionId).ChainedTargetSeats;
        var cursor = attack is ProgramAttackHandle ? GetProgramAttackState(attack.ResolutionId).ChainedTargetIndex :
            GetCardAttackState(attack.ResolutionId).ChainedTargetIndex;
        if (seats is null || !seats.Skip(cursor).Any(seat => _players[seat].IsAlive)) return;
        // The first recipient's finalized amount is the propagation base. A
        // newly obtained charge used on a later recipient is local to it.
        if (attack.IsChainPropagation && r.ChainCursor == YintianDamageCursor(attack) &&
            (r.TransferredTargetSeat ?? r.TargetSeat) == attack.TargetSeat)
        {
            SetYintianChainDamage(attack, r.InheritedAmount);
            AdvanceEventRulesAndQueueFact(new YintianDamageRebasedEvent(r.InvoiceId, r.AttackId,
                attack.TargetSeat, r.ChainCursor, r.InheritedAmount));
        }
        SetYintianDamageState(attack.ResolutionId, null);
    }
    private void ApplyYintianChainedDamageBonus(IDamageAttempt attack)
    {
        if (!HasYintianCapability || !attack.IsChainPropagation || attack.IsSourceLess) return;
        // Formal armor reruns the ordinary finalizer. Legacy propagation has
        // already finalized and needs only this recipient's new charge.
        if (attack.DamageAmountFinalized)
            SetYintianChainDamage(attack, checked(attack.DamageAmount + YintianDamageBonus(attack)));
    }

    // A program frame can execute another damage instruction after its typed
    // attack returns. The completed attempt must not reserve an unspent charge
    // or lend its invoice to that later instruction on the same frame.
    private ProgramSkillFrame CaptureYintianProgramDamageCompleted(ProgramSkillFrame frame, IDamageAttempt attack)
    {
        if (frame.YintianDamage is not { } receipt) return frame;
        if (attack is not ProgramAttackHandle || frame.Id != attack.ResolutionId || frame.AttackAttempt is null ||
            _resolutionStack.LastOrDefault()?.Id != frame.Id || !SameAttackOwner(attack, CurrentDamageAttempt) ||
            receipt.AttackId != frame.Id || receipt.SourceSeat != attack.SourceSeat ||
            (receipt.TransferredTargetSeat ?? receipt.TargetSeat) != attack.TargetSeat ||
            receipt.ChainCursor != YintianDamageCursor(attack) || !ValidYintianDamageReceipt(receipt))
            throw new InvalidOperationException("Yintian's program damage return lost its exact completed attempt.");
        return frame with { YintianDamage = null };
    }

    private bool ValidYintianDamageReceipt(YintianDamageReceipt r) => r.InvoiceId > 0 && r.AttackId > 0 &&
        r.InheritedAmount > 0 && r.ChainCursor >= -1 && IsValidPlayerSeat(r.SourceSeat) && IsValidPlayerSeat(r.TargetSeat) &&
        ProgramEventHistory<YintianDamageIssuedEvent>().Count(e => e.InvoiceId == r.InvoiceId && e.GrantId == r.GrantId &&
            e.AttackId == r.AttackId && e.SourceSeat == r.SourceSeat && e.TargetSeat == r.TargetSeat &&
            e.ChainCursor == r.ChainCursor && e.InheritedAmount == r.InheritedAmount) == 1 &&
        ProgramEventHistory<YintianChargeIssuedEvent>().Where(g => g.GrantId == r.GrantId).ToArray() is [var grant] &&
        grant.Source.OwnerSeat == r.SourceSeat && ValidYintianCharge(grant) &&
        (r.TransferredTargetSeat is { } target ? IsValidPlayerSeat(target) && target != r.TargetSeat && r.TransferWindowId is { } id &&
            ProgramEventHistory<YintianDamageTransferredEvent>().Count(e => e.InvoiceId == r.InvoiceId && e.AttackId == r.AttackId &&
                e.GrantId == r.GrantId && e.SourceSeat == r.SourceSeat && e.OriginalTargetSeat == r.TargetSeat && e.TargetSeat == target &&
                e.ChainCursor == r.ChainCursor && e.WindowId == id) == 1 : r.TransferWindowId is null);

    private void AssertYintianDamageStates()
    {
        if (!HasYintianCapability) return;
        foreach (var first in ProgramEventHistory<YintianFirstRecoveryEvent>())
            if (first.Amount <= 0 || first.HpAfter - first.HpBefore != first.Amount ||
                ProgramEventHistory<YintianFirstRecoveryEvent>().Count(e => e.ActualTurn == first.ActualTurn && e.TurnOwner == first.TurnOwner) != 1)
                throw new InvalidOperationException("Yintian's global first recovery must be one actual positive event per turn.");
        foreach (var owner in ProgramEventHistory<YintianChargeIssuedEvent>().Select(e => e.Source.OwnerSeat).Distinct())
            _ = AvailableYintianCharge(owner);
        foreach (var frame in _resolutionStack)
        {
            if (YintianDamageState(frame.Id) is not { } r) continue;
            if (r.AttackId != frame.Id || !ValidYintianDamageReceipt(r) ||
                ProgramEventHistory<YintianChargeConsumedEvent>().Count(e => e.GrantId == r.GrantId) > 1)
                throw new InvalidOperationException("Yintian lost its native damage-owner receipt or consumed a charge twice.");
        }
    }
}
