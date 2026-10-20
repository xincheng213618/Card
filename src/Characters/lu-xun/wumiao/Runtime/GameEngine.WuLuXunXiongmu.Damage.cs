namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasWuXiongmuFirstDamageCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveWuLuXunXiongmuFirstDamage);
    private int WuXiongmuChainCursor(IDamageAttempt attack) => !attack.IsChainPropagation ? -1 : attack is ProgramAttackHandle
        ? GetProgramAttackState(attack.ResolutionId).ChainedTargetIndex : GetCardAttackState(attack.ResolutionId).ChainedTargetIndex;
    private XiongmuDamageReceipt? WuXiongmuDamageState(long id) => _resolutionStack.SingleOrDefault(f => f.Id == id) switch
    { CardUseFrame c => c.XiongmuDamage, ProgramSkillFrame p => p.XiongmuDamage, JudgmentFrame j => j.XiongmuDamage, _ => null };
    private void SetWuXiongmuDamageState(long id, XiongmuDamageReceipt? r)
    {
        var f = _resolutionStack.Single(f => f.Id == id);
        ReplaceRuntimeFrame(id, f switch { CardUseFrame c => c with { XiongmuDamage = r },
            ProgramSkillFrame p => p with { XiongmuDamage = r }, JudgmentFrame j => j with { XiongmuDamage = r },
            _ => throw new InvalidOperationException("Xiongmu needs the actual native damage owning frame.") });
    }
    private bool WuXiongmuHasReceivedDamage(int owner) =>
        EventsSinceLastBoundary(e => e is TurnStartedEvent).OfType<DamageAppliedEvent>().Any(e => e.Amount > 0 && e.TargetSeat == owner) ||
        ProgramEventHistory<XiongmuFirstDamageReducedEvent>().Any(e => e.Receipt.ActualTurn == _turnNumber &&
            e.Receipt.TurnOwner == _turnProgression.OwnerSeat && e.Receipt.TargetSeat == owner);
    private bool WuXiongmuFirstDamageEligible(int owner, long windowId) =>
        _setupComplete && _players[owner].IsAlive && _players[owner].HasHpValue && !WuXiongmuHasReceivedDamage(owner) &&
        GetHand(_players[owner]).Count <= _players[owner].Hp &&
        _resolutionStack.SingleOrDefault(f => f.Id == windowId) is BeforeDamageProgramWindowFrame
            { Prevented: false, RedirectedTargetSeat: null, Amount: > 0 } w && w.TargetSeat == owner &&
        CurrentDamageAttempt is { DamageAmountFinalized: true } attack && attack.TargetSeat == owner &&
        (w.ContinuationAttackResolutionId ?? w.ParentFrameId) == attack.ResolutionId && attack.DamageAmount == w.Amount;

    // This memo is global while the capability is present, independent of who
    // currently owns the skill. The ordinary native DamageApplied history is
    // also consulted, so receiving the skill later cannot erase an earlier hit.
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 29001)]
    private void ObserveWuXiongmuNativeDamage(IGameEvent payload)
    {
        if (payload is ProgramDamageTransferredEvent transferred) { TransferWuXiongmuDamage(transferred); return; }
        if (!HasWuXiongmuFirstDamageCapability || !_setupComplete || payload is not DamageAppliedEvent { Amount: > 0 } fact ||
            CurrentDamageAttempt is not { } attack || _resolutionStack.LastOrDefault() is not DamageFrame native ||
            native.ParentFrameId != attack.ResolutionId || native.TargetSeat != fact.TargetSeat || native.Amount != fact.Amount ||
            attack.TargetSeat != fact.TargetSeat || attack.SourceSeat != fact.SourceSeat || attack.IsSourceLess != fact.SourceLess ||
            ProgramEventHistory<XiongmuFirstPositiveDamageEvent>().Any(e => e.ActualTurn == _turnNumber &&
                e.TurnOwner == _turnProgression.OwnerSeat && e.TargetSeat == fact.TargetSeat)) return;
        AdvanceEventRulesAndQueueFact(new XiongmuFirstPositiveDamageEvent(_turnNumber, _turnProgression.OwnerSeat,
            attack.ResolutionId, fact.SourceSeat, fact.TargetSeat, fact.Amount, fact.Nature, fact.SourceLess));
    }
    private SkillProgramStepOutcome ResolveWuXiongmuFirstDamage(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != WuXiongmuSkill || f.TriggerId != "first-damage" || f.ActivationId != f.TriggerId ||
            f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.WindowContext is not { } context ||
            !WuXiongmuNativeCandidate(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash,
                GetProgramTrigger(f).Priority, context.OccurrenceIndex), context) || !WuXiongmuFirstDamageEligible(f.OwnerSeat, context.ParentFrameId) ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            _resolutionStack.Count < 2 || _resolutionStack[^2] is not BeforeDamageProgramWindowFrame window || window.Id != context.ParentFrameId ||
            CurrentDamageAttempt is not { } attack)
            throw new InvalidOperationException("Xiongmu cannot reduce another native damage, repeat its consumed first hit or invent a late-granted first hit.");
        var receipt = new XiongmuDamageReceipt(f.Id, WuXiongmuSource(f), f.GameplayHash, window.Id, attack.ResolutionId,
            f.OwnerSeat, WuXiongmuChainCursor(attack), window.Amount, Math.Max(0, window.Amount - 1), _turnNumber, _turnProgression.OwnerSeat);
        var count = GetHand(_players[f.OwnerSeat]).Count; var hp = _players[f.OwnerSeat].Hp;
        ReduceProgramCurrentDamage(f, 1);
        SetWuXiongmuDamageState(attack.ResolutionId, receipt);
        AdvanceEventRulesAndQueueFact(new XiongmuFirstDamageReducedEvent(receipt, count, hp));
        return SkillProgramStepOutcome.Continue;
    }
    private bool ValidWuXiongmuDamage(XiongmuDamageReceipt r) =>
        r.Source.SkillId == WuXiongmuSkill && r.Source.BindingId == "first-damage" && r.Source.OwnerSeat == r.TargetSeat &&
        r.AmountBefore > 0 && r.AmountAfter == Math.Max(0, r.AmountBefore - 1) && r.AttackId > 0 && r.WindowId > 0 &&
        IsValidPlayerSeat(r.TargetSeat) && IsValidPlayerSeat(r.TurnOwner) && r.ChainCursor >= -1 &&
        _contentRegistry.GetSkill(r.Source.SkillId).Program?.GameplayHash == r.GameplayHash &&
        ProgramEventHistory<XiongmuFirstDamageReducedEvent>().Count(e => e.Receipt.ProgramFrameId == r.ProgramFrameId &&
            e.Receipt == (r with { TransferredTargetSeat = null, TransferWindowId = null }) && e.HandCount <= e.Hp) == 1 &&
        ProgramEventHistory<XiongmuFirstDamageReducedEvent>().Count(e => e.Receipt.ActualTurn == r.ActualTurn &&
            e.Receipt.TurnOwner == r.TurnOwner && e.Receipt.TargetSeat == r.TargetSeat) == 1 &&
        ProgramEventHistory<ProgramDamageReducedEvent>().Count(e => e.FrameId == r.ProgramFrameId && e.SkillId == WuXiongmuSkill &&
            e.OwnerSeat == r.TargetSeat && e.TargetSeat == r.TargetSeat && e.AmountBefore == r.AmountBefore && e.AmountAfter == r.AmountAfter) == 1 &&
        ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == r.ProgramFrameId && e.SkillId == r.Source.SkillId &&
            e.BindingId == r.Source.BindingId && e.SkillInstanceId == r.Source.SkillInstanceId && e.OwnerSeat == r.TargetSeat &&
            e.Window == SkillProgramTriggerWindow.BeforeDamageApplied) == 1;
    private void SetWuXiongmuChainAmount(IDamageAttempt attack, int amount)
    {
        if (!SameAttackOwner(attack, CurrentDamageAttempt) || amount < 0) throw new InvalidOperationException("Xiongmu cannot rebase a foreign native attack.");
        if (attack is ProgramAttackHandle) UpdateProgramAttackState(attack.ResolutionId, s => s with { DamageAmount = amount });
        else UpdateCardAttackState(attack.ResolutionId, s => s! with { DamageAmount = amount });
    }
    private void PrepareWuXiongmuChainAdvance(IDamageAttempt attack)
    {
        if (WuXiongmuDamageState(attack.ResolutionId) is not { } r) return;
        if (!ValidWuXiongmuDamage(r)) throw new InvalidOperationException("Xiongmu lost its exact first-damage invoice before native chain advance.");
        var remaining = attack is ProgramAttackHandle ? GetProgramAttackState(attack.ResolutionId).ChainedTargetSeats : GetCardAttackState(attack.ResolutionId).ChainedTargetSeats;
        var cursor = attack is ProgramAttackHandle ? GetProgramAttackState(attack.ResolutionId).ChainedTargetIndex : GetCardAttackState(attack.ResolutionId).ChainedTargetIndex;
        if (remaining is null || !remaining.Skip(cursor).Any(seat => _players[seat].IsAlive))
        {
            ValidateWuXiongmuCurrentAttempt(attack, r);
            AdvanceEventRulesAndQueueFact(new XiongmuDamageRetiredEvent(r.AttackId, r.WindowId, r.ProgramFrameId, r.TargetSeat, r.ChainCursor));
            SetWuXiongmuDamageState(attack.ResolutionId, null); return;
        }
        // Initial damage establishes the native chain base. A reduction on a
        // later recipient is local and must not leak into the next recipient.
        if (attack.IsChainPropagation && r.ChainCursor == WuXiongmuChainCursor(attack) && (r.TransferredTargetSeat ?? r.TargetSeat) == attack.TargetSeat)
        {
            SetWuXiongmuChainAmount(attack, r.AmountBefore);
            AdvanceEventRulesAndQueueFact(new XiongmuChainRebasedEvent(r.AttackId, r.WindowId, attack.TargetSeat, r.ChainCursor, r.AmountBefore));
        }
        SetWuXiongmuDamageState(attack.ResolutionId, null);
    }
    private bool ValidWuXiongmuCangxinReduction(LuYuShengDamageReceipt receipt, out LuYuShengDamageAdjustedEvent adjusted)
    {
        adjusted = null!;
        var original = receipt with { TransferredTargetSeat = null, TransferWindowId = null };
        if (ProgramEventHistory<LuYuShengDamageAdjustedEvent>().Where(e => e.Applied && e.Receipt == original).ToArray() is not [var fact] ||
            fact.Hearts < 0 || fact.Hearts > 3 || original.Before <= 0 || original.After != Math.Max(0, original.Before - fact.Hearts) ||
            ProgramEventHistory<LuYuShengStartedEvent>().Where(e => e.FrameId == fact.FrameId).ToArray() is not [var start] ||
            start.Operation != SkillProgramEffectOp.DiscardBottomHeartsForDamageReduction || start.WindowId != original.WindowId ||
            start.AttackId != original.AttackId || start.Payer != original.TargetSeat || start.Source.OwnerSeat != original.TargetSeat ||
            _contentRegistry.Skills.GetValueOrDefault(start.Source.SkillId)?.Program?.GameplayHash != start.GameplayHash ||
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(e => e.FrameId == fact.FrameId && e.SkillId == start.Source.SkillId &&
                e.SkillInstanceId == start.Source.SkillInstanceId && e.BindingId == start.Source.BindingId &&
                e.OwnerSeat == original.TargetSeat && e.Window == SkillProgramTriggerWindow.BeforeDamageApplied) != 1 ||
            ProgramEventHistory<LuYuShengMovementIssuedEvent>().Where(e => e.FrameId == fact.FrameId &&
                e.Stage == LuYuShengStage.BottomDiscardChildren).ToArray() is not [var paid] || paid.Payer != original.TargetSeat ||
            paid.Before < 0 || paid.After < paid.Before || paid.After > _movementSequence ||
            _cardMovements.Count(m => m.Sequence > paid.Before && m.Sequence <= paid.After && m.From == CardLocation.DrawPile &&
                m.To == CardLocation.DiscardPile && m.Reason.Value == LuYuShengBottomDiscardReason) != paid.Count ||
            _cardMovements.Count(m => m.Sequence > paid.Before && m.Sequence <= paid.After && m.From == CardLocation.DrawPile &&
                m.To == CardLocation.DiscardPile && m.Reason.Value == LuYuShengBottomDiscardReason && GetAdvancedCard(m.CardId).Suit == Suit.Heart) != fact.Hearts)
            return false;
        if (receipt.TransferredTargetSeat is { } recipient && (receipt.TransferWindowId is not { } window ||
            ProgramEventHistory<LuYuShengDamageTransferredEvent>().Count(e => e.WindowId == receipt.WindowId && e.AttackId == receipt.AttackId &&
                e.TargetSeat == recipient && e.OriginalTargetSeat == receipt.TargetSeat && e.ChainCursor == receipt.ChainCursor && e.TransferWindowId == window) != 1 ||
            !ProgramEventHistory<ProgramDamageTransferredEvent>().Any(e => e.ResolutionId == receipt.AttackId && e.TargetSeat == recipient))) return false;
        adjusted = fact; return true;
    }
    [ProgramDamageHook(ProgramDamagePhase.BeforeChainAdvance, 10)]
    private void PrepareWuXiongmuCangxinChainAdvance(IDamageAttempt attack)
    {
        var xiongmu = WuXiongmuDamageState(attack.ResolutionId); var cangxin = LuYuShengDamageState(attack.ResolutionId);
        if (xiongmu is null || cangxin is null)
        {
            PrepareLuYuShengChainAdvance(attack); PrepareWuXiongmuChainAdvance(attack); return;
        }
        var cursor = WuXiongmuChainCursor(attack);
        var remaining = attack is ProgramAttackHandle ? GetProgramAttackState(attack.ResolutionId).ChainedTargetSeats : GetCardAttackState(attack.ResolutionId).ChainedTargetSeats;
        var nextIndex = attack is ProgramAttackHandle ? GetProgramAttackState(attack.ResolutionId).ChainedTargetIndex : GetCardAttackState(attack.ResolutionId).ChainedTargetIndex;
        if (!attack.IsChainPropagation || xiongmu.AttackId != attack.ResolutionId ||
            cangxin.AttackId != attack.ResolutionId || xiongmu.ChainCursor != cursor || cangxin.ChainCursor != cursor ||
            (xiongmu.TransferredTargetSeat ?? xiongmu.TargetSeat) != attack.TargetSeat ||
            (cangxin.TransferredTargetSeat ?? cangxin.TargetSeat) != attack.TargetSeat ||
            remaining is null || !remaining.Skip(nextIndex).Any(seat => _players[seat].IsAlive))
        {
            PrepareLuYuShengChainAdvance(attack); PrepareWuXiongmuChainAdvance(attack); return;
        }
        if (!SameAttackOwner(attack, CurrentDamageAttempt) || !ValidWuXiongmuDamage(xiongmu) ||
            !ValidWuXiongmuCangxinReduction(cangxin, out var cangxinFact))
            throw new InvalidOperationException("The combined first-damage rebase lost one of its exact paid native recipient invoices.");
        var history = CompleteProgramEventHistory().ToArray();
        var xIndex = Array.FindIndex(history, e => e is XiongmuFirstDamageReducedEvent fact && fact.Receipt.ProgramFrameId == xiongmu.ProgramFrameId &&
            fact.Receipt == (xiongmu with { TransferredTargetSeat = null, TransferWindowId = null }));
        var cIndex = Array.FindIndex(history, e => e is LuYuShengDamageAdjustedEvent fact && fact == cangxinFact);
        if (xIndex < 0 || cIndex < 0 || xIndex == cIndex)
            throw new InvalidOperationException("The combined damage rebase lost its original applied-effect order.");
        // Each before-damage effect retains its own actual before/after invoice.
        // Whichever ran first observed this recipient's original local base.
        // Restore that base once, instead of letting two setters overwrite it
        // in an unrelated fixed skill order. Other native rebase producers are
        // still dispatched by their existing chain-advance hooks.
        var firstXiongmu = xIndex < cIndex;
        var originalAmount = firstXiongmu ? xiongmu.AmountBefore : cangxin.Before;
        SetWuXiongmuChainAmount(attack, originalAmount);
        AdvanceEventRulesAndQueueFact(new XiongmuCangxinChainRebasedEvent(attack.ResolutionId, attack.TargetSeat, cursor,
            xiongmu.ProgramFrameId, xiongmu.WindowId, cangxinFact.FrameId, cangxin.WindowId, firstXiongmu, originalAmount));
        SetLuYuShengDamageState(attack.ResolutionId, null); SetWuXiongmuDamageState(attack.ResolutionId, null);
    }
    private void TransferWuXiongmuDamage(ProgramDamageTransferredEvent transfer)
    {
        if (CurrentDamageAttempt is not { } attack || WuXiongmuDamageState(attack.ResolutionId) is not { TransferredTargetSeat: null } r ||
            _resolutionStack.Count < 2 || _resolutionStack[^1] is not ProgramSkillFrame f || _resolutionStack[^2] is not BeforeDamageProgramWindowFrame w ||
            GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.RedirectCurrentDamage || f.WindowContext?.ParentFrameId != w.Id || w.Id != r.WindowId ||
            (w.ContinuationAttackResolutionId ?? w.ParentFrameId) != attack.ResolutionId || w.TargetSeat != r.TargetSeat ||
            transfer.ResolutionId != attack.ResolutionId || transfer.SourceSeat != attack.SourceSeat || transfer.TargetSeat != attack.TargetSeat ||
            w.RedirectedTargetSeat != attack.TargetSeat || transfer.DamageAmount != w.Amount || transfer.OwnerSeat != f.OwnerSeat ||
            transfer.SkillId != f.SkillId || !attack.DamageRedirected || r.ChainCursor != WuXiongmuChainCursor(attack)) return;
        if (!ValidWuXiongmuDamage(r)) throw new InvalidOperationException("Xiongmu's transferred damage lost its original consumed first-hit reduction.");
        SetWuXiongmuDamageState(attack.ResolutionId, r with { TransferredTargetSeat = attack.TargetSeat, TransferWindowId = w.Id });
        AdvanceEventRulesAndQueueFact(new XiongmuDamageTransferredEvent(r.AttackId, r.WindowId, r.TargetSeat, attack.TargetSeat, r.ChainCursor, w.Id));
    }
    private void ValidateWuXiongmuCurrentAttempt(IDamageAttempt attack, XiongmuDamageReceipt receipt)
    {
        if (!SameAttackOwner(attack, CurrentDamageAttempt) || receipt.AttackId != attack.ResolutionId ||
            (receipt.TransferredTargetSeat ?? receipt.TargetSeat) != attack.TargetSeat ||
            receipt.ChainCursor != WuXiongmuChainCursor(attack) || !ValidWuXiongmuDamage(receipt))
            throw new InvalidOperationException("Xiongmu cannot retire a different native damage attempt's reduction invoice.");
    }
    private void ValidateWuXiongmuCangxinCurrentAttempt(IDamageAttempt attack, LuYuShengDamageReceipt receipt,
        out LuYuShengDamageAdjustedEvent adjusted)
    {
        adjusted = null!;
        if (!SameAttackOwner(attack, CurrentDamageAttempt) || receipt.AttackId != attack.ResolutionId ||
            (receipt.TransferredTargetSeat ?? receipt.TargetSeat) != attack.TargetSeat ||
            receipt.ChainCursor != WuXiongmuChainCursor(attack) || !ValidWuXiongmuCangxinReduction(receipt, out adjusted))
            throw new InvalidOperationException("The composed native damage return lost Cangxin's exact completed attempt.");
    }
    private ProgramSkillFrame CaptureWuXiongmuProgramDamageCompleted(ProgramSkillFrame frame, IDamageAttempt attack)
    {
        if (frame.XiongmuDamage is null && (!HasWuXiongmuFirstDamageCapability || frame.LuYuShengDamage is null)) return frame;
        if (attack is not ProgramAttackHandle || frame.Id != attack.ResolutionId || frame.AttackAttempt is null ||
            _resolutionStack.LastOrDefault()?.Id != frame.Id || !SameAttackOwner(attack, CurrentDamageAttempt))
            throw new InvalidOperationException("Xiongmu's native program damage return lost its actual completed producer.");
        if (frame.XiongmuDamage is { } xiongmu)
        {
            ValidateWuXiongmuCurrentAttempt(attack, xiongmu);
            AdvanceEventRulesAndQueueFact(new XiongmuDamageRetiredEvent(xiongmu.AttackId, xiongmu.WindowId,
                xiongmu.ProgramFrameId, xiongmu.TargetSeat, xiongmu.ChainCursor));
        }
        if (HasWuXiongmuFirstDamageCapability && frame.LuYuShengDamage is { } cangxin)
        {
            ValidateWuXiongmuCangxinCurrentAttempt(attack, cangxin, out var fact);
            AdvanceEventRulesAndQueueFact(new XiongmuCangxinDamageRetiredEvent(cangxin.AttackId, cangxin.WindowId,
                fact.FrameId, cangxin.TargetSeat, cangxin.ChainCursor));
            frame = frame with { LuYuShengDamage = null };
        }
        return frame with { XiongmuDamage = null };
    }
    private void RetireWuXiongmuCardDamageCompleted(IDamageAttempt attack)
    {
        var xiongmu = WuXiongmuDamageState(attack.ResolutionId);
        var cangxin = HasWuXiongmuFirstDamageCapability ? LuYuShengDamageState(attack.ResolutionId) : null;
        if (xiongmu is null && cangxin is null) return;
        if (attack is not CardAttackHandle || !SameAttackOwner(attack, CurrentDamageAttempt))
            throw new InvalidOperationException("Xiongmu cannot clear another native card attack's completed damage state.");
        if (xiongmu is not null)
        {
            ValidateWuXiongmuCurrentAttempt(attack, xiongmu);
            AdvanceEventRulesAndQueueFact(new XiongmuDamageRetiredEvent(xiongmu.AttackId, xiongmu.WindowId,
                xiongmu.ProgramFrameId, xiongmu.TargetSeat, xiongmu.ChainCursor));
            SetWuXiongmuDamageState(attack.ResolutionId, null);
        }
        if (cangxin is not null)
        {
            ValidateWuXiongmuCangxinCurrentAttempt(attack, cangxin, out var fact);
            AdvanceEventRulesAndQueueFact(new XiongmuCangxinDamageRetiredEvent(cangxin.AttackId, cangxin.WindowId,
                fact.FrameId, cangxin.TargetSeat, cangxin.ChainCursor));
            SetLuYuShengDamageState(attack.ResolutionId, null);
        }
    }
    private void AssertWuXiongmuDamageStates()
    {
        foreach (var frame in _resolutionStack)
        {
            var r = frame switch { CardUseFrame c => c.XiongmuDamage, ProgramSkillFrame p => p.XiongmuDamage,
                JudgmentFrame j => j.XiongmuDamage, _ => null };
            if (r is null) continue;
            if (r.AttackId != frame.Id || !ValidWuXiongmuDamage(r) ||
                ProgramEventHistory<XiongmuDamageRetiredEvent>().Any(e => e.ProgramFrameId == r.ProgramFrameId) ||
                frame is ProgramSkillFrame { AttackAttempt: null, CardAttack: null } ||
                (r.TransferredTargetSeat is null) != (r.TransferWindowId is null) ||
                r.TransferredTargetSeat is { } target && (target == r.TargetSeat || !IsValidPlayerSeat(target) ||
                    ProgramEventHistory<XiongmuDamageTransferredEvent>().Count(e => e.AttackId == r.AttackId && e.WindowId == r.WindowId &&
                        e.OriginalTarget == r.TargetSeat && e.Target == target && e.ChainCursor == r.ChainCursor && e.TransferWindowId == r.TransferWindowId) != 1 ||
                    !ProgramEventHistory<ProgramDamageTransferredEvent>().Any(e => e.ResolutionId == r.AttackId && e.TargetSeat == target)))
                throw new InvalidOperationException("Xiongmu's cold native damage state lost its consumed first-hit or exact target transfer invoice.");
        }
    }

}
