namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramCapability]private sealed class QushiCapability(GameEngine e):IQushiHost
    {public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginQushi(f,op);}
    private SkillProgramStepOutcome BeginQushi(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(f.Qushi is not null||f.InstructionIndex!=1||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||!GuoTuQushiComposition.Handles(op))throw new InvalidOperationException("Qushi requires its original single native instruction.");
        QushiSeal? seal=null;
        if(op==SkillProgramEffectOp.ResolveQushiDrawAndPrivateSeal)
        {if(f.ActivationId!="draw-seal"||f.TriggerId is not null||f.WindowContext is not null||f.OwnerSeat!=_currentSeat||_phase!=TurnPhase.Play||!_players[f.OwnerSeat].IsAlive||!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId))throw new InvalidOperationException("Qushi requires the original living actual Play activation.");}
        else
        {var c=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,f.WindowContext!.OccurrenceIndex);if(!IsIssuedQushiEndingCandidate(c,f.WindowContext)||CurrentQushiEndingSeal(c,f.WindowContext)is not{}issued)throw new InvalidOperationException("Qushi cannot remove a different private seal or borrow an unissued Ending.");seal=issued;}
        var r=new QushiReceipt(QushiSource(f),f.GameplayHash,op,_turnNumber,_currentSeat,f.WindowContext?.ParentFrameId??0,seal is null?QushiStage.DrawChildren:QushiStage.RemoveChildren){Seal=seal,HolderSeat=seal?.HolderSeat,CardId=seal?.CardId};
        ReplaceRuntimeTop(f=f with{Qushi=r});AdvanceEventRulesAndQueueFact(new QushiStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.ActualTurn,r.TurnOwner,r.ParentId));
        if(seal is null)BeginQushiDraw(f,1,QushiStage.DrawChildren);else RemoveQushiSeal(f,seal);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private PromptChoice QushiChoice(ProgramSkillFrame f,string branch,string label,int? card=null,int? target=null)=>new(new($"qushi.{f.Id}.{branch}.{card??target??-1}"),label,card is{}id?[id]:[],target is{}seat?[seat]:[],new Dictionary<string,string>{{"program-action","qushi"},{"branch",branch}});
    private IReadOnlyList<PromptChoice> QushiChoices(ProgramSkillFrame f)
    {
        if(!_players[f.OwnerSeat].IsAlive||_winner!=Winner.None)return [];var r=f.Qushi!;
        if(r.Stage==QushiStage.SealCard)return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id,f.OwnerSeat,f.OwnerSeat,[CardZoneKind.Hand],OwnedCardMoveIntent.Transfer,canSelect:(_,card)=>!card.IsGeneralWeapon).Select(c=>QushiChoice(f,"card",$"扣置【{GetAdvancedCard(c.Cards.Single()).DisplayName}】",card:c.Cards.Single())).ToArray());
        if(r.Stage==QushiStage.SealTarget&&r.CardId is{}id&&_cardZones.GetLocation(id)==CardLocation.Hand(f.OwnerSeat))return Array.AsReadOnly(_players.Where(p=>p.IsAlive&&p.Seat!=f.OwnerSeat).Select(p=>QushiChoice(f,"holder",$"扣置于{p.Name}的武将牌",target:p.Seat)).ToArray());return [];
    }
    private void PublishQushi(ProgramSkillFrame f)
    {
        var choices=QushiChoices(f);if(choices.Count==0){FinishQushi(f);return;}ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});var s=_contentRegistry.GetSkill(f.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,f.Qushi!.Stage==QushiStage.SealCard?"趋势：私下选择一张手牌。":"趋势：选择一名其他角色扣置原牌。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=true,Choices=choices,SkillPrompt=new(f.SkillId,s.Name,s.Name,s.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("qushi")]private void AnswerQushi(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Qushi:{}r}f||_pendingDecision?.PlayerSeat!=f.OwnerSeat||!QushiChoices(f).Any(c=>AssistedChoicesEqual([c],[choice])))throw new InvalidOperationException("Qushi requires its exact private original Hand/holder choice.");
        AssertQushi(f);ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});
        if(r.Stage==QushiStage.SealCard){ReplaceRuntimeTop(f=f with{Qushi=r with{CardId=choice.Cards.Single(),Stage=QushiStage.SealTarget}});PublishQushi(f);return;}
        var holder=choice.Targets.Single();var card=GetAdvancedCard(r.CardId!.Value);var location=new CardLocation(CardZoneKind.PrivatePersistentPile,holder,$"qushi:{f.Id}");_cardZones.EnsurePrivatePersistentPile(location);
        StartQushiMovement(ref f,QushiStage.SealChildren,holder);MoveCard(card,CardLocation.Hand(f.OwnerSeat),location,new(QushiSealReason),m=>
        {var seal=new QushiSeal(f.Id,r.Source,r.GameplayHash,holder,card.Id,card.Kind,GetProgramCardCategory(card.Kind),location,m.Sequence);var now=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(now with{Qushi=now.Qushi! with{Seal=seal,HolderSeat=holder}});AdvanceEventRulesAndQueueFact(new QushiSealPlacedEvent(seal));});
        CompleteQushiMovement(f.Id,1);
    }
    private void StartQushiMovement(ref ProgramSkillFrame f,QushiStage stage,int participant)
    {
        foreach(var p in _players)_observedSkillGrantRevisions.TryAdd(p.Seat,p.SkillGrants.Revision);var r=f.Qushi!;
        ReplaceRuntimeTop(f=f with{Qushi=r with{Paid=true,Stage=stage,Step=r.Step+1,Before=_movementSequence,After=_movementSequence,HolderSeat=stage==QushiStage.SealChildren?participant:r.HolderSeat},PendingMovementContinuation=new(participant,0,null)});
    }
    private void CompleteQushiMovement(long id,int count)
    {var f=GetActiveProgramFrame(id);var r=f.Qushi!;ReplaceRuntimeTop(f=f with{Qushi=r with{After=_movementSequence}});AdvanceEventRulesAndQueueFact(new QushiMovementPaidEvent(id,r.Step,r.Stage,r.Before,_movementSequence,count));ContinueQushi(id);}
    private void BeginQushiDraw(ProgramSkillFrame f,int amount,QushiStage stage)
    {
        StartQushiMovement(ref f,stage,f.OwnerSeat);DrawProgramCards(f.Id,f.OwnerSeat,amount,null,null,SkillProgramCardSetVisibility.Private,new(QushiDrawReason));var r=f.Qushi!;
        CompleteQushiMovement(f.Id,_cardMovements.Count(m=>m.Sequence>r.Before&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==QushiDrawReason));
    }
    private void RemoveQushiSeal(ProgramSkillFrame f,QushiSeal seal)
    {
        if(_cardZones.GetLocation(seal.CardId)!=seal.Location)throw new InvalidOperationException("Qushi removal cannot replace its original character-zone entity.");
        StartQushiMovement(ref f,QushiStage.RemoveChildren,seal.HolderSeat);MoveCard(GetAdvancedCard(seal.CardId),seal.Location,CardLocation.DiscardPile,new(QushiRemoveReason),m=>AdvanceEventRulesAndQueueFact(new QushiSealRemovedEvent(f.Id,seal.SealId,seal.HolderSeat,seal.CardId,m.Sequence,false)));CompleteQushiMovement(f.Id,1);
    }
    private bool DrainQushi(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueQushi(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Qushi:{}r}f||f.Id!=id)return;AssertQushi(f);
        if(r.Stage is QushiStage.SealCard or QushiStage.SealTarget){if(_pendingDecision is null)PublishQushi(f);return;}
        if(DrainQushi(f))return;f=GetActiveProgramFrame(id);r=f.Qushi!;ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null});
        if(r.Stage==QushiStage.DrawChildren&&_players[f.OwnerSeat].IsAlive&&_winner==Winner.None){ReplaceRuntimeTop(f=f with{Qushi=r with{Stage=QushiStage.SealCard}});PublishQushi(f);return;}
        if(r.Stage==QushiStage.RemoveChildren)
        {var uses=QushiActualUses(r.HolderSeat!.Value);var same=uses.Any(u=>GetProgramCardCategory(u.EffectiveKind)==r.Seal!.Category);var maximum=uses.Select(u=>u.TargetCount).DefaultIfEmpty(0).Max();var count=same?Math.Min(5,maximum):0;ReplaceRuntimeTop(f=f with{Qushi=r with{DrawCount=count}});AdvanceEventRulesAndQueueFact(new QushiRewardIssuedEvent(f.Id,r.Seal!.SealId,same,maximum,count,r.ActualTurn,r.HolderSeat.Value));if(count>0&&_players[f.OwnerSeat].IsAlive&&_winner==Winner.None){BeginQushiDraw(f,count,QushiStage.RewardChildren);return;}}
        FinishQushi(f);
    }
    private void FinishQushi(ProgramSkillFrame f){AdvanceEventRulesAndQueueFact(new QushiCompletedEvent(f.Id));ReplaceRuntimeTop(f=f with{Qushi=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    [ProgramContinuation(ProgramContinuationKind.Resume,27460)]private bool ResumeQushi(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Qushi:not null}f||f.Id!=id)return false;ContinueQushi(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,27460)]private bool ReturnQushiMovement(ProgramSkillFrame f){if(f.Qushi is null)return false;ContinueQushi(f.Id);return true;}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27460)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27460)]private bool ContinuePaidQushi(ProgramSkillFrame f)
    {
        if(f.Qushi is{Paid:true})return ValidQushi(f);
        if(f.TriggerId!="issued-holder-ending"||f.Qushi is not null||f.WindowContext is not{}context||f.InstructionIndex is not(0 or 1)||!ProgramEventHistory<ProgramBindingStartedEvent>().Any(e=>e.FrameId==f.Id&&e.SkillInstanceId==f.SkillInstanceId&&e.BindingId==f.TriggerId))return false;
        return IsIssuedQushiEndingCandidate(new(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,context.OccurrenceIndex),context);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveQushiDrawAndPrivateSeal,SkillProgramEffectOp.ResolveQushiIssuedHolderEnding)]private PromptChoice ChooseQushi(PendingDecision p,ProgramSkillFrame f)=>p.Choices.OrderBy(c=>c.Cards.Count==0?0:GetKeepValue(GetAdvancedCard(c.Cards[0]),_players[f.OwnerSeat])).ThenBy(c=>c.Targets.FirstOrDefault()).First();
}
