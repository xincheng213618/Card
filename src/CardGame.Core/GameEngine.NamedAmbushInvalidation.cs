namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsExactNamedAmbushInvalidation(NamedAmbushUseInvalidatedEvent fact, CardActionContext action, long parent)
    {
        var history = CompleteProgramEventHistory().ToArray();
        var offers = history.Select((e, i) => (e, i)).Where(p => p.e is NamedAmbushOfferedEvent issued &&
            issued.ProgramFrameId == fact.ProgramFrameId && issued.Receipt.Removing && issued.Receipt.Use == fact.Use).ToArray();
        if (offers is not [var offer] || offer.e is not NamedAmbushOfferedEvent { Receipt: var receipt } ||
            receipt.OriginalAction is not { } original || receipt.Source.SkillId != fact.Deposit.Source.SkillId ||
            receipt.Source.OwnerSeat != fact.Deposit.Source.OwnerSeat || receipt.Source.SkillInstanceId != fact.Deposit.Source.SkillInstanceId ||
            receipt.InstructionIndex != 1 || _contentRegistry.GetSkill(receipt.Source.SkillId).Program is not { } program ||
            receipt.GameplayHash != program.GameplayHash ||
            ProgramInstructionResolver.Default.FindTrigger(program, receipt.Source.BindingId)?.Effects is not [{ Op: SkillProgramEffectOp.NullifyMatchingHandUse }] ||
            history.OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == fact.ProgramFrameId &&
                e.SkillId == receipt.Source.SkillId && e.OwnerSeat == receipt.Source.OwnerSeat && e.BindingId == receipt.Source.BindingId &&
                e.SkillInstanceId == receipt.Source.SkillInstanceId && e.Window == SkillProgramTriggerWindow.CardUseCommitted) != 1 ||
            fact.Use.WindowFrameId != receipt.WindowFrameId || fact.Use.ParentFrameId != parent ||
            fact.Use.ActionId != original.ActionId || fact.Use.ActorSeat != original.ActorSeat ||
            fact.Use.ProviderSeat != original.ProviderSeat || fact.Use.EffectiveKind != original.EffectiveKind ||
            original.ActorSeat != original.ProviderSeat || fact.Use.ResponseUse != (original.Type == CardActionType.Response) ||
            original.PhysicalCards.Count == 0 || original.PhysicalCards.Any(c => c.From != CardLocation.Hand(original.ActorSeat)) ||
            original.ActionId != action.ActionId || original.ParentActionId != action.ParentActionId || original.Type != action.Type ||
            original.ProviderSeat != action.ProviderSeat || original.RequesterSeat != action.RequesterSeat ||
            original.ResponderSeat != action.ResponderSeat || original.OpponentSeat != action.OpponentSeat ||
            original.EffectiveSuit != action.EffectiveSuit || original.EffectiveRank != action.EffectiveRank ||
            !original.PhysicalCards.SequenceEqual(action.PhysicalCards) ||
            fact.Deposit.RecordedKind != AmbushCardName(original.EffectiveKind) || fact.SequenceAfter <= fact.SequenceBefore ||
            history.OfType<NamedAmbushDepositedEvent>().Count(e => e.Deposit == fact.Deposit) != 1 ||
            history.OfType<NamedAmbushRemovedEvent>().Count(e => e.ProgramFrameId == fact.ProgramFrameId && e.Deposit == fact.Deposit &&
                e.Use == fact.Use && e.SequenceBefore == fact.SequenceBefore && e.SequenceAfter == fact.SequenceAfter && e.Reason == "used") != 1 ||
            _cardMovements.Count(m => m.CardId == fact.Deposit.CardId && m.From == fact.Deposit.Location && m.To == CardLocation.DiscardPile &&
                m.Sequence > fact.SequenceBefore && m.Sequence <= fact.SequenceAfter && m.Reason.Value == NamedAmbushRemoveReason) != 1 ||
            history.OfType<NamedAmbushUseInvalidatedEvent>().Count(e => e == fact) != 1) return false;
        if (original.ActorSeat == action.ActorSeat && original.EffectiveKind == action.EffectiveKind &&
            original.ConversionChain.SequenceEqual(action.ConversionChain)) return true;
        // A paid ineffective state follows the same real use through later
        // proven role/kind changes; its hand qualification stays at issuance.
        return !fact.Use.ResponseUse && LifecycleCardUse(parent) is { Action: { } current } use &&
            SameOverflowAction(current, action) && HasExactAcceptedActualHandGainUse(use, action) &&
            RebuildAcceptedActualHandGainUse(use, original, action, history, offer.i);
    }
    private void InvalidateNamedAmbushUse(ProgramSkillFrame f)
    {
        var r = f.NamedAmbush!; var use = r.Use!;
        if (!TryGetAmbushUse(f.WindowContext!, out var live, out var action) || live != use)
            throw new InvalidOperationException("Paid ambush cannot invalidate a different actual use.");
        var fact = new NamedAmbushUseInvalidatedEvent(f.Id, r.Deposit!, use, r.MovementBefore, r.MovementAfter);
        AdvanceEventRulesAndQueueFact(fact);
        if (!IsExactNamedAmbushInvalidation(fact, action, use.ParentFrameId)) throw new InvalidOperationException("Invalidation lost exact private-card payment proof.");
        if (!use.ResponseUse)
            UpdateLifecycleCardUse(use.ParentFrameId, parent => parent with { NamedAmbushInvalidation = fact });
        else if (action.EffectiveKind == CardKind.Dodge)
            UpdateLifecycleCardUse(use.ParentFrameId, parent => parent with { NamedAmbushResponseInvalidations = (parent.NamedAmbushResponseInvalidations ?? []).Append(fact).ToArray() });
        else if (action.EffectiveKind == CardKind.Nullification && ActiveNullificationWindow is { } counter && counter.Id == use.ParentFrameId &&
            counter.NamedAmbushResponse is { } response && response.ActionId == action.ActionId)
            ReplaceNullificationWindowFrame(counter with { NamedAmbushResponse = response with { Invalidated = fact } });
        else throw new InvalidOperationException("The invalidated response lost its exact typed native owner.");
        AddLog("SkillTriggered", $"{_players[f.OwnerSeat].Name} 发动【{_contentRegistry.GetSkill(f.SkillId).Name}】，令【{CardCatalog.Get(action.EffectiveKind).DisplayName}】无效。", f.OwnerSeat, action.ActorSeat);
    }
    private bool IsNamedAmbushUseIneffective(CardUseFrame use)
    {
        if (IsHappyWeiYanUseIneffective(use) || IsMouCyUseIneffective(use) || IsAnniversaryHzUseIneffective(use)) return true;
        if (use.NamedAmbushInvalidation is not { } proof) return false;
        if (use.Action is not { } action || !IsExactNamedAmbushInvalidation(proof, action, use.Id))
            throw new InvalidOperationException("A globally invalidated use lost its exact paid ambush proof.");
        return true;
    }
    private bool ContinueNamedAmbushIneffectiveDodge(CardAttackHandle attack, CardActionContext action, ProgramCardContinuation continuation)
    {
        if (ContinueHappyWeiYanIneffectiveDodge(attack, action, continuation) || ContinueMouCyIneffectiveDodge(attack, action, continuation) ||
            ContinueAnniversaryHzIneffectiveDodge(attack, action, continuation)) return true;
        if (continuation != ProgramCardContinuation.Dodge || LifecycleCardUse(attack.ResolutionId)?.NamedAmbushResponseInvalidations is not { } proofs) return false;
        var matches = proofs.Where(p => p.Use.ActionId == action.ActionId).ToArray(); if (matches.Length == 0) return false;
        if (action.ParentActionId != LifecycleCardUse(attack.ResolutionId)?.Action?.ActionId || attack.TargetSeat != action.ActorSeat ||
            action.EffectiveKind != CardKind.Dodge || matches.Any(p => !IsExactNamedAmbushInvalidation(p, action, attack.ResolutionId)))
            throw new InvalidOperationException("An ineffective Dodge changed its Slash or committed response identity.");
        ClearRoundOnceCardlessDodgeResponse(attack); ClearTieredRoundDodgeResponse(attack); ClearChainedStateBasicDodgeResponse(attack); ClearDrawFundedDistinctBasicDodgeResponse(attack);
        SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        if (!ApplyAttackDamage(attack)) CompleteAttack(attack); return true;
    }
    private NullificationWindowFrame PrepareNamedAmbushCounterspell(NullificationWindowFrame pending, int responder, int cardId)
    {
        if (!HasNamedAmbushCapability && !HasMouChengYuAnniversary && !HasAnniversaryHuangZhongCapability && !HasHappyWeiYanEzhiCapability) return pending;
        if (pending.NamedAmbushResponse is not null) throw new InvalidOperationException("A counterspell already awaits its actual response use.");
        return ReplaceNullificationWindowFrame(pending with { NamedAmbushResponse = new(0, responder, cardId, pending.EffectNullified, pending.ChainDepth),
            ChainDepth = pending.ChainDepth + 1, CandidateSeats = BuildNullificationCandidateSeats(responder), CandidateIndex = 0 });
    }
    private bool HasEnabledMatchingNamedAmbush(int actor, CardKind kind) => HasNamedAmbushCapability &&
        _players.Where(p => p.IsAlive && p.Seat != actor && p.Seat != _currentSeat).Any(owner =>
            GetSkillBindingShard(owner).GetInstanceTriggers(SkillProgramTriggerWindow.CardUseCommitted)
                .Any(b => b.Trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.NullifyMatchingHandUse) &&
                    LiveAmbushDeposits(owner.Seat, b.SkillId, b.SkillInstanceId).Any(d => d.RecordedKind == AmbushCardName(kind))));
    private void BindNamedAmbushCounterspell(long id, CardActionContext action)
    {
        if (!HasNamedAmbushCapability && !HasMouChengYuAnniversary && !HasAnniversaryHuangZhongCapability && !HasHappyWeiYanEzhiCapability) return;
        var pending = _resolutionStack.OfType<NullificationWindowFrame>().Single(f => f.Id == id);
        if (pending.NamedAmbushResponse is not { ActionId: 0 } r || action.EffectiveKind != CardKind.Nullification || action.Type != CardActionType.Response ||
            action.ActorSeat != r.ResponderSeat || (!action.PhysicalCards.Any(c => c.CardId == r.CardId) && !IsDeferredZeroCounterspell(pending, action, r)))
            throw new InvalidOperationException("A deferred counterspell lost its committed paid response.");
        ReplaceNullificationWindowFrame(pending with { NamedAmbushResponse = r with { ActionId = action.ActionId } });
        CaptureLiJueCompletedNullificationUse(pending.Id, action);
    }
    private bool IsDeferredZeroCounterspell(NullificationWindowFrame pending, CardActionContext action,
        NamedAmbushCounterspellResponse response) =>
        response.CardId == 0 && action.PhysicalCards.Count == 0 &&
        pending.TieredRoundResponseUse is { } receipt && receipt.OwnerFrameId == pending.Id &&
        receipt.CardActionId == action.ActionId && receipt.ActorSeat == action.ActorSeat &&
        receipt.EffectiveKind == CardKind.Nullification &&
        ProgramEventHistory<TieredRoundConversionUseIssuedEvent>().Count(e => e.Receipt == receipt) == 1;
    private NullificationWindowFrame CompleteNamedAmbushCounterspell(NullificationWindowFrame pending)
    {
        if (pending.NamedAmbushResponse is not { } r) return pending;
        if (r.ActionId <= 0 || pending.ChainDepth != r.PreviousChainDepth + 1 || pending.EffectNullified != r.PreviousEffectNullified ||
            _resolutionStack.LastOrDefault()?.Id != pending.Id)
            throw new InvalidOperationException("Deferred counterspell completion changed the original chain state.");
        var action = CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Select(e => e.Action).LastOrDefault(a => a.ActionId == r.ActionId) ??
            throw new InvalidOperationException("Deferred counterspell lost its real response use.");
        if (r.Invalidated is { } invalidation && !IsExactNamedAmbushInvalidation(invalidation, action, pending.Id))
            throw new InvalidOperationException("Counterspell cancellation lost paid ambush provenance.");
        var ineffective = r.Invalidated is not null || MouCyCounterspellInvalidated(pending, action) ||
            AnniversaryHzCounterspellInvalidated(pending, action) || HappyWeiYanCounterspellInvalidated(pending, action);
        pending = ReplaceNullificationWindowFrame(pending with { NamedAmbushResponse = null, MouCyResponseInvalidation = null,
            AnniversaryHzInvalidation = null, HappyWeiYanCancellation = null,
            EffectNullified = !ineffective ? !r.PreviousEffectNullified : r.PreviousEffectNullified,
            ChainDepth = r.PreviousChainDepth + 1, CandidateSeats = BuildNullificationCandidateSeats(r.ResponderSeat), CandidateIndex = 0 });
        CaptureCaoXiNativeCounterspellOffset(pending, action, invalidated: ineffective);
        AdvanceEventRulesAndQueueFact(new NullificationRespondedEvent(pending.ParentFrameId, pending.EffectCardId, pending.EffectCardKind,
            r.ResponderSeat, r.CardId, pending.EffectNullified, pending.ChainDepth)
            { WindowFrameId = pending.Id, TargetSeat = GetNullificationTargetSeat(pending) });
        AddLog("NullificationResponded", ineffective ? $"{_players[r.ResponderSeat].Name} 使用的【无懈可击】无效。" :
            pending.EffectNullified ? $"{_players[r.ResponderSeat].Name} 使用【无懈可击】，使【{CardCatalog.Get(pending.EffectCardKind).DisplayName}】失效。" :
            $"{_players[r.ResponderSeat].Name} 使用【无懈可击】，使【{CardCatalog.Get(pending.EffectCardKind).DisplayName}】恢复。", r.ResponderSeat, pending.SourceSeat);
        return pending;
    }
}
