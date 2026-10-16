namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string CaiMaoExchangeReason="skill-program.cai-mao.zuolian.exchange";
    private const string CaiMaoRevealPrefix="cai-mao.reveal.";
    private bool HasCaiMaoCapability=>_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveZuolianWholeRevealExchange)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveJingzhouBeforeDamageToggles);
    private static CardConversionSource CaiMaoSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    [ProgramCapability]private sealed class CaiMaoCapability(GameEngine e):ICaiMaoHost
    {public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginCaiMao(f,op);}
    private SkillProgramStepOutcome BeginCaiMao(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(f.CaiMao is not null||f.InstructionIndex!=1||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||!_players[f.OwnerSeat].IsAlive||!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId))
            throw new InvalidOperationException("Cai Mao requires an original living, unpaid binding.");
        long parent=0;
        if(op==SkillProgramEffectOp.ResolveZuolianWholeRevealExchange)
        {
            if(f.SkillId!="ol:zuolian"||f.ActivationId!="whole-exchange"||f.TriggerId is not null||f.WindowContext is not null||_phase!=TurnPhase.Play||_currentSeat!=f.OwnerSeat)
                throw new InvalidOperationException("Zuolian requires the owner's actual Play.");
        }
        else
        {
            if(op!=SkillProgramEffectOp.ResolveJingzhouBeforeDamageToggles||f.SkillId!="ol:jingzhou"||f.TriggerId!="before-own-damage"||f.ActivationId!=f.TriggerId||f.WindowContext is not{}context||!CaiMaoDamageParentMatches(f,context))
                throw new InvalidOperationException("Jingzhou requires its exact pending native damage before HP is lost.");
            parent=context.ParentFrameId;
        }
        var r=new CaiMaoReceipt(CaiMaoSource(f),f.GameplayHash,op,_turnNumber,_turnProgression.OwnerSeat,Math.Max(0,_players[f.OwnerSeat].Hp),parent,CaiMaoStage.Targets){Before=_movementSequence,After=_movementSequence};
        ReplaceRuntimeTop(f=f with{CaiMao=r});
        AdvanceEventRulesAndQueueFact(new CaiMaoStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.ActualTurn,r.TurnOwner,r.FrozenHp,parent));
        PublishCaiMao(f);return SkillProgramStepOutcome.AwaitChild;
    }
    private bool CaiMaoDamageParentMatches(ProgramSkillFrame f,ProgramSkillWindowContext context)=>
        context.Window==SkillProgramTriggerWindow.BeforeDamageApplied&&context.OwnerSeat==f.OwnerSeat&&context.TargetSeat==f.OwnerSeat&&context.Amount>0&&
        _resolutionStack.OfType<BeforeDamageProgramWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId)is{}parent&&parent.TargetSeat==f.OwnerSeat&&parent.Amount==context.Amount&&!parent.Prevented&&parent.RedirectedTargetSeat is null&&
        parent.CandidateIndex>=0&&parent.CandidateIndex<parent.Candidates.Count&&MountObserverCandidateMatches(f,parent.Candidates[parent.CandidateIndex].Candidate);
    private bool CaiMaoSourceLive(ProgramSkillFrame f)=>_players[f.OwnerSeat].IsAlive&&HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId);
    private IReadOnlyList<PromptChoice> CaiMaoChoices(ProgramSkillFrame f)
    {
        var r=f.CaiMao!;var result=new List<PromptChoice>();
        if(r.Stage==CaiMaoStage.Targets)
        {
            if(r.Targets.Count<r.FrozenHp)
                foreach(var p in _players.Where(p=>p.IsAlive&&!r.Targets.Contains(p.Seat)&&(r.Op==SkillProgramEffectOp.ResolveJingzhouBeforeDamageToggles||GetHand(p).Count>0)))
                    result.Add(new(new($"cai-mao.{f.Id}.target.{p.Seat}"),$"选择{p.Name}",[],[p.Seat],new Dictionary<string,string>{{"program-action","cai-mao"},{"branch","target"}}));
            result.Add(new(new($"cai-mao.{f.Id}.done"),r.Targets.Count==0?"不选择角色":"确认所选角色",[],[],new Dictionary<string,string>{{"program-action","cai-mao"},{"branch","done"}}));
        }
        else if(r.Stage==CaiMaoStage.Exchange)
        {
            foreach(var accept in new[]{true,false})result.Add(new(new($"cai-mao.{f.Id}.exchange.{accept}"),accept?"全体交换":"全体不换",[],[],new Dictionary<string,string>{{"program-action","cai-mao"},{"branch",accept?"exchange":"decline"}}));
        }
        return Array.AsReadOnly(result.ToArray());
    }
    private void PublishCaiMao(ProgramSkillFrame f)
    {
        AssertCaiMao(f);var choices=CaiMaoChoices(f);if(choices.Count==0){FinishCaiMao(f);return;}
        var skill=_contentRegistry.GetSkill(f.SkillId);ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,f.CaiMao!.Stage==CaiMaoStage.Exchange?"佐练：是否将本次全体展示牌与属性【杀】交换？":$"{skill.Name}：选择至多{f.CaiMao.FrozenHp}名角色，再确认。",[],choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat)
        {PromptId=CreatePromptId(),IsPrivate=true,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("cai-mao")]private void AnswerCaiMao(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{CaiMao:{}r}f||_pendingDecision?.PlayerSeat!=f.OwnerSeat||!CaiMaoChoices(f).Any(c=>AssistedChoicesEqual([c],[choice])))
            throw new InvalidOperationException("Cai Mao requires the exact current private whole-operation choice.");
        AssertCaiMao(f);ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});
        if(!CaiMaoSourceLive(f)){FinishCaiMao(f);return;}
        var branch=choice.Parameters["branch"];
        if(branch=="target")
        {
            var seat=choice.Targets.Single();var targets=r.Targets.Append(seat).ToArray();
            ReplaceRuntimeTop(f=f with{CaiMao=r with{Targets=targets}});AdvanceEventRulesAndQueueFact(new CaiMaoTargetChosenEvent(f.Id,targets.Length-1,seat));PublishCaiMao(f);return;
        }
        if(branch=="done")
        {
            var targets=r.Targets.OrderBy(s=>(s-r.TurnOwner+_playerCount)%_playerCount).ToArray();
            ReplaceRuntimeTop(f=f with{CaiMao=r=r with{Targets=targets,Committed=true}});
            AdvanceEventRulesAndQueueFact(new CaiMaoTargetsCommittedEvent(f.Id,targets.Length));
            if(targets.Length==0){FinishCaiMao(f);return;}
            if(r.Op==SkillProgramEffectOp.ResolveJingzhouBeforeDamageToggles)
            {ReplaceRuntimeTop(f=f with{CaiMao=r with{Stage=CaiMaoStage.ToggleChildren}});ContinueCaiMao(f.Id);return;}
            var reveals=new List<CaiMaoReveal>();
            foreach(var holder in targets)
            {
                var hand=GetHand(_players[holder]).OrderBy(c=>c.Id).ToArray();
                if(!_players[holder].IsAlive||hand.Length==0)continue;
                var card=hand[_random.Next(hand.Length)];var entry=CurrentHandEntry(holder,card.Id)??throw new InvalidOperationException("Zuolian needs a real original hand entry.");
                var reveal=new CaiMaoReveal(holder,card.Id,card.Kind,entry,CaiMaoRevealPrefix+reveals.Count);
                reveals.Add(reveal);ReplaceRuntimeTop(f=(GetActiveProgramFrame(f.Id) with{CaiMao=r=r with{Reveals=reveals.ToArray()}}));
                CommitProgramHandReveal(f.Id,holder,reveal.Bind,[card.Id]);f=GetActiveProgramFrame(f.Id);
                AdvanceEventRulesAndQueueFact(new CaiMaoHandRevealedEvent(f.Id,reveals.Count-1,reveal));
            }
            f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{CaiMao=f.CaiMao! with{Stage=CaiMaoStage.Exchange}});PublishCaiMao(f);return;
        }
        if(r.Stage!=CaiMaoStage.Exchange||branch is not("exchange" or "decline"))throw new InvalidOperationException("Zuolian requires one whole-roster exchange decision.");
        var accepted=branch=="exchange";ReplaceRuntimeTop(f=f with{CaiMao=r=r with{ExchangeAccepted=accepted}});
        AdvanceEventRulesAndQueueFact(new CaiMaoExchangeChosenEvent(f.Id,accepted));
        if(!accepted){FinishCaiMao(f);return;}
        PayCaiMaoExchange(f);
    }
    private void PayCaiMaoExchange(ProgramSkillFrame f)
    {
        var r=f.CaiMao!;var used=new HashSet<int>();var exchanges=new List<CaiMaoExchange>();
        foreach(var old in r.Reveals)
        {
            if(!_players[old.HolderSeat].IsAlive||CurrentHandEntry(old.HolderSeat,old.CardId)!=old.HandEntry)continue;
            Card? card=null;CardLocation from=default;
            foreach(var zone in new[]{CardLocation.DiscardPile,CardLocation.DrawPile})
            {
                foreach(var kind in new[]{CardKind.FireSlash,CardKind.ThunderSlash})
                {
                    var eligible=_cardZones.CardsAt(zone).Where(c=>c.Kind==kind&&!used.Contains(c.Id)).OrderBy(c=>c.Id).ToArray();
                    if(eligible.Length==0)continue;card=eligible[_random.Next(eligible.Length)];from=zone;break;
                }
                if(card is not null)break;
            }
            if(card is null)break;used.Add(card.Id);
            exchanges.Add(new(old.HolderSeat,old.CardId,old.PrintedKind,old.HandEntry,card.Id,card.Kind,from));
        }
        ReplaceRuntimeTop(f=f with{CaiMao=r=r with{Stage=CaiMaoStage.ExchangeChildren,Exchanges=exchanges.ToArray(),Before=_movementSequence,After=_movementSequence}});
        for(var i=0;i<exchanges.Count;i++)AdvanceEventRulesAndQueueFact(new CaiMaoExchangeFrozenEvent(f.Id,i,exchanges[i]));
        var replacements=exchanges.Where(e=>e.Supply==CardLocation.DrawPile).ToDictionary(e=>e.NewCardId,e=>e.OldCardId);
        var desiredDeck=_cardZones.CardsAt(CardLocation.DrawPile).Select(c=>replacements.GetValueOrDefault(c.Id,c.Id)).ToArray();
        if(exchanges.Count>0)
        {
            var map=exchanges.SelectMany(e=>new[]{(Id:e.OldCardId,To:e.Supply),(Id:e.NewCardId,To:CardLocation.Hand(e.HolderSeat))}).ToDictionary(e=>e.Id,e=>e.To);
            MoveProgramCardsFromMultipleSources(map.Keys.ToArray(),CardLocation.Processing,new(CaiMaoExchangeReason),(batch,moves)=>
            {
                var current=GetActiveProgramFrame(f.Id);
                ReplaceRuntimeTop(current with{CaiMao=current.CaiMao! with{After=_movementSequence}});
                AdvanceEventRulesAndQueueFact(new CaiMaoExchangePaidEvent(f.Id,batch,r.Before,_movementSequence,exchanges.Count));
            },(card,_)=>map[card.Id]);
            if(replacements.Count>0)_cardZones.ReorderDrawPileTop(_cardZones.CardsAt(CardLocation.DrawPile).Reverse().Select(c=>c.Id).ToArray(),desiredDeck.Reverse().ToArray(),[]);
        }
        else AdvanceEventRulesAndQueueFact(new CaiMaoExchangePaidEvent(f.Id,0,r.Before,r.Before,0));
        ContinueCaiMao(f.Id);
    }
    private bool DrainCaiMao(long id)=>TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.Program)||TryBeginCardsMovedProgramWindow(id)||TryBeginAdvancedSkillsChanged(id);
    private void ContinueCaiMao(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{CaiMao:{}r}f||f.Id!=id)return;AssertCaiMao(f);
        if(r.Stage is CaiMaoStage.Targets or CaiMaoStage.Exchange){if(_pendingDecision is null)PublishCaiMao(f);return;}
        if(DrainCaiMao(id))return;f=GetActiveProgramFrame(id);r=f.CaiMao!;
        if(r.Stage==CaiMaoStage.ToggleChildren)
        {
            while(r.Cursor<r.Targets.Count)
            {
                var index=r.Cursor;var target=r.Targets[index];var before=_players[target].IsChained;
                ReplaceRuntimeTop(f=f with{CaiMao=r=r with{Cursor=index+1}});
                if(_players[target].IsAlive)SetProgramChainedState(f,!before,target);
                var actual=_players[target].IsChained;
                AdvanceEventRulesAndQueueFact(new CaiMaoToggleIssuedEvent(id,index,target,before,!before,actual));
                if(DrainCaiMao(id))return;f=GetActiveProgramFrame(id);r=f.CaiMao!;
            }
        }
        FinishCaiMao(f);
    }
    private void FinishCaiMao(ProgramSkillFrame f)
    {
        var r=f.CaiMao!;AdvanceEventRulesAndQueueFact(new CaiMaoCompletedEvent(f.Id,r.Op,r.Exchanges.Count,r.Cursor));
        ReplaceRuntimeTop(f=f with{CaiMao=null,CardSetBindings=Array.AsReadOnly(f.CardSetBindings.Where(b=>!b.Name.StartsWith(CaiMaoRevealPrefix,StringComparison.Ordinal)).ToArray())});FinishProgramSkill(f,true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume,27250)]private bool ResumeCaiMao(long id)
    {if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{CaiMao:not null}f||f.Id!=id)return false;ContinueCaiMao(id);return true;}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27250)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27250)]
    private bool ContinuePaidCaiMao(ProgramSkillFrame f)=>f.CaiMao is{Stage:CaiMaoStage.ExchangeChildren or CaiMaoStage.ToggleChildren}&&ValidCaiMao(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveZuolianWholeRevealExchange,SkillProgramEffectOp.ResolveJingzhouBeforeDamageToggles)]
    private PromptChoice ChooseCaiMaoAi(PendingDecision p,ProgramSkillFrame f)
    {
        var r=f.CaiMao!;
        if(r.Stage==CaiMaoStage.Exchange)
        {
            var good=r.Reveals.Sum(e=>_aiBrains[f.OwnerSeat].ScoreProgramTarget(CreateSnapshot(f.OwnerSeat),e.HolderSeat,new SkillProgramAiHint(1,0,0,0,0,0,false,false)));
            return p.Choices.Single(c=>c.Parameters["branch"]==(good>=0?"exchange":"decline"));
        }
        var candidates=p.Choices.Where(c=>c.Targets.Count==1).Select(c=>(Choice:c,Score:CaiMaoTargetScore(f,c.Targets[0]))).OrderByDescending(x=>x.Score).ThenBy(x=>x.Choice.Targets[0]).ToArray();
        return candidates.Length>0&&candidates[0].Score>0?candidates[0].Choice:p.Choices.Single(c=>c.Parameters["branch"]=="done");
    }
    private double CaiMaoTargetScore(ProgramSkillFrame f,int target)
    {
        var support=_aiBrains[f.OwnerSeat].ScoreProgramTarget(CreateSnapshot(f.OwnerSeat),target,new SkillProgramAiHint(1,0,0,0,0,0,false,false));
        if(f.CaiMao!.Op==SkillProgramEffectOp.ResolveZuolianWholeRevealExchange)return support;
        var elemental=f.WindowContext?.ParentFrameId is{}id&&_resolutionStack.OfType<BeforeDamageProgramWindowFrame>().Any(w=>w.Id==id&&w.Nature is DamageNature.Fire or DamageNature.Thunder);
        if(target==f.OwnerSeat&&elemental)return _players[target].IsChained?0:100;
        return _players[target].IsChained?support:-support;
    }
}
