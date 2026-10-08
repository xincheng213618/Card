namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static string DyingSuitsCursorHash<T>(T value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(value))));
    private DyingSuitsOriginalCursor CaptureDyingSuitsOriginalCursor(DyingFrame dying, ProgramLifecycleTriggerWindowFrame entry)
    {
        var parent = _resolutionStack.Single(f => f.Id == dying.ParentFrameId);
        var attackOwner = parent is DamageFrame damage ? _resolutionStack.Single(f => f.Id == damage.ParentFrameId) : null;
        return new(dying.Id, dying.ParentFrameId, dying.VictimSeat, dying.KillerSeat, dying.Continuation, dying.Step,
            dying.ResponderIndex, dying.ResponderSeats, dying.AttemptedSelfDyingBindings, entry.Id, entry.CandidateIndex, entry.Step,
            entry.Candidates[entry.CandidateIndex], parent.Kind, parent.Step, DyingSuitsCursorHash<ResolutionFrame>(parent),
            attackOwner?.Id, attackOwner is null ? null : DyingSuitsCursorHash<ResolutionFrame>(attackOwner));
    }
    // Pure exact original-token proof. Never queries ActiveDying or a helper which does.
    private bool ExactDyingSuitsOriginalCursor(ProgramSkillFrame root, DyingFrame dying, ProgramLifecycleTriggerWindowFrame entry)
    {
        var r = root.DyingSuits!; var old = r.OriginalCursor;
        if (dying.Id != old.DyingFrameId || dying.ParentFrameId != old.ParentFrameId || dying.VictimSeat != old.VictimSeat ||
            dying.KillerSeat != old.KillerSeat || dying.Continuation != old.Continuation || dying.Step != old.Step ||
            dying.ResponderIndex != old.ResponderIndex || !dying.ResponderSeats.SequenceEqual(old.ResponderSeats) ||
            !dying.AttemptedSelfDyingBindings.SequenceEqual(old.AttemptedSelfDyingBindings) ||
            dying.PendingRecoveryAttempts is { Count: > 0 } || dying.PaidFactionRequestCostRecovery is not null ||
            entry.Id != old.EntryFrameId || entry.CandidateIndex != old.CandidateIndex || entry.Step != old.EntryStep ||
            entry.Candidates[entry.CandidateIndex] != old.Candidate ||
            _resolutionStack.SingleOrDefault(f => f.Id == old.ParentFrameId) is not { } parent ||
            _resolutionStack.FindIndex(f => f.Id == parent.Id) + 1 != _resolutionStack.FindIndex(f => f.Id == dying.Id) ||
            parent.Kind != old.ParentKind || parent.Step != old.ParentStep || DyingSuitsCursorHash<ResolutionFrame>(parent) != old.ParentHash ||
            CompleteProgramEventHistory().OfType<PlayerDyingEvent>().Count(e => e.ResolutionId == dying.Id && e.VictimSeat == dying.VictimSeat && e.KillerSeat == dying.KillerSeat) != 1 ||
            CompleteProgramEventHistory().OfType<DyingSuitsOriginalCursorIssuedEvent>().Count(e => e.ProgramFrameId == root.Id && e.CursorHash == DyingSuitsCursorHash(old)) != 1)
            return false;
        if (dying.Continuation == DyingContinuationKind.Damage)
            return parent is DamageFrame damage && damage.TargetSeat == dying.VictimSeat && damage.ParentFrameId == old.AttackOwnerFrameId &&
                _resolutionStack.SingleOrDefault(f => f.Id == old.AttackOwnerFrameId) is { } owner &&
                _resolutionStack.FindIndex(f => f.Id == owner.Id) + 1 == _resolutionStack.FindIndex(f => f.Id == damage.Id) &&
                DyingSuitsCursorHash<ResolutionFrame>(owner) == old.AttackOwnerHash;
        return old.AttackOwnerFrameId is null && old.AttackOwnerHash is null &&
            (dying.Continuation == DyingContinuationKind.ProgramSkill && parent is ProgramSkillFrame ||
             dying.Continuation == DyingContinuationKind.AttackHpLoss && parent is ProgramSkillFrame or CardUseFrame or JudgmentFrame);
    }
    private bool ValidDyingSuitsReceipt(ProgramSkillFrame f)
    {
        if (f.DyingSuits is not { } r || f.InstructionIndex != 1 || r.InstructionIndex != 1 || r.Source != DyingSuitsSource(f) || r.GameplayHash != f.GameplayHash ||
            r.ActualTurn != _turnNumber || r.TurnOwnerSeat != _currentSeat || r.ActualRound != _roundNumber || r.ActualRound < 1 || !Enum.IsDefined(r.Stage) ||
            !ExactDyingOwnedCardEntry(f,out var dying,out var entry) || dying.Id != r.DyingFrameId || dying.VictimSeat != r.VictimSeat || entry.Id != r.EntryFrameId ||
            !ExactDyingSuitsOriginalCursor(f,dying,entry) ||
            r.SelectedCardIds.Count != r.SelectedLocations.Count || r.SelectedCardIds.Distinct().Count() != r.SelectedCardIds.Count || r.SelectedCardIds.Count > r.RequiredDiscardCount ||
            r.RequiredDiscardCount is < 0 or > 4 || r.SelectedLocations.Any(l => l.OwnerSeat != r.RecipientSeat || l.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) ||
            ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).Instructions is not [{Op:SkillProgramEffectOp.DrawThenDiscardSuitsForDyingPeach}]) return false;
        var draws=CompleteProgramEventHistory().OfType<DyingSuitsDrawIssuedEvent>().Where(e=>e.ProgramFrameId==f.Id).ToArray();
        if(r.Stage==DyingSuitsStage.Recipient)return r.RecipientSeat is null && draws.Length==0 && f.PendingMovementContinuation is null && r.SelectedCardIds.Count==0;
        if(r.RecipientSeat is not {} seat || !IsValidPlayerSeat(seat) || seat==f.OwnerSeat || seat==r.VictimSeat || r.DrawBefore<0 || r.DrawAfter<r.DrawBefore || r.ActualDrawCount is <0 or >4 ||
            draws is not [var draw] || draw != new DyingSuitsDrawIssuedEvent(f.Id,r.Source,r.GameplayHash,r.ActualRound,r.ActualTurn,r.TurnOwnerSeat,r.EntryFrameId,r.DyingFrameId,r.VictimSeat,seat,r.DrawBefore,r.DrawAfter,r.ActualDrawCount) ||
            _cardMovements.Count(m=>m.Sequence>r.DrawBefore && m.Sequence<=r.DrawAfter && m.From==CardLocation.DrawPile && m.To==CardLocation.Hand(seat) && m.Reason.Value==DyingSuitsDrawReason(f)) != r.ActualDrawCount)return false;
        if(r.Stage is DyingSuitsStage.Drawing or DyingSuitsStage.Discarding)
        {if(f.PendingMovementContinuation is not {BeforeCount:0,CoverageResultBind:null} p || p.SubjectSeat!=seat)return false;}
        else if(f.PendingMovementContinuation is not null)return false;
        if(r.Stage is DyingSuitsStage.Drawing or DyingSuitsStage.SelectingDiscard)return r.DiscardSuits.Count==0 && r.DiscardBefore==0 && r.DiscardAfter==0 && r.PeachReturn is null;
        if(r.DiscardBefore<r.DrawAfter || r.DiscardAfter<r.DiscardBefore || r.SelectedCardIds.Count!=r.RequiredDiscardCount || r.DiscardSuits.Count!=r.SelectedCardIds.Count ||
            CompleteProgramEventHistory().OfType<DyingSuitsDiscardPaidEvent>().Count(e=>e.ProgramFrameId==f.Id && e.RecipientSeat==seat && e.Before==r.DiscardBefore && e.After==r.DiscardAfter && e.CardIds.SequenceEqual(r.SelectedCardIds) && e.Suits.SequenceEqual(r.DiscardSuits))!=1)return false;
        for(var i=0;i<r.SelectedCardIds.Count;i++)if(_cardMovements.Count(m=>m.Sequence>r.DiscardBefore && m.Sequence<=r.DiscardAfter && m.CardId==r.SelectedCardIds[i] &&
            m.From==r.SelectedLocations[i] && m.To==CardLocation.DiscardPile && m.Reason.Value==DyingSuitsDiscardReason(f))!=1)return false;
        if(r.PeachReturn is not {} ret)return r.Stage==DyingSuitsStage.Discarding;
        return ret.ProgramFrameId==f.Id && ret.InstructionIndex==1 && ret.Source==r.Source && ret.GameplayHash==r.GameplayHash && ret.DyingFrameId==r.DyingFrameId &&
            ret.VictimSeat==r.VictimSeat && ret.ActorSeat==seat && ret.CardUseFrameId>f.Id && ret.CardActionId>0 &&
            CompleteProgramEventHistory().OfType<DyingSuitsPeachIssuedEvent>().Count(e=>e.Return==ret)==1 &&
            (r.Stage==DyingSuitsStage.PeachIssued ? LifecycleCardUse(ret.CardUseFrameId)is {} use && ValidDyingSuitsPeachUse(use,ret) :
             r.Stage==DyingSuitsStage.Complete && LifecycleCardUse(ret.CardUseFrameId)is null && CompleteProgramEventHistory().OfType<DyingSuitsPeachReturnedEvent>().Count(e=>e.Return==ret)==1);
    }
    private bool ValidDyingSuitsPeachUse(CardUseFrame use,DyingSuitsPeachReturn ret) => use.DyingSuitsPeachReturn==ret && use.Id==ret.CardUseFrameId && use.CardId==0 &&
        use.PhysicalCardIds is {Count:0} && use.CardKind==CardKind.Peach && use.SourceSeat==ret.ActorSeat && use.TargetSeats.SequenceEqual([ret.VictimSeat]) &&
        use.Action is {Type:CardActionType.Use,EffectiveKind:CardKind.Peach} action && action.ActionId==ret.CardActionId && action.ActorSeat==ret.ActorSeat && action.ProviderSeat==ret.ActorSeat &&
        action.PhysicalCards.Count==0 && action.ConversionChain.Count==0 && action.TargetSeats.SequenceEqual(use.TargetSeats) && action.RequesterSeat is null && action.ResponderSeat is null &&
        CompleteProgramEventHistory().OfType<CardUseDeclaredEvent>().Count(e=>e.ResolutionId==use.Id && e.CardId==0 && e.CardKind==CardKind.Peach && e.SourceSeat==ret.ActorSeat)==1 &&
        CompleteProgramEventHistory().OfType<TargetsConfirmedEvent>().Count(e=>e.ResolutionId==use.Id && e.TargetSeats.SequenceEqual([ret.VictimSeat]))==1;
    private void AssertDyingSuits(ProgramSkillFrame f)
    {if(f.DyingSuits is not null && !ValidDyingSuitsReceipt(f))throw new InvalidOperationException("Dying suits lost original live Dying, round issuance, paid invoice or immutable typed Peach return.");}

    // This pure predicate is intentionally independent of ActiveDying and of
    // helpers which query ActiveDying. It hides only its exact original token.
    private bool IsOriginalDyingSuspendedByDyingSuits(DyingFrame dying) => _resolutionStack.OfType<ProgramSkillFrame>().Any(f=>
        f.DyingSuits is {Stage:not DyingSuitsStage.Recipient and not DyingSuitsStage.Complete} r && r.DyingFrameId==dying.Id && DyingSuitsStructuralPrefix(f));
    private bool DyingSuitsStructuralPrefix(ProgramSkillFrame root)
    {
        var index = _resolutionStack.FindIndex(f => f.Id == root.Id);
        if (index < 2 || !ValidDyingSuitsReceipt(root)) return false;
        if (index == _resolutionStack.Count - 1) return true;
        if (!DyingSuitsFirstChild(root, _resolutionStack[index + 1])) return false;
        for (var i = index + 2; i < _resolutionStack.Count; i++)
            if (!DyingSuitsStructuralEdge(_resolutionStack[i - 1], _resolutionStack[i]) &&
                !OrderedPrintedSkillLossStructuralEdge(_resolutionStack[i - 1], _resolutionStack[i]) &&
                !OverflowTargetCancellationStructuralEdge(_resolutionStack[i - 1], _resolutionStack[i]) &&
                !OrderedPrintedSkillLossSkillsChangedEdge(_resolutionStack[i - 1], _resolutionStack[i])) return false;
        return true;
    }
    // The filter's complete ancestry proof is deliberately separate from the
    // mature observer helpers: those helpers may themselves query ActiveDying.
    private bool DyingSuitsStructuralEdge(ResolutionFrame parent, ResolutionFrame child)
    {
        if (child is ProgramSkillFrame observer)
        {
            if (observer.WindowContext is not { } context || context.ParentFrameId != parent.Id ||
                CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == observer.Id && e.OwnerSeat == observer.OwnerSeat &&
                    e.SkillId == observer.SkillId && e.BindingId == observer.TriggerId && e.SkillInstanceId == observer.SkillInstanceId && e.Window == context.Window) != 1) return false;
            return parent switch
            {
                CardsMovedTriggerWindowFrame w => w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count && MountObserverCandidateMatches(observer,w.Candidates[w.CandidateIndex]) && context.MovementBatch?.Id == w.Batch.Id,
                HpChangedTriggerWindowFrame w => w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count && MountObserverCandidateMatches(observer,w.Candidates[w.CandidateIndex]) && context.HpChange == w.Change,
                ProgramLifecycleTriggerWindowFrame w => w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count && MountObserverCandidateMatches(observer,w.Candidates[w.CandidateIndex]) && context.Window == w.Window,
                BeforeDamageProgramWindowFrame w => w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count && MountObserverCandidateMatches(observer,w.Candidates[w.CandidateIndex].Candidate) && context.Window == SkillProgramTriggerWindow.BeforeDamageApplied,
                DamageTriggerWindowFrame w => w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count && MountObserverCandidateMatches(observer,w.Candidates[w.CandidateIndex].ToProgramCandidate()) && context.Window == w.TriggerWindow,
                ProgramCardTriggerWindowFrame w => w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count &&
                    observer.OwnerSeat == w.Candidates[w.CandidateIndex].OwnerSeat && observer.SkillId == w.Candidates[w.CandidateIndex].SkillId && observer.TriggerId == w.Candidates[w.CandidateIndex].TriggerId &&
                    observer.SkillInstanceId == w.Candidates[w.CandidateIndex].SkillInstanceId && observer.GameplayHash == w.Candidates[w.CandidateIndex].GameplayHash && context.CardUse?.CardActionId == w.Action.ActionId,
                ProgramJudgmentTriggerWindowFrame w => w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count &&
                    observer.OwnerSeat == w.Candidates[w.CandidateIndex].OwnerSeat && observer.SkillId == w.Candidates[w.CandidateIndex].SkillId && observer.TriggerId == w.Candidates[w.CandidateIndex].TriggerId &&
                    observer.SkillInstanceId == w.Candidates[w.CandidateIndex].SkillInstanceId && observer.GameplayHash == w.Candidates[w.CandidateIndex].GameplayHash &&
                    context.Window == SkillProgramTriggerWindow.JudgmentFinalized && context.Judgment == w.Judgment,
                JudgmentFrame j => context.Window == SkillProgramTriggerWindow.JudgmentReplacing && context.JudgmentReplacement?.JudgmentFrameId == j.Id &&
                    CurrentJudgmentCandidate(j) is { } candidate && candidate.OwnerSeat == observer.OwnerSeat && candidate.ProgramId == observer.SkillId && candidate.ProgramTriggerId == observer.TriggerId &&
                    candidate.SkillInstanceId == observer.SkillInstanceId && candidate.GameplayHash == observer.GameplayHash,
                DyingFrame d => context.TargetSeat == d.VictimSeat && d.ResponderIndex >= 0 && d.ResponderIndex < d.ResponderSeats.Count && observer.OwnerSeat == d.ResponderSeat &&
                    context.Window is SkillProgramTriggerWindow.SelfDyingResponse or SkillProgramTriggerWindow.DyingResponse,
                ProgramDeathTriggerWindowFrame w => w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count && MountObserverCandidateMatches(observer,w.Candidates[w.CandidateIndex]) && context.Window == SkillProgramTriggerWindow.OwnerDied,
                ProgramKillTriggerWindowFrame w => w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count && MountObserverCandidateMatches(observer,w.Candidates[w.CandidateIndex]) && context.Window == SkillProgramTriggerWindow.CharacterDied,
                _ => false
            };
        }
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.Batch.ParentFrameId == parent.Id && moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(_cardMovements.Contains) &&
                (parent is ProgramSkillFrame p ? moved.ResumeProgramFrameId == p.Id || moved.Batch.AwaitingProgramFrameId == p.Id : moved.Batch.AwaitingProgramFrameId is null);
        if (child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId == parent.Id && hp.Change.ParentFrameId == parent.Id && hp.Change.Amount > 0;
        if (child is RecoveryReplacementFrame recovery) return recovery.ParentFrameId == parent.Id && recovery.Return.ResumeFrameId == parent.Id;
        if (child is JudgmentFrame judgment && parent is ProgramSkillFrame judgmentProgram)
            return IsValidProgramJudgmentContinuation(judgment, judgmentProgram) && CompleteProgramEventHistory().OfType<JudgmentRequestedEvent>().Count(e =>
                e.ResolutionId == judgment.Id && e.ParentResolutionId == parent.Id && e.TargetSeat == judgment.TargetSeat && e.Reason == judgment.Reason) == 1;
        if (child is ProgramJudgmentTriggerWindowFrame finalJudgment && parent is JudgmentFrame finalized)
            return finalJudgment.ParentFrameId == finalized.Id && finalJudgment.Judgment.JudgmentFrameId == finalized.Id && finalJudgment.Judgment.SubjectSeat == finalized.TargetSeat &&
                finalJudgment.Judgment.Reason == finalized.Reason && finalJudgment.Judgment.SourceSeat == finalized.SourceSeat && finalJudgment.Judgment.CardId == finalized.CardId &&
                finalJudgment.Judgment.CardKind == finalized.CardKind && finalJudgment.Judgment.Suit == finalized.Suit && finalJudgment.Judgment.Succeeded == finalized.Succeeded;
        if (child is BeforeDamageProgramWindowFrame before && parent is ProgramSkillFrame { AttackAttempt: { } attempt })
            return before.ParentFrameId == parent.Id && before.Continuation == BeforeDamageProgramContinuation.Attack && (before.ContinuationAttackResolutionId is null || before.ContinuationAttackResolutionId == parent.Id) &&
                before.SourceSeat == attempt.SourceSeat && (before.RedirectedTargetSeat ?? before.TargetSeat) == attempt.TargetSeat && before.Nature == attempt.Nature;
        if (child is DamageFrame damage && parent is ProgramSkillFrame { AttackAttempt: { } actual, AttackReturn: not null })
            return damage.ParentFrameId == parent.Id && damage.SourceSeat == actual.SourceSeat && damage.TargetSeat == actual.TargetSeat && damage.Amount == actual.DamageAmount && damage.Nature == actual.Nature &&
                CompleteProgramEventHistory().OfType<DamageRequestedEvent>().Count(e => e.ResolutionId == damage.Id && e.SourceSeat == damage.SourceSeat && e.TargetSeat == damage.TargetSeat && e.Amount == damage.Amount && e.Nature == damage.Nature) == 1;
        if (child is DamageTriggerWindowFrame damageWindow && parent is DamageFrame applied)
            return damageWindow.ParentFrameId == applied.Id && damageWindow.SourceSeat == applied.SourceSeat && damageWindow.TargetSeat == applied.TargetSeat && damageWindow.TriggerWindow is SkillProgramTriggerWindow.DamageAppliedBeforeDying or SkillProgramTriggerWindow.AfterDamageApplied;
        if (child is DyingFrame dying)
            return dying.ParentFrameId == parent.Id && CompleteProgramEventHistory().OfType<PlayerDyingEvent>().Count(e => e.ResolutionId == dying.Id && e.VictimSeat == dying.VictimSeat && e.KillerSeat == dying.KillerSeat) == 1 &&
                (parent is DamageFrame d && dying.Continuation == DyingContinuationKind.Damage && dying.VictimSeat == d.TargetSeat ||
                 parent is ProgramSkillFrame p && dying.Continuation == DyingContinuationKind.ProgramSkill ||
                 parent is ProgramSkillFrame { AttackAttempt: { } loss } && dying.Continuation == DyingContinuationKind.AttackHpLoss && dying.VictimSeat == loss.TargetSeat && dying.KillerSeat is null);
        if (child is ProgramLifecycleTriggerWindowFrame entry && parent is DyingFrame entered)
            return entry.Window == SkillProgramTriggerWindow.DyingEntering && entry.Continuation == ProgramLifecycleContinuation.ResumeDyingEntry && entry.ResumeDyingFrameId == entered.Id && entry.OwnerSeat == entered.VictimSeat;
        if (child is ProgramLifecycleTriggerWindowFrame state && parent is ProgramSkillFrame changed)
            return state.ResumeProgramFrameId == changed.Id && state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
                state.CharacterStateContinuation == CharacterStateContinuation.Program && state.Window is SkillProgramTriggerWindow.CharacterTurnedOver or SkillProgramTriggerWindow.CharacterTurnedFaceUp or SkillProgramTriggerWindow.CharacterEnteredChain &&
                CompleteProgramEventHistory().OfType<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == changed.Id && e.Change.TargetSeat == state.OwnerSeat && e.Change.Window == state.Window);
        if (child is ProgramCardTriggerWindowFrame cards && parent is CardUseFrame use)
            return cards.ParentFrameId == use.Id && cards.Action.ActionId == use.Action?.ActionId;
        if (RandomEquipmentFrameRidesOn(child, parent)) return true;
        if (child is CardUseFrame delayed && parent is ProgramSkillFrame delayedProgram && delayed.Action is { } delayedAction)
        {
            var plan = ProgramInstructionResolver.Default.Resolve(delayedProgram, _contentRegistry.GetSkill(delayedProgram.SkillId).Program!);
            if (delayedProgram.InstructionIndex < 1 || plan.Instructions[delayedProgram.InstructionIndex - 1] is not
                { Op: SkillProgramEffectOp.UseDiscardedCardAsDelayedTrick, OutputKind: CardKind.SupplyShortage } ||
                delayedProgram.WindowContext is not { Window: SkillProgramTriggerWindow.DiscardPileReceived, MovementBatch: { } batch, MovementIndex: { } occurrence } ||
                occurrence < 0 || occurrence >= batch.Movements.Count || delayedProgram.SelectedTargetSeats is not [var target]) return false;
            var cost = batch.Movements[occurrence];
            return _cardMovements.Contains(cost) && cost.To == CardLocation.DiscardPile && cost.From != cost.To &&
                GetProgramDiscardSource(cost)?.OwnerSeat == delayedProgram.OwnerSeat && delayed.CardId == cost.CardId && delayed.CardKind == CardKind.SupplyShortage &&
                delayed.SourceSeat == delayedProgram.OwnerSeat && delayed.TargetSeats.SequenceEqual([target]) && delayedAction.Type == CardActionType.Use &&
                delayedAction.ActorSeat == delayedProgram.OwnerSeat && delayedAction.ProviderSeat == delayedProgram.OwnerSeat && delayedAction.EffectiveKind == CardKind.SupplyShortage &&
                delayedAction.PhysicalCards is [{ CardId: var physical }] && physical == cost.CardId && delayedAction.ConversionChain.SequenceEqual([DyingSuitsSource(delayedProgram)]) &&
                CompleteProgramEventHistory().OfType<CardUseDeclaredEvent>().Count(e => e.ResolutionId == delayed.Id && e.CardId == delayed.CardId && e.CardKind == delayed.CardKind && e.SourceSeat == delayed.SourceSeat) == 1;
        }
        if (child is NullificationWindowFrame nullification && parent is CardUseFrame trick)
            return nullification.ParentFrameId == trick.Id && nullification.SourceSeat == trick.SourceSeat && nullification.EffectCardId == trick.CardId && nullification.EffectCardKind == trick.CardKind &&
                nullification.TargetSeats.SequenceEqual(trick.TargetSeats) && nullification.CandidateIndex >= 0 && nullification.CandidateIndex <= nullification.CandidateSeats.Count;
        if (child is CardUseFrame rescue && parent is DyingFrame victim)
            return rescue.DyingResponse is { } response && response.ResolutionId == victim.Id && response.ResponderSeat == rescue.SourceSeat &&
                rescue.TargetSeats.SequenceEqual([victim.VictimSeat]) && rescue.CardKind is CardKind.Peach or CardKind.Alcohol;
        if (child is CardUseFrame virtualAlcohol && parent is ProgramSkillFrame responseProgram && virtualAlcohol.CardId == 0 && virtualAlcohol.CardKind == CardKind.Alcohol)
            return responseProgram.WindowContext is { Window: SkillProgramTriggerWindow.SelfDyingResponse } && virtualAlcohol.SourceSeat == responseProgram.OwnerSeat && virtualAlcohol.TargetSeats.SequenceEqual([responseProgram.OwnerSeat]) &&
                ProgramInstructionResolver.Default.Resolve(responseProgram,_contentRegistry.GetSkill(responseProgram.SkillId).Program!).GetPausedInstruction(responseProgram.InstructionIndex).Effect.Op == SkillProgramEffectOp.UseVirtualDyingAlcohol;
        if (child is DeathFrame death && parent is DyingFrame dead) return death.ParentFrameId == dead.Id && death.ReturnKind == DeathReturnKind.Dying && death.VictimSeat == dead.VictimSeat && !_players[dead.VictimSeat].IsAlive;
        if (child is ProgramDeathTriggerWindowFrame ownerDeath && parent is DeathFrame deadParent) return ownerDeath.DeathFrameId == deadParent.Id && ownerDeath.OwnerSeat == deadParent.VictimSeat && ownerDeath.KillerSeat == deadParent.KillerSeat;
        if (child is ProgramKillTriggerWindowFrame kill && parent is DeathFrame killed) return kill.DeathFrameId == killed.Id && kill.VictimSeat == killed.VictimSeat && kill.KillerSeat == killed.KillerSeat;
        return false;
    }
    private bool DyingSuitsFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(!ValidDyingSuitsReceipt(f))return false;var r=f.DyingSuits!;var seat=r.RecipientSeat!.Value;
        if(r.Stage==DyingSuitsStage.PeachIssued)return child is CardUseFrame use && use.Id==r.PeachReturn!.CardUseFrameId && ValidDyingSuitsPeachUse(use,r.PeachReturn);
        if(r.Stage is not (DyingSuitsStage.Drawing or DyingSuitsStage.Discarding))return false;
        var drawing=r.Stage==DyingSuitsStage.Drawing;var before=drawing?r.DrawBefore:r.DiscardBefore;var after=drawing?r.DrawAfter:r.DiscardAfter;var reason=drawing?DyingSuitsDrawReason(f):DyingSuitsDiscardReason(f);
        if(child is CardsMovedTriggerWindowFrame moved)return
            (moved.ResumeProgramFrameId==f.Id || moved.ResumeProgramFrameId is null && moved.Batch.AwaitingProgramFrameId==f.Id) && moved.Batch.ParentFrameId==f.Id &&
            (moved.Batch.AwaitingProgramFrameId is null || moved.Batch.AwaitingProgramFrameId==f.Id) && moved.Batch.OriginOwnerSeat==f.OwnerSeat && moved.Batch.OriginSkillId==f.SkillId && moved.Batch.OriginSkillInstanceId==f.SkillInstanceId &&
            moved.Batch.Movements.Count>0 && moved.Batch.Movements.All(m=>_cardMovements.Contains(m) &&
                (m.Sequence>before && m.Sequence<=after && m.Reason.Value==reason && (drawing?m.From==CardLocation.DrawPile && m.To==CardLocation.Hand(seat):
                 m.To==CardLocation.DiscardPile && r.SelectedCardIds.Select((id,i)=>(id,from:r.SelectedLocations[i])).Any(p=>p.id==m.CardId && p.from==m.From)) ||
                 !drawing && m.Reason==CardMoveReasons.WoodenOxGrainDiscard && m.From==CardLocation.WoodenOxGrain(seat) && m.To==CardLocation.DiscardPile &&
                 _cardMovements.Any(cost=>cost.Sequence>before && cost.Sequence<=after && r.SelectedCardIds.Contains(cost.CardId) && cost.CardKind==CardKind.WoodenOx && cost.From==CardLocation.Equipment(seat) && cost.To==CardLocation.DiscardPile && cost.Reason.Value==reason)));
        var lion=!drawing && _cardMovements.Any(m=>m.Sequence>before && m.Sequence<=after && r.SelectedCardIds.Contains(m.CardId) && m.CardKind==CardKind.SilverLion && m.From==CardLocation.Equipment(seat) && m.To==CardLocation.DiscardPile && m.Reason.Value==reason);
        if(!lion)return false;
        if(child is HpChangedTriggerWindowFrame hp)return hp.ResumeFrameId==f.Id && hp.Change.ParentFrameId==f.Id && hp.Continuation==PostEventContinuation.AwaitedProgramMovement && hp.Change.SourceSeat==seat && hp.Change.TargetSeat==seat && hp.Change.Kind==HpChangeKind.Recovery && hp.Change.Amount==1;
        return child is RecoveryReplacementFrame recovery && RecoveryReplacementFrameRidesOn(recovery,f) && recovery.Return.Continuation==PostEventContinuation.AwaitedProgramMovement &&
            recovery.Attempt.SourceSeat==seat && recovery.Attempt.TargetSeat==seat && recovery.Attempt.Amount==1 && recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason?.Value==reason;
    }
    private bool HistoricalUseObserverEdge(int index)
    {
        var parent=_resolutionStack[index-1];var child=_resolutionStack[index];
        if(parent is CardUseFrame use && (use.DyingSuitsPeachReturn is not null || use.EndingHistoricalUseReturn is not null))
        {
            if(child is CardsMovedTriggerWindowFrame moved)return use.EndingHistoricalUseReturn is {} ret && !use.EndingHistoricalCostDrained && moved.ResumeHistoricalEndingUseFrameId==use.Id && moved.Batch.ParentFrameId==use.Id && moved.Batch.AwaitingProgramFrameId is null &&
                moved.Batch.Movements.Count>0 && moved.Batch.Movements.All(m=>m.CardId==ret.PhysicalCardId && m.From==CardLocation.Hand(use.SourceSeat) && m.To==CardLocation.Processing && m.Reason==CardMoveReasons.Use && m.Sequence>use.EndingHistoricalCostBefore && m.Sequence<=use.EndingHistoricalCostAfter && _cardMovements.Contains(m));
            if(child is HpChangedTriggerWindowFrame hp)return hp.Change.ParentFrameId==use.Id && hp.ResumeFrameId==use.Id && (hp.Continuation==PostEventContinuation.CardUse && hp.CardId==use.CardId || hp.Continuation==PostEventContinuation.HistoricalEndingCardUse && use.EndingHistoricalUseReturn is not null && !use.EndingHistoricalCostDrained);
            if(child is ProgramCardTriggerWindowFrame cards)return cards.ParentFrameId==use.Id && cards.Action.ActionId==use.Action!.ActionId &&
                cards.Continuation is ProgramCardContinuation.CommittedSimpleCard or ProgramCardContinuation.FinalizedSimpleCard or ProgramCardContinuation.CompletedCard or ProgramCardContinuation.CommittedTrick or ProgramCardContinuation.FinalizedTrick or ProgramCardContinuation.CommittedSlash or ProgramCardContinuation.Slash or ProgramCardContinuation.CompletedSlash;
            if(child is RecoveryReplacementFrame recovery)return RecoveryReplacementFrameRidesOn(recovery,use);
        }
        return DyingSuitsStructuralEdge(parent,child) || HalfHandPaidDamageObserverEdge(index) || PaidTargetObserverEdge(index);
    }
    private bool DyingSuitsPaidPrefix(ProgramSkillFrame root)
    {
        var index=_resolutionStack.FindIndex(f=>f.Id==root.Id);if(index<2 || !ValidDyingSuitsReceipt(root))return false;
        if(index==_resolutionStack.Count-1)return true;if(!DyingSuitsFirstChild(root,_resolutionStack[index+1]))return false;
        for(var i=index+2;i<_resolutionStack.Count;i++)
        {
            if(_resolutionStack[i-1] is DyingFrame dying && (IsRoundPricedPileAlcoholRide(i-1,dying) || IsPaidHandRepaymentProgramAlcoholRide(i-1,dying) || IsPaidHandRepaymentRescueRide(i-1,dying) || PolicyCounterspellVirtualAlcoholRide(i-1,dying) || PaidObserverDamageVirtualAlcoholRide(i-1,dying)))return true;
            if(!HistoricalUseObserverEdge(i))return false;
        }return true;
    }
    private ProgramSkillFrame? DyingSuitsObserverRoot() => _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.DyingSuits is not null && DyingSuitsPaidPrefix(f));
    private bool HasDyingSuitsSuspendedAttack(long attackId) => _resolutionStack.OfType<ProgramSkillFrame>().Any(f =>
        f.DyingSuits is { Stage: not DyingSuitsStage.Recipient and not DyingSuitsStage.Complete } r &&
        (r.OriginalCursor.AttackOwnerFrameId == attackId || r.OriginalCursor.ParentFrameId == attackId && r.OriginalCursor.Continuation == DyingContinuationKind.AttackHpLoss) &&
        DyingSuitsStructuralPrefix(f));
    private bool IsDyingSuitsProgramDying() => ActiveDying is {} d && DyingSuitsObserverRoot() is {} root && _resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool AllowsDyingSuitsNestedDamage(ProgramSkillFrame observer,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if(observer.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id!=observer.Id || observer.InstructionIndex<1 || ActiveDying is not null || DyingSuitsObserverRoot() is not {} root || root.Id==observer.Id)return false;
        var effect=ProgramInstructionResolver.Default.Resolve(observer,_contentRegistry.GetSkill(observer.SkillId).Program!).GetPausedInstruction(observer.InstructionIndex).Effect;
        return !sourceLess && effect.Op==SkillProgramEffectOp.Damage && amount==effect.Amount && source==effect.ActorReference && nature==effect.DamageNature &&
            target==(effect.TargetReference is {} reference?ResolveProgramParticipant(observer,reference):ResolveProgramEffectTarget(observer,effect.Target));
    }
    private bool TryAdvanceDyingSuitsSubtree()
    {
        if(_pendingDecision is not null || DyingSuitsObserverRoot() is null)return false;var top=_resolutionStack.LastOrDefault();
        if(top is ProgramSkillFrame {AttackAttempt:not null} attack)
        {if(CurrentDamageAttempt?.ResolutionId!=attack.Id || ActiveDying is not null || attack.AttackReturn is null || _resolutionStack.Any(f=>f is DamageFrame damage && damage.ParentFrameId==attack.Id ||
            f is BeforeDamageProgramWindowFrame before && (before.ContinuationAttackResolutionId ?? before.ParentFrameId)==attack.Id)) return false;
            CompleteDamageAttack(new ProgramAttackHandle(this,attack.Id));AdvanceRulesAndPublishState();return true;}
        if(top is CardUseFrame {DyingSuitsPeachReturn:not null})return false;
        if(top is ProgramSkillFrame or CardsMovedTriggerWindowFrame or HpChangedTriggerWindowFrame or ProgramLifecycleTriggerWindowFrame or ProgramCardTriggerWindowFrame or BeforeDamageProgramWindowFrame or ProgramDeathTriggerWindowFrame or ProgramKillTriggerWindowFrame)
        {AdvanceRuntimeFrame(top.Id);AdvanceRulesAndPublishState();return true;}
        if(top is DeathFrame death){ContinueDeathResolution(death.Id);AdvanceRulesAndPublishState();return true;}
        return false;
    }
    private bool AssertDyingSuitsSubtree()
    {
        var roots=_resolutionStack.OfType<ProgramSkillFrame>().Where(f=>f.DyingSuits is not null).ToArray();if(roots.Length==0)return false;
        foreach(var root in roots)if(!DyingSuitsPaidPrefix(root))throw new InvalidOperationException("Original Dying suits lost its exact paid subtree and original entry token.");
        AssertProgramAttackState();AssertProgramSkillState();AssertPostEventProgramInvariants();return true;
    }

    private void AssertEndingHistoricalUses(ProgramSkillFrame f)
    {
        if(f.EndingHistoricalUses is not {} r)return;
        if(r.InstructionIndex!=1 || f.InstructionIndex!=1 || r.Source!=DyingSuitsSource(f) || r.GameplayHash!=f.GameplayHash || r.ActualTurn!=_turnNumber ||
            !ExactHistoricalEnding(f) || r.EndingFrameId!=f.WindowContext!.ParentFrameId || !r.Slots.SequenceEqual(OwnPlayHistory(f.OwnerSeat)) || r.Slots.Count is <1 or >2 || r.SlotIndex<0 || r.SlotIndex>r.Slots.Count ||
            r.SlotIndex>0 && !CompleteProgramEventHistory().OfType<EndingHistoricalUseReturnedEvent>().Any(e=>e.Return.ProgramFrameId==f.Id && e.Return.SlotIndex==r.SlotIndex-1))throw new InvalidOperationException("Historical Ending lost its own-turn accepted history or first-before-second completion.");
        if(r.UseReturn is {} ret && (ret.Source!=r.Source || ret.GameplayHash!=r.GameplayHash || ret.ProgramFrameId!=f.Id || ret.InstructionIndex!=1 || ret.EndingFrameId!=r.EndingFrameId || ret.ActualTurn!=r.ActualTurn ||
            ret.SlotIndex!=r.SlotIndex || ret.OriginalActionId!=r.Slots[r.SlotIndex].CardActionId || ret.EffectiveKind!=r.Slots[r.SlotIndex].EffectiveKind || LifecycleCardUse(ret.CardUseFrameId)is not {} use || use.EndingHistoricalUseReturn!=ret))
            throw new InvalidOperationException("Historical Ending lost its exact issued whole-use child.");
    }
    private void AssertHistoricalEndingUse(CardUseFrame use)
    {
        if(use.EndingHistoricalUseReturn is not {} ret)return;
        var index=_resolutionStack.FindIndex(frame=>frame.Id==use.Id);
        if(index<1 || _resolutionStack[index-1] is not ProgramSkillFrame f || f.Id!=ret.ProgramFrameId ||
            f.EndingHistoricalUses?.UseReturn!=ret)
            throw new InvalidOperationException("Historical Ending lost its exact suspended program owner.");
        AssertEndingHistoricalUses(f);
        if(use.Id!=ret.CardUseFrameId || use.CardId!=ret.PhysicalCardId || use.PhysicalCardIds is not [var id] || id!=ret.PhysicalCardId ||
            (use.CurrentSlashFirePolicy?.OriginalAction ?? use.Action)is not {Type:CardActionType.Use} original || original.ActionId!=ret.CardActionId || original.ActorSeat!=f.OwnerSeat || original.ProviderSeat!=f.OwnerSeat ||
            original.EffectiveKind!=ret.EffectiveKind || original.PhysicalCards is not [var cost] || cost.CardId!=id || cost.From!=CardLocation.Hand(f.OwnerSeat) || !original.ConversionChain.SequenceEqual([ret.Source]) ||
            use.EndingHistoricalCostBefore<0 || use.EndingHistoricalCostAfter<use.EndingHistoricalCostBefore || _cardMovements.Count(m=>m.Sequence>use.EndingHistoricalCostBefore && m.Sequence<=use.EndingHistoricalCostAfter && m.CardId==id && m.From==cost.From && m.To==CardLocation.Processing && m.Reason==CardMoveReasons.Use)!=1 ||
            CompleteProgramEventHistory().OfType<EndingHistoricalUseIssuedEvent>().Count(e=>e.Return==ret)!=1)throw new InvalidOperationException("Historical Ending's one-Hand cost or exact use identity changed.");
    }
}
