using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string SunHaoCanshiDiscardReason="skill-program.sun-hao.canshi.discard";
    private static CardConversionSource SunHaoSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    [ProgramCapability] private sealed class SunHaoCapability(GameEngine engine):ISunHaoHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>engine.BeginSunHao(f,op); }
    private bool SunHaoCanshiUseKind(CardActionContext a)=>a.Type==CardActionType.Use ? IsSlashCard(a.EffectiveKind)||IsOrdinaryTrick(a.EffectiveKind) :
        a.Type==CardActionType.Response && a.EffectiveKind==CardKind.Nullification;
    private IReadOnlyList<SunHaoCanshiGrantedEvent> CurrentSunHaoGrants(int owner)
    {
        if(!IsValidPlayerSeat(owner)||_turnNumber<=0)return [];
        var grants=ProgramEventHistory<SunHaoCanshiGrantedEvent>().Where(g=>g.Source.OwnerSeat==owner&&g.ActualTurn==_turnNumber&&g.TurnOwner==_currentSeat).OrderBy(g=>g.FrameId).ToArray();
        return grants.Length==0||!SunHaoActualTurnOpen?[]:Array.AsReadOnly(grants);
    }
    private bool HasSunHaoCanshiResponseObligation(CardActionContext action,ProgramCardContinuation continuation)=>HasSunHaoCanshiCapability&&
        continuation==ProgramCardContinuation.NullificationResponse&&action.Type==CardActionType.Response&&action.EffectiveKind==CardKind.Nullification&&
        IsValidPlayerSeat(action.ActorSeat)&&_players[action.ActorSeat].IsAlive&&CurrentSunHaoGrants(action.ActorSeat).Count>0;
    [ProgramPipelineHook(ProgramPipelinePhase.CollectIssuedCardTrigger, 18)]
    private void CollectIssuedSunHaoCanshiCandidates(CardActionContext action,SkillProgramTriggerWindow window,List<ProgramCardTriggerCandidate> result)
    {
        if(!HasSunHaoCanshiCapability||window!=SkillProgramTriggerWindow.CardUseCommitted||!SunHaoCanshiUseKind(action)||_winner!=Winner.None||!_players[action.ActorSeat].IsAlive)return;
        foreach(var g in CurrentSunHaoGrants(action.ActorSeat).DistinctBy(g=>(g.Source.SkillId,g.Source.SkillInstanceId)))
        {
            var p=_contentRegistry.GetSkill(g.Source.SkillId).Program;
            if(p is null||p.GameplayHash!=g.GameplayHash)continue;
            var t=p.Triggers.Single(t=>t.Id=="issued-turn-use-discard");
            if(result.Any(c=>c.OwnerSeat==action.ActorSeat&&c.SkillId==g.Source.SkillId&&c.SkillInstanceId==g.Source.SkillInstanceId&&c.TriggerId==t.Id))continue;
            var c=CreateCardActionProgramContext(action,window,0,action.ActorSeat,action.ActorSeat,CaptureProgramTriggerFacts(_players[action.ActorSeat],action));
            result.Add(new(action.ActorSeat,action.ActorSeat,g.Source.SkillId,t.Id,g.GameplayHash,g.Source.SkillInstanceId,t.Priority,c));
        }
    }
    private bool IsIssuedSunHaoCanshiCandidate(ProgramTriggerCandidate candidate,ProgramSkillWindowContext c)
    {
        if(candidate.SkillId!="ol:sun-hao-canshi"||candidate.BindingId!="issued-turn-use-discard"||c is not{Window:SkillProgramTriggerWindow.CardUseCommitted,CardUse:{} card}||c.OwnerSeat!=candidate.OwnerSeat||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w=>w.Id==c.ParentFrameId) is not{} w||w.CandidateIndex<0||w.CandidateIndex>=w.Candidates.Count||
            !MaMiDiCandidateMatches(candidate,ToSharedCandidate(w.Candidates[w.CandidateIndex]))||CreateCardActionProgramContext(w,w.Candidates[w.CandidateIndex])!=c||
            w.Action.ActionId!=card.CardActionId||w.Action.ActorSeat!=candidate.OwnerSeat||!SunHaoCanshiUseKind(w.Action)||!_players[candidate.OwnerSeat].IsAlive||_winner!=Winner.None)return false;
        if(w.Action.Type==CardActionType.Use)
        { if(LifecycleCardUse(w.ParentFrameId) is not{} use||use.Action?.ActionId!=w.Action.ActionId||use.SourceSeat!=candidate.OwnerSeat||!ProgramEventHistory<CardUseDeclaredEvent>().Any(e=>e.ResolutionId==use.Id))return false; }
        else if(w.CompletedResponseReturn is not{Kind:ProgramCompletedResponseKind.Nullification,IsCommitted:true}||!IsCompletedResponseUse(w.Action,ProgramCardContinuation.NullificationResponse))return false;
        return CurrentSunHaoGrants(candidate.OwnerSeat).Any(g=>g.Source.SkillId==candidate.SkillId&&g.Source.SkillInstanceId==candidate.SkillInstanceId&&g.GameplayHash==candidate.GameplayHash)&&
            !ProgramEventHistory<SunHaoCanshiStartedEvent>().Any(e=>e.WindowId==w.Id&&e.Source.SkillInstanceId==candidate.SkillInstanceId);
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27330)]
    private bool CanOfferSunHao(ProgramTriggerCandidate candidate,SkillProgramTrigger trigger,ProgramSkillWindowContext c)
    {
        if(trigger.Effects is not[var e]||!SunHaoComposition.Handles(e.Op))return true;
        if(e.Op==SkillProgramEffectOp.ResolveSunHaoCanshiUseDiscard)return IsIssuedSunHaoCanshiCandidate(candidate,c);
        return candidate.SkillId=="ol:sun-hao-canshi"&&candidate.BindingId=="normal-draw-add-wounded"&&c.Window==SkillProgramTriggerWindow.DrawPhaseStarting&&c.OwnerSeat==candidate.OwnerSeat&&
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w=>w.Id==c.ParentFrameId) is{NormalDrawReplaced:false,Continuation:ProgramLifecycleContinuation.CompleteDrawPhase}&&
            SunHaoActualTurnOpen&&candidate.OwnerSeat==_currentSeat&&_players.Any(p=>p.IsAlive&&IsRuleWounded(p));
    }
    private SkillProgramStepOutcome BeginSunHao(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(f.SunHaoCanshi is not null||f.InstructionIndex!=1||f.TriggerId is null||f.WindowContext is not{} c||!_players[f.OwnerSeat].IsAlive)throw new InvalidOperationException("Sun Hao requires his exact native issuing instruction.");
        var candidate=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,c.OccurrenceIndex);
        if(!CanOfferSunHao(candidate,GetProgramTrigger(f),c))throw new InvalidOperationException("Sun Hao lost his current normal-draw or already-issued actual-turn entitlement.");
        if(op==SkillProgramEffectOp.GrantSunHaoCanshiDrawObligation)
        {
            var x=_players.Count(p=>p.IsAlive&&IsRuleWounded(p));if(x<=0)throw new InvalidOperationException("Zero wounded characters cannot issue Canshi.");
            AdjustProgramNormalDraw(f,x);
            AdvanceEventRulesAndQueueFact(new SunHaoCanshiGrantedEvent(f.Id,SunHaoSource(f),f.GameplayHash,_turnNumber,_currentSeat,c.ParentFrameId,x));
            return SkillProgramStepOutcome.Continue;
        }
        var w=_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w=>w.Id==c.ParentFrameId);
        var grants=CurrentSunHaoGrants(f.OwnerSeat).Where(g=>g.Source.SkillId==f.SkillId&&g.Source.SkillInstanceId==f.SkillInstanceId&&g.GameplayHash==f.GameplayHash).ToArray();
        var grant=grants[0];
        var use=w.Action.Type==CardActionType.Use?LifecycleCardUse(w.ParentFrameId):null;
        var r=new SunHaoCanshiReceipt(SunHaoSource(f),f.GameplayHash,grant.FrameId,_turnNumber,_currentSeat,w.Id,w.Action.ActionId,use?.Id,SunHaoCanshiStage.SelectDiscard,use?.TargetSeats??[]){GrantIds=grants.Select(g=>g.FrameId).ToArray()};
        ReplaceRuntimeTop(f=f with{SunHaoCanshi=r});
        AdvanceEventRulesAndQueueFact(new SunHaoCanshiStartedEvent(f.Id,r.Source,r.GameplayHash,r.GrantId,r.ActualTurn,r.TurnOwner,r.WindowId,r.ActionId,r.UseId,r.OriginalTargets.Count,r.GrantIds.Count));
        for(var i=0;i<r.GrantIds.Count;i++)AdvanceEventRulesAndQueueFact(new SunHaoCanshiFrozenGrantEvent(f.Id,i,r.GrantIds[i]));
        for(var i=0;i<r.OriginalTargets.Count;i++)AdvanceEventRulesAndQueueFact(new SunHaoCanshiOriginalTargetEvent(f.Id,i,r.OriginalTargets[i]));
        ContinueSunHao(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }
    private IReadOnlyList<PromptChoice> SunHaoChoices(ProgramSkillFrame f)=>f.SunHaoCanshi is not{Stage:SunHaoCanshiStage.SelectDiscard}||!_players[f.OwnerSeat].IsAlive||_winner!=Winner.None ? [] :
        Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id,f.OwnerSeat,f.OwnerSeat,[CardZoneKind.Hand,CardZoneKind.Equipment],OwnedCardMoveIntent.Discard)
            .Select(c=>PromptChoiceProjection.Freeze(c with{Parameters=new Dictionary<string,string>(c.Parameters){["program-action"]="sun-hao-canshi",["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture)}})).ToArray());
    private void PublishSunHao(ProgramSkillFrame f,IReadOnlyList<PromptChoice> choices)
    {
        var skill=_contentRegistry.GetSkill(f.SkillId);ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"残蚀：弃置一张手牌或装备牌，然后继续使用此牌。",choices.SelectMany(c=>c.Cards).Distinct().ToArray(),[],f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=true,TargetSeat=f.OwnerSeat,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("sun-hao-canshi")]
    private void AnswerSunHao(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{SunHaoCanshi:{Stage:SunHaoCanshiStage.SelectDiscard} r} f||_pendingDecision?.PlayerSeat!=f.OwnerSeat||!SunHaoChoices(f).Any(c=>AssistedChoicesEqual([c],[choice])))
            throw new InvalidOperationException("Canshi requires the exact current owner's legal private HE payment.");
        AssertSunHao(f);ClearPendingDecision();var id=choice.Cards.Single();var from=_cardZones.GetLocation(id);var card=GetAdvancedCard(id);
        var grain=from==CardLocation.Equipment(f.OwnerSeat)&&card.Kind==CardKind.WoodenOx?GetWoodenOxGrain(_players[f.OwnerSeat]).Select(c=>c.Id).ToArray():[];
        var before=_movementSequence;
        ReplaceRuntimeTop(f=f with{SunHaoCanshi=r with{Stage=SunHaoCanshiStage.DiscardChildren,WoodenOxGrain=grain},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        MoveLogicalDiscard(f.OwnerSeat,f.OwnerSeat,card,from,new(SunHaoCanshiDiscardReason));
        var m=_cardMovements.Single(m=>m.Sequence>before&&m.CardId==id&&m.Reason.Value==SunHaoCanshiDiscardReason);
        var paid=new SunHaoCanshiDiscardedEvent(f.Id,id,card.Kind,from,m.To,before,_movementSequence,r.Cursor);
        f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f with{SunHaoCanshi=f.SunHaoCanshi! with{Payment=paid}});
        AdvanceEventRulesAndQueueFact(paid);ContinueSunHao(f.Id);
    }
    private bool DrainSunHao(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||
        TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueSunHao(long id)
    {
        while(_resolutionStack.LastOrDefault() is ProgramSkillFrame{SunHaoCanshi:{} r} f&&f.Id==id)
        {
            AssertSunHao(f);
            if(r.Stage==SunHaoCanshiStage.DiscardChildren)
            {
                if(DrainSunHao(f))return;
                f=GetActiveProgramFrame(id);r=f.SunHaoCanshi!;
                ReplaceRuntimeTop(f=f with{SunHaoCanshi=r with{Stage=SunHaoCanshiStage.SelectDiscard,Cursor=r.Cursor+1,Payment=null,WoodenOxGrain=[]},PendingMovementContinuation=null});r=f.SunHaoCanshi!;
            }
            if(r.Cursor>=r.GrantIds.Count){FinishSunHao(f);return;}
            var choices=SunHaoChoices(f);if(choices.Count>0){PublishSunHao(f,choices);return;}
            AdvanceEventRulesAndQueueFact(new SunHaoCanshiSkippedEvent(f.Id,r.Cursor,r.GrantIds[r.Cursor]));
            ReplaceRuntimeTop(f with{SunHaoCanshi=r with{Cursor=r.Cursor+1}});
        }
    }
    private void FinishSunHao(ProgramSkillFrame f)
    {AdvanceEventRulesAndQueueFact(new SunHaoCanshiCompletedEvent(f.Id,ProgramEventHistory<SunHaoCanshiDiscardedEvent>().Any(e=>e.FrameId==f.Id)));ReplaceRuntimeTop(f=f with{SunHaoCanshi=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    [ProgramContinuation(ProgramContinuationKind.Resume,27330)] private bool ResumeSunHao(long id)
    {if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{SunHaoCanshi:not null} f||f.Id!=id)return false;ContinueSunHao(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,27330)] private bool ReturnSunHaoMovement(ProgramSkillFrame f)
    {if(f.SunHaoCanshi is null)return false;ContinueSunHao(f.Id);return true;}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27330)]
    private bool ContinueIssuedSunHao(ProgramSkillFrame f)
    {
        if(f.SunHaoCanshi is not null){AssertSunHao(f);return true;}
        return f.InstructionIndex is 0 or 1&&f.TriggerId=="issued-turn-use-discard"&&f.WindowContext is{} c&&
            IsIssuedSunHaoCanshiCandidate(new(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,c.OccurrenceIndex),c);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27330)] private bool ContinueDeadSunHao(ProgramSkillFrame f)
    {if(f.SunHaoCanshi is null)return false;AssertSunHao(f);return true;}
    [ProgramAiChoice(SkillProgramEffectOp.ResolveSunHaoCanshiUseDiscard)]
    private PromptChoice AiSunHao(PendingDecision p,ProgramSkillFrame f)=>p.Choices.OrderBy(c=>c.Cards is[var id]?GetKeepValue(GetAdvancedCard(id),_players[p.PlayerSeat]):0).ThenBy(c=>c.Id.Value,StringComparer.Ordinal).First();
    private bool HasSunHaoCanshiIssuedDistance(long? useId,int actorSeat,int targetSeat)
    {
        if(!HasSunHaoCanshiCapability||useId is not{} id||LifecycleCardUse(id) is not{Action:{} action} use||action.ActorSeat!=actorSeat||use.SourceSeat!=actorSeat)return false;
        return ProgramEventHistory<SunHaoCanshiStartedEvent>().Any(s=>s.UseId==id&&s.ActionId==action.ActionId&&s.Source.OwnerSeat==actorSeat&&
            ProgramEventHistory<SunHaoCanshiOriginalTargetEvent>().Any(t=>t.FrameId==s.FrameId&&t.TargetSeat==targetSeat)&&
            ProgramEventHistory<SunHaoCanshiDiscardedEvent>().Any(p=>p.FrameId==s.FrameId&&p.From.OwnerSeat==actorSeat&&p.Before<p.After&&
                _cardMovements.Any(m=>m.Sequence>p.Before&&m.Sequence<=p.After&&m.CardId==p.CardId&&m.From==p.From&&m.To==p.To&&m.Reason.Value==SunHaoCanshiDiscardReason)));
    }

    [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption, 70)]
    private bool DistanceCardUseExemption70HasSunHaoCanshiIssuedDistance(ProgramCardDistanceQuery q) =>
        HasSunHaoCanshiIssuedDistance(q.CardUseFrameId, q.Actor.Seat, q.Target.Seat);


    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 20)]
    private bool DistancePhysicalSlash20HasSunHaoCanshiIssuedDistance(ProgramSlashDistanceQuery q) =>
        HasSunHaoCanshiIssuedDistance(q.ExistingUseFrameId, q.Actor.Seat, q.Target.Seat);

}
