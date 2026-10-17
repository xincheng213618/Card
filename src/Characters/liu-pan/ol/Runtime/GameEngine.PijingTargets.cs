using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private static IReadOnlyList<int> PijingFoldTargets(IReadOnlyList<int> original,IReadOnlyList<PijingToggle> toggles,CardKind kind)
    {
        var targets=original.ToList();foreach(var toggle in toggles){if(!toggle.Remove){targets.AddRange(toggle.Pair);continue;}var at=kind==CardKind.BorrowedSword?Enumerable.Range(0,targets.Count/2).Select(i=>i*2).FirstOrDefault(i=>targets[i]==toggle.Seat,-1):targets.IndexOf(toggle.Seat);if(at<0)throw new InvalidOperationException("Pijing cannot remove an absent original target.");targets.RemoveRange(at,kind==CardKind.BorrowedSword?2:1);}return Array.AsReadOnly(targets.ToArray());
    }
    private IReadOnlyList<PijingToggle> PijingInitialOptions(CardUseFrame use,IReadOnlyList<PijingToggle> selected)
    {
        var targets=PijingFoldTargets(use.TargetSeats,selected,use.CardKind);var current=use with{TargetSeats=targets,Action=CloneDesignatedExtraTargetAction(use.Action!,targets)};var result=new List<PijingToggle>();var mains=use.CardKind==CardKind.BorrowedSword?targets.Where((_,i)=>i%2==0):targets;
        foreach(var seat in mains.Where(s=>s!=use.SourceSeat&&_players[s].IsAlive&&!selected.Any(t=>t.Seat==s)))
        {var index=targets.ToList().IndexOf(seat);result.Add(new(seat,true,use.CardKind==CardKind.BorrowedSword?[seat,targets[index+1]]:[seat]));}
        foreach(var pair in BingzhongLegalTargets(current).Where(p=>p[0]!=use.SourceSeat&&!selected.Any(t=>t.Seat==p[0])))result.Add(new(pair[0],false,pair));return Array.AsReadOnly(result.ToArray());
    }
    private IReadOnlyList<IReadOnlyList<int>> PijingReciprocalOptions(CardUseFrame use,int issuer)
    {
        if(!_players[issuer].IsAlive)return [];
        if(use.CardKind!=CardKind.UnexpectedAssault)return Array.AsReadOnly(DesignatedExtraTargetCandidates(use).Where(p=>p[0]==issuer).ToArray());
        var a=use.Action!;var actor=_players[a.ActorSeat];var target=_players[issuer];
        return issuer!=a.ActorSeat&&!a.EffectiveDesignatedTargetSeats.Contains(issuer)&&GetHand(target).Count>0&&!IsDirectedCardTargetProhibited(actor.Seat,issuer,use.CardKind)&&!IsCardTargetProhibited(target,use.CardKind,a.EffectiveSuit??Suit.None,ActualTargetPolicyColor(a))&&!HasBeneficiarySuitShield(actor.Seat,issuer,a.EffectiveSuit)&&!IsSelfTargetForbiddenAction(actor,use.CardKind,[issuer])?new IReadOnlyList<int>[]{Array.AsReadOnly(new[]{issuer})}:[];
    }
    private IReadOnlyList<PromptChoice> PijingChoices(ProgramSkillFrame f)
    {
        var r=f.Pijing!;var use=RequirePijingUse(f,out _);var choices=new List<PromptChoice>();
        Dictionary<string,string> Args(string branch)=>new(){{"program-action","pijing"},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)},{"branch",branch}};
        if(r.Stage==PijingStage.Toggles)
        {
            if(r.Toggles.Count<r.X)foreach(var toggle in PijingInitialOptions(use,r.Toggles))choices.Add(new(new($"pijing.{f.Id}.{r.Toggles.Count}.{(toggle.Remove?"remove":"add")}.{string.Join('.',toggle.Pair)}"),(toggle.Remove?"移除此目标：":"追加此目标：")+string.Join("、",toggle.Pair.Select(s=>_players[s].Name)),[],toggle.Pair,Args(toggle.Remove?"remove":"add")));
            choices.Add(new(new($"pijing.{f.Id}.finish"),"完成目标选择",[],[],Args("finish")));
        }
        else if(r.Stage==PijingStage.Reciprocal)
        {
            foreach(var pair in PijingReciprocalOptions(use,f.OwnerSeat))choices.Add(new(new($"pijing.{f.Id}.reciprocal.{string.Join('.',pair)}"),"额外指定"+_players[f.OwnerSeat].Name,[],pair,Args("add")));
            choices.Add(new(new($"pijing.{f.Id}.draw"),"摸一张牌",[],[],Args("draw")));choices.Add(new(new($"pijing.{f.Id}.decline"),"不执行",[],[],Args("decline")));
        }
        return Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray());
    }
    private void PublishPijing(ProgramSkillFrame f)
    {
        var r=f.Pijing!;var actor=r.Reciprocal?r.ActorSeat:f.OwnerSeat;var choices=PijingChoices(f);var skill=_contentRegistry.GetSkill(f.SkillId);ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});_pendingDecision=new(DecisionKind.ProgramTrigger,actor,r.Reciprocal?"披荆：可额外指定原刘磐，或摸一张牌。":$"披荆：增减至多{r.X}名其他角色。",[],choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=false,Choices=choices,TargetSeat=actor,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};_status=_players[actor].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("pijing")]
    private void AnswerPijing(PromptChoice selected)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Pijing:{}r}f||r.Stage is not(PijingStage.Toggles or PijingStage.Reciprocal)||_pendingDecision?.PlayerSeat!=(r.Reciprocal?r.ActorSeat:f.OwnerSeat)||!PijingChoices(f).Any(c=>AssistedChoicesEqual([c],[selected])))throw new InvalidOperationException("Pijing requires its original actual chooser and currently legal public target choice.");AssertPijing(f);ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});var branch=selected.Parameters["branch"];
        if(r.Stage==PijingStage.Toggles)
        {
            if(branch=="finish"){ApplyPijingTargets(f);f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{Pijing=f.Pijing! with{Stage=PijingStage.Gifts}});ContinuePijing(f.Id);return;}
            var toggle=new PijingToggle(selected.Targets[0],branch=="remove",selected.Targets);AdvanceEventRulesAndQueueFact(new PijingToggleSelectedEvent(f.Id,r.Toggles.Count,toggle.Seat,toggle.Remove,toggle.Pair.Count));for(var i=0;i<toggle.Pair.Count;i++)AdvanceEventRulesAndQueueFact(new PijingTogglePairEvent(f.Id,r.Toggles.Count,i,toggle.Pair[i]));ReplaceRuntimeTop(f=f with{Pijing=r with{Toggles=r.Toggles.Append(toggle).ToArray()}});PublishPijing(f);return;
        }
        AdvanceEventRulesAndQueueFact(new PijingNextChoiceEvent(f.Id,r.GroupId,branch));
        if(branch=="draw"){DrawPijingReciprocal(f);return;}
        if(branch=="add"){var toggle=new PijingToggle(f.OwnerSeat,false,selected.Targets);AdvanceEventRulesAndQueueFact(new PijingToggleSelectedEvent(f.Id,0,toggle.Seat,false,toggle.Pair.Count));for(var i=0;i<toggle.Pair.Count;i++)AdvanceEventRulesAndQueueFact(new PijingTogglePairEvent(f.Id,0,i,toggle.Pair[i]));ReplaceRuntimeTop(f=f with{Pijing=r with{Toggles=[toggle]}});ApplyPijingTargets(f);}
        FinishPijing(GetActiveProgramFrame(f.Id));
    }
    private void ApplyPijingTargets(ProgramSkillFrame f)
    {
        var r=f.Pijing!;var use=RequirePijingUse(f,out var window);var targets=PijingFoldTargets(r.Original,r.Toggles,use.CardKind);var action=CloneDesignatedExtraTargetAction(use.Action!,targets);var simple=use.AdjustedSimpleContinuation;
        if(r.Toggles.Count>0&&window.Continuation==ProgramCardContinuation.FinalizedSimpleCard)
        {var continuation=window.SimpleContinuation??throw new InvalidOperationException("Pijing lost its native supportive basic effect.");if(continuation.Effect is not(SimpleCardUseEffect.Recovery or SimpleCardUseEffect.Alcohol))throw new InvalidOperationException("Pijing cannot invent an equipment or basic-card effect.");simple??=continuation with{RecoveryPolicySources=continuation.RecoveryPolicySources is{}p?Array.AsReadOnly(p.ToArray()):null};}
        for(var i=0;i<targets.Count;i++)AdvanceEventRulesAndQueueFact(new PijingTargetSlotEvent(f.Id,true,i,targets[i]));
        var tail=new PijingTargetTail(f.Id,use.Id,r.ActionId,r.Source,r.GameplayHash,r.ActorSeat,r.ProviderSeat,r.Reciprocal,r.Original,targets);
        AdvanceEventRulesAndQueueFact(new PijingTargetsAppliedEvent(f.Id,use.Id,r.ActionId,r.Source,r.GameplayHash,r.ActorSeat,r.ProviderSeat,r.Original.Count,targets.Count,r.Reciprocal));
        var rebased=RebasePijingSlashTargets(use,targets);
        UpdateProgramRoleCardUse(rebased with{TargetSeats=targets,Action=action,PijingTail=tail,AdjustedSimpleContinuation=simple,TargetsAdjusted=use.TargetsAdjusted||r.Toggles.Count>0,Enhancements=r.Toggles.Count>0?use.Enhancements|CurrentCardEnhancement.ExtraTarget:use.Enhancements},action);
        var added=r.Toggles.Where(t=>!t.Remove).Select(t=>t.Seat).ToArray();if(added.Length>0)RefreshCurrentUseAddedTargetCandidates(LifecycleCardUse(use.Id)!,added);
    }
    private CardUseFrame RebasePijingSlashTargets(CardUseFrame use,IReadOnlyList<int> targets)
    {
        if(!IsSlashCard(use.CardKind)||use.CardAttack is not{}original)return use with{TargetIndex=0,SequentialTrick=targets.Count==0?null:use.SequentialTrick};
        CardAttackState Fresh(int seat)=>new(){Active=original.Active,SourceSeat=use.SourceSeat,CardUserSeat=use.SourceSeat,TargetSeat=seat,DamageAmount=use.ProgramAdjustedSlashBaseDamage??use.Continuations.FangtianHalberd?.DamageAmount??original.DamageAmount,IgnoresArmor=use.IgnoresArmor||HasCardArmorBypass(_players[use.SourceSeat],_players[seat],use.CardKind),EffectiveCardKind=use.CardKind,IsDelayedJudgmentDamage=original.IsDelayedJudgmentDamage,IsSourceLess=original.IsSourceLess,ProgramJudgmentFrameId=original.ProgramJudgmentFrameId,ProgramSkillFrameId=original.ProgramSkillFrameId,ProgramSkillCardUseFrameId=original.ProgramSkillCardUseFrameId,DelayedJudgmentSeat=original.DelayedJudgmentSeat,DamageNatureOverride=original.DamageNatureOverride,ConversionSource=original.ConversionSource,CardId=original.CardId,AppearanceKind=original.AppearanceKind,AppearanceSuit=original.AppearanceSuit,AppearanceRank=original.AppearanceRank,PhysicalCardIds=Array.AsReadOnly(original.PhysicalCardIds.ToArray()),AdjustedSlashReturn=original.AdjustedSlashReturn};
        IReadOnlyList<CardAttackState>? prepared=use.PreparedTargetAttacks is{}old?Array.AsReadOnly(targets.Select(seat=>old.FirstOrDefault(a=>a.TargetSeat==seat)??Fresh(seat)).ToArray()):null;
        var first=targets.Count==0?original:prepared is{Count:>0}?prepared[0]:targets[0]==original.TargetSeat?original:Fresh(targets[0]);
        return use with{TargetIndex=0,CardAttack=first,PreparedTargetAttacks=prepared,SlashTargetsCancelled=targets.Count==0,Continuations=use.Continuations with{FangtianHalberd=use.Continuations.FangtianHalberd is{Active:true}multi?multi with{TargetSeats=Array.AsReadOnly(targets.ToArray()),CurrentAttackOwnerId=use.Id}:use.Continuations.FangtianHalberd}};
    }
    private bool TryCompletePijingZeroTargetUse(ProgramCardTriggerWindowFrame window)
    {
        if(LifecycleCardUse(window.ParentFrameId)is not{}use||!HasPijingZeroTargetTail(use))return false;
        if(_resolutionStack.LastOrDefault()?.Id!=use.Id||window.Action.ActionId!=use.Action!.ActionId||window.Continuation is not(ProgramCardContinuation.Slash or ProgramCardContinuation.FinalizedTrick))throw new InvalidOperationException("Pijing's zero-target finish lost its true whole-use return.");
        if(window.Continuation==ProgramCardContinuation.Slash)
        {if(window.AttackOwnerFrameId!=use.Id||ActiveCardAttack is not{}attack||attack.ResolutionId!=use.Id)throw new InvalidOperationException("Pijing's removed Slash lost its actual attack.");CompleteAttack(attack);}
        else {var card=GetTrickRepresentation(use.Id,use.CardId,requireProcessing:false);MoveFinishedTrickCard(use.Id,card);FinishCardUse(use.Id,card,use.CardKind);}return true;
    }
}
