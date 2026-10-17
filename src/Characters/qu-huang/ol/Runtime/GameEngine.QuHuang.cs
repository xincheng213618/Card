using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string QiejianDrawReason="skill-program.qu-huang.qiejian.draw",QiejianDiscardReason="skill-program.qu-huang.qiejian.discard",NishouHoldReason="skill-program.qu-huang.nishou.hold",NishouGiveReason="skill-program.qu-huang.nishou.give",NishouCleanupReason="skill-program.qu-huang.nishou.cleanup";
    [ProgramCapability]private sealed class QuHuangCapability(GameEngine e):IQuHuangHost {public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginQuHuang(f,op);}
    private SkillProgramStepOutcome BeginQuHuang(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);var c=f.WindowContext??throw new InvalidOperationException("Qu Huang requires a genuine native trigger.");var candidate=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,c.OccurrenceIndex);
        if(f.QuHuang is not null||f.InstructionIndex!=1||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||!_players[f.OwnerSeat].IsAlive||(op==SkillProgramEffectOp.ResolveNishouIssuedCurrentPhaseSwap?!IsIssuedNishouCandidate(candidate,c):!QuHuangMovementCandidateCurrent(candidate,c)))throw new InvalidOperationException("Qu Huang lost his original native source, movement or current phase entitlement.");
        var target=op==SkillProgramEffectOp.ResolveQiejianLastHandLoss?c.SourceSeat!.Value:f.OwnerSeat;
        var stage=op==SkillProgramEffectOp.ResolveQiejianLastHandLoss?QuHuangStage.DrawOwner:op==SkillProgramEffectOp.ResolveNishouEquipmentLossChoice?QuHuangStage.NishouChoice:QuHuangStage.SwapTarget;
        var r=new QuHuangReceipt(QuHuangSource(f),f.GameplayHash,op,c.ParentFrameId,_turnNumber,_turnProgression.OwnerSeat,stage,target);
        if(op==SkillProgramEffectOp.ResolveNishouEquipmentLossChoice)r=r with{Equipment=ProgramEventHistory<NishouEquipmentLostEvent>().Single(e=>e.BatchId==c.MovementBatch!.Id&&e.OwnerSeat==f.OwnerSeat)};
        if(op==SkillProgramEffectOp.ResolveNishouIssuedCurrentPhaseSwap)r=r with{Paid=true,DebtId=c.ActualPhaseEnd!.EntitlementId};
        ReplaceRuntimeTop(f=f with{QuHuang=r});AdvanceEventRulesAndQueueFact(new QuHuangStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.ParentId,r.ActualTurn,target));
        if(r.DebtId is{}debt)AdvanceEventRulesAndQueueFact(new NishouSwapDebtConsumedEvent(debt,f.Id,c.ActualPhaseEnd!.BoundaryFrameId));
        if(stage==QuHuangStage.DrawOwner)PayQuHuangDraw(f,f.OwnerSeat,QuHuangStage.DrawOwner);else ContinueQuHuang(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private QuHuangEntity[] QiejianFieldCards(ProgramSkillFrame f)=>new[]{f.OwnerSeat,f.QuHuang!.TargetSeat}.Distinct().Where(s=>_players[s].IsAlive).SelectMany(s=>new[]{CardLocation.Equipment(s),CardLocation.Judgment(s)}.SelectMany(l=>_cardZones.CardsAt(l).Where(c=>!IsForeignEquipmentDiscardPrevented(f.OwnerSeat,c,l,OwnedCardMoveIntent.Discard)&&!IsActiveProgramSourceEquipmentCard(f.OwnerSeat,f.SkillId,f.SkillInstanceId,c)).Select(c=>new QuHuangEntity(c.Id,c.Kind,l)))).ToArray();
    private int[] NishouMinimumTargets(int owner)
    {var alive=_players.Where(p=>p.IsAlive).ToArray();if(alive.Length==0)return [];var least=alive.Min(p=>GetHand(p).Count);return alive.Where(p=>p.Seat!=owner&&GetHand(p).Count==least).Select(p=>p.Seat).ToArray();}
    private PromptChoice QuHuangChoice(ProgramSkillFrame f,string branch,string label,int? card=null,int? seat=null)=>new(new($"qu-huang.{f.Id}.{branch}.{card??seat??-1}"),label,card is{}id?[id]:[],seat is{}s?[s]:[],new Dictionary<string,string>{{"program-action","qu-huang"},{"branch",branch}});
    private IReadOnlyList<PromptChoice> QuHuangChoices(ProgramSkillFrame f)
    {
        var r=f.QuHuang!;var choices=new List<PromptChoice>();
        if(r.Stage==QuHuangStage.QiejianChoice)
        {choices.AddRange(QiejianFieldCards(f).Select(c=>QuHuangChoice(f,"discard",$"弃置场上【{GetAdvancedCard(c.CardId).DisplayName}】",c.CardId,c.From.OwnerSeat)));choices.Add(QuHuangChoice(f,"block","本轮不再对其发动切谏"));}
        if(r.Stage==QuHuangStage.NishouChoice)
        {if(CanIssueQuHuangNishouLightning(f))choices.Add(QuHuangChoice(f,"lightning","将原装备牌当闪电使用",r.Equipment!.CardId));if(NishouSwapAvailable(f.OwnerSeat,r.Equipment?.Phase))choices.Add(QuHuangChoice(f,"defer","本阶段结束时交换手牌"));}
        if(r.Stage==QuHuangStage.SwapTarget)choices.AddRange(NishouMinimumTargets(f.OwnerSeat).Select(s=>QuHuangChoice(f,"swap","与其交换本次原手牌",seat:s)));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishQuHuang(ProgramSkillFrame f)
    {
        var choices=QuHuangChoices(f);if(choices.Count==0){FinishQuHuang(f);return;}var skill=_contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});_pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,skill.Name+"：选择本次结算。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=true,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("qu-huang")]private void AnswerQuHuang(PromptChoice supplied)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{QuHuang:{}r}f||_pendingDecision?.PlayerSeat!=f.OwnerSeat||!QuHuangChoices(f).Any(c=>AssistedChoicesEqual([c],[supplied])))throw new InvalidOperationException("Qu Huang requires his exact private current entity/branch answer.");
        AssertQuHuang(f);ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});
        if(!_players[f.OwnerSeat].IsAlive||_winner!=Winner.None){ContinueQuHuang(f.Id);return;}
        var branch=supplied.Parameters["branch"];
        if(branch=="block"){AdvanceEventRulesAndQueueFact(new QiejianRoundBlockedEvent(f.Id,f.OwnerSeat,r.TargetSeat,_roundNumber));FinishQuHuang(f);return;}
        if(branch=="discard")
        {var card=QiejianFieldCards(f).Single(c=>c.CardId==supplied.Cards.Single());var logical=CaptureNativeLogicalDiscardInvocation(f.OwnerSeat,f.OwnerSeat,[card.CardId],new(QiejianDiscardReason),f.Id);PayQuHuangCards(f,QuHuangStage.FieldChildren,[card],CardLocation.DiscardPile,new(QiejianDiscardReason));CompleteNativeLogicalDiscardInvocation(logical);CompleteQuHuangMovement(f.Id);return;}
        if(branch=="defer")
        {var phase=r.Equipment!.Phase!;ReplaceRuntimeTop(f=f with{QuHuang=r with{Paid=true}});AdvanceEventRulesAndQueueFact(new NishouSwapDebtIssuedEvent(f.Id,r.Source,r.GameplayHash,phase));FinishQuHuang(f);return;}
        if(branch=="lightning"){IssueQuHuangLightning(f);return;}
        if(branch=="swap")
        {
            var target=supplied.Targets.Single();var own=GetHand(_players[f.OwnerSeat]).Select(c=>new QuHuangEntity(c.Id,c.Kind,CardLocation.Hand(f.OwnerSeat))).ToArray();var other=GetHand(_players[target]).Select(c=>new QuHuangEntity(c.Id,c.Kind,CardLocation.Hand(target))).ToArray();
            ReplaceRuntimeTop(f=f with{QuHuang=r with{Paid=true,TargetSeat=target,OwnerCards=own,OtherCards=other}});foreach(var c in own.Concat(other))AdvanceEventRulesAndQueueFact(new NishouOriginalHandFrozenEvent(f.Id,c.From.OwnerSeat!.Value,c.CardId,c.Kind));
            PayQuHuangCards(f,QuHuangStage.HoldOwner,own,CardLocation.Processing,new(NishouHoldReason));CompleteQuHuangMovement(f.Id);return;
        }
        throw new InvalidOperationException("Unknown Qu Huang branch.");
    }
    private void PrepareQuHuangPayment(ref ProgramSkillFrame f,QuHuangStage stage,IReadOnlyList<QuHuangEntity> entities)
    {
        foreach(var p in _players)_observedSkillGrantRevisions.TryAdd(p.Seat,p.SkillGrants.Revision);var r=f.QuHuang!;
        var grain=entities.Where(c=>c.Kind==CardKind.WoodenOx&&c.From.Zone==CardZoneKind.Equipment).SelectMany(c=>_cardZones.CardsAt(CardLocation.WoodenOxGrain(c.From.OwnerSeat!.Value))).Select(c=>c.Id).ToArray();
        ReplaceRuntimeTop(f=f with{QuHuang=r with{Paid=true,Stage=stage,Entities=entities,Grain=grain,Step=r.Step+1,Before=_movementSequence,After=_movementSequence,ActualCount=0},PendingMovementContinuation=new(r.TargetSeat,0,null)});
    }
    private void PayQuHuangCards(ProgramSkillFrame f,QuHuangStage stage,IReadOnlyList<QuHuangEntity> entities,CardLocation destination,CardMoveReason reason)
    {
        var actual=entities.Where(c=>_cardZones.GetLocation(c.CardId)==c.From).ToArray();PrepareQuHuangPayment(ref f,stage,actual);
        if(actual.Length>0)MoveProgramCardsFromMultipleSources(actual.Select(c=>c.CardId).ToArray(),destination,reason,(batch,records)=>{foreach(var m in records)AdvanceEventRulesAndQueueFact(new QuHuangEntityMovedEvent(f.Id,f.QuHuang!.Step,stage,batch,m.CardId,m.CardKind,m.From,m.To,m.Sequence));});
    }
    private void CompleteQuHuangMovement(long id,int? count=null)
    {var f=GetActiveProgramFrame(id);var r=f.QuHuang!;var actual=count??ProgramEventHistory<QuHuangEntityMovedEvent>().Count(e=>e.FrameId==id&&e.Step==r.Step);ReplaceRuntimeTop(f=f with{QuHuang=r with{After=_movementSequence,ActualCount=actual}});AdvanceEventRulesAndQueueFact(new QuHuangMovementPaidEvent(f.Id,r.Step,r.Stage,r.Before,_movementSequence,actual));ContinueQuHuang(id);}
    private void PayQuHuangDraw(ProgramSkillFrame f,int seat,QuHuangStage stage)
    {PrepareQuHuangPayment(ref f,stage,[]);var actual=_players[seat].IsAlive&&_winner==Winner.None?DrawCards(_players[seat],1,true,new(QiejianDrawReason)).Count:0;CompleteQuHuangMovement(f.Id,actual);}
    private bool DrainQuHuang(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueQuHuang(long id)
    {
        while(_resolutionStack.LastOrDefault()is ProgramSkillFrame{QuHuang:{}r}f&&f.Id==id)
        {
            AssertQuHuang(f);
            if(r.Stage==QuHuangStage.Lightning)return;
            if(r.Stage is QuHuangStage.QiejianChoice or QuHuangStage.NishouChoice or QuHuangStage.SwapTarget)
            {if(!_players[f.OwnerSeat].IsAlive||_winner!=Winner.None){FinishQuHuang(f);return;}if(_pendingDecision is null)PublishQuHuang(f);return;}
            if(DrainQuHuang(f))return;f=GetActiveProgramFrame(id);r=f.QuHuang!;ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null});
            if(r.Op==SkillProgramEffectOp.ResolveNishouIssuedCurrentPhaseSwap&&(!_players[f.OwnerSeat].IsAlive||!_players[r.TargetSeat].IsAlive||_winner!=Winner.None)&&r.Stage!=QuHuangStage.Cleanup)
            {var held=r.OwnerCards.Concat(r.OtherCards).Where(c=>IsNishouHeldProcessingCard(c.CardId)).Select(c=>c with{From=CardLocation.Processing}).ToArray();PayQuHuangCards(f,QuHuangStage.Cleanup,held,CardLocation.DiscardPile,new(NishouCleanupReason));CompleteQuHuangMovement(id);return;}
            switch(r.Stage)
            {
                case QuHuangStage.DrawOwner:PayQuHuangDraw(f,r.TargetSeat,QuHuangStage.DrawOther);return;
                case QuHuangStage.DrawOther:ReplaceRuntimeTop(f with{QuHuang=r with{Stage=QuHuangStage.QiejianChoice}});continue;
                case QuHuangStage.HoldOwner:PayQuHuangCards(f,QuHuangStage.HoldOther,r.OtherCards,CardLocation.Processing,new(NishouHoldReason));CompleteQuHuangMovement(id);return;
                case QuHuangStage.HoldOther:PayQuHuangCards(f,QuHuangStage.GiveOwner,r.OtherCards.Where(c=>IsNishouHeldProcessingCard(c.CardId)).Select(c=>c with{From=CardLocation.Processing}).ToArray(),CardLocation.Hand(f.OwnerSeat),new(NishouGiveReason));CompleteQuHuangMovement(id);return;
                case QuHuangStage.GiveOwner:PayQuHuangCards(f,QuHuangStage.GiveOther,r.OwnerCards.Where(c=>IsNishouHeldProcessingCard(c.CardId)).Select(c=>c with{From=CardLocation.Processing}).ToArray(),CardLocation.Hand(r.TargetSeat),new(NishouGiveReason));CompleteQuHuangMovement(id);return;
                default:FinishQuHuang(f);return;
            }
        }
    }
    private void FinishQuHuang(ProgramSkillFrame f){AdvanceEventRulesAndQueueFact(new QuHuangCompletedEvent(f.Id));ReplaceRuntimeTop(f=f with{QuHuang=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    [ProgramContinuation(ProgramContinuationKind.Resume,27420)]private bool ResumeQuHuang(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{QuHuang:not null}f||f.Id!=id)return false;ContinueQuHuang(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,27420)]private bool ReturnQuHuangMovement(ProgramSkillFrame f){if(f.QuHuang is null)return false;ContinueQuHuang(f.Id);return true;}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27420)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27420)]private bool ContinuePaidQuHuang(ProgramSkillFrame f)
    {if(f.QuHuang is{Paid:true}){AssertQuHuang(f);return true;}return f.QuHuang is null&&f.InstructionIndex is 0 or 1&&f.TriggerId=="issued-current-phase-swap"&&f.WindowContext is{}c&&ProgramEventHistory<ProgramBindingStartedEvent>().Any(e=>e.FrameId==f.Id&&e.BindingId==f.TriggerId&&e.SkillInstanceId==f.SkillInstanceId)&&IsIssuedNishouCandidate(new(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,c.OccurrenceIndex),c);}
    [ProgramAiChoice(SkillProgramEffectOp.ResolveQiejianLastHandLoss,SkillProgramEffectOp.ResolveNishouEquipmentLossChoice,SkillProgramEffectOp.ResolveNishouIssuedCurrentPhaseSwap)]private PromptChoice ChooseQuHuang(PendingDecision p,ProgramSkillFrame f)
    {var r=f.QuHuang!;if(r.Stage==QuHuangStage.QiejianChoice)return p.Choices.FirstOrDefault(c=>c.Parameters["branch"]=="discard"&&c.Targets.Single()!=f.OwnerSeat)??p.Choices.First(c=>c.Parameters["branch"]=="block");if(r.Stage==QuHuangStage.NishouChoice)return p.Choices.FirstOrDefault(c=>c.Parameters["branch"]=="defer")??p.Choices[0];return p.Choices.OrderByDescending(c=>GetHand(_players[f.OwnerSeat]).Count-GetHand(_players[c.Targets.Single()]).Count).ThenBy(c=>c.Targets.Single()).First();}
}
