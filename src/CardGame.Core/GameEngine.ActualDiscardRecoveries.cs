using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool CanOfferActualDiscardRecovery(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        var op = GetProgramTrigger(c).Effects.SingleOrDefault(e=>e.Op==SkillProgramEffectOp.RestoreActualDiscardBatch);
        if (op is null) return true;
        return context is { Window:SkillProgramTriggerWindow.DiscardPileReceived, MovementBatch:{ } batch,
            MovementIndex:{ } index, SourceSeat:{ } source } && source != c.OwnerSeat &&
            _players[source].IsAlive && _players[c.OwnerSeat].IsAlive &&
            MatchingActualDiscardRecoveryIndexes(batch,c).Contains(index);
    }

    private SkillProgramStepOutcome BeginActualDiscardRecovery(ProgramSkillFrame f, string stateId)
    {
        if (f.ActualDiscardRecovery is not null || f.WindowContext is not { MovementBatch:{ } batch,SourceSeat:{ } source } context ||
            !CanOfferActualDiscardRecovery(new(f.OwnerSeat,f.SkillId,f.TriggerId!,f.SkillInstanceId,f.GameplayHash,0,context.OccurrenceIndex),context))
            throw new InvalidOperationException("Discard recovery requires its unpaid exact original batch/source opportunity.");
        var phase = batch.DiscardRecoveryPhase!;
        var receipt = new ProgramActualDiscardRecoveryReceipt(f.InstructionIndex,context.ParentFrameId,batch.Id,
            new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId),f.GameplayHash,stateId,phase,source,
            ActualDiscardRecoveryEntities(batch,source),ActualDiscardRecoveryStage.SelectingReturn);
        ReplaceRuntimeTop(f = f with { ActualDiscardRecovery = receipt });
        PublishActualDiscardRecoveryPrompt(f); return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> ActualDiscardRecoveryChoices(ProgramSkillFrame f)
    {
        var r = f.ActualDiscardRecovery!;
        var parameters = new Dictionary<string,string> { ["program-action"]="actual-discard-recovery",["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture) };
        if (r.Stage == ActualDiscardRecoveryStage.SelectingClaim)
            return Array.AsReadOnly(new[]
            {
                new PromptChoice(new($"actual-discard-recovery.frame-{f.Id}.claim"),"获得该次弃置后仍在弃牌堆的其余牌。",[],[],new Dictionary<string,string>(parameters){["option"]="claim"}),
                new PromptChoice(new($"actual-discard-recovery.frame-{f.Id}.skip"),"不获得其余牌。",[],[],new Dictionary<string,string>(parameters){["option"]="skip"})
            });
        return Array.AsReadOnly(r.OriginalEntities.Where(ActualDiscardEntityStillAvailable)
            .Select(e=>new PromptChoice(new($"actual-discard-recovery.frame-{f.Id}.return-{e.CardId}"),
                $"将【{GetAdvancedCard(e.CardId).DisplayName}】交还给 {_players[r.DiscardOwnerSeat].Name}。",[e.CardId],[r.DiscardOwnerSeat],
                new Dictionary<string,string>(parameters){["option"]="return"})).ToArray());
    }

    private void PublishActualDiscardRecoveryPrompt(ProgramSkillFrame f)
    {
        var choices = ActualDiscardRecoveryChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger,f.OwnerSeat,f.ActualDiscardRecovery!.Stage==ActualDiscardRecoveryStage.SelectingReturn
            ? "选择一张本次真实弃置的牌交还给原角色。" : "是否获得该次弃置后仍在弃牌堆的其余牌？",
            choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat)
        { PromptId=CreatePromptId(),Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description) };
        _status=_players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveActualDiscardRecovery(PromptChoice selected)
    {
        var f=_resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Discard recovery lost its owning frame.");
        AssertActualDiscardRecovery(f); var r=f.ActualDiscardRecovery!;
        if (_pendingDecision is not { Kind:DecisionKind.ProgramTrigger } p || p.PlayerSeat!=f.OwnerSeat ||
            !ActualDiscardRecoveryChoices(f).Any(c=>c.Id==selected.Id && c.Cards.SequenceEqual(selected.Cards) && c.Targets.SequenceEqual(selected.Targets) &&
                c.Parameters.OrderBy(x=>x.Key).SequenceEqual(selected.Parameters.OrderBy(x=>x.Key))))
            throw new InvalidOperationException("Discard recovery must name its exact published original entity or claim choice.");
        ClearPendingDecision();
        if (r.Stage==ActualDiscardRecoveryStage.SelectingReturn)
        {
            if (_winner!=Winner.None || !_players[f.OwnerSeat].IsAlive || !_players[r.DiscardOwnerSeat].IsAlive ||
                !HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId) ||
                ActualDiscardRecoveryPhaseUsed(f.OwnerSeat,f.SkillId,r.StateId,r.Phase))
            { CancelProgramBindingAndCleanup(f,"交还前参与者、来源或阶段机会已失效。");return; }
            var id=selected.Cards.Single();var entity=r.OriginalEntities.Single(e=>e.CardId==id);
            if(!ActualDiscardEntityStillAvailable(entity))throw new InvalidOperationException("The original discarded entity left its exact discard provenance.");
            ReplaceRuntimeTop(f with { PendingMovementContinuation=new(f.OwnerSeat,0,null),ActualDiscardRecovery=r with
                { Stage=ActualDiscardRecoveryStage.ReturnPaid,ReturnedCardId=id } });
            MoveCard(GetAdvancedCard(id),CardLocation.DiscardPile,CardLocation.Hand(r.DiscardOwnerSeat),
                new($"skill-program.{f.SkillId}.actual-discard-return"),move=>
                {
                    var active=GetActiveProgramFrame(f.Id);
                    ReplaceRuntimeTop(active with { ActualDiscardRecovery=active.ActualDiscardRecovery! with { ReturnMovementSequence=move.Sequence } });
                    AdvanceEventRulesAndQueueFact(new ActualDiscardRecoveryReturnedEvent(f.Id,r.Source,r.GameplayHash,r.StateId,r.Phase,
                        r.BatchId,r.DiscardOwnerSeat,id,move.Sequence));
                });
        }
        else if(r.Stage==ActualDiscardRecoveryStage.SelectingClaim)
        {
            var claim=selected.Parameters["option"]=="claim" && _players[f.OwnerSeat].IsAlive && _winner==Winner.None;
            var ids=claim ? r.OriginalEntities.Where(e=>e.CardId!=r.ReturnedCardId && ActualDiscardEntityStillAvailable(e)).Select(e=>e.CardId).ToArray() : [];
            var before=_cardMovements.LastOrDefault()?.Sequence??0;
            ReplaceRuntimeTop(f with { PendingMovementContinuation=new(f.OwnerSeat,0,null),ActualDiscardRecovery=r with
                { Stage=ActualDiscardRecoveryStage.ClaimPaid,ClaimRemaining=claim,ClaimedCardIds=ids,ClaimSequenceBefore=before } });
            if(ids.Length>0)MoveCards(ids.Select(GetAdvancedCard).ToArray(),CardLocation.DiscardPile,CardLocation.Hand(f.OwnerSeat),
                new($"skill-program.{f.SkillId}.actual-discard-claim"));
            var after=_cardMovements.LastOrDefault()?.Sequence??before;
            var active=GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(active with { ActualDiscardRecovery=active.ActualDiscardRecovery! with { ClaimSequenceAfter=after } });
            AdvanceEventRulesAndQueueFact(new ActualDiscardRecoveryClaimedEvent(f.Id,r.BatchId,ids,before,after));
        }
        else throw new InvalidOperationException("Discard recovery cannot repay a completed choice.");
        AdvanceRuntimeProgram(f.Id);
    }

    private bool DrainCapturedEquipmentOrDiscardChildren(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id);

    private bool ResumeActualDiscardRecovery(long id)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id!=id || f.ActualDiscardRecovery is not { } r)return false;
        AssertActualDiscardRecovery(f);
        if(r.Stage is ActualDiscardRecoveryStage.SelectingReturn or ActualDiscardRecoveryStage.SelectingClaim)return true;
        if(DrainCapturedEquipmentOrDiscardChildren(f))return true;
        f=GetActiveProgramFrame(id); r=f.ActualDiscardRecovery!;
        if(f.PendingMovementContinuation is not null)ReplaceRuntimeTop(f=f with { PendingMovementContinuation=null });
        if(r.Stage==ActualDiscardRecoveryStage.ReturnPaid && _winner==Winner.None && _players[f.OwnerSeat].IsAlive &&
            r.OriginalEntities.Any(e=>e.CardId!=r.ReturnedCardId && ActualDiscardEntityStillAvailable(e)))
        {
            ReplaceRuntimeTop(f=f with { ActualDiscardRecovery=r with { Stage=ActualDiscardRecoveryStage.SelectingClaim } });
            PublishActualDiscardRecoveryPrompt(f);return true;
        }
        ReplaceRuntimeTop(f with { ActualDiscardRecovery=null });AdvanceRuntimeProgram(id);return true;
    }

    private bool ReturnCapturedEquipmentOrDiscardMovement(ProgramSkillFrame f)
    {
        if(f.ActualDiscardRecovery is null && f.CapturedEquipmentDraw is null)return false;
        if(f.PendingMovementContinuation is null)throw new InvalidOperationException("A paid actual equipment/discard return lost its movement continuation.");
        ReplaceRuntimeTop(f with { PendingMovementContinuation=null });AdvanceRuntimeProgram(f.Id);return true;
    }

    private PromptChoice SelectAiActualDiscardRecovery(PendingDecision p,ProgramSkillFrame f)
    {
        if(f.ActualDiscardRecovery!.Stage==ActualDiscardRecoveryStage.SelectingClaim)
            return p.Choices.Single(c=>c.Parameters["option"]=="claim");
        return p.Choices.OrderBy(c=>GetKeepValue(GetAdvancedCard(c.Cards.Single()),_players[f.OwnerSeat]))
            .ThenBy(c=>c.Id.Value,StringComparer.Ordinal).First();
    }

    private void AssertActualDiscardRecovery(ProgramSkillFrame f)
    {
        if(f.ActualDiscardRecovery is not { } r)return;
        var effect=ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
        if(effect.Op!=SkillProgramEffectOp.RestoreActualDiscardBatch || effect.StateId!=r.StateId ||r.InstructionIndex!=f.InstructionIndex ||
            r.Source!=new CardConversionSource(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId) ||r.GameplayHash!=f.GameplayHash ||
            f.WindowContext is not { MovementBatch:{ } batch,SourceSeat:{ } source } context ||context.ParentFrameId!=r.WindowFrameId ||
            batch.Id!=r.BatchId || batch.DiscardRecoveryPhase!=r.Phase ||!IsRecordedActualDiscardRecoveryPhase(r.Phase) ||
            batch.TurnNumber!=r.Phase.TurnNumber ||source!=r.DiscardOwnerSeat ||source==f.OwnerSeat ||
            !Enum.IsDefined(r.Stage) ||r.OriginalEntities.Count<2 ||!r.OriginalEntities.SequenceEqual(ActualDiscardRecoveryEntities(batch,source)) ||
            r.OriginalEntities is not System.Collections.IList { IsReadOnly:true } ||
            ActualDiscardRecoveryQualification(batch,source) is not { OriginalCount:>=2 } q || q.OriginalCount!=r.OriginalEntities.Count)
            throw new InvalidOperationException("Discard recovery lost its original public phase/batch/cohort/candidate.");
        var wi=_resolutionStack.FindIndex(x=>x.Id==r.WindowFrameId);
        if(wi<0 ||_resolutionStack[wi] is not CardsMovedTriggerWindowFrame window ||window.Batch!=batch ||window.CandidateIndex<0 ||window.CandidateIndex>=window.Candidates.Count ||
            !MountObserverCandidateMatches(f,window.Candidates[window.CandidateIndex]))
            throw new InvalidOperationException("Discard recovery lost its exact owning movement candidate.");
        if(r.Stage==ActualDiscardRecoveryStage.SelectingReturn &&(r.ReturnedCardId is not null ||r.ReturnMovementSequence!=0) ||
            r.Stage!=ActualDiscardRecoveryStage.ClaimPaid &&(r.ClaimRemaining is not null ||r.ClaimedCardIds is not null ||
                r.ClaimSequenceBefore!=0 ||r.ClaimSequenceAfter!=0))
            throw new InvalidOperationException("Discard recovery carries a payment/tail before its actual issued stage.");
        if(r.Stage!=ActualDiscardRecoveryStage.SelectingReturn)
        {
            if(r.ReturnedCardId is not { } id ||!r.OriginalEntities.Any(e=>e.CardId==id) ||r.ReturnMovementSequence<=0 ||
                _cardMovements.Count(m=>m.Sequence==r.ReturnMovementSequence &&m.CardId==id &&m.From==CardLocation.DiscardPile &&m.To==CardLocation.Hand(source) &&
                    m.Reason.Value==$"skill-program.{f.SkillId}.actual-discard-return")!=1 ||
                CompleteProgramEventHistory().OfType<ActualDiscardRecoveryReturnedEvent>().Count(e=>e.ProgramFrameId==f.Id &&e.Source==r.Source &&
                    e.GameplayHash==r.GameplayHash &&e.StateId==r.StateId &&e.Phase==r.Phase &&e.BatchId==batch.Id &&e.DiscardOwnerSeat==source &&
                    e.CardId==id &&e.MovementSequence==r.ReturnMovementSequence)!=1 ||
                CompleteProgramEventHistory().OfType<ActualDiscardRecoveryReturnedEvent>().Count(e=>e.Source.OwnerSeat==f.OwnerSeat &&
                    e.Source.SkillId==f.SkillId &&e.StateId==r.StateId &&e.Phase.Token==r.Phase.Token)!=1 ||
                _cardMovements.Any(m=>m.CardId==id &&m.Sequence>r.OriginalEntities.Single(e=>e.CardId==id).MovementSequence &&
                    m.Sequence<r.ReturnMovementSequence &&m.From==CardLocation.DiscardPile))
                throw new InvalidOperationException("Discard recovery lost its one actual returned entity and phase consumption.");
        }
        if(r.Stage==ActualDiscardRecoveryStage.ClaimPaid)
        {
            if(r.ClaimRemaining is null ||r.ClaimedCardIds is null ||r.ClaimedCardIds is not System.Collections.IList { IsReadOnly:true } ||
                r.ClaimedCardIds.Distinct().Count()!=r.ClaimedCardIds.Count ||r.ClaimedCardIds.Any(id=>id==r.ReturnedCardId ||!r.OriginalEntities.Any(e=>e.CardId==id)) ||
                r.ClaimSequenceBefore<r.ReturnMovementSequence ||r.ClaimSequenceAfter<r.ClaimSequenceBefore ||!r.ClaimRemaining.Value &&r.ClaimedCardIds.Count>0 ||
                r.ClaimedCardIds.Any(id=>_cardMovements.Any(m=>m.CardId==id &&
                    m.Sequence>r.OriginalEntities.Single(e=>e.CardId==id).MovementSequence &&m.Sequence<=r.ClaimSequenceBefore &&m.From==CardLocation.DiscardPile)) ||
                !_cardMovements.Where(m=>m.Sequence>r.ClaimSequenceBefore &&m.Sequence<=r.ClaimSequenceAfter &&m.From==CardLocation.DiscardPile &&
                    m.To==CardLocation.Hand(f.OwnerSeat) &&m.Reason.Value==$"skill-program.{f.SkillId}.actual-discard-claim")
                    .Select(m=>m.CardId).SequenceEqual(r.ClaimedCardIds) ||
                _cardMovements.Any(m=>m.Sequence>r.ClaimSequenceBefore &&m.Sequence<=r.ClaimSequenceAfter &&
                    !(r.ClaimedCardIds.Contains(m.CardId) &&m.From==CardLocation.DiscardPile &&m.To==CardLocation.Hand(f.OwnerSeat) &&
                      m.Reason.Value==$"skill-program.{f.SkillId}.actual-discard-claim")) ||
                CompleteProgramEventHistory().OfType<ActualDiscardRecoveryClaimedEvent>().Count(e=>e.ProgramFrameId==f.Id &&e.BatchId==r.BatchId &&
                    e.CardIds.SequenceEqual(r.ClaimedCardIds) &&e.SequenceBefore==r.ClaimSequenceBefore &&e.SequenceAfter==r.ClaimSequenceAfter)!=1)
                throw new InvalidOperationException("Discard recovery lost its frozen one-time atomic claim tail.");
        }
    }

    private sealed partial class ProgramSkillHost : IActualDiscardRecoveryProgramHost
    {
        public SkillProgramStepOutcome RestoreActualDiscardBatch(ProgramSkillFrame f,string stateId)=>engine.BeginActualDiscardRecovery(f,stateId);
        public SkillProgramStepOutcome PlaceCapturedEquipmentAndDraw(ProgramSkillFrame f,int target,string bind,int draw)=>engine.BeginCapturedEquipmentAndDraw(f,target,bind,draw);
    }
}
