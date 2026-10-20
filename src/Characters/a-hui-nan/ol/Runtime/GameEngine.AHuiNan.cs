using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string AHuiNanDrawReason="skill-program.a-hui-nan.jueman.draw";
    private static bool AHuiNanBasicKind(CardKind kind) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_a_hui_nan_ol_AHuiNan.AHuiNanBasicKind(kind);
    private bool HasAHuiNanCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveJuemanFirstBasicTurnEnd);
    private static CardConversionSource AHuiNanSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_a_hui_nan_ol_AHuiNan.AHuiNanSource(f);
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,27200)]
    private void ObserveAHuiNanBasicUses(IGameEvent payload)
    {
        if(!HasAHuiNanCapability||!HasOngoingActualUseTurn)return;
        var recorded=ProgramEventHistory<AHuiNanBasicUseRecordedEvent>().Where(e=>e.ActualTurn==_turnNumber&&e.TurnOwner==_currentSeat).ToArray();
        if(recorded.Length>=3)return;
        if(payload is CardUseDeclaredEvent d)
        {
            if(_resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u=>u.Id==d.ResolutionId)is not{}use||use.SourceSeat!=d.SourceSeat||use.CardId!=d.CardId||use.CardKind!=d.CardKind||!AHuiNanBasicKind(use.CardKind)||!IsValidPlayerSeat(use.SourceSeat))return;
            if(use.Action is{}a)
            {if(a.Type!=CardActionType.Use||a.ActorSeat!=use.SourceSeat||a.EffectiveKind!=use.CardKind)return;}
            else if(!AHuiNanLegacyBasicUse(use))return;
            if(recorded.Any(e=>e.OriginFrameId==use.Id&&e.ActionId==use.Action?.ActionId))return;
            AdvanceEventRulesAndQueueFact(new AHuiNanBasicUseRecordedEvent(_turnNumber,_currentSeat,use.Id,use.Action?.ActionId,use.SourceSeat,use.CardKind,recorded.Length+1));
            return;
        }
        if(payload is not CardActionAcceptedEvent{Action:{Type:CardActionType.Response,EffectiveKind:CardKind.Dodge}response}||response.ActorSeat!=response.ProviderSeat||response.ResponderSeat!=response.ActorSeat||response.RequesterSeat is not null||!IsValidPlayerSeat(response.ActorSeat)||recorded.Any(e=>e.ActionId==response.ActionId))return;
        var defending=_resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u=>u.Action?.ActionId==response.ParentActionId&&u.CardAttack?.TargetSeat==response.ActorSeat);
        if(defending?.CardAttack is not{EffectiveCardKind:{}incoming}attack||!IsSlashCard(incoming)||response.OpponentSeat!=attack.SourceSeat||!IsProgramResponseCardUse(_players[response.ActorSeat],CardKind.Dodge))return;
        AdvanceEventRulesAndQueueFact(new AHuiNanBasicUseRecordedEvent(_turnNumber,_currentSeat,defending.Id,response.ActionId,response.ActorSeat,CardKind.Dodge,recorded.Length+1));
    }
    private bool AHuiNanLegacyBasicUse(CardUseFrame use)
    {
        if(use.CardId!=0||use.PhysicalCardIds is not{Count:0})return false;
        var i=_resolutionStack.FindIndex(f=>f.Id==use.Id);
        if(i<=0||_resolutionStack[i-1]is not ProgramSkillFrame f||f.OwnerSeat!=use.SourceSeat||_contentRegistry.GetSkill(f.SkillId).Program is not{}p||p.GameplayHash!=f.GameplayHash)return false;
        var effect=ProgramInstructionResolver.Default.Resolve(f,p).GetPausedInstruction(f.InstructionIndex).Effect;
        if(use.CardKind==CardKind.Slash)return f.SelectedTargetSeats.Count==1&&f.SelectedTargetSeats.SequenceEqual(use.TargetSeats)&&effect is{Op:SkillProgramEffectOp.UseVirtualCard,OutputKind:CardKind.Slash,UseCardActionWindows:false};
        if(use.CardKind!=CardKind.Alcohol||effect.Op!=SkillProgramEffectOp.UseVirtualDyingAlcohol||f.WindowContext is not{Window:SkillProgramTriggerWindow.SelfDyingResponse}context||context.TargetSeat!=use.SourceSeat||!use.TargetSeats.SequenceEqual([use.SourceSeat]))return false;
        return _resolutionStack.OfType<DyingFrame>().SingleOrDefault(d=>d.Id==context.ParentFrameId)is{}dying&&dying.VictimSeat==use.SourceSeat&&dying.ResponderIndex>=0&&dying.ResponderIndex<dying.ResponderSeats.Count&&dying.ResponderSeats[dying.ResponderIndex]==use.SourceSeat;
    }
    private AHuiNanBasicUseSnapshot[] AHuiNanHistory(int turn)=>ProgramEventHistory<AHuiNanBasicUseRecordedEvent>().Where(e=>e.ActualTurn==turn).OrderBy(e=>e.Index).Select(e=>
        new AHuiNanBasicUseSnapshot(e,ProgramEventHistory<ProgramCurrentSlashFireChangedEvent>().LastOrDefault(c=>c.CardUseFrameId==e.OriginFrameId&&c.ActionId==e.ActionId&&IsSlashCard(e.Kind))?.FinalKind??e.Kind)).ToArray();
    private bool AHuiNanEndingParent(ProgramTriggerCandidate c,ProgramSkillWindowContext context,out DeferredTurnEndFrame? parent)
    {
        parent=_resolutionStack.OfType<DeferredTurnEndFrame>().SingleOrDefault(p=>p.Id==context.ParentFrameId);
        return HasAHuiNanCapability&&c.SkillId=="ol:jueman"&&c.BindingId is("actual-end-own"or"actual-end-foreign")&&IsValidPlayerSeat(c.OwnerSeat)&&
            context.Window==SkillProgramTriggerWindow.AfterTurnEnded&&context.OwnerSeat==c.OwnerSeat&&parent is not null&&IsActualAfterTurnEndedParent(parent)&&AfterTurnEndedCandidate(parent)==c&&
            context.SourceSeat==parent.OwnerSeat&&context.TargetSeat==parent.OwnerSeat&&context.OccurrenceIndex==c.OccurrenceIndex&&context.Facts==parent.AfterTurnEnded!.Items[parent.AfterTurnEnded.ItemIndex].Facts&&
            c.BindingId==(c.OwnerSeat==parent.OwnerSeat?"actual-end-own":"actual-end-foreign")&&_contentRegistry.GetSkill(c.SkillId).Program?.GameplayHash==c.GameplayHash;
    }
    private bool IsAHuiNanJuemanEndingCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext context)=>_winner==Winner.None&&AHuiNanEndingParent(c,context,out _)&&_players[c.OwnerSeat].IsAlive&&HasRuntimeSkillInstance(_players[c.OwnerSeat],c.SkillId,c.SkillInstanceId);
    private bool AHuiNanStartedBinding(ProgramSkillFrame f)
    {
        if(f.SkillId!="ol:jueman"||f.TriggerId is null||f.ActivationId!=f.TriggerId||f.WindowContext is not{}context)return false;
        var c=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,context.OccurrenceIndex);
        return AHuiNanEndingParent(c,context,out var parent)&&parent!.AfterTurnEnded?.CurrentChild?.FrameId==f.Id&&MountObserverCandidateMatches(f,c)&&
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(e=>e.FrameId==f.Id&&e.SkillId==f.SkillId&&e.BindingId==f.TriggerId&&e.SkillInstanceId==f.SkillInstanceId&&e.OwnerSeat==f.OwnerSeat&&e.Window==SkillProgramTriggerWindow.AfterTurnEnded)==1;
    }
    [ProgramCapability]private sealed class AHuiNanCapability(GameEngine e):IAHuiNanHost
    {public SkillProgramStepOutcome Resolve(ProgramSkillFrame f)=>e.BeginAHuiNan(f);}
    private SkillProgramStepOutcome BeginAHuiNan(ProgramSkillFrame input)
    {
        var f=GetActiveProgramFrame(input.Id);
        if(f.AHuiNanJueman is not null||f.InstructionIndex!=1||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||!AHuiNanStartedBinding(f))throw new InvalidOperationException("Jueman requires its genuine started actual-turn-end binding.");
        var history=AHuiNanHistory(_turnNumber);var own=history.Take(2).Count(e=>e.Declaration.ActorSeat==f.OwnerSeat);
        var stage=history.Length>=2&&own==1?AHuiNanJuemanStage.DrawChildren:history.Length==3&&own==0?AHuiNanJuemanStage.UseChoice:AHuiNanJuemanStage.NoEffect;
        var r=new AHuiNanJuemanReceipt(AHuiNanSource(f),f.GameplayHash,1,_turnNumber,_currentSeat,stage){History=history,Before=_movementSequence,After=_movementSequence};
        ReplaceRuntimeTop(f=f with{AHuiNanJueman=r});AdvanceEventRulesAndQueueFact(new AHuiNanJuemanStartedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.TurnOwner,history.Length,own,history.Length==3?history[2].Kind:null));
        if(stage==AHuiNanJuemanStage.DrawChildren)
        {
            var count=_players[f.OwnerSeat].IsAlive?DrawCards(_players[f.OwnerSeat],1,true,new(AHuiNanDrawReason)).Count:0;f=GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(f=f with{AHuiNanJueman=f.AHuiNanJueman! with{DrawCount=count,After=_movementSequence}});
            AdvanceEventRulesAndQueueFact(new AHuiNanJuemanDrawnEvent(f.Id,f.OwnerSeat,count,r.Before,_movementSequence));
        }
        ContinueAHuiNan(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }
    private IReadOnlyList<PromptChoice> AHuiNanChoices(ProgramSkillFrame f)
    {
        var options=AHuiNanUseTargets(f);return Array.AsReadOnly(options.Select((targets,i)=>new PromptChoice(new($"jueman.{f.Id}.{i}"),$"蟨蛮：视为对{string.Join('、',targets.Select(s=>_players[s].Name))}使用【{CardCatalog.Get(f.AHuiNanJueman!.History[2].Kind).DisplayName}】",[],targets,
            new Dictionary<string,string>{["program-action"]="a-hui-nan-jueman",["option-index"]=i.ToString(CultureInfo.InvariantCulture),["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture)})).ToArray());
    }
    private void PublishAHuiNan(ProgramSkillFrame f)
    {
        var choices=AHuiNanChoices(f);if(choices.Count==0){FinishAHuiNan(f,"no-legal-use");return;}
        var skill=_contentRegistry.GetSkill(f.SkillId);ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"蟨蛮：必须选择第三张基本牌的当前合法用法。",[],choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=true,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("a-hui-nan-jueman")]private void AnswerAHuiNan(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{AHuiNanJueman:{Stage:AHuiNanJuemanStage.UseChoice}r}f||_pendingDecision?.PlayerSeat!=f.OwnerSeat||!AHuiNanChoices(f).Any(c=>AssistedChoicesEqual([c],[choice])))throw new InvalidOperationException("Jueman changed its genuine private chooser or current legal basic use.");
        AssertAHuiNan(f);var targets=AHuiNanUseTargets(f)[int.Parse(choice.Parameters["option-index"],CultureInfo.InvariantCulture)];ClearPendingDecision();
        var o=new AHuiNanJuemanUseOrigin(f.Id,_resolutionSequence+1,r.Source,r.GameplayHash,r.ActualTurn,f.OwnerSeat,r.History[2].Kind,string.Join(',',targets));
        ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect,AHuiNanJueman=r with{Stage=AHuiNanJuemanStage.NativeUse,Use=o,Before=_movementSequence,After=_movementSequence}});
        AdvanceEventRulesAndQueueFact(new AHuiNanJuemanUseIssuedEvent(o));BeginAHuiNanJuemanUse(f,o,targets);
    }
    private bool DrainAHuiNan(long id)=>TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.Program)||TryBeginCardsMovedProgramWindow(id)||TryBeginAdvancedSkillsChanged(id);
    private void ContinueAHuiNan(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{AHuiNanJueman:{}r}f||f.Id!=id)return;AssertAHuiNan(f);
        if(r.Stage is AHuiNanJuemanStage.DrawChildren or AHuiNanJuemanStage.NativeChildren)
        {if(DrainAHuiNan(id))return;FinishAHuiNan(GetActiveProgramFrame(id),r.Stage==AHuiNanJuemanStage.DrawChildren?"draw":"used");return;}
        if(_winner!=Winner.None||!_players[f.OwnerSeat].IsAlive||r.Stage==AHuiNanJuemanStage.NoEffect){FinishAHuiNan(f,"no-effect");return;}
        if(r.Stage==AHuiNanJuemanStage.UseChoice)PublishAHuiNan(f);
    }
    private void FinishAHuiNan(ProgramSkillFrame f,string outcome)
    {var r=f.AHuiNanJueman!;AdvanceEventRulesAndQueueFact(new AHuiNanJuemanCompletedEvent(f.Id,r.DrawCount,r.Use is not null,outcome));ReplaceRuntimeTop(f=f with{AHuiNanJueman=null});FinishProgramSkill(f,true);}
    [ProgramContinuation(ProgramContinuationKind.Resume,27200)]private bool ResumeAHuiNan(long id)
    {if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{AHuiNanJueman:not null}f||f.Id!=id)return false;ContinueAHuiNan(id);return true;}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27200)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27200)]private bool ContinuePaidAHuiNan(ProgramSkillFrame f)=>f.AHuiNanJueman is not null?ValidAHuiNan(f):f.InstructionIndex is 0 or 1&&AHuiNanStartedBinding(f);
}
