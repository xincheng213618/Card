using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string HappyLuBuBasicReason="skill-program.happy-wuqian.basic";
    private bool HasHappyLuBuWuqianCapability(CharacterState source)=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.EnhanceHappyShenLuBuFirstUse)&&HasProgramSkill(source,HappyLuBuComposition.Wuqian);
    private static CardConversionSource HappyLuBuSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    [ProgramCapability] private sealed class HappyLuBuHost(GameEngine e):IHappyLuBuHost { public SkillProgramStepOutcome Execute(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginHappyLuBu(f,op); }
    private bool HappyLuBuFirstUse(CardUseFrame use)
    {
        if(use.SourceSeat<0||!HasOngoingActualUseTurn||!IsSlashCard(use.CardKind)&&use.CardKind!=CardKind.Duel)return false;
        return EventsSinceLastBoundary(e=>e is TurnStartedEvent or TurnEndedEvent).OfType<CurrentTurnCardUseKindsRecordedEvent>().Where(e=>e.TurnNumber==_turnNumber&&e.ActorSeat==use.SourceSeat&&e.RoundActionType==CardActionType.Use&&
            (use.CardKind==CardKind.Duel?e.EffectiveCardKind==CardKind.Duel:e.EffectiveCardKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.ColdSlash)).FirstOrDefault()?.OriginFrameId==use.Id;
    }
    private bool TryHappyLuBuUse(int owner,ProgramSkillWindowContext w,out CardUseFrame use,out ProgramCardTriggerWindowFrame window)
    {
        use=null!;window=null!;
        if(w.Window!=SkillProgramTriggerWindow.CardUseCommitted||w.CardUse is not{}id||_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(p=>p.Id==w.ParentFrameId) is not{}p||
            LifecycleCardUse(id.ParentCardUseFrameId) is not{Action:{}a}u||p.Action.ActionId!=a.ActionId||id.CardActionId!=a.ActionId||a.Type!=CardActionType.Use||a.ActorSeat!=owner||u.SourceSeat!=owner||!HappyLuBuFirstUse(u))return false;
        use=u;window=p;return true;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,41200,SkillProgramEffectOp.ObtainHappyShenLuBuTurnBasic,SkillProgramEffectOp.EnhanceHappyShenLuBuFirstUse,SkillProgramEffectOp.ResetHappyShenLuBuShenfen)]
    private bool HappyLuBuEligible(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext w)
    {
        if(t.Effects[0].Op==SkillProgramEffectOp.ObtainHappyShenLuBuTurnBasic)return _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(p=>p.Id==w.ParentFrameId) is{}p&&IsHappyLuBuActualStart(p)&&p.CandidateIndex==0&&p.Candidates[0]==c;
        if(t.Effects[0].Op==SkillProgramEffectOp.EnhanceHappyShenLuBuFirstUse)return TryHappyLuBuUse(c.OwnerSeat,w,out var use,out _)&&use.HappyLuBuWuqian is null;
        var owner=_players[c.OwnerSeat];return owner.IsAlive&&owner.HasHpValue&&owner.Hp==owner.MaxHp&&_skillRuntimeState.GetUsage(owner.Seat,HappyLuBuComposition.Shenfen,HappyLuBuShenfenActivation().UsageGroup,SkillUsageScope.Game)>0;
    }
    private SkillProgramActivation HappyLuBuShenfenActivation()=>_contentRegistry.GetSkill(HappyLuBuComposition.Shenfen).Program!.Activations.Single();
    private SkillProgramStepOutcome BeginHappyLuBu(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);if(f.InstructionIndex!=1||f.WindowContext is not{}w||f.HappyLuBu is not null)throw new InvalidOperationException("Happy Lu Bu requires the original unpaid trigger instruction.");
        if(op==SkillProgramEffectOp.ResetHappyShenLuBuShenfen)
        {
            var parent=_resolutionStack.OfType<DeferredTurnEndFrame>().SingleOrDefault(p=>p.Id==w.ParentFrameId)??throw new InvalidOperationException("Shenfen lost its real completed turn.");
            if(!IsActualAfterTurnEndedParent(parent)||!MountObserverCandidateMatches(f,AfterTurnEndedCandidate(parent))||!_players[f.OwnerSeat].IsAlive||_players[f.OwnerSeat].Hp!=_players[f.OwnerSeat].MaxHp)throw new InvalidOperationException("Shenfen reset requires actual completed-turn full HP.");
            var group=HappyLuBuShenfenActivation().UsageGroup;var before=_skillRuntimeState.GetUsage(f.OwnerSeat,f.SkillId,group,SkillUsageScope.Game);
            if(before!=1||!_skillRuntimeState.ClearUsage(f.OwnerSeat,f.SkillId,group,SkillUsageScope.Game))throw new InvalidOperationException("Shenfen cannot refund an unconsumed limited use.");
            AdvanceEventRulesAndQueueFact(new HappyLuBuShenfenResetEvent(f.Id,HappyLuBuSource(f),f.GameplayHash,_turnNumber,parent.OwnerSeat,group,before));return SkillProgramStepOutcome.Continue;
        }
        CardUseFrame? use=null;
        if(op==SkillProgramEffectOp.EnhanceHappyShenLuBuFirstUse){if(!TryHappyLuBuUse(f.OwnerSeat,w,out var original,out _)||original.HappyLuBuWuqian is not null)throw new InvalidOperationException("Wuqian requires the first real use of this card name.");use=original;}
        var r=new HappyLuBuReceipt(HappyLuBuSource(f),f.GameplayHash,op,w.ParentFrameId,_turnNumber){Stage=use is null?HappyLuBuStage.BasicChildren:HappyLuBuStage.AdditionalTarget,UseId=use?.Id??0,ActionId=use?.Action!.ActionId??0,Before=_movementSequence,After=_movementSequence};
        ReplaceRuntimeTop(f=f with{HappyLuBu=r});AdvanceEventRulesAndQueueFact(new HappyLuBuStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.ParentId,r.ActualTurn,r.UseId,r.ActionId));
        if(use is not null)
        {
            UpdateLifecycleCardUse(use.Id,u=>u with{HappyLuBuWuqian=new(f.Id,w.ParentFrameId,u.Id,r.ActionId,_turnNumber,u.CardKind,r.Source,r.GameplayHash){OriginalTargets=u.TargetSeats}});
            ContinueHappyLuBu(f.Id);return SkillProgramStepOutcome.AwaitChild;
        }
        var candidates=_cardZones.CardsAt(CardLocation.DrawPile).Where(c=>GetProgramCardCategory(c.Kind)==SkillProgramCardCategory.Basic).ToArray();
        if(candidates.Length>0)
        {
            var card=candidates[_random.Next(candidates.Length)];
            MoveProgramCardsFromMultipleSources([card.Id],CardLocation.Hand(f.OwnerSeat),new(HappyLuBuBasicReason),(_,moves)=>
            {var current=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(current with{HappyLuBu=current.HappyLuBu! with{After=_movementSequence,Paid=moves}});AdvanceEventRulesAndQueueFact(new HappyLuBuBasicPaidEvent(f.Id,r.Before,_movementSequence,moves.Count));});
            if(AwaitProgramBoundCardMovements(f.Id,f.OwnerSeat)==SkillProgramStepOutcome.AwaitChild)return SkillProgramStepOutcome.AwaitChild;
        }
        ContinueHappyLuBu(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }
    private bool HappyLuBuExtraTarget(CardUseFrame use,CharacterState target)
    {
        if(!CanBeDistanceFreeDesignatedTarget(use,target))return false;
        return !IsSlashCard(use.CardKind)||CanUseSlashTarget(_players[use.SourceSeat],target,new(-1,use.CardKind,use.Action!.EffectiveSuit??Suit.None,use.Action.EffectiveRank??0),
            use.Action.ConversionChain.FirstOrDefault(),use.CardKind,existingUseFrameId:use.Id,specificEffectiveRank:use.Action.EffectiveRank,physicalCardIds:use.Action.PhysicalCards.Select(c=>c.CardId).ToArray());
    }
    private IReadOnlyList<PromptChoice> HappyLuBuChoices(ProgramSkillFrame f)
    {
        var use=LifecycleCardUse(f.HappyLuBu!.UseId)??throw new InvalidOperationException("Wuqian lost the original Use.");
        Dictionary<string,string> Parameters()=>new(){["program-action"]="happy-lu-bu-target",["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture)};
        var choices=_players.Where(p=>HappyLuBuExtraTarget(use,p)).OrderBy(p=>RelativeTableOrder(f.OwnerSeat,p.Seat)).Select(p=>new PromptChoice(new($"happy-wuqian.{f.Id}.{p.Seat}"),"额外指定“"+p.Name+"”",[],[p.Seat],Parameters())).ToList();
        choices.Add(new(new($"happy-wuqian.{f.Id}.skip"),"不追加目标",[],[],Parameters()));return choices;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume,41200)] private bool ContinueHappyLuBu(long id)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{HappyLuBu:{}r}f||f.Id!=id)return false;AssertHappyLuBu(f);
        if(r.Stage==HappyLuBuStage.BasicChildren)
        {
            if(TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginAdvancedSkillsChanged(id)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(id))return true;
            f=GetActiveProgramFrame(id);ReplaceRuntimeTop(f=f with{HappyLuBu=f.HappyLuBu! with{Stage=HappyLuBuStage.Finished},PendingMovementContinuation=null});FinishProgramSkill(f,true);return true;
        }
        var choices=HappyLuBuChoices(f);if(choices.Count==1){ApplyHappyLuBuUse(f,null);return true;}
        ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});var skill=_contentRegistry.GetSkill(f.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"无前：可以为此牌额外指定一名合法目标。",[],choices.SelectMany(c=>c.Targets).ToArray(),f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=false,TargetSeat=f.OwnerSeat,Choices=Array.AsReadOnly(choices.ToArray()),SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();return true;
    }
    [ProgramAnswer("happy-lu-bu-target")] private void AnswerHappyLuBuTarget(PromptChoice chosen)
    {
        var f=_resolutionStack.LastOrDefault() as ProgramSkillFrame??throw new InvalidOperationException("Wuqian lost its target parent.");AssertHappyLuBu(f);
        if(!HappyLuBuChoices(f).Any(c=>AssistedChoicesEqual([c],[chosen])))throw new InvalidOperationException("Wuqian requires its exact published target choice.");
        ClearPendingDecision();ApplyHappyLuBuUse(f,chosen.Targets.Count==0?null:chosen.Targets.Single());
    }
    private void ApplyHappyLuBuUse(ProgramSkillFrame f,int? added)
    {
        var r=f.HappyLuBu!;var use=LifecycleCardUse(r.UseId)??throw new InvalidOperationException("Wuqian lost its paid native Use.");
        if(use.HappyLuBuWuqian is not{Applied:false}policy||policy.ProgramId!=f.Id||added is{}s&&!HappyLuBuExtraTarget(use,_players[s]))throw new InvalidOperationException("Wuqian cannot enhance twice or add an illegal target.");
        var targets=added is{}seat?use.TargetSeats.Append(seat).ToArray():use.TargetSeats.ToArray();var action=CloneDesignatedExtraTargetAction(use.Action!,targets);
        UpdateProgramRoleCardUse(use with{TargetSeats=targets,Action=action,HappyLuBuWuqian=policy with{Applied=true,AddedTarget=added},IgnoresArmor=true,EnhancementOwnerSeat=use.EnhancementOwnerSeat??f.OwnerSeat,
            Enhancements=use.Enhancements|CurrentCardEnhancement.IgnoreArmor|(added is null?CurrentCardEnhancement.None:CurrentCardEnhancement.ExtraTarget),TargetsAdjusted=use.TargetsAdjusted||added is not null},action);
        if((use.Enhancements&CurrentCardEnhancement.IgnoreArmor)==0)AdvanceEventRulesAndQueueFact(new CurrentCardEnhancedEvent(use.Id,r.ActionId,f.OwnerSeat,CurrentCardEnhancement.IgnoreArmor,null));
        if(added is{}extra)AdvanceEventRulesAndQueueFact(new CurrentCardEnhancedEvent(use.Id,r.ActionId,f.OwnerSeat,CurrentCardEnhancement.ExtraTarget,extra));
        if(ActiveCardAttack is{}attack&&attack.ResolutionId==use.Id)attack.SetIgnoresArmor(true);
        AdvanceEventRulesAndQueueFact(new HappyLuBuUseAppliedEvent(f.Id,use.Id,r.ActionId,added));ReplaceRuntimeTop(f=f with{HappyLuBu=r with{Stage=HappyLuBuStage.Finished},Step=ResolutionFrameStep.ResolvingEffect});FinishProgramSkill(f,true);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement,41200)] private bool ReturnHappyLuBuMovement(ProgramSkillFrame f)=>f.HappyLuBu is not null&&ContinueHappyLuBu(f.Id);
    [ProgramAiChoice(SkillProgramEffectOp.EnhanceHappyShenLuBuFirstUse)] private PromptChoice HappyLuBuAi(PendingDecision p)=>p.Choices.First();
    private int HappyLuBuRequiredResponseCount(int skillOwner,int source,int responder,CardKind incoming,CardKind required)
    {
        if(skillOwner!=source||incoming==CardKind.Duel&&required!=CardKind.Slash||IsSlashCard(incoming)&&required!=CardKind.Dodge)return 1;
        var id=incoming==CardKind.Duel?ActiveDuel?.ResolutionId:ActiveCardAttack?.ResolutionId;
        return id is{}useId&&LifecycleCardUse(useId) is{HappyLuBuWuqian:{Applied:true}r}use&&r.Source.OwnerSeat==source&&use.SourceSeat==source&&r.ActualTurn==_turnNumber&&
            (incoming==CardKind.Duel?use.CardKind==CardKind.Duel:IsSlashCard(incoming)&&IsSlashCard(use.CardKind))&&use.TargetSeats.Contains(responder)?2:1;
    }
}
