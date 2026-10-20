namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string KenshangDrawReason="skill-program.ma-xiu-ma-tie.kenshang.draw";
    private void IssueKenshangUse(long id,CardActionContext action)
    {
        if(action.Type!=CardActionType.Use||!IsSlashCard(action.EffectiveKind)||action.PhysicalCards.Count<2)return;
        var source=action.ConversionChain.SingleOrDefault(s=>_contentRegistry.Skills.GetValueOrDefault(s.SkillId)?.Program?.ViewAs.Any(r=>r.Id==s.BindingId&&r.KenshangUse==true)==true);
        if(source is null)return;
        var use=LifecycleCardUse(id)??throw new InvalidOperationException("Kenshang lost its native issuance owner.");
        if(use.Kenshang is not null||source.OwnerSeat!=action.ProviderSeat||action.ActorSeat!=source.OwnerSeat||use.Action?.ActionId!=action.ActionId||!use.PhysicalCardIds!.SequenceEqual(action.PhysicalCards.Select(c=>c.CardId)))throw new InvalidOperationException("Kenshang cannot issue another use or another provider's materials.");
        var hash=_contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash;
        UpdateLifecycleCardUse(id,u=>u with{Kenshang=new(id,action.ActionId,source,hash,action.ActorSeat,action.ProviderSeat,action.PhysicalCards.Count,use.TargetSeats)});
        AdvanceEventRulesAndQueueFact(new KenshangUseIssuedEvent(id,action.ActionId,source,hash,action.ActorSeat,action.ProviderSeat,action.PhysicalCards.Count));
        for(var i=0;i<action.PhysicalCards.Count;i++)AdvanceEventRulesAndQueueFact(new KenshangMaterialEvent(id,i,action.PhysicalCards[i]));
    }
    private bool KenshangUseProof(CardUseFrame use)
    {
        if(use.Kenshang is not{}r||use.Action is not{Type:CardActionType.Use}a||r.UseId!=use.Id||r.ActionId!=a.ActionId||r.Source.OwnerSeat!=r.ActorSeat||r.ProviderSeat!=r.ActorSeat||a.ProviderSeat!=r.ProviderSeat||!IsSlashCard(use.CardKind)||a.EffectiveKind!=use.CardKind||r.MaterialCount<2||a.PhysicalCards.Count!=r.MaterialCount||
            !ShownEntityUseActorMatches(use,r.ActorSeat,r.ProviderSeat)||_contentRegistry.GetSkill(r.Source.SkillId).Program is not{}p||p.GameplayHash!=r.GameplayHash||!p.ViewAs.Any(v=>v.Id==r.Source.BindingId&&v.KenshangUse==true)||!a.ConversionChain.Contains(r.Source)||
            ProgramEventHistory<KenshangUseIssuedEvent>().Where(e=>e.UseId==use.Id).ToArray()is not[var issued]||issued.ActionId!=a.ActionId||issued.Source!=r.Source||issued.GameplayHash!=r.GameplayHash||issued.ActorSeat!=r.ActorSeat||issued.ProviderSeat!=r.ProviderSeat||issued.MaterialCount!=r.MaterialCount)return false;
        var costs=ProgramEventHistory<KenshangMaterialEvent>().Where(e=>e.UseId==use.Id).OrderBy(e=>e.Index).ToArray();
        return costs.Length==r.MaterialCount&&costs.Select(e=>e.Index).SequenceEqual(Enumerable.Range(0,r.MaterialCount))&&costs.Select(e=>e.Cost).SequenceEqual(a.PhysicalCards)&&a.PhysicalCards.All(c=>c.From.OwnerSeat==r.ProviderSeat&&c.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment)&&use.PhysicalCardIds!.SequenceEqual(a.PhysicalCards.Select(c=>c.CardId));
    }
    private void RecordKenshangDamage(IDamageAttempt attack,long damageId,int amount)
    {
        if(amount<=0||!attack.DamageWasApplied||attack is not CardAttackHandle card||attack.IsDelayedJudgmentDamage||attack.IsProgramJudgmentDamage||card.IsProgramSkillDamage||LifecycleCardUse(attack.ResolutionId)is not{Kenshang:{}r}use)return;
        if(!KenshangUseProof(use)||_resolutionStack.LastOrDefault()is not DamageFrame d||d.Id!=damageId||d.ParentFrameId!=use.Id||d.Amount!=amount||d.SourceSeat!=attack.SourceSeat||d.TargetSeat!=attack.TargetSeat||ProgramEventHistory<KenshangDamageEvent>().Any(e=>e.DamageFrameId==damageId))throw new InvalidOperationException("Kenshang damage requires the exact once-applied native use damage, including its real chain propagation.");
        AdvanceEventRulesAndQueueFact(new KenshangDamageEvent(use.Id,r.ActionId,damageId,attack.SourceSeat,attack.TargetSeat,amount,attack.IsChainPropagation));
    }
    private int KenshangDamageTotal(CardUseFrame use)=>ProgramEventHistory<KenshangDamageEvent>().Where(e=>e.UseId==use.Id&&e.ActionId==use.Kenshang!.ActionId).Sum(e=>e.Amount);
    private int[] KenshangLegalTargets(CardUseFrame use)
    {
        var action=use.Action!;var actor=_players[use.SourceSeat];
        // This is the target frontier of an already-issued Slash, whose quota and
        // equipment payment have completed. Rechecking a new Use quota here would
        // erase its own legal targets after the ordinary Slash debit.
        return _players.Where(t=>CanUseSlashTarget(actor,t,new(-1,use.CardKind,action.EffectiveSuit??Suit.None,action.EffectiveRank??0),
            action.ConversionChain.FirstOrDefault(),use.CardKind,existingUseFrameId:use.Id,specificEffectiveRank:action.EffectiveRank,
            physicalCardIds:action.PhysicalCards.Select(c=>c.CardId).ToArray())&&
            !IsCardTargetProhibited(t,use.CardKind,action.EffectiveSuit,ActualTargetPolicyColor(action))&&
            !HasBeneficiarySuitShield(use.SourceSeat,t.Seat,action.EffectiveSuit)&&!IsSelfTargetForbiddenAction(actor,use.CardKind,[t.Seat]))
            .Select(t=>t.Seat).ToArray();
    }
    [ProgramPipelineHook(ProgramPipelinePhase.CollectIssuedCardTrigger, 12)]
    private void CollectIssuedKenshangCandidates(CardActionContext a,SkillProgramTriggerWindow window,List<ProgramCardTriggerCandidate> result)
    {
        if(window is not(SkillProgramTriggerWindow.CardUseCommitted or SkillProgramTriggerWindow.CardUseCompleted)||_resolutionStack.LastOrDefault()is not CardUseFrame{Kenshang:{}r}u||u.Action?.ActionId!=a.ActionId||!KenshangUseProof(u)||!_players[r.Source.OwnerSeat].IsAlive||window==SkillProgramTriggerWindow.CardUseCommitted&&r.Offered||window==SkillProgramTriggerWindow.CardUseCompleted&&(r.DrawIssued||KenshangDamageTotal(u)>=r.MaterialCount))return;
        var op=window==SkillProgramTriggerWindow.CardUseCommitted?SkillProgramEffectOp.ResolveKenshangTargetRewrite:SkillProgramEffectOp.DrawForKenshangShortDamage;var p=_contentRegistry.GetSkill(r.Source.SkillId).Program!;var t=p.Triggers.Single(t=>t.Effects is[var e]&&e.Op==op);
        if(result.Any(c=>c.OwnerSeat==r.Source.OwnerSeat&&c.SkillId==p.Id&&c.SkillInstanceId==r.Source.SkillInstanceId&&c.TriggerId==t.Id))return;
        result.Add(new(r.Source.OwnerSeat,r.Source.OwnerSeat,p.Id,t.Id,p.GameplayHash,r.Source.SkillInstanceId,t.Priority,CreateCardActionProgramContext(a,window,0,r.Source.OwnerSeat,r.Source.OwnerSeat,CaptureProgramTriggerFacts(_players[r.Source.OwnerSeat],a))));
    }
    private bool IsIssuedKenshangCandidate(ProgramTriggerCandidate c)=>_contentRegistry.Skills.GetValueOrDefault(c.SkillId)?.Program?.Triggers.Any(t=>t.Id==c.BindingId&&t.Effects.Any(e=>KenshangComposition.Handles(e.Op)))==true;
    private bool CanRunIssuedKenshangCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(!IsIssuedKenshangCandidate(c)||context.CardUse is not{}card||LifecycleCardUse(card.ParentCardUseFrameId)is not{Kenshang:{}r,Action:{}a}u||!KenshangUseProof(u)||card.CardActionId!=a.ActionId||!_players[c.OwnerSeat].IsAlive||c.OwnerSeat!=r.Source.OwnerSeat||c.SkillId!=r.Source.SkillId||c.SkillInstanceId!=r.Source.SkillInstanceId||c.GameplayHash!=r.GameplayHash||context.OwnerSeat!=c.OwnerSeat||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId)is not{}w||w.ParentFrameId!=u.Id||w.Action.ActionId!=a.ActionId||w.CandidateIndex<0||w.CandidateIndex>=w.Candidates.Count)return false;
        var current=w.Candidates[w.CandidateIndex];var t=GetProgramTrigger(c);
        return current.OwnerSeat==c.OwnerSeat&&current.SkillId==c.SkillId&&current.TriggerId==c.BindingId&&current.SkillInstanceId==c.SkillInstanceId&&current.GameplayHash==c.GameplayHash&&CreateCardActionProgramContext(w,current)==context&&
            (context.Window==SkillProgramTriggerWindow.CardUseCommitted&&t.Effects is[{Op:SkillProgramEffectOp.ResolveKenshangTargetRewrite}]&&!r.Offered||context.Window==SkillProgramTriggerWindow.CardUseCompleted&&t.Effects is[{Op:SkillProgramEffectOp.DrawForKenshangShortDamage}]&&!r.DrawIssued&&KenshangDamageTotal(u)<r.MaterialCount);
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27110)]
    private bool QualifyKenshang(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext context)=>!t.Effects.Any(e=>KenshangComposition.Handles(e.Op))||CanRunIssuedKenshangCandidate(c,context);
    [ProgramCapability]private sealed class KenshangCapability(GameEngine e):IKenshangHost{public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginKenshang(f,op);}
    private CardUseFrame RequireKenshangUse(ProgramSkillFrame f)
    {
        if(f.WindowContext?.CardUse is not{}card||LifecycleCardUse(card.ParentCardUseFrameId)is not{Kenshang:{}r}u||!KenshangUseProof(u)||r.Source.OwnerSeat!=f.OwnerSeat||r.Source.SkillId!=f.SkillId||r.Source.SkillInstanceId!=f.SkillInstanceId||r.GameplayHash!=f.GameplayHash||r.ActionId!=card.CardActionId)throw new InvalidOperationException("Kenshang lost its exact issued native conversion.");return u;
    }
    private SkillProgramStepOutcome BeginKenshang(ProgramSkillFrame input,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(input.Id);var c=f.WindowContext;
        if(f.Kenshang is not null||f.InstructionIndex!=1||c is null||!CanRunIssuedKenshangCandidate(new(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,0,c.OccurrenceIndex),c))throw new InvalidOperationException("Kenshang must start at its exact unconsumed use candidate.");
        var u=RequireKenshangUse(f);var r=u.Kenshang!;
        ReplaceRuntimeTop(f=f with{Kenshang=new(u.Id,r.ActionId,r.Source,r.GameplayHash,f.InstructionIndex,op==SkillProgramEffectOp.ResolveKenshangTargetRewrite?KenshangProgramStage.Targets:KenshangProgramStage.DrawChildren)});
        if(op==SkillProgramEffectOp.ResolveKenshangTargetRewrite)
        {
            UpdateLifecycleCardUse(u.Id,x=>x with{Kenshang=x.Kenshang! with{Offered=true}});
            if(KenshangLegalTargets(u).Length<r.MaterialCount)FinishKenshang(f);else PublishKenshangTargets(f);
        }
        else
        {
            var total=KenshangDamageTotal(u);if(total>=r.MaterialCount)throw new InvalidOperationException("Kenshang cannot draw after full or excess actual damage.");
            UpdateLifecycleCardUse(u.Id,x=>x with{Kenshang=x.Kenshang! with{DrawIssued=true}});var before=_movementSequence;
            ReplaceRuntimeTop(f=f with{PendingMovementContinuation=new(f.OwnerSeat,0,null)});var count=DrawCards(_players[f.OwnerSeat],1,true,new(KenshangDrawReason)).Count;
            f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{Kenshang=f.Kenshang! with{Before=before,After=_movementSequence,DrawCount=count}});
            AdvanceEventRulesAndQueueFact(new KenshangDrawIssuedEvent(f.Id,u.Id,r.ActionId,total,r.MaterialCount,before,_movementSequence,count));ContinueKenshang(f.Id);
        }
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> KenshangTargetChoices(ProgramSkillFrame f)
    {
        var r=f.Kenshang!;var u=RequireKenshangUse(f);var choices=new List<PromptChoice>();
        if(r.Selected.Count<u.Kenshang!.MaterialCount)foreach(var seat in KenshangLegalTargets(u).Except(r.Selected))choices.Add(new(new($"kenshang.{f.Id}.target.{seat}"),"重选目标："+_players[seat].Name,[],[seat],new Dictionary<string,string>{{"program-action","kenshang-target"},{"branch","select"}}));
        if(r.Selected.Count==u.Kenshang!.MaterialCount)choices.Add(new(new($"kenshang.{f.Id}.commit"),"将目标改为所选的等量角色",[],[],new Dictionary<string,string>{{"program-action","kenshang-target"},{"branch","commit"}}));
        choices.Add(new(new($"kenshang.{f.Id}.keep"),"保留原目标",[],[],new Dictionary<string,string>{{"program-action","kenshang-target"},{"branch","keep"}}));return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishKenshangTargets(ProgramSkillFrame f)
    {
        var choices=KenshangTargetChoices(f);ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});var skill=_contentRegistry.GetSkill(f.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"垦伤：可保留原目标，或重新指定恰等于实体牌数的合法目标。",[],choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=CreatePromptId(),Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("kenshang-target")]private void AnswerKenshangTarget(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Kenshang:{Stage:KenshangProgramStage.Targets}r}f||_pendingDecision?.PlayerSeat!=f.OwnerSeat||!KenshangTargetChoices(f).Any(c=>AssistedChoicesEqual([c],[choice])))throw new InvalidOperationException("Kenshang needs its current exact target choice.");
        var u=RequireKenshangUse(f);ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});
        if(choice.Parameters["branch"]=="select")
        {var seat=choice.Targets.Single();AdvanceEventRulesAndQueueFact(new KenshangTargetSelectedEvent(f.Id,u.Id,r.Selected.Count,seat));ReplaceRuntimeTop(f=f with{Kenshang=r with{Selected=r.Selected.Append(seat).ToArray()}});PublishKenshangTargets(f);return;}
        if(choice.Parameters["branch"]=="commit")
        {
            if(r.Selected.Count!=u.Kenshang!.MaterialCount||r.Selected.Distinct().Count()!=r.Selected.Count||r.Selected.Any(s=>!KenshangLegalTargets(u).Contains(s)))throw new InvalidOperationException("Kenshang must replace the entire frontier with exactly as many distinct currently legal targets as original materials.");
            var targets=Array.AsReadOnly(r.Selected.ToArray());var action=CloneRoleAction(u.Action!,u.SourceSeat,targets);var previous=u.TargetSeats;
            var updated=u with{TargetSeats=targets,TargetIndex=0,Action=action,TargetsAdjusted=true,Kenshang=u.Kenshang! with{RewriteFrameId=f.Id,RewrittenTargets=targets},Enhancements=u.Enhancements|CurrentCardEnhancement.ExtraTarget};
            UpdateProgramRoleCardUse(updated,action);var attack=ProgramCardAttack??throw new InvalidOperationException("Kenshang lost its retained native Slash.");attack.SetDamageParticipants(u.SourceSeat,targets[0]);attack.SetIgnoresArmor(HasArmorBypass(_players[u.SourceSeat])||HasCardArmorBypass(_players[u.SourceSeat],_players[targets[0]],u.CardKind));
            AdvanceEventRulesAndQueueFact(new KenshangTargetsCommittedEvent(f.Id,u.Id,action.ActionId,targets.Count));AdvanceEventRulesAndQueueFact(new SlashTargetsReplacedEvent(f.Id,f.SkillId,f.OwnerSeat,u.Id,previous,targets));
        }
        FinishKenshang(GetActiveProgramFrame(f.Id));
    }
    private void FinishKenshang(ProgramSkillFrame f){ReplaceRuntimeTop(f=f with{Kenshang=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    private void ContinueKenshang(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Kenshang:{}r}f||f.Id!=id)return;
        if(r.Stage==KenshangProgramStage.Targets){if(_pendingDecision is null)PublishKenshangTargets(f);return;}
        if(TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(id)||TryBeginAdvancedSkillsChanged(id))return;
        FinishKenshang(GetActiveProgramFrame(id));
    }
    [ProgramContinuation(ProgramContinuationKind.Resume,27110)]private bool ResumeKenshang(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Kenshang:not null}f||f.Id!=id)return false;ContinueKenshang(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,27110)]private bool ReturnKenshangMovement(ProgramSkillFrame f){if(f.Kenshang is null)return false;ContinueKenshang(f.Id);return true;}
    [ProgramAiChoice(SkillProgramEffectOp.ResolveKenshangTargetRewrite,SkillProgramEffectOp.DrawForKenshangShortDamage)]private PromptChoice AiKenshang(PendingDecision p,ProgramSkillFrame f)=>p.Choices.Single(c=>c.Parameters["branch"]=="keep");
}
