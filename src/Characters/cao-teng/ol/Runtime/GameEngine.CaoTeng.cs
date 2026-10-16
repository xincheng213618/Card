namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string CaoTengYongzuDrawReason="skill-program.cao-teng.yongzu.draw";
    private const string CaoTengJianxiongDrawReason="skill-program.cao-teng.jianxiong.draw";
    private const string CaoTengJianxiongClaimReason="skill-program.cao-teng.jianxiong.claim";
    private bool HasCaoTengCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveQingliuFactionChoice)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveYongzuDistinctBenefits)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveCaoTengJianxiongDrawAndClaim);
    private static CardConversionSource CaoTengSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    [ProgramCapability]private sealed class CaoTengCapability(GameEngine engine):ICaoTengHost{public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>engine.BeginCaoTeng(f,op);}
    private bool IsCaoTengQingliuDyingExitContext(ProgramTriggerCandidate candidate,ProgramSkillWindowContext context)
    {
        if(GetProgramTrigger(candidate).Effects is not[{Op:SkillProgramEffectOp.ResolveQingliuFactionChoice}]||context.Window!=SkillProgramTriggerWindow.DyingExited||context.OwnerSeat!=candidate.OwnerSeat||
            context.SourceSeat!=candidate.OwnerSeat||context.TargetSeat!=candidate.OwnerSeat||!_players[candidate.OwnerSeat].IsAlive||_players[candidate.OwnerSeat].Hp<=0||
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId)is not{Window:SkillProgramTriggerWindow.DyingExited,Continuation:ProgramLifecycleContinuation.ResumeYanPuDyingExit,YanPuDyingExit:{}ret}parent||
            ret.VictimSeat!=candidate.OwnerSeat||ret.Round!=_roundNumber||ret.Turn!=_turnNumber||ret.TurnOwner!=_currentSeat||parent.OwnerSeat!=candidate.OwnerSeat||
            parent.CandidateIndex<0||parent.CandidateIndex>=parent.Candidates.Count||parent.Candidates[parent.CandidateIndex]!=candidate)return false;
        var i=_resolutionStack.FindIndex(w=>w.Id==parent.Id);
        return i>0&&_resolutionStack[i-1].Id==ret.DyingParentFrameId&&ProgramEventHistory<PlayerDyingEvent>().FirstOrDefault(e=>e.VictimSeat==candidate.OwnerSeat)?.ResolutionId==ret.DyingFrameId&&
            ProgramEventHistory<DyingResolvedEvent>().Count(e=>e.ResolutionId==ret.DyingFrameId&&e.VictimSeat==candidate.OwnerSeat&&e.Survived)==1&&
            !ProgramEventHistory<CaoTengFactionCompletedEvent>().Any(e=>e.Source.OwnerSeat==candidate.OwnerSeat&&!e.Setup);
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27300)]
    private bool CanOfferCaoTeng(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext context)=>
        !t.Effects.Any(e=>e.Op==SkillProgramEffectOp.ResolveQingliuFactionChoice)||context.Window==SkillProgramTriggerWindow.GameStarting||IsCaoTengQingliuDyingExitContext(c,context);
    private bool CaoTengParentMatches(ProgramSkillFrame f)
    {
        if(f.TriggerId is null||f.ActivationId!=f.TriggerId||f.WindowContext is not{}c||c.OwnerSeat!=f.OwnerSeat)return false;
        if(c.Window==SkillProgramTriggerWindow.AfterDamageApplied)
            return c.Amount>0&&c.TargetSeat==f.OwnerSeat&&_resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w=>w.Id==c.ParentFrameId)is{}w&&w.ParentFrameId==c.DamageFrameId&&w.TargetSeat==f.OwnerSeat&&w.TriggerWindow==c.Window&&
                w.CandidateIndex>=0&&w.CandidateIndex<w.Candidates.Count&&MountObserverCandidateMatches(f,w.Candidates[w.CandidateIndex].ToProgramCandidate());
        if(_resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w=>w.Id==c.ParentFrameId)is not{}p||p.Window!=c.Window||p.CandidateIndex<0||p.CandidateIndex>=p.Candidates.Count||!MountObserverCandidateMatches(f,p.Candidates[p.CandidateIndex]))return false;
        return c.Window switch
        {
            SkillProgramTriggerWindow.GameStarting=>_phase==TurnPhase.NotStarted&&p.Continuation==ProgramLifecycleContinuation.CompleteGameStarting&&c.TargetSeat==p.OwnerSeat&&c.SourceSeat==p.OwnerSeat,
            SkillProgramTriggerWindow.TurnStartBeforeNormalFlow=>p.OwnerSeat==f.OwnerSeat&&c.TargetSeat==f.OwnerSeat&&c.SourceSeat==f.OwnerSeat&&_currentSeat==f.OwnerSeat,
            SkillProgramTriggerWindow.DyingExited=>IsCaoTengQingliuDyingExitContext(p.Candidates[p.CandidateIndex],c),_=>false
        };
    }
    private SkillProgramStepOutcome BeginCaoTeng(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);var q=op==SkillProgramEffectOp.ResolveQingliuFactionChoice;var y=op==SkillProgramEffectOp.ResolveYongzuDistinctBenefits;
        if(f.CaoTeng is not null||f.InstructionIndex!=1||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||!_players[f.OwnerSeat].IsAlive||!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)||!CaoTengParentMatches(f)||
            f.SkillId!=(q?"ol:qingliu":y?"ol:yongzu":"ol:caoteng-jianxiong")||GetPausedPrivateOfferEffect(f)?.Op!=op)
            throw new InvalidOperationException("Cao Teng requires his exact living unpaid native binding.");
        // Prime only missing native ownership observations before any benefit can
        // grant a skill. Preserve known revisions and their unobserved changes.
        if(y)foreach(var player in _players.Where(p=>p.IsAlive))
            _observedSkillGrantRevisions.TryAdd(player.Seat,player.SkillGrants.Revision);
        var r=new CaoTengReceipt(CaoTengSource(f),f.GameplayHash,op,_turnNumber,f.WindowContext!.ParentFrameId,q?CaoTengStage.FactionChoice:y?CaoTengStage.YongzuTarget:CaoTengStage.JianxiongDrawChildren){Before=_movementSequence,After=_movementSequence};
        if(!q&&!y)r=r with{Materials=GetClaimableProgramDamageCards(f).Select(c=>new CaoTengDamageMaterial(c.Id,c.Kind,_cardMovements.Last(m=>m.CardId==c.Id).Sequence)).ToArray()};
        ReplaceRuntimeTop(f=f with{CaoTeng=r});AdvanceEventRulesAndQueueFact(new CaoTengStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.ActualTurn,r.ParentId));
        if(q&&f.WindowContext.Window==SkillProgramTriggerWindow.DyingExited)
        {var faction=GetEffectiveFactionId(_players[f.OwnerSeat])=="wei"?"qun":"wei";ApplyCaoTengFaction(f,faction,false);return SkillProgramStepOutcome.AwaitChild;}
        if(!q&&!y)
        {
            for(var i=0;i<r.Materials.Count;i++)AdvanceEventRulesAndQueueFact(new CaoTengDamageMaterialFrozenEvent(f.Id,i,r.Materials[i]));
            AdvanceEventRulesAndQueueFact(new CaoTengJianxiongDrawIssuedEvent(f.Id,r.Before));DrawProgramCards(f.Id,f.OwnerSeat,1,null,null,SkillProgramCardSetVisibility.Private,new(CaoTengJianxiongDrawReason));
            f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f with{CaoTeng=f.CaoTeng! with{After=_movementSequence}});ContinueCaoTeng(f.Id);return SkillProgramStepOutcome.AwaitChild;
        }
        if(y&&!_players.Any(p=>p.IsAlive&&p.Seat!=f.OwnerSeat)){FinishCaoTeng(f);return SkillProgramStepOutcome.AwaitChild;}
        PublishCaoTeng(f);return SkillProgramStepOutcome.AwaitChoice;
    }
    private int CaoTengChooser(ProgramSkillFrame f)=>f.CaoTeng is{Stage:CaoTengStage.YongzuChoice or CaoTengStage.YongzuChildren,Cursor:1,TargetSeat:{}target}?target:f.OwnerSeat;
    private IReadOnlyList<PromptChoice> CaoTengChoices(ProgramSkillFrame f)
    {
        var r=f.CaoTeng!;var choices=new List<PromptChoice>();
        void Add(string branch,string label,int? target=null)=>choices.Add(new(new($"cao-teng.{f.Id}.{branch}.{target}"),label,[],target is{}s?[s]:[],new Dictionary<string,string>{{"program-action","cao-teng"},{"branch",branch}}));
        if(r.Stage==CaoTengStage.FactionChoice){Add("qun","加入群势力");Add("wei","加入魏势力");}
        else if(r.Stage==CaoTengStage.YongzuTarget){foreach(var p in _players.Where(p=>p.IsAlive&&p.Seat!=f.OwnerSeat))Add("target",$"选择{p.Name}",p.Seat);}
        else if(r.Stage==CaoTengStage.YongzuChoice)
            foreach(var item in new[]{("draw","摸两张牌"),("recover","回复1点体力"),("restore","复原武将牌"),("limit","手牌上限+1"),("skill","获得本势力技能至自己下回合开始")})
                if(item.Item1!=r.FirstOption&&(r.SameFaction||item.Item1 is not("limit" or "skill"))&&(item.Item1!="recover"||_players[CaoTengChooser(f)].HasHpValue&&_players[CaoTengChooser(f)].Hp<_players[CaoTengChooser(f)].MaxHp))Add(item.Item1,item.Item2);
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishCaoTeng(ProgramSkillFrame f)
    {
        var chooser=CaoTengChooser(f);var s=_contentRegistry.GetSkill(f.SkillId);ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.AwaitingResponse});
        _pendingDecision=new(DecisionKind.ProgramTrigger,chooser,s.Name,[],CaoTengChoices(f).SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=true,Choices=CaoTengChoices(f),SkillPrompt=new(f.SkillId,s.Name,s.Name,s.Description)};
        _status=_players[chooser].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;
    }
    [ProgramAnswer("cao-teng")]private void AnswerCaoTeng(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{CaoTeng:{}r}f||_pendingDecision?.PlayerSeat!=CaoTengChooser(f)||!AssistedChoicesEqual([choice],CaoTengChoices(f).Where(c=>c.Id==choice.Id).ToArray()))throw new InvalidOperationException("Cao Teng lost the actual private chooser or original distinct choice.");
        AssertCaoTeng(f);ClearPendingDecision();var branch=choice.Parameters["branch"];
        if(r.Stage==CaoTengStage.FactionChoice){ApplyCaoTengFaction(f,branch,true);return;}
        if(r.Stage==CaoTengStage.YongzuTarget)
        {
            var target=choice.Targets.Single();var same=GetEffectiveFactionId(_players[target])==GetEffectiveFactionId(_players[f.OwnerSeat]);
            ReplaceRuntimeTop(f=f with{CaoTeng=r with{TargetSeat=target,SameFaction=same,Stage=CaoTengStage.YongzuChoice}});
            AdvanceEventRulesAndQueueFact(new CaoTengRecipientChosenEvent(f.Id,target,same));PublishCaoTeng(f);return;
        }
        var actor=CaoTengChooser(f);var recovery=branch=="recover"&&_players[actor].HasHpValue?Math.Min(1,Math.Max(0,_players[actor].MaxHp-_players[actor].Hp)):0;
        ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect,CaoTeng=r=r with{Option=branch,RecoveryRequested=recovery,Before=_movementSequence,After=_movementSequence,Stage=CaoTengStage.YongzuChildren}});
        AdvanceEventRulesAndQueueFact(new CaoTengBenefitIssuedEvent(f.Id,r.Cursor,actor,branch,recovery,r.Before));
        if(_players[actor].IsAlive)
        {
            switch(branch)
            {
                case "draw":DrawProgramCards(f.Id,actor,2,null,null,SkillProgramCardSetVisibility.Private,new(CaoTengYongzuDrawReason));break;
                case "recover":if(recovery>0)new ProgramSkillHost(this).Recover(f.Id,f.OwnerSeat,actor,recovery,null,null);break;
                case "restore":SetProgramTargetFaceState(f.Id,f.OwnerSeat,actor,false);SetProgramChainedState(GetActiveProgramFrame(f.Id),false,actor);break;
                case "limit":AdvanceEventRulesAndQueueFact(new CaoTengHandLimitGrantedEvent(f.Id,r.Cursor,actor));break;
                case "skill":GrantCaoTengSkill(f,actor);break;
            }
        }
        f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f with{CaoTeng=f.CaoTeng! with{After=_movementSequence}});AdvanceEventRulesAndQueueFact(new CaoTengBenefitAppliedEvent(f.Id,r.Cursor,_movementSequence));ContinueCaoTeng(f.Id);
    }
    private void ApplyCaoTengFaction(ProgramSkillFrame f,string faction,bool setup)
    {
        ReplaceRuntimeTop(f=f with{CaoTeng=f.CaoTeng! with{Faction=faction,Stage=CaoTengStage.FactionApplying}});
        AdvanceEventRulesAndQueueFact(new CaoTengFactionChosenEvent(f.Id,faction,setup));ApplyCaoTengFactionChange(f,faction);
        AdvanceEventRulesAndQueueFact(new CaoTengFactionCompletedEvent(f.Id,CaoTengSource(f),faction,setup));FinishCaoTeng(GetActiveProgramFrame(f.Id));
    }
    private bool IsCaoTengFactionChange(ProgramSkillFrame f,string faction)=>f.CaoTeng is{Op:SkillProgramEffectOp.ResolveQingliuFactionChoice,Stage:CaoTengStage.FactionApplying}r&&r.Faction==faction&&faction is("qun" or "wei")&&
        r.Source==CaoTengSource(f)&&r.GameplayHash==f.GameplayHash&&f.InstructionIndex==1&&!ProgramEventHistory<CaoTengFactionCompletedEvent>().Any(e=>e.FrameId==f.Id)&&CaoTengParentMatches(f)&&_resolutionStack.LastOrDefault()?.Id==f.Id&&
        ProgramEventHistory<CaoTengFactionChosenEvent>().Count(e=>e.FrameId==f.Id&&e.Faction==faction&&e.Setup==(f.WindowContext!.Window==SkillProgramTriggerWindow.GameStarting))==1&&
        (f.WindowContext!.Window==SkillProgramTriggerWindow.GameStarting||faction==(GetEffectiveFactionId(_players[f.OwnerSeat])=="wei"?"qun":"wei"));
    private void GrantCaoTengSkill(ProgramSkillFrame f,int recipient)
    {
        var faction=GetEffectiveFactionId(_players[recipient]);if(faction is not("wei" or "qun"))return;
        var skill=faction=="wei"?"ol:caoteng-jianxiong":"ol:tianming";_= _contentRegistry.GetSkill(skill);var r=f.CaoTeng!;
        var source=$"caoteng-yongzu:{f.Id}:{r.Cursor}:{recipient}";var grantId=source+":"+skill;
        var existing=GetSkillBindingShard(_players[recipient]).ActiveGrants.Where(g=>g.SkillId==skill).OrderBy(g=>g.SourceId.StartsWith("caoteng-yongzu:",StringComparison.Ordinal)?1:0).ThenBy(g=>g.SkillInstanceId,StringComparer.Ordinal).FirstOrDefault();
        var instance=existing?.SkillInstanceId??grantId;_players[recipient].SkillGrants.Grant(new(grantId,skill,instance,source));
        AdvanceEventRulesAndQueueFact(new CaoTengSkillGrantedEvent(f.Id,r.Cursor,recipient,skill,instance,grantId,source,_turnNumber));
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,27300)]private void ExpireCaoTengNextOwnTurn(TurnStartedEvent started)
    {
        if(!HasCaoTengCapability)return;var expired=ProgramEventHistory<CaoTengSkillExpiredEvent>().Select(e=>e.GrantId).ToHashSet(StringComparer.Ordinal);
        foreach(var grant in ProgramEventHistory<CaoTengSkillGrantedEvent>().Where(e=>e.RecipientSeat==started.ActorSeat&&e.ActualTurn<started.TurnNumber&&!expired.Contains(e.GrantId)).ToArray())
        {
            var actual=_players[grant.RecipientSeat].SkillGrants.Grants.SingleOrDefault(g=>g.GrantId==grant.GrantId);
            if(actual is not null){if(actual.SkillId!=grant.SkillId||actual.SkillInstanceId!=grant.SkillInstanceId||actual.SourceId!=grant.SourceId)throw new InvalidOperationException("Yongzu's exact grant source was replaced.");_players[grant.RecipientSeat].SkillGrants.RemoveGrant(grant.GrantId);}
            AdvanceEventRulesAndQueueFact(new CaoTengSkillExpiredEvent(grant.GrantId,grant.RecipientSeat,started.TurnNumber));
        }
    }
    private int CaoTengYongzuHandLimitBonus(int seat)=>HasCaoTengCapability?ProgramEventHistory<CaoTengHandLimitGrantedEvent>().Count(e=>e.RecipientSeat==seat):0;
    private bool DrainCaoTeng(long id)=>TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.Program)||TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.Program)||TryBeginCardsMovedProgramWindow(id)||TryBeginAdvancedSkillsChanged(id);
    private void ContinueCaoTeng(long id)
    {
        while(_resolutionStack.LastOrDefault()is ProgramSkillFrame{CaoTeng:{}r}f&&f.Id==id)
        {
            AssertCaoTeng(f);
            if(r.Stage is CaoTengStage.YongzuChildren or CaoTengStage.JianxiongDrawChildren or CaoTengStage.JianxiongClaimChildren)
            {if(DrainCaoTeng(id))return;f=GetActiveProgramFrame(id);r=f.CaoTeng!;}
            if(_winner!=Winner.None){FinishCaoTeng(f);return;}
            if(r.Stage==CaoTengStage.YongzuChildren)
            {
                if(r.Cursor==1||r.TargetSeat is not{}target||!_players[target].IsAlive){FinishCaoTeng(f);return;}
                ReplaceRuntimeTop(f=f with{CaoTeng=r with{Cursor=1,FirstOption=r.Option,Option=null,RecoveryRequested=0,Before=_movementSequence,After=_movementSequence,Stage=CaoTengStage.YongzuChoice}});PublishCaoTeng(f);return;
            }
            if(r.Stage==CaoTengStage.JianxiongDrawChildren)
            {
                ReplaceRuntimeTop(f=f with{CaoTeng=r=r with{Stage=CaoTengStage.JianxiongClaimChildren,Before=_movementSequence,After=_movementSequence}});
                AdvanceEventRulesAndQueueFact(new CaoTengJianxiongClaimIssuedEvent(f.Id,r.Before));
                if(_players[f.OwnerSeat].IsAlive)
                {
                    var cards=GetClaimableProgramDamageCards(f).Where(c=>r.Materials.Any(m=>m.CardId==c.Id&&m.Kind==c.Kind&&_cardMovements.Last(x=>x.CardId==c.Id).Sequence==m.EntrySequence)).ToArray();
                    if(cards.Length>0)
                    {
                        var damage=_resolutionStack.OfType<DamageTriggerWindowFrame>().Single(w=>w.Id==r.ParentId);var attack=GetDamageTriggerAttack(damage);
                        MoveCards(cards,CardLocation.Processing,CardLocation.Hand(f.OwnerSeat),new(CaoTengJianxiongClaimReason));
                        if(ActiveGroupCard is{}group){var claimed=group.DamageClaimedPhysicalCardIds.ToHashSet();foreach(var card in cards)RecordClaimedGroupPhysicalCard(claimed,group.ResolutionId,group.PhysicalCards,attack.ResolutionId,card.Id);group.SetClaimedPhysicalCards(claimed.ToArray());}
                        foreach(var card in cards)AdvanceEventRulesAndQueueFact(new CaoTengJianxiongClaimedEvent(f.Id,card.Id,_cardMovements.Single(m=>m.Sequence>r.Before&&m.CardId==card.Id&&m.From==CardLocation.Processing&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==CaoTengJianxiongClaimReason).Sequence));
                        CaptureExtraEffectMaterialClaims(f,attack,cards,r.Before,new(CaoTengJianxiongClaimReason));
                    }
                }
                f=GetActiveProgramFrame(id);ReplaceRuntimeTop(f with{CaoTeng=f.CaoTeng! with{After=_movementSequence}});continue;
            }
            if(r.Stage==CaoTengStage.JianxiongClaimChildren){FinishCaoTeng(f);return;}
            if(_pendingDecision is null)PublishCaoTeng(f);return;
        }
    }
    private void FinishCaoTeng(ProgramSkillFrame f){AdvanceEventRulesAndQueueFact(new CaoTengCompletedEvent(f.Id));ReplaceRuntimeTop(f=f with{CaoTeng=null});FinishProgramSkill(f,true);}
    [ProgramContinuation(ProgramContinuationKind.Resume,27300)]private bool ResumeCaoTeng(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{CaoTeng:not null}f||f.Id!=id)return false;ContinueCaoTeng(id);return true;}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27300)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27300)]
    private bool ContinuePaidCaoTeng(ProgramSkillFrame f)=>f.CaoTeng is{}r&&(r.Op==SkillProgramEffectOp.ResolveCaoTengJianxiongDrawAndClaim||ProgramEventHistory<CaoTengBenefitIssuedEvent>().Any(e=>e.FrameId==f.Id&&e.Index==0))&&ValidCaoTeng(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveQingliuFactionChoice,SkillProgramEffectOp.ResolveYongzuDistinctBenefits,SkillProgramEffectOp.ResolveCaoTengJianxiongDrawAndClaim)]
    private PromptChoice ChooseCaoTengAi(PendingDecision p,ProgramSkillFrame f)
    {
        if(f.CaoTeng!.Stage==CaoTengStage.FactionChoice)return p.Choices.First(c=>c.Parameters["branch"]=="wei");
        if(f.CaoTeng.Stage==CaoTengStage.YongzuTarget)return p.Choices.OrderByDescending(c=>_aiBrains[f.OwnerSeat].ScoreProgramTarget(CreateSnapshot(f.OwnerSeat),c.Targets[0],new(1,0,0,0,0,0,false,false))).ThenBy(c=>c.Targets[0]).First();
        var actor=_players[CaoTengChooser(f)];var best=actor.IsFaceDown?"restore":actor.Hp<actor.MaxHp?"recover":"draw";
        return p.Choices.FirstOrDefault(c=>c.Parameters["branch"]==best)??p.Choices.First();
    }
}
