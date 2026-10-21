using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string MouHgLieji="mou:lieji",MouHgQuzhou="mou:quzhou",MouHgRevealBind="quzhou-reveal",
        MouHgObtainReason="skill-program.mou:quzhou.obtain",MouHgDiscardReason="skill-program.mou:quzhou.unusable-slash";
    private static CardConversionSource MouHgSource(ProgramSkillFrame f)=>new(f.SkillId,f.TriggerId??f.ActivationId,f.OwnerSeat,f.SkillInstanceId);
    [ProgramCapability]private sealed class MouHuangGaiHost(GameEngine e):IMouHuangGaiHost
    {public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>op==SkillProgramEffectOp.ArmMouHuangGaiLieji?e.ArmMouHgLieji(f):e.BeginMouHgQuzhou(f);}
    private bool MouHgLiejiParent(ProgramSkillFrame f)
    {
        if(f.SkillId!=MouHgLieji||f.TriggerId!="after-trick"||f.ActivationId!=f.TriggerId||f.InstructionIndex!=1||f.WindowContext is not{Window:SkillProgramTriggerWindow.CardUseCompleted,CardUse:{}info}w||w.OwnerSeat!=f.OwnerSeat)return false;
        var at=_resolutionStack.FindIndex(x=>x.Id==f.Id);
        if(at<1||_resolutionStack[at-1]is not ProgramCardTriggerWindowFrame p||p.Id!=w.ParentFrameId||p.Action.ActionId!=info.CardActionId||p.Action.ActorSeat!=f.OwnerSeat||
            CardUseCategoryCatalog.Get(p.Action.EffectiveKind)is not(CardUseCategories.InstantTrick or CardUseCategories.DelayedTrick)||p.CandidateIndex<0||p.CandidateIndex>=p.Candidates.Count)return false;
        var c=p.Candidates[p.CandidateIndex];return c.OwnerSeat==f.OwnerSeat&&c.SkillId==f.SkillId&&c.TriggerId==f.TriggerId&&c.SkillInstanceId==f.SkillInstanceId&&c.GameplayHash==f.GameplayHash;
    }
    private SkillProgramStepOutcome ArmMouHgLieji(ProgramSkillFrame supplied)
    {
        var f=GetActiveProgramFrame(supplied.Id);if(!MouHgLiejiParent(f)||ProgramEventHistory<MouHgLiejiGrantedEvent>().Any(e=>e.FrameId==f.Id))throw new InvalidOperationException("Lieji requires its original completed actual Trick use.");
        var owner=_players[f.OwnerSeat];var grants=GetHand(owner).Where(c=>IsDamageCardKind(AdvancedEffectiveHandKind(owner,c))).Select(c=>new MouHgLiejiGrant(f.Id,MouHgSource(f),f.GameplayHash,_turnNumber,_turnProgression.OwnerSeat,c.Id)).ToArray();
        owner.MouHgLiejiGrants=Array.AsReadOnly((owner.MouHgLiejiGrants??[]).Concat(grants).ToArray());
        AdvanceEventRulesAndQueueFact(new MouHgLiejiGrantedEvent(f.Id,MouHgSource(f),_turnNumber,_turnProgression.OwnerSeat));return SkillProgramStepOutcome.Continue;
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,35400)]
    private void BindMouHgBonuses(CardUseDeclaredEvent declared)
    {
        if(LifecycleCardUse(declared.ResolutionId)is not{Action:{Type:CardActionType.Use}a}u||a.PhysicalCards.Count==0)return;
        var grants=_players.SelectMany(p=>p.MouHgLiejiGrants??[]).Where(g=>g.Turn==_turnNumber&&g.TurnOwner==_turnProgression.OwnerSeat&&a.PhysicalCards.Any(c=>c.CardId==g.CardId)).ToArray();
        if(grants.Length==0)return;if(u.MouHgBonuses is not null)throw new InvalidOperationException("Lieji cannot issue its material benefits twice.");
        UpdateLifecycleCardUse(u.Id,x=>x with{MouHgBonuses=grants.Select(g=>new MouHgUseBonus(u.Id,a.ActionId,g)).ToArray()});
        foreach(var g in grants)AdvanceEventRulesAndQueueFact(new MouHgBonusIssuedEvent(u.Id,a.ActionId,g.FrameId,g.CardId));
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,35401)]
    private void ExpireMouHgLieji(TurnEndedEvent ended)
    {foreach(var p in _players)if(p.MouHgLiejiGrants is{}grants)p.MouHgLiejiGrants=grants.Where(g=>g.Turn!=ended.TurnNumber||g.TurnOwner!=ended.ActorSeat).ToArray()is{Length:>0}left?Array.AsReadOnly(left):null;}
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount,35400)]
    private int MouHgDamageBonus(IDamageAttempt attack)
    {
        if(attack.IsChainPropagation||attack is not CardAttackHandle||LifecycleCardUse(attack.ResolutionId)is not{Action:{Type:CardActionType.Use}a,MouHgBonuses:{}bonuses}u)return 0;
        return bonuses.Count(b=>b.UseId==u.Id&&b.ActionId==a.ActionId&&a.PhysicalCards.Any(c=>c.CardId==b.Grant.CardId)&&
            b.Grant.Turn==_turnNumber&&b.Grant.TurnOwner==_turnProgression.OwnerSeat&&_contentRegistry.GetSkill(b.Grant.Source.SkillId).Program?.GameplayHash==b.Grant.GameplayHash&&
            ProgramEventHistory<MouHgBonusIssuedEvent>().Count(e=>e.UseId==u.Id&&e.ActionId==a.ActionId&&e.GrantFrameId==b.Grant.FrameId&&e.CardId==b.Grant.CardId)==1);
    }
    private SkillProgramStepOutcome BeginMouHgQuzhou(ProgramSkillFrame supplied)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(f.MouHuangGai is not null){ContinueMouHg(f.Id);return SkillProgramStepOutcome.AwaitChild;}
        if(f.SkillId!=MouHgQuzhou||f.ActivationId!="reveal-and-sail"||f.TriggerId is not null||f.InstructionIndex!=1||f.OwnerSeat!=_currentSeat||_phase!=TurnPhase.Play||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0)throw new InvalidOperationException("Quzhou requires its original actual Play entry.");
        var r=new MouHgReceipt(MouHgSource(f),f.GameplayHash,_turnNumber,_turnProgression.OwnerSeat){Stage=MouHgStage.Reveal};ReplaceRuntimeTop(f=f with{MouHuangGai=r});
        AdvanceEventRulesAndQueueFact(new MouHgStartedEvent(f.Id,r.Source,r.GameplayHash,r.Turn,r.TurnOwner));RevealMouHg(f);return SkillProgramStepOutcome.AwaitChild;
    }
    private bool ValidMouHg(ProgramSkillFrame f)=>f.MouHuangGai is{}r&&f.SkillId==MouHgQuzhou&&f.ActivationId=="reveal-and-sail"&&f.TriggerId is null&&f.InstructionIndex==1&&r.Source==MouHgSource(f)&&r.GameplayHash==f.GameplayHash&&
        _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash==r.GameplayHash&&r.Turn==_turnNumber&&r.TurnOwner==_turnProgression.OwnerSeat&&r.Before<=r.After&&r.After<=_movementSequence&&r.Reveals.Select(c=>c.CardId).Distinct().Count()==r.Reveals.Count&&
        ProgramEventHistory<MouHgStartedEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.Turn==r.Turn&&e.TurnOwner==r.TurnOwner)==1;
    private void RevealMouHg(ProgramSkillFrame f)
    {
        var before=_movementSequence;var bind=MouHgRevealBind+"-"+f.MouHuangGai!.Reveals.Count.ToString(CultureInfo.InvariantCulture);
        ReplaceRuntimeTop(f=f with{MouHuangGai=f.MouHuangGai with{Stage=MouHgStage.RevealChildren,Before=before,After=before},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        RevealProgramTopCards(f.Id,f.OwnerSeat,1,null,bind,SkillProgramCardSetVisibility.Public);f=GetActiveProgramFrame(f.Id);var r=f.MouHuangGai!;
        var card=GetProgramCardSet(f,bind).CardIds.SingleOrDefault();
        if(card>0)
        {
            var actual=_cardMovements.Last(m=>m.Sequence>before&&m.CardId==card&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Processing);var revealed=new MouHgReveal(card,actual.CardKind,actual.Sequence);
            r=r with{Reveals=r.Reveals.Append(revealed).ToArray()};AdvanceEventRulesAndQueueFact(new MouHgRevealedEvent(f.Id,r.Reveals.Count-1,card,actual.CardKind,actual.Sequence));
        }
        ReplaceRuntimeTop(f=f with{MouHuangGai=r with{After=_movementSequence,RevealExhausted=card==0}});ContinueMouHg(f.Id);
    }
    private static PromptChoice MouHgChoice(ProgramSkillFrame f,string branch,string label,IReadOnlyList<int>? targets=null)=>new(new($"mou-hg.{f.Id}.{branch}.{string.Join('-',targets??[])}"),label,[],targets??[],new Dictionary<string,string>{{"program-action","mou-hg"},{"branch",branch},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)}});
    private IReadOnlyList<PromptChoice> MouHgChoices(ProgramSkillFrame f)
    {
        var r=f.MouHuangGai!;
        if(r.Stage==MouHgStage.Target)return Array.AsReadOnly(MouHgTargets(f).Select(seat=>MouHgChoice(f,"slash","对"+_players[seat].Name+"使用亮出的杀",[seat])).ToArray());
        if(r.Stage!=MouHgStage.Choice)return [];
        var choices=new List<PromptChoice>{MouHgChoice(f,"obtain","获得所有亮出的牌")};
        if(r.Reveals.Count-1<_players.Count(p=>p.IsAlive))choices.Add(MouHgChoice(f,"again","再次亮出牌堆顶一张牌"));return choices.AsReadOnly();
    }
    private void PublishMouHg(ProgramSkillFrame f)
    {
        var choices=MouHgChoices(f);if(choices.Count==0){ObtainMouHg(f);return;}
        ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});var s=_contentRegistry.GetSkill(f.SkillId);
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"趋舟：请选择。",[],choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=false,Choices=choices,SkillPrompt=new(s.Id,s.Name,s.Name,s.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("mou-hg")]
    private void AnswerMouHg(PromptChoice supplied)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{MouHuangGai:{}r}f||!ValidMouHg(f)||_pendingDecision?.PlayerSeat!=f.OwnerSeat||!MouHgChoices(f).Any(c=>AssistedChoicesEqual([c],[supplied])))throw new InvalidOperationException("Quzhou requires the exact currently published choice.");
        ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});
        switch(supplied.Parameters["branch"]){case"again":RevealMouHg(f);break;case"obtain":ObtainMouHg(f);break;case"slash":IssueMouHgSlash(f,supplied.Targets.Single());break;default:throw new InvalidOperationException("Unknown Quzhou choice.");}
    }
    private void ObtainMouHg(ProgramSkillFrame f)
    {
        var ids=f.MouHuangGai!.Reveals.Where(c=>_cardZones.GetLocation(c.CardId)==CardLocation.Processing).Select(c=>c.CardId).ToArray();
        ReplaceRuntimeTop(f=f with{MouHuangGai=f.MouHuangGai with{Stage=MouHgStage.ObtainChildren,Before=_movementSequence,After=_movementSequence},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        if(ids.Length>0)MoveProgramCardsFromMultipleSources(ids,CardLocation.Hand(f.OwnerSeat),new(MouHgObtainReason),(_,moves)=>SealMouHgMoves(f.Id,moves),disclosure:CardMovementDisclosure.FaceUp);
        f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f with{MouHuangGai=f.MouHuangGai! with{After=_movementSequence}});ContinueMouHg(f.Id);
    }
    private void SealMouHgMoves(long id,IReadOnlyList<CardMovementRecord> moves)
    {
        var f=GetActiveProgramFrame(id);ReplaceRuntimeFrame(id,f with{MouHuangGai=f.MouHuangGai! with{After=_movementSequence}});
        foreach(var m in moves)AdvanceEventRulesAndQueueFact(new MouHgMovedEvent(id,m.CardId,m.From,m.To,m.Sequence));
    }
    private bool DrainMouHg(long id)=>TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.Program)||TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.Program)||TryBeginCardsMovedProgramWindow(id)||TryBeginAdvancedSkillsChanged(id);
    private void ContinueMouHg(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{MouHuangGai:{}r}f||f.Id!=id)return;
        if(!ValidMouHg(f))throw new InvalidOperationException("Quzhou lost its actual original owning continuation.");
        if(r.Stage is MouHgStage.Choice or MouHgStage.Target){PublishMouHg(f);return;}if(r.Stage==MouHgStage.NativeUse)return;
        if(DrainMouHg(id))return;f=GetActiveProgramFrame(id);r=f.MouHuangGai!;ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null});
        if(_winner!=Winner.None||!_players[f.OwnerSeat].IsAlive){FinishMouHg(f);return;}
        if(r.Stage==MouHgStage.RevealChildren)
        {
            if(r.RevealExhausted){ObtainMouHg(f);return;}
            if(r.Reveals.Count==0){FinishMouHg(f);return;}
            if(IsSlashCard(r.Reveals[^1].Kind))
            {
                if(MouHgTargets(f).Count==0){DiscardMouHgSlash(f);return;}
                ReplaceRuntimeTop(f=f with{MouHuangGai=r with{Stage=MouHgStage.Target}});PublishMouHg(f);return;
            }
            ReplaceRuntimeTop(f=f with{MouHuangGai=r with{Stage=MouHgStage.Choice}});PublishMouHg(f);return;
        }
        if(r.Stage is MouHgStage.NativeChildren or MouHgStage.DiscardSlashChildren){ObtainMouHg(f);return;}
        FinishMouHg(f);
    }
    private void FinishMouHg(ProgramSkillFrame f)
    {AdvanceEventRulesAndQueueFact(new MouHgCompletedEvent(f.Id,f.MouHuangGai!.Reveals.Count));ReplaceRuntimeTop(f=f with{MouHuangGai=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    [ProgramContinuation(ProgramContinuationKind.Resume,35400)]private bool ResumeMouHg(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{MouHuangGai:not null}f||f.Id!=id)return false;ContinueMouHg(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,35400)]private bool MoveMouHg(ProgramSkillFrame f)=>f.MouHuangGai is not null&&f.PendingMovementContinuation is not null&&ResumeMouHg(f.Id);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,35400)]private void AssertMouHg(ProgramSkillFrame f){if(f.MouHuangGai is not null&&!ValidMouHg(f))throw new InvalidOperationException("Quzhou lost its immutable actual source or entity interval.");}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,35400)][ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,35400)]private bool KeepMouHg(ProgramSkillFrame f)=>f.MouHuangGai is not null&&ValidMouHg(f);
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments,35400)]private bool MouHgMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)=>f.MouHuangGai is{Stage:MouHgStage.RevealChildren or MouHgStage.ObtainChildren or MouHgStage.NativeChildren or MouHgStage.DiscardSlashChildren}&&ValidMouHg(f)&&e?.Op==SkillProgramEffectOp.ResolveMouHuangGaiQuzhou&&p==f.PendingMovementContinuation&&p.BeforeCount==0&&p.CoverageResultBind is null;
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouHuangGaiQuzhou)]private PromptChoice MouHgAi(PendingDecision p,ProgramSkillFrame f)=>f.MouHuangGai?.Stage==MouHgStage.Target?
        p.Choices.OrderByDescending(c=>_aiBrains[f.OwnerSeat].ScoreProgramTarget(CreateSnapshot(f.OwnerSeat),c.Targets.Single(),new(0,0,1,0,0,0,false,false))).First():
        p.Choices.FirstOrDefault(c=>c.Parameters["branch"]=="again")??p.Choices.First();
}
