namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasPhaseAlcoholChain(ProgramSkillFrame frame) =>
        _contentRegistry.ProgramDependencies.GetTriggerOperationSkillIds(SkillProgramEffectOp.UsePhaseOwnerVirtualAlcohol).Contains(frame.SkillId) &&
        ForcedPhaseAlcoholContract.IsChain(ProgramInstructionResolver.Default.Resolve(frame,
            _contentRegistry.GetSkill(frame.SkillId).Program!).Instructions);

    private bool CanUsePhaseOwnerAlcohol(ProgramSkillWindowContext? context) =>
        context is { Window: SkillProgramTriggerWindow.PlayPhaseStarting, SourceSeat: { } seat, TargetSeat: { } target } &&
        seat == target && seat == _currentSeat && _phase == TurnPhase.Play &&
        IsValidPlayerSeat(seat) && VirtualBasicOptions(seat).Contains((CardKind.Alcohol, seat)) &&
        _resolutionStack.OfType<PlayPhaseStartingBoundaryFrame>().Any(p => p.Id == context.ParentFrameId && p.OwnerSeat == seat);

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeCondition, 90)]
    private bool CanOfferPhaseAlcohol(SkillProgramTrigger trigger, ProgramSkillWindowContext context) =>
        !trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.UsePhaseOwnerVirtualAlcohol) || CanUsePhaseOwnerAlcohol(context);

    private bool PreparePhaseAlcoholPayment(ProgramSkillFrame f, string bind, CardMoveReason reason)
    {
        if (!HasPhaseAlcoholChain(f)) return true;
        if (f.PhaseAlcohol is not null || f.InstructionIndex != 2 || !CanUsePhaseOwnerAlcohol(f.WindowContext))
        { CancelProgramBindingAndCleanup(f, "该出牌阶段角色当前不能使用酒，未支付弃牌费用。"); return false; }
        var cost = GetProgramCardSet(f, bind);
        if (cost.CardIds is not [var id] || cost.SourceLocations is not [var from] || from != CardLocation.Hand(f.OwnerSeat) ||
            _cardZones.GetLocation(id) != from || cost.SelectionActorSeat is { } selector && selector != f.OwnerSeat)
            throw new InvalidOperationException("Phase Wine requires its one exact owner hand cost.");
        ReplaceRuntimeTop(f with { PhaseAlcohol = new(2, 3, f.WindowContext!.ParentFrameId,
            f.WindowContext.SourceSeat!.Value, _turnNumber, id, _movementSequence, 0, reason.Value, ProgramPhaseAlcoholStage.Paid) });
        return true;
    }

    private void CommitPhaseAlcoholPayment(long id)
    {
        var f = GetActiveProgramFrame(id);
        if (f.PhaseAlcohol is not { Stage: ProgramPhaseAlcoholStage.Paid, PaymentSequenceAfter: 0 } receipt) return;
        ReplaceRuntimeTop(f with { PhaseAlcohol = receipt with { PaymentSequenceAfter = _movementSequence } });
        if (!HasExactPhaseAlcoholReceipt(GetActiveProgramFrame(id)))
            throw new InvalidOperationException("Phase Wine did not commit its one exact hand-to-discard payment.");
    }

    private bool HasExactPhaseAlcoholReceipt(ProgramSkillFrame f)
    {
        if (f.PhaseAlcohol is not { } r || f.WindowContext is not { Window: SkillProgramTriggerWindow.PlayPhaseStarting } context ||
            !HasPhaseAlcoholChain(f) || r.PaymentInstruction != 2 || r.WineInstruction != 3 ||
            f.InstructionIndex is < 2 or > 3 || context.ParentFrameId != r.PhaseParentFrameId ||
            context.SourceSeat != r.ActorSeat || context.TargetSeat != r.ActorSeat || context.OwnerSeat != f.OwnerSeat ||
            r.ActorSeat != _currentSeat || r.TurnNumber != _turnNumber || _phase != TurnPhase.Play ||
            r.PaymentSequenceAfter <= r.PaymentSequenceBefore ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            _resolutionStack.OfType<PlayPhaseStartingBoundaryFrame>().SingleOrDefault(p => p.Id == r.PhaseParentFrameId) is not { } parent ||
            parent.ItemIndex >= parent.Items.Count || parent.Items[parent.ItemIndex].Candidate is not { } candidate ||
            !MountObserverCandidateMatches(f, candidate) || candidate.BindingId != f.TriggerId ||
            _cardMovements.Count(m => m.CardId == r.CardId && m.Sequence > r.PaymentSequenceBefore &&
                m.Sequence <= r.PaymentSequenceAfter && m.From == CardLocation.Hand(f.OwnerSeat) &&
                m.To == CardLocation.DiscardPile && m.Reason.Value == r.PaymentReason) != 1)
            return false;
        return r.Stage == ProgramPhaseAlcoholStage.Paid ? r.WineFrameId is null && !r.WineSkipped :
            r.Stage == ProgramPhaseAlcoholStage.WineIssued ? r.WineFrameId is not null && !r.WineSkipped :
            r.Stage == ProgramPhaseAlcoholStage.DamageIssued && (r.WineFrameId is not null || r.WineSkipped);
    }

    private bool CanContinuePhaseAlcohol(ProgramSkillFrame frame) =>
        frame.PhaseAlcohol is { } r && _winner == Winner.None && _players[frame.OwnerSeat].IsAlive &&
        _players[r.ActorSeat].IsAlive && HasExactPhaseAlcoholReceipt(frame);

    private SkillProgramStepOutcome BeginPhaseOwnerAlcohol(ProgramSkillFrame frame, int damageToOther)
    {
        var f = GetActiveProgramFrame(frame.Id);
        if (f.PhaseAlcohol is not { Stage: ProgramPhaseAlcoholStage.Paid } receipt || f.InstructionIndex != 3 ||
            f.PendingMovementContinuation is not null || !HasExactPhaseAlcoholReceipt(f))
            throw new InvalidOperationException("Phase Wine requires its once-paid, drained actual phase instruction.");
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !_players[receipt.ActorSeat].IsAlive)
            return SkillProgramStepOutcome.Continue;
        // A later payment child can change Wine legality. Its independent damage
        // tail is still the paid skill effect, just as a canceled Wine is.
        if (!CanUsePhaseOwnerAlcohol(f.WindowContext))
        {
            if (receipt.ActorSeat != f.OwnerSeat && damageToOther > 0)
            {
                ReplaceRuntimeTop(f = f with { PhaseAlcohol = receipt with { Stage = ProgramPhaseAlcoholStage.DamageIssued, WineSkipped = true } });
                return BeginProgramSkillDamage(f, receipt.ActorSeat, damageToOther);
            }
            FinishProgramSkill(f, completed: true); return SkillProgramStepOutcome.AwaitChild;
        }
        var actor = _players[receipt.ActorSeat]; var id = ++_resolutionSequence;
        ReplaceRuntimeTop(f with { PhaseAlcohol = receipt with { Stage = ProgramPhaseAlcoholStage.WineIssued, WineFrameId = id },
            VirtualBasicDraft = new(f.InstructionIndex, id) });
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence, null, CardActionType.Use,
            actor.Seat, f.OwnerSeat, null, null, null, CardKind.Alcohol, [actor.Seat], [], [], effectiveSuit: Suit.None, effectiveRank: 0));
        PushRuntimeFrame(new CardUseFrame(id, actor.Seat, 0, CardKind.Alcohol, [actor.Seat], PhysicalCardIds: [])
        { Action = action, VirtualBasicReturn = new(f.Id, f.InstructionIndex), FirstOwnPlayUseDistanceUnlimited = HasFirstActualPlayUseDistance(actor) });
        // The use allowance is spent by the real actor at commitment, even when
        // a card-use trigger later cancels Wine's effect.
        actor.UsedPlayPhaseAlcoholThisTurn = true;
        if (TracksPlayCardHistory) AdvanceEventRulesAndQueueFact(new CardUseAppearanceCapturedEvent(action));
        AdvanceEventRulesAndQueueFact(new CardUseDeclaredEvent(id, 0, CardKind.Alcohol, actor.Seat));
        AdvanceEventRulesAndQueueFact(new TargetsConfirmedEvent(id, [actor.Seat]));
        RecordYingboCardUse(id, actor.Seat, CardKind.Alcohol); RecordProgramUsedBasicCard(actor.Seat, CardKind.Alcohol); RecordActualPlayPhaseUse(action);
        BeginSimpleCardUse(id, new(0, SimpleCardUseEffect.Alcohol)); return SkillProgramStepOutcome.AwaitChild;
    }

    private bool IsExactPhaseAlcoholVirtualDraft(ProgramSkillFrame f, ProgramVirtualBasicDraft draft) =>
        f.PhaseAlcohol is { Stage: ProgramPhaseAlcoholStage.WineIssued, WineFrameId: { } child } r &&
        HasExactPhaseAlcoholReceipt(f) && draft.InstructionIndex == r.WineInstruction && draft.ChildFrameId == child;

    [ProgramContinuation(ProgramContinuationKind.Resume, 645)]
    private bool ResumePhaseAlcohol(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { PhaseAlcohol: { } r } f || f.Id != id) return false;
        if (!HasExactPhaseAlcoholReceipt(f)) throw new InvalidOperationException("Phase Wine lost its exact paid parent or actual phase.");
        if (r.Stage == ProgramPhaseAlcoholStage.Paid) return false;
        if (f.VirtualBasicDraft is not null) throw new InvalidOperationException("Phase Wine resumed before its typed zero-entity child returned.");
        if (r.Stage == ProgramPhaseAlcoholStage.DamageIssued)
        {
            if (f.AttackAttempt is not null) { CompleteDamageAttack(new ProgramAttackHandle(this, id)); return true; }
            FinishProgramSkill(f, completed: true); return true;
        }
        if (CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Count(e => e.ResolutionId == r.WineFrameId &&
                e.CardId == 0 && e.CardKind == CardKind.Alcohol) != 1)
            throw new InvalidOperationException("Phase Wine needs one exact completed declaration, including a canceled effect.");
        var damage = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).Instructions[2].Amount;
        if (r.ActorSeat != f.OwnerSeat && damage > 0 && CanContinuePhaseAlcohol(f))
        {
            ReplaceRuntimeTop(f = f with { PhaseAlcohol = r with { Stage = ProgramPhaseAlcoholStage.DamageIssued } });
            BeginProgramSkillDamage(f, r.ActorSeat, damage); return true;
        }
        FinishProgramSkill(f, completed: true); return true;
    }

    private (int Deaths, int Kills) ActualTurnDeathCounts(int ownerSeat)
    {
        var deaths = 0; var kills = 0; var started = false;
        foreach (var item in CompleteProgramEventHistory())
        {
            if (item is TurnStartedEvent begun)
            {
                started = begun.TurnNumber == _turnNumber && begun.ActorSeat == _currentSeat;
                if (started) { deaths = 0; kills = 0; }
                continue;
            }
            if (!started) continue;
            if (item is TurnEndedEvent ended && ended.TurnNumber == _turnNumber && ended.ActorSeat == _currentSeat) break;
            if (item is PlayerDiedEvent death) { deaths++; if (death.KillerSeat == ownerSeat) kills++; }
        }
        return (deaths, kills);
    }
    private int ActualTurnDeathDrawCount(ProgramSkillFrame f, int seat)
    {
        if (seat != f.OwnerSeat || f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterTurnEnded, Facts: { ActualTurnDeathCount: { } deaths, OwnerActualTurnKillCount: { } kills } } context ||
            _resolutionStack.OfType<DeferredTurnEndFrame>().SingleOrDefault(p => p.Id == context.ParentFrameId) is not { } parent ||
            !IsActualAfterTurnEndedParent(parent) || parent.AfterTurnEnded?.CurrentChild is not { } child ||
            child.FrameId != f.Id || !MountObserverCandidateMatches(f, child.Candidate) ||
            !CanRunAfterTurnEndedCandidate(child.Candidate, context, GetProgramTrigger(child.Candidate)) ||
            context.Facts != parent.AfterTurnEnded.Items[parent.AfterTurnEnded.ItemIndex].Facts ||
            ActualTurnDeathCounts(f.OwnerSeat) != (deaths, kills) || deaths <= 0 || kills < 0 || kills > deaths)
            throw new InvalidOperationException("Death benefit draw requires exact frozen deaths from its completed actual turn.");
        return checked(deaths + 2 * kills);
    }
    [ProgramCapability]
    private sealed class ForcedPhaseAlcoholCapability(GameEngine engine) : IForcedPhaseAlcoholHost
    {
        public SkillProgramStepOutcome UsePhaseOwnerVirtualAlcohol(ProgramSkillFrame f, int damageToOther) =>
            engine.BeginPhaseOwnerAlcohol(f, damageToOther);
    }
}
