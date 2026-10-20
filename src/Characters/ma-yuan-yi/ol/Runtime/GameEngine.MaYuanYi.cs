using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string JibingStoreReason="skill-program.ma-yuan-yi.jibing.store",WangjingDrawReason="skill-program.ma-yuan-yi.wangjing.draw",BinghuoBind="ma-yuan-yi-binghuo";
    private bool HasMaYuanYiCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ReplaceJibingDrawWithTwoBing);
    private static CardConversionSource MaYuanYiSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    [ProgramCapability] private sealed class MaYuanYiCapability(GameEngine e):IMaYuanYiHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginMaYuanYi(f,op); }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,27370)]
    private void RecordMaYuanYiBingAction(IGameEvent fact)
    {
        if(!HasMaYuanYiCapability||fact is not CardActionAcceptedEvent{Action:var a}||!IsSlashCard(a.EffectiveKind)&&a.EffectiveKind!=CardKind.Dodge||a.PhysicalCards is not[var cost]||
            cost.From.Zone!=CardZoneKind.PublicPersistentPile||cost.From.OwnerSeat!=a.ProviderSeat||a.ConversionChain.SingleOrDefault(s=>s.SkillId=="ol:jibing"&&s.OwnerSeat==a.ProviderSeat) is not{}source||
            source.BindingId!=(IsSlashCard(a.EffectiveKind)?"bing-slash":"bing-dodge")||ProgramEventHistory<MaYuanYiBingActionEvent>().Any(e=>e.ActionId==a.ActionId))return;
        var stored=ProgramEventHistory<MaYuanYiStoredEvent>().LastOrDefault(e=>e.Source.SkillInstanceId==source.SkillInstanceId&&e.Source.OwnerSeat==a.ProviderSeat&&e.CardId==cost.CardId&&e.Pile==cost.From);
        if(stored is null||_cardMovements.Count(m=>m.Sequence==stored.Sequence&&m.CardId==cost.CardId&&m.From==CardLocation.DrawPile&&m.To==cost.From&&m.Reason.Value==JibingStoreReason)!=1)
            throw new InvalidOperationException("A Jibing action requires its original stored pile entity and source instance.");
        var payment=_cardMovements.LastOrDefault(m=>m.Sequence>stored.Sequence&&m.CardId==cost.CardId&&m.From==cost.From&&m.To==CardLocation.Processing)??throw new InvalidOperationException("A Jibing accepted action requires its actual paid original pile movement.");
        AdvanceEventRulesAndQueueFact(new MaYuanYiBingActionEvent(a.ActionId,a.ParentActionId,_turnNumber,_currentSeat,a.Type,a.ActorSeat,a.ProviderSeat,a.RequesterSeat,a.OpponentSeat,a.EffectiveKind,cost.CardId,cost.From,source,stored.FrameId,stored.Sequence,payment.Sequence));
    }
    private bool ValidBingAction(MaYuanYiBingActionEvent e)
    {
        var stored=ProgramEventHistory<MaYuanYiStoredEvent>().SingleOrDefault(s=>s.FrameId==e.StoredFrameId&&s.CardId==e.CardId&&s.Sequence==e.StoredSequence&&s.Source.OwnerSeat==e.ProviderSeat&&s.Source.SkillInstanceId==e.Source.SkillInstanceId&&s.Pile==e.From);
        var accepted=ProgramEventHistory<CardActionAcceptedEvent>().Select(x=>x.Action).FirstOrDefault(a=>a.ActionId==e.ActionId);
        if(stored is null||accepted is null||accepted.Type!=e.Type||accepted.ActorSeat!=e.ActorSeat||accepted.ProviderSeat!=e.ProviderSeat||accepted.RequesterSeat!=e.RequesterSeat||accepted.OpponentSeat!=e.OpponentSeat||
            accepted.EffectiveKind!=e.Kind||accepted.PhysicalCards is not[var cost]||cost.CardId!=e.CardId||cost.From!=e.From||!accepted.ConversionChain.Contains(e.Source)||e.Source.SkillId!="ol:jibing"||e.Source.OwnerSeat!=e.ProviderSeat)return false;
        return e.PaymentSequence>e.StoredSequence&&_cardMovements.Count(m=>m.Sequence==e.PaymentSequence&&m.CardId==e.CardId&&m.From==e.From&&m.To==CardLocation.Processing)==1;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27370)]
    private bool CanOfferMaYuanYi(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext context)
    {
        if(t.Effects is not[var e]||!MaYuanYiComposition.Handles(e.Op))return true;
        if(!_players[c.OwnerSeat].IsAlive||context.OwnerSeat!=c.OwnerSeat)return false;
        return e.Op switch{
            SkillProgramEffectOp.ReplaceJibingDrawWithTwoBing=>context.Window==SkillProgramTriggerWindow.DrawPhaseStarting&&c.OwnerSeat==_currentSeat&&JibingPileCards(_players[c.OwnerSeat]).Count<GetLivingFactionCount()&&
                _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId) is{NormalDrawReplaced:false,Continuation:ProgramLifecycleContinuation.CompleteDrawPhase},
            SkillProgramEffectOp.ResolveMoucuanAwakening=>context.Window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow&&c.OwnerSeat==_currentSeat&&_players[c.OwnerSeat].MaxHp>0&&JibingPileCards(_players[c.OwnerSeat]).Count>=MoucuanFactionCount()&&
                !ProgramEventHistory<MoucuanMaximumPaidEvent>().Any(x=>x.OwnerSeat==c.OwnerSeat),
            SkillProgramEffectOp.ResolveWangjingBingOpponentDraw=>WangjingQualification(c,context)is not null,
            _=>context.Window==SkillProgramTriggerWindow.TurnEnding&&ProgramEventHistory<MaYuanYiBingActionEvent>().Any(x=>x.ActualTurn==_turnNumber&&x.TurnOwner==_currentSeat&&x.ProviderSeat==c.OwnerSeat&&ValidBingAction(x))};
    }
    private SkillProgramStepOutcome BeginMaYuanYi(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);var c=f.WindowContext;
        if(f.MaYuanYi is not null||f.InstructionIndex!=1||f.TriggerId is null||c is null||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||_winner!=Winner.None||
            !CanOfferMaYuanYi(new(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,c.OccurrenceIndex),GetProgramTrigger(f),c))
            throw new InvalidOperationException("Ma Yuan Yi lost its actual native trigger, unpaid qualification or source.");
        var owner=_players[f.OwnerSeat];var wang=op==SkillProgramEffectOp.ResolveWangjingBingOpponentDraw?WangjingQualification(new(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,c.OccurrenceIndex),c):null;
        var stage=op switch{SkillProgramEffectOp.ReplaceJibingDrawWithTwoBing=>MaYuanYiStage.StoreChildren,SkillProgramEffectOp.ResolveWangjingBingOpponentDraw=>MaYuanYiStage.OpponentDrawChildren,SkillProgramEffectOp.ResolveMoucuanAwakening=>MaYuanYiStage.MaximumChildren,_=>MaYuanYiStage.TargetChoice};
        var r=new MaYuanYiReceipt(MaYuanYiSource(f),f.GameplayHash,op,c.ParentFrameId,_turnNumber,_currentSeat,op==SkillProgramEffectOp.ResolveMoucuanAwakening?MoucuanFactionCount():GetLivingFactionCount(),JibingPileCards(owner).Count,owner.MaxHp,owner.Hp,stage,wang?.Fact.ActionId,wang?.Opponent);
        ReplaceRuntimeTop(f=f with{MaYuanYi=r});
        AdvanceEventRulesAndQueueFact(new MaYuanYiStartedEvent(f.Id,r.Source,r.GameplayHash,op,r.ParentId,r.ActualTurn,r.TurnOwner,r.FactionCount,r.BingBefore,r.MaximumBefore,r.HpBefore,r.ActionId,r.OpponentSeat));
        if(stage==MaYuanYiStage.StoreChildren)
        {
            var pile=EnsurePublicPileSource(f,int.MaxValue);var before=_movementSequence;var count=0;
            ReplaceRuntimeTop(f=f with{MaYuanYi=r with{Pile=pile.Location,Before=before,After=before},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
            for(var i=0;i<2&&EnsureDrawPile();i++)
            {
                var card=_cardZones.CardsAt(CardLocation.DrawPile)[^1];var index=i;
                MoveCard(card,CardLocation.DrawPile,pile.Location,new(JibingStoreReason),m=>AdvanceEventRulesAndQueueFact(new MaYuanYiStoredEvent(f.Id,r.Source,index,card.Id,pile.Location,m.Sequence)));count++;
            }
            FinishMaYuanYiMovement(f.Id,before,count);
        }
        else if(stage==MaYuanYiStage.OpponentDrawChildren)
        {
            var opposite=wang!.Value.Opponent;var hp=_players[opposite].Hp;var highest=_players.Where(p=>p.IsAlive&&p.HasHpValue).Max(p=>p.Hp);var before=_movementSequence;
            ReplaceRuntimeTop(f with{MaYuanYi=r with{Before=before,After=before},PendingMovementContinuation=new(f.OwnerSeat,0,null)});
            var count=DrawCards(owner,1,true,new(WangjingDrawReason)).Count;FinishMaYuanYiMovement(f.Id,before,count);
            AdvanceEventRulesAndQueueFact(new WangjingDrawIssuedEvent(f.Id,r.ActionId!.Value,f.OwnerSeat,opposite,hp,highest,count));
        }
        else if(stage==MaYuanYiStage.MaximumChildren)
        {
            var after=r.MaximumBefore-1;var hp=Math.Min(r.HpBefore,after);
            if(after==0){owner.MaxHp=0;owner.Hp=hp;AdvanceEventRulesAndQueueFact(new MaximumHpChangedEvent(owner.Seat,-1,0,f.SkillId){HpBeforeClamp=r.HpBefore});}
            else ChangeProgramMaximumHp(f,-1);
            AdvanceEventRulesAndQueueFact(new MoucuanMaximumPaidEvent(f.Id,f.OwnerSeat,r.MaximumBefore,after,r.HpBefore,hp));
            if(after==0){BeginPlayerDeath(f.Id,owner,null,null,null,causingProgramSkillFrameId:f.Id);return SkillProgramStepOutcome.AwaitChild;}
        }
        ContinueMaYuanYi(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }
    private void FinishMaYuanYiMovement(long id,long before,int count)
    {
        var f=GetActiveProgramFrame(id);var r=f.MaYuanYi!;ReplaceRuntimeTop(f with{MaYuanYi=r with{Before=before,After=_movementSequence,ActualCount=count}});
        AdvanceEventRulesAndQueueFact(new MaYuanYiMovementEvent(id,r.Stage,before,_movementSequence,count));
    }
    private IReadOnlyList<PromptChoice> MaYuanYiTargets(ProgramSkillFrame f)=>Array.AsReadOnly(_players.Where(p=>p.IsAlive).Select(p=>new PromptChoice(new($"ma-yuan-yi.{f.Id}.target.{p.Seat}"),p.Name,[],[p.Seat],new Dictionary<string,string>{{"program-action","ma-yuan-yi"},{"frame-id",f.Id.ToString(CultureInfo.InvariantCulture)}})).ToArray());
    private void PublishMaYuanYiTarget(ProgramSkillFrame f)
    {
        if(!_players[f.OwnerSeat].IsAlive||_winner!=Winner.None){FinishMaYuanYi(f);return;}
        var choices=MaYuanYiTargets(f);var s=_contentRegistry.GetSkill(f.SkillId);ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});
        _pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"兵祸：选择一名角色判定。",[],choices.SelectMany(x=>x.Targets).ToArray(),f.OwnerSeat){PromptId=CreatePromptId(),Choices=choices,SkillPrompt=new(f.SkillId,s.Name,s.Name,s.Description)};
        _status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("ma-yuan-yi")]
    private void AnswerMaYuanYiTarget(PromptChoice supplied)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{MaYuanYi:{Stage:MaYuanYiStage.TargetChoice}r}f||_pendingDecision?.PlayerSeat!=f.OwnerSeat||!MaYuanYiTargets(f).Any(x=>AssistedChoicesEqual([x],[supplied])))
            throw new InvalidOperationException("Binghuo requires its published actual target choice.");
        AssertMaYuanYi(f);ClearPendingDecision();var target=supplied.Targets.Single();var judgment=_resolutionSequence+1;
        ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect,MaYuanYi=r with{Stage=MaYuanYiStage.Judgment,TargetSeat=target,JudgmentId=judgment}});
        AdvanceEventRulesAndQueueFact(new BinghuoTargetIssuedEvent(f.Id,target,judgment));
        _=BeginJudgment(null,target,"binghuo",f.Id,null,JudgmentContinuationKind.ProgramSkill,sourceSeat:f.OwnerSeat,programResultBind:BinghuoBind,programResultVisibility:SkillProgramCardSetVisibility.Public);
    }
    private bool DrainMaYuanYi(ProgramSkillFrame f)=>HasPaidMoucuanSourceLoss(f)&&TryBeginAdvancedSkillsChanged(f.Id)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,f.PendingMovementContinuation is null?PostEventContinuation.Program:PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueMaYuanYi(long id)
    {
        while(_resolutionStack.LastOrDefault()is ProgramSkillFrame{MaYuanYi:{}r}f&&f.Id==id)
        {
            AssertMaYuanYi(f);
            if(r.Stage==MaYuanYiStage.TargetChoice){if(_pendingDecision is null)PublishMaYuanYiTarget(f);return;}
            if(r.Stage==MaYuanYiStage.Judgment||f.AttackAttempt is not null)return;
            if(DrainMaYuanYi(f))return;f=GetActiveProgramFrame(id);r=f.MaYuanYi!;
            if(f.PendingMovementContinuation is not null)ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null});
            if(r.Stage==MaYuanYiStage.MaximumChildren&&_players[f.OwnerSeat].IsAlive&&_winner==Winner.None)
            {
                ReplaceRuntimeTop(f=f with{MaYuanYi=r with{Stage=MaYuanYiStage.GrantChildren}});GrantProgramSkills(f,["ol:binghuo"]);AdvanceEventRulesAndQueueFact(new MoucuanSkillGrantedEvent(f.Id,f.OwnerSeat));continue;
            }
            if(r.Stage==MaYuanYiStage.JudgmentChildren&&r.JudgmentSuit is Suit.Spade or Suit.Club&&r.TargetSeat is{}target&&_players[target].IsAlive&&_winner==Winner.None)
            {
                ReplaceRuntimeTop(f=f with{MaYuanYi=r with{Stage=MaYuanYiStage.DamageChildren}});
                AdvanceEventRulesAndQueueFact(new BinghuoDamageIssuedEvent(f.Id,f.OwnerSeat,target,1,DamageNature.Thunder));BeginProgramSkillDamage(f,target,1,nature:DamageNature.Thunder);return;
            }
            FinishMaYuanYi(f);return;
        }
    }
    private void FinishMaYuanYi(ProgramSkillFrame f)
    {AdvanceEventRulesAndQueueFact(new MaYuanYiCompletedEvent(f.Id));ReplaceRuntimeTop(f=f with{MaYuanYi=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    [ProgramContinuation(ProgramContinuationKind.Resume,27370)] private bool ResumeMaYuanYi(long id)
    {if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{MaYuanYi:not null}f||f.Id!=id)return false;ContinueMaYuanYi(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,27370)] private bool ReturnMaYuanYiMovement(ProgramSkillFrame f)
    {if(f.MaYuanYi is null)return false;ContinueMaYuanYi(f.Id);return true;}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27370)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27370)]
    private bool ContinuePaidMaYuanYi(ProgramSkillFrame f){if(f.MaYuanYi is null)return false;AssertMaYuanYi(f);return true;}
    [ProgramAiChoice(SkillProgramEffectOp.ResolveBinghuoEndingJudgment)]
    private PromptChoice ChooseMaYuanYi(PendingDecision p,ProgramSkillFrame f)=>p.Choices.OrderByDescending(c=>c.Targets is[var t]?_aiBrains[f.OwnerSeat].ScoreProgramTarget(CreateSnapshot(f.OwnerSeat),t,new SkillProgramAiHint(1,0,0,0,0,0,false,false)):0).ThenBy(c=>c.Id.Value,StringComparer.Ordinal).First();
}
