using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int HappyGuardCount(int seat)=>_players[seat].Markers.GetValueOrDefault(PlayerMarkerKind.HappyGuard);
    private bool HappyGuardOnField()=>_players.Any(p=>p.IsAlive&&HappyGuardCount(p.Seat)>0);
    private CardConversionSource HappyGuardSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    private IDamageAttempt? HappyGuardAttack(long id)=>_resolutionStack.SingleOrDefault(f=>f.Id==id) switch
    {
        ProgramSkillFrame{AttackAttempt:not null}=>new ProgramAttackHandle(this,id),
        CardUseFrame{CardAttack:not null} or ProgramSkillFrame{CardAttack:not null} or JudgmentFrame{CardAttack:not null}=>new CardAttackHandle(this,id),
        _=>null
    };
    private bool HappyGuardDamageOffer(int owner,ProgramSkillWindowContext? context)
    {
        if(context is not{Window:SkillProgramTriggerWindow.BeforeDamageApplied,TargetSeat:{}target,Amount:>0}||
            !_players[owner].IsAlive||!_players[target].IsAlive||HappyGuardCount(target)!=1||
            _players[target].MarkerSourceCounts.GetValueOrDefault((PlayerMarkerKind.HappyGuard,owner))!=1||
            _resolutionStack.OfType<BeforeDamageProgramWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId) is not{Prevented:false,RedirectedTargetSeat:null}window||
            CurrentDamageAttempt is not{}attack||attack.SourceSeat!=window.SourceSeat||attack.TargetSeat!=target||attack.DamageAmount!=window.Amount||
            attack.DamageRedirected&&target!=owner)return false;
        return context.Amount==window.Amount&&context.TargetSeat==window.TargetSeat;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,41980)]
    private bool CanOfferHappyDianWei(ProgramTriggerLegalityContext g)=>g.SkillId switch
    {
        HappyDianWeiComposition.Shenwei when g.Trigger.Id=="grant-guards"=>_players.Any(p=>p.IsAlive&&HappyGuardCount(p.Seat)==0),
        HappyDianWeiComposition.Shenwei when g.Trigger.Id=="guard-damage"=>HappyGuardDamageOffer(g.OwnerSeat,g.WindowContext),
        HappyDianWeiComposition.Elai=>g.WindowContext is{}context&&IsHappyDianWeiElaiCandidate(g.Candidate,context),
        _=>true
    };
    [ProgramDamageHook(ProgramDamagePhase.FactModifiers,41980)]
    private IEnumerable<(CardUseEffectSource Source,int Amount)> HappyDianWeiKuangxi(IDamageAttempt attack)
    {
        if(attack.IsSourceLess||attack.IsChainPropagation||!IsValidPlayerSeat(attack.SourceSeat)||!_players[attack.SourceSeat].IsAlive||!HappyGuardOnField())return [];
        return GetSkillBindingShard(_players[attack.SourceSeat]).ActiveGrants.Where(s=>s.SkillId==HappyDianWeiComposition.Kuangxi)
            .Take(1).Select(s=>(new CardUseEffectSource(s.SkillId,"field-guard-damage",attack.SourceSeat,s.SkillInstanceId),1));
    }
    private void SetHappyGuard(ProgramSkillFrame f,int target,int count)
    {
        var p=_players[target];var key=(PlayerMarkerKind.HappyGuard,f.OwnerSeat);var before=p.MarkerSourceCounts.GetValueOrDefault(key);
        if(before==count)return;
        if(count is <0 or >1||count==1&&HappyGuardCount(target)!=0)throw new InvalidOperationException("Every character can carry at most one real guard.");
        if(count==0)p.MarkerSourceCounts.Remove(key);else p.MarkerSourceCounts[key]=count;
        var total=HappyGuardCount(target)+count-before;
        if(total==0)p.Markers.Remove(PlayerMarkerKind.HappyGuard);else p.Markers[PlayerMarkerKind.HappyGuard]=total;
        AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(f.Id,target,PlayerMarkerKind.HappyGuard,count-before,total,f.OwnerSeat,"skill-program.happy:shenwei.guard"));
    }
    [ProgramCapability]private sealed class HappyDianWeiCapability(GameEngine e):IHappyDianWeiHost
    {public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.ResolveHappyDianWei(f,op);}
    private SkillProgramStepOutcome ResolveHappyDianWei(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(f.HappyGuard is not null||f.InstructionIndex!=1||f.WindowContext is not{}context||!HappyGuardOriginalCandidate(f))
            throw new InvalidOperationException("Happy Dian Wei requires its unique original native trigger candidate.");
        HappyGuardStage stage;var target=-1;long attackId=0;var damageSource=-1;var amount=0;var nature=DamageNature.Normal;var limit=0;
        if(op==SkillProgramEffectOp.GrantHappyDianWeiShenwei&&context.Window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)
        {stage=HappyGuardStage.GrantChoice;limit=_players[f.OwnerSeat].Hp==1?2:1;}
        else if(op==SkillProgramEffectOp.ConsumeHappyDianWeiShenwei&&HappyGuardDamageOffer(f.OwnerSeat,context)&&CurrentDamageAttempt is{}attack)
        {stage=HappyGuardStage.GuardChoice;target=context.TargetSeat!.Value;attackId=attack.ResolutionId;damageSource=attack.SourceSeat;amount=attack.DamageAmount;nature=GetDamageNature(attack);}
        else if(op==SkillProgramEffectOp.ResolveHappyDianWeiElai&&IsHappyDianWeiElaiContext(f))stage=HappyGuardStage.ElaiChoice;
        else throw new InvalidOperationException("Happy Dian Wei operation lost its exact real turn, damage or removed-guard boundary.");
        var r=new HappyGuardReceipt(HappyGuardSource(f),f.GameplayHash,context.ParentFrameId,_turnNumber,stage,target,attackId,damageSource,amount,nature,limit);
        ReplaceRuntimeTop(f=f with{HappyGuard=r});
        AdvanceEventRulesAndQueueFact(new HappyGuardStartedEvent(f.Id,r.Source,r.GameplayHash,r.ParentId,r.ActualTurn,stage,target,attackId,amount,limit));
        PublishHappyGuardChoice(f);return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> HappyGuardChoices(ProgramSkillFrame f)
    {
        var r=f.HappyGuard!;var choices=new List<PromptChoice>();
        PromptChoice Make(string mode,string text,IReadOnlyList<int>? targets=null)=>new(new ChoiceId($"happy-guard.{f.Id}.{mode}.{string.Join('-',targets??[])}"),text,[],targets??[],
            new Dictionary<string,string>{{"program-action","happy-dian-guard"},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)},{"mode",mode}});
        if(r.Stage==HappyGuardStage.GrantChoice)
        {
            var seats=CurrentTableOrder().Where(s=>_players[s].IsAlive&&HappyGuardCount(s)==0).ToArray();
            if(r.GrantLimit==2)choices.Add(Make("grant","不授予卫。"));
            for(var i=0;i<seats.Length;i++)
            {
                choices.Add(Make("grant",$"令{_players[seats[i]].Name}获得卫。",[seats[i]]));
                if(r.GrantLimit==2)for(var j=i+1;j<seats.Length;j++)choices.Add(Make("grant",$"令{_players[seats[i]].Name}、{_players[seats[j]].Name}各获得卫。",[seats[i],seats[j]]));
            }
        }
        else if(r.Stage==HappyGuardStage.GuardChoice)
        {
            choices.Add(Make("decline","保留卫，自行承受本次伤害。"));
            if(_players[f.OwnerSeat].IsAlive&&_players[r.GuardedSeat].IsAlive&&HappyGuardCount(r.GuardedSeat)==1)
                choices.Add(Make("protect",$"移除卫，由{_players[f.OwnerSeat].Name}承受本次伤害。"));
        }
        else if(r.Stage==HappyGuardStage.ElaiChoice)
        {
            choices.Add(Make("none","不选择额外效果。"));
            if(_players[f.OwnerSeat].IsAlive)
            {
                if(IsRuleWounded(_players[f.OwnerSeat]))choices.Add(Make("recover","回复1点体力。"));
                foreach(var seat in CurrentTableOrder().Where(s=>s!=f.OwnerSeat&&_players[s].IsAlive&&IsWithinAttackRange(f.OwnerSeat,s)))
                    choices.Add(Make("damage",$"对{_players[seat].Name}造成1点伤害。",[seat]));
            }
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishHappyGuardChoice(ProgramSkillFrame f)
    {
        var choices=HappyGuardChoices(f);if(choices.Count==0){ReplaceRuntimeTop(f with{HappyGuard=f.HappyGuard! with{Stage=HappyGuardStage.Declined}});AdvanceRuntimeProgram(f.Id);return;}
        var chooser=f.HappyGuard!.Stage==HappyGuardStage.GuardChoice?f.HappyGuard.GuardedSeat:f.OwnerSeat;
        ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.AwaitingResponse});var skill=_contentRegistry.GetSkill(f.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,chooser,$"【{skill.Name}】请选择。",[],Array.AsReadOnly(choices.SelectMany(c=>c.Targets).Distinct().ToArray()),f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=false,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[chooser].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("happy-dian-guard")]private void AnswerHappyGuard(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame{HappyGuard:{}r}f||!ValidHappyGuardFrame(f)||
            _pendingDecision?.PlayerSeat!=(r.Stage==HappyGuardStage.GuardChoice?r.GuardedSeat:f.OwnerSeat)||
            !AssistedChoicesEqual([choice],HappyGuardChoices(f).Where(c=>c.Id==choice.Id).ToArray()))throw new InvalidOperationException("Guard choice lost its exact current chooser, native occurrence or unpaid source.");
        ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});var mode=choice.Parameters["mode"];
        if(r.Stage==HappyGuardStage.GrantChoice)
        {
            foreach(var target in choice.Targets)SetHappyGuard(f,target,1);
            ReplaceRuntimeTop(f with{HappyGuard=r with{Stage=HappyGuardStage.GrantCommitted}});AdvanceRuntimeProgram(f.Id);return;
        }
        if(r.Stage==HappyGuardStage.GuardChoice)
        {
            if(mode=="decline"){ReplaceRuntimeTop(f with{HappyGuard=r with{Stage=HappyGuardStage.Declined}});AdvanceRuntimeProgram(f.Id);return;}
            ReplaceRuntimeTop(f=f with{HappyGuard=r with{Stage=HappyGuardStage.GuardPaid}});SetHappyGuard(f,r.GuardedSeat,0);
            AdvanceEventRulesAndQueueFact(new HappyGuardPaidEvent(f.Id,r.Source,r.GameplayHash,r.ParentId,r.AttackId,r.GuardedSeat,r.DamageSource,r.Amount,r.Nature));
            ReplaceRuntimeTop(f=f with{HappyGuard=f.HappyGuard! with{Stage=HappyGuardStage.RemovedChildren}});
            var ret=new HappyGuardRemovalReturn(f.Id,r.GuardedSeat,f.OwnerSeat,f.Id,false);
            if(!BeginHappyGuardRemovedWindow(ret))FinishHappyGuardTransfer(f.Id);return;
        }
        if(r.Stage!=HappyGuardStage.ElaiChoice)throw new InvalidOperationException("Unknown original guard menu stage.");
        var targetSeat=choice.Targets.Count==1?choice.Targets[0]:-1;var removal=_resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(w=>w.Id==r.ParentId).HappyGuardRemoval!;
        ReplaceRuntimeTop(f with{HappyGuard=r with{Stage=HappyGuardStage.ElaiCommitted,ElaiMode=mode,ElaiTarget=targetSeat},
            SelectedTargetSeats=Array.AsReadOnly(choice.Targets.ToArray()),ChoiceBindings=Array.AsReadOnly(f.ChoiceBindings.Append(new ProgramChoiceResultBinding("elai-mode",mode,f.OwnerSeat)).ToArray())});
        AdvanceEventRulesAndQueueFact(new HappyGuardElaiChoiceEvent(f.Id,removal.RemovalId,f.OwnerSeat,mode,targetSeat));AdvanceRuntimeProgram(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.GrantHappyDianWeiShenwei,SkillProgramEffectOp.ConsumeHappyDianWeiShenwei,SkillProgramEffectOp.ResolveHappyDianWeiElai)]
    private PromptChoice HappyGuardAi(PendingDecision p,ProgramSkillFrame f)
    {
        if(f.HappyGuard?.Stage==HappyGuardStage.GuardChoice)return p.Choices.FirstOrDefault(c=>c.Parameters["mode"]=="protect"&&
            (f.OwnerSeat==f.HappyGuard.GuardedSeat||_players[p.PlayerSeat].TeamId is{}team&&team==_players[f.OwnerSeat].TeamId))??p.Choices[0];
        return p.Choices.FirstOrDefault(c=>c.Parameters["mode"]=="recover")??
            p.Choices.FirstOrDefault(c=>c.Parameters["mode"]=="damage"&&c.Targets is[var target]&&PublicTeamEnemy(f.OwnerSeat,_players[target]))??
            p.Choices.FirstOrDefault(c=>c.Parameters["mode"]=="grant"&&c.Targets.Contains(f.OwnerSeat))??p.Choices[0];
    }
}
