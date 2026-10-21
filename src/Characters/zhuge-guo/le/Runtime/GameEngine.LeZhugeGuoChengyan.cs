namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 30200, SkillProgramEffectOp.ResolveLeZhugeGuoChengyan)]
    private bool CanOfferLeChengyan(ProgramTriggerLegalityContext gate) => gate.Context.CardUse is { } a &&
        a.ActorSeat == gate.Candidate.OwnerSeat && (IsSlashCard(a.EffectiveKind) || IsOrdinaryTrick(a.EffectiveKind)) &&
        a.DesignatedTargetSeats?.Any(s => s != a.ActorSeat) == true &&
        !ProgramEventHistory<LeChengyanCompletedEvent>().Any(e => e.UseId == a.ParentCardUseFrameId);
    private SkillProgramStepOutcome StartLeChengyan(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != LeChengyan || f.ActivationId != f.TriggerId || f.TriggerId != "replace-use-effect" ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } a } c ||
            _resolutionStack.SingleOrDefault(x => x.Id == c.ParentFrameId) is not ProgramCardTriggerWindowFrame w ||
            LifecycleCardUse(w.ParentFrameId) is not { Action.Type: CardActionType.Use } use || use.Action.ActionId != a.CardActionId || a.ActorSeat != f.OwnerSeat ||
            !(IsSlashCard(a.EffectiveKind) || IsOrdinaryTrick(a.EffectiveKind)) || a.DesignatedTargetSeats?.Any(s => s != f.OwnerSeat) != true ||
            f.LeChengyan is not null || ProgramEventHistory<LeChengyanCompletedEvent>().Any(e => e.UseId == use.Id))
            throw new InvalidOperationException("Chengyan requires its one real targeted Use and native finalized window.");
        ReplaceRuntimeTop(f = f with { LeChengyan = new(LeZhugeGuoSource(f), f.GameplayHash, use.Id, a.CardActionId) });
        return BeginOperationProgramDraw(f, f.OwnerSeat, 1, new(LeChengyanDrawReason));
    }
    private bool ValidLeChengyan(ProgramSkillFrame f) => f.LeChengyan is { } r && r.Source == LeZhugeGuoSource(f) &&
        r.GameplayHash == f.GameplayHash && f.InstructionIndex == 1 && Enum.IsDefined(r.Stage) &&
        f.WindowContext?.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized &&
        _resolutionStack.SingleOrDefault(x => x.Id == f.WindowContext.ParentFrameId) is ProgramCardTriggerWindowFrame w && w.ParentFrameId == r.UseId &&
        LifecycleCardUse(r.UseId)?.Action is { Type: CardActionType.Use } a && a.ActionId == r.ActionId && a.ActorSeat == f.OwnerSeat &&
        r.Before >= 0 && r.Before <= r.After && r.After <= _movementSequence;
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveLeZhugeGuoChengyan)]
    private void ReturnLeChengyanDraw(ProgramSkillFrame f, NativeDrawOutcome d)
    {
        if (!ValidLeChengyan(f) || f.LeChengyan!.Stage is not (LeChengyanStage.FirstDraw or LeChengyanStage.SecondDraw) ||
            d.RecipientSeat != f.OwnerSeat || d.RequestedCount != 1 || d.ActualDrawIds.Count > 1)
            throw new InvalidOperationException("Chengyan requires its exact first or second native Draw1 return.");
        var r = f.LeChengyan; var second = r.Stage == LeChengyanStage.SecondDraw;
        ReplaceRuntimeTop(f = f with { LeChengyan = r with { Stage = second ? LeChengyanStage.SecondDrawChildren : LeChengyanStage.RevealChildren,
            Draw = d, Before = d.SequenceBefore, After = d.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new LeChengyanDrawReturnedEvent(f.Id, second, d.ActualDrawIds.Count, d.ReplacementGainedIds.Count, d.AttemptFrameId));
        if (second)
            foreach (var id in d.ActualDrawIds)
            {
                var m = _cardMovements.Single(m => m.Sequence > d.SequenceBefore && m.Sequence <= d.SequenceAfter && m.CardId == id && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat));
                AdvanceEventRulesAndQueueFact(new LeXidiEntityMarkedEvent(f.Id, LeZhugeGuoSource(f), id, m.Sequence, false));
            }
        ContinueLeChengyan(f.Id);
    }
    private void ContinueLeChengyan(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { LeChengyan: { } r } f || f.Id != id) return;
        if (!ValidLeChengyan(f)) throw new InvalidOperationException("Chengyan lost its original Use, source or child return.");
        if (r.Stage is LeChengyanStage.FirstDraw or LeChengyanStage.SecondDraw) return;
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return;
        f = GetActiveProgramFrame(id); r = f.LeChengyan!;
        if (r.Stage == LeChengyanStage.RevealChildren && r.RevealedCardId is null)
        {
            var card = r.Draw!.ActualDrawIds is [var cardId] && _cardZones.GetLocation(cardId) == CardLocation.Hand(f.OwnerSeat) ? GetAdvancedCard(cardId) : null;
            if (card is not null)
            {
                var kind = AdvancedEffectiveHandKind(_players[f.OwnerSeat], card);
                ReplaceRuntimeTop(f = f with { LeChengyan = r = r with { RevealedCardId = card.Id, RevealedKind = kind } });
                AdvanceEventRulesAndQueueFact(new ProgramCardsRevealedEvent(f.Id, f.SkillId, f.TriggerId!, f.OwnerSeat, "chengyan-shown", [ToSnapshot(card)]));
                AdvanceEventRulesAndQueueFact(new LeChengyanRevealedEvent(f.Id, r.UseId, r.ActionId, card.Id, kind));
                if(TryBeginPublicHandRevealWindow(f.Id,"chengyan-drawn-hand"))return;
                ContinueChengyanAfterPublicReveal(GetActiveProgramFrame(f.Id));return;
            }
        }
        FinishLeChengyan(f, false);
    }
    private void FinishLeChengyan(ProgramSkillFrame f, bool replaced)
    {
        AdvanceEventRulesAndQueueFact(new LeChengyanCompletedEvent(f.Id, f.LeChengyan!.UseId, replaced));
        ReplaceRuntimeTop(f = f with { LeChengyan = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [PublicHandRevealReturn("chengyan-drawn-hand")]
    private void ContinueChengyanAfterPublicReveal(ProgramSkillFrame f)
    {
        var r=f.LeChengyan??throw new InvalidOperationException("Chengyan lost its native draw/reveal receipt.");
        if(r.RevealedCardId is null||r.RevealedKind is not{}kind)throw new InvalidOperationException("Chengyan lost its frozen shown identity.");
        if(kind==CardKind.Nullification){FinishLeChengyan(f,false);return;}
        if(IsSlashCard(kind)||IsOrdinaryTrick(kind))
        {
            var use=LifecycleCardUse(r.UseId);if(use is null){FinishLeChengyan(f,false);return;}
            ReplaceRuntimeFrame(use.Id,use with{LeChengyanEffect=new(f.Id,r.Source,f.GameplayHash,r.ActionId,r.RevealedCardId.Value,kind)});
            AdvanceEventRulesAndQueueFact(new LeChengyanEffectIssuedEvent(f.Id,r.UseId,r.ActionId,r.Source,r.RevealedCardId.Value,kind));FinishLeChengyan(f,true);return;
        }
        if(_players[f.OwnerSeat].IsAlive&&_winner==Winner.None)
        {
            ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null,LeChengyan=r with{Stage=LeChengyanStage.SecondDraw,Draw=null}});
            BeginOperationProgramDraw(f,f.OwnerSeat,1,new(LeChengyanDrawReason));return;
        }
        FinishLeChengyan(f,false);
    }

}
