using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string CuiYanDrawReason="skill-program.cui-yan.shutong.draw";
    private static CardConversionSource CuiYanSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_cui_yan_ol_CuiYan.CuiYanSource(f);
    [ProgramCapability]private sealed class CuiYanCapability(GameEngine e):ICuiYanHost{public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginCuiYan(f,op);}
    private ProgramCardTriggerWindowFrame? CurrentCuiYanUseWindow(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(context.Window!=SkillProgramTriggerWindow.CardUseTargetsFinalized||context.OwnerSeat!=c.OwnerSeat||context.CardUse is not{}identity||_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId)is not{}window||window.CandidateIndex<0||window.CandidateIndex>=window.Candidates.Count||ToSharedCandidate(window.Candidates[window.CandidateIndex])!=c||window.ParentFrameId!=identity.ParentCardUseFrameId||window.Action.ActionId!=identity.CardActionId||LifecycleCardUse(window.ParentFrameId)is not{Action:{Type:CardActionType.Use}a}use||a.ActorSeat!=c.OwnerSeat||use.SourceSeat!=c.OwnerSeat||a.ActionId!=window.Action.ActionId)return null;
        return window;
    }
    private bool CurrentCuiYanEnd(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(context.Window!=SkillProgramTriggerWindow.AfterTurnEnded||context.OwnerSeat!=c.OwnerSeat||_resolutionStack.OfType<DeferredTurnEndFrame>().SingleOrDefault(p=>p.Id==context.ParentFrameId)is not{}p||!IsActualAfterTurnEndedParent(p)||AfterTurnEndedCandidate(p)!=c||p.TurnNumber!=_turnNumber||p.OwnerSeat!=_turnProgression.OwnerSeat||context.SourceSeat!=p.OwnerSeat||context.TargetSeat!=p.OwnerSeat)return false;
        return ProgramEventHistory<CuiYanEndQualifiedEvent>().Any(e=>e.ActualTurn==_turnNumber&&e.TurnOwner==p.OwnerSeat&&e.OwnerSeat==c.OwnerSeat&&e.UsedOrPlayed&&e.WasTargeted);
    }
    private IReadOnlyList<int> CuiYanDamageTargets(int owner)=>Array.AsReadOnly(_players.Where(p=>p.IsAlive&&p.Seat!=owner&&(HasSayingRoundRange(owner,p.Seat)||GetCombatDistance(owner,p.Seat)<=GetAttackRange(owner))).Select(p=>p.Seat).ToArray());
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27470)]
    private bool CanOfferCuiYan(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext context)
    {
        if(t.Effects is not[var e]||!CuiYanComposition.Handles(e.Op))return true;
        return e.Op==SkillProgramEffectOp.ResolveShutongFinalizedUse?CurrentCuiYanUseWindow(c,context)is not null:CurrentCuiYanEnd(c,context)&&CuiYanDamageTargets(c.OwnerSeat).Count>0;
    }
    private SkillProgramStepOutcome BeginCuiYan(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);var context=f.WindowContext??throw new InvalidOperationException("Cui Yan requires its exact native trigger.");
        var c=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,context.OccurrenceIndex);
        if(f.CuiYan is not null||f.InstructionIndex!=1||f.TriggerId is null||!_players[f.OwnerSeat].IsAlive||!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)||!CanOfferCuiYan(c,GetProgramTrigger(f),context))throw new InvalidOperationException("Cui Yan lost its unpaid current source or actual boundary.");
        var r=new CuiYanReceipt(CuiYanSource(f),f.GameplayHash,op,context.ParentFrameId,_turnNumber,_turnProgression.OwnerSeat,op==SkillProgramEffectOp.ResolveShutongFinalizedUse?CuiYanStage.DrawChildren:CuiYanStage.DamageChoice);
        if(op==SkillProgramEffectOp.ResolveShutongFinalizedUse)
        {
            var w=CurrentCuiYanUseWindow(c,context)!;var use=LifecycleCardUse(w.ParentFrameId)!;var a=use.Action!;var targets=a.EffectiveDesignatedTargetSeats.Distinct().ToArray();
            if(targets.Length==0&&!use.TargetsAdjusted&&(GetProgramCardCategory(use.CardKind)==SkillProgramCardCategory.Equipment||use.CardKind is CardKind.DrawTwo or CardKind.Peach or CardKind.Alcohol))targets=[use.SourceSeat];
            var same=targets.Length>0&&_players[f.OwnerSeat].HasHpValue&&targets.All(s=>_players[s].HasHpValue&&_players[s].Hp==_players[f.OwnerSeat].Hp);
            r=r with{UseId=use.Id,ActionId=a.ActionId,Suit=CuiYanTrueUseSuit(a.PhysicalCards.Select(x=>x.CardId).ToArray(),a.EffectiveSuit),Targets=targets,DrawQualified=same};
        }
        ReplaceRuntimeTop(f=f with{CuiYan=r});AdvanceEventRulesAndQueueFact(new CuiYanStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.WindowId,r.ActualTurn,r.TurnOwner,r.UseId,r.ActionId,r.Suit,r.DrawQualified));
        for(var i=0;i<r.Targets.Count;i++)AdvanceEventRulesAndQueueFact(new CuiYanFrozenTargetEvent(f.Id,i,r.Targets[i]));
        if(op==SkillProgramEffectOp.ResolveZhijieActualTurnEnd){PublishCuiYanDamage(f);return SkillProgramStepOutcome.AwaitChoice;}
        if(r.Suit!=Suit.None)
            foreach(var seat in r.Targets.Append(f.OwnerSeat).Distinct().Where(s=>_players[s].IsAlive))AdvanceEventRulesAndQueueFact(new ShutongNextUseGrantedEvent(f.Id,r.Source,r.GameplayHash,r.UseId,r.ActionId,r.ActualTurn,r.TurnOwner,seat,r.Suit));
        var before=_movementSequence;ReplaceRuntimeTop(f=f with{CuiYan=r with{Before=before,After=before},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        var count=r.DrawQualified&&_players[f.OwnerSeat].IsAlive&&_winner==Winner.None?DrawCards(_players[f.OwnerSeat],1,true,new(CuiYanDrawReason)).Count:0;
        f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{CuiYan=f.CuiYan! with{After=_movementSequence,ActualDraw=count}});AdvanceEventRulesAndQueueFact(new CuiYanDrawIssuedEvent(f.Id,before,_movementSequence,count));ContinueCuiYan(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }
    private IReadOnlyList<PromptChoice> CuiYanDamageChoices(ProgramSkillFrame f)=>Array.AsReadOnly(CuiYanDamageTargets(f.OwnerSeat).Select(s=>new PromptChoice(new($"cui-yan.{f.Id}.damage.{s}"),"对"+_players[s].Name+"造成1点伤害",[],[s],new Dictionary<string,string>{{"program-action","cui-yan-damage"},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)}})).ToArray());
    private void PublishCuiYanDamage(ProgramSkillFrame f)
    {
        var choices=CuiYanDamageChoices(f);if(choices.Count==0){FinishCuiYan(f);return;}var skill=_contentRegistry.GetSkill(f.SkillId);ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"直节：选择攻击范围内的一名其他角色造成1点伤害。",[],choices.SelectMany(c=>c.Targets).ToArray(),f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=false,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};_status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("cui-yan-damage")]private void AnswerCuiYanDamage(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{CuiYan:{Stage:CuiYanStage.DamageChoice}}f||_pendingDecision?.PlayerSeat!=f.OwnerSeat||!CuiYanDamageChoices(f).Any(c=>AssistedChoicesEqual([c],[choice])))throw new InvalidOperationException("Zhijie lost its actual native range/target choice.");
        AssertCuiYan(f);ClearPendingDecision();var target=choice.Targets.Single();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect,CuiYan=f.CuiYan! with{Stage=CuiYanStage.DamageChildren,DamageTarget=target}});AdvanceEventRulesAndQueueFact(new CuiYanDamageIssuedEvent(f.Id,f.OwnerSeat,target));BeginProgramSkillDamage(f,target,1);
    }
    private void ContinueCuiYan(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{CuiYan:{}r}f||f.Id!=id)return;AssertCuiYan(f);
        if(r.Stage==CuiYanStage.DamageChoice){if(_pendingDecision is null)PublishCuiYanDamage(f);return;}
        if(r.Stage==CuiYanStage.DamageChildren&&f.AttackAttempt is not null)return;
        if(TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(id)||TryBeginAdvancedSkillsChanged(id))return;
        FinishCuiYan(GetActiveProgramFrame(id));
    }
    private void FinishCuiYan(ProgramSkillFrame f){AdvanceEventRulesAndQueueFact(new CuiYanCompletedEvent(f.Id));ReplaceRuntimeTop(f=f with{CuiYan=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    [ProgramContinuation(ProgramContinuationKind.Resume,27470)]private bool ResumeCuiYan(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{CuiYan:not null}f||f.Id!=id)return false;ContinueCuiYan(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,27470)]private bool ReturnCuiYanMovement(ProgramSkillFrame f){if(f.CuiYan is null)return false;ContinueCuiYan(f.Id);return true;}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27470)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27470)]private bool ContinuePaidCuiYan(ProgramSkillFrame f){if(f.CuiYan is not{Stage:not CuiYanStage.DamageChoice})return false;AssertCuiYan(f);return true;}
    [ProgramAiChoice(SkillProgramEffectOp.ResolveZhijieActualTurnEnd)]private PromptChoice AiCuiYanDamage(PendingDecision p,ProgramSkillFrame f)=>p.Choices.FirstOrDefault(c=>!AreProgramDistributionAllies(_players[f.OwnerSeat],_players[c.Targets.Single()]))??p.Choices.First();
}
