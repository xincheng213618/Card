namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramCapability]private sealed class MaChengCapability(GameEngine e):IMaChengHost
    {public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginChenglie(f,op);}
    private SkillProgramStepOutcome BeginChenglie(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);var outcome=op==SkillProgramEffectOp.ResolveChenglieRedHolderOutcome;
        if(f.Chenglie is not null||f.InstructionIndex!=1||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||f.WindowContext is not{}context)throw new InvalidOperationException("Chenglie requires its exact original native single instruction.");
        var candidate=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,0);
        CardUseFrame use;
        if(outcome)use=ChenglieOutcomeUse(candidate,context)??throw new InvalidOperationException("Chenglie cannot borrow an unissued completed-use entitlement.");
        else if(!ChenglieInitialEligible(candidate,context)||!TryGetChenglieUse(context,out use,out _))throw new InvalidOperationException("Chenglie requires at least one genuinely additional legal Slash target.");
        var r=new ChenglieReceipt(ChenglieSource(f),f.GameplayHash,use.Id,use.Action!.ActionId,context.ParentFrameId,outcome?use.ChenglieLease!.SetupFrameId:f.Id,outcome,outcome?ChenglieStage.Outcomes:ChenglieStage.Targets){Original=outcome?use.ChenglieLease!.Original:use.TargetSeats,Added=outcome?use.ChenglieLease!.Added:[],Paid=outcome};
        ReplaceRuntimeTop(f=f with{Chenglie=r});AdvanceEventRulesAndQueueFact(new ChenglieStartedEvent(f.Id,r.Source,r.GameplayHash,r.UseId,r.ActionId,r.WindowId,r.SetupFrameId,outcome,r.Original.Count));
        if(!outcome){for(var i=0;i<r.Original.Count;i++)AdvanceEventRulesAndQueueFact(new ChenglieTargetSlotEvent(f.Id,false,i,r.Original[i]));PublishChenglie(f);}else ContinueChenglie(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private PromptChoice ChenglieChoice(ProgramSkillFrame f,string branch,string label,IReadOnlyList<int>? cards=null,IReadOnlyList<int>? targets=null)=>new(new($"chenglie.{f.Id}.{branch}.{string.Join("-",cards??[])}.{string.Join("-",targets??[])}"),label,cards??[],targets??[],new Dictionary<string,string>{{"program-action","chenglie"},{"branch",branch}});
    private IReadOnlyList<PromptChoice> ChenglieChoices(ProgramSkillFrame f)
    {
        var r=f.Chenglie!;var use=RequireChenglieParent(f);var result=new List<PromptChoice>();
        if(r.Stage==ChenglieStage.Targets)
        {
            if(r.Added.Count<2)foreach(var seat in ChenglieLegalTargets(use,r.Added))result.Add(ChenglieChoice(f,"target",$"额外指定{_players[seat].Name}",targets:[seat]));
            result.Add(ChenglieChoice(f,r.Added.Count==0?"cancel":"targets-done",r.Added.Count==0?"取消发动":"完成额外目标"));
        }
        else if(r.Stage==ChenglieStage.Exchange)
        {
            foreach(var hand in GetHand(_players[f.OwnerSeat]))foreach(var id in r.Pool.Where(id=>_cardZones.GetLocation(id)==CardLocation.Processing))result.Add(ChenglieChoice(f,"exchange",$"以【{hand.DisplayName}】秘密交换【{GetAdvancedCard(id).DisplayName}】",[hand.Id,id]));
        }
        else if(r.Stage==ChenglieStage.Assignment)
        {
            var targets=r.Original.Concat(r.Added).ToArray();if(r.Index<targets.Length)foreach(var id in r.Pool.Where(id=>_cardZones.GetLocation(id)==CardLocation.Processing))result.Add(ChenglieChoice(f,"assign",$"将【{GetAdvancedCard(id).DisplayName}】暗置于{_players[targets[r.Index]].Name}",[id],[targets[r.Index]]));
        }
        else if(r.Stage==ChenglieStage.Gift&&use.ChenglieLease is{}lease&&r.Index<lease.Cards.Count&&_players[f.OwnerSeat].IsAlive)
        {
            var holder=lease.Cards[r.Index].HolderSeat;
            if(_players[holder].IsAlive)foreach(var old in BuildOwnedCardPaymentChoices(f.Id,holder,holder,[CardZoneKind.Hand,CardZoneKind.Equipment],OwnedCardMoveIntent.Transfer,canSelect:(_,card)=>!card.IsGeneralWeapon,destination:SkillProgramCardDestination.SelectedTargetHand,destinationSeat:f.OwnerSeat))result.Add(old with{Id=new($"chenglie.{f.Id}.give.{old.Id.Value}"),Parameters=new System.Collections.ObjectModel.ReadOnlyDictionary<string,string>(new Dictionary<string,string>(old.Parameters){["program-action"]="chenglie",["branch"]="give"})});
        }
        return Array.AsReadOnly(result.Select(PromptChoiceProjection.Freeze).ToArray());
    }
    private void PublishChenglie(ProgramSkillFrame f)
    {
        var r=f.Chenglie!;var choices=ChenglieChoices(f);if(choices.Count==0){ContinueChenglie(f.Id);return;}
        var holder=r.Stage==ChenglieStage.Gift?RequireChenglieParent(f).ChenglieLease!.Cards[r.Index].HolderSeat:f.OwnerSeat;var skill=_contentRegistry.GetSkill(f.SkillId);ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});
        _pendingDecision=new(DecisionKind.ProgramTrigger,holder,r.Stage switch{ChenglieStage.Targets=>"骋烈：额外指定一至两名合法目标。",ChenglieStage.Exchange=>"骋烈：秘密交换一张手牌与一张展示牌。",ChenglieStage.Assignment=>"骋烈：秘密分配原牌。",_=>"骋烈：交给来源一张手牌或装备牌。"},choices.SelectMany(c=>c.Cards).Distinct().ToArray(),choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=r.Stage!=ChenglieStage.Targets,Choices=choices,TargetSeat=holder,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};
        _status=_players[holder].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("chenglie")]private void AnswerChenglie(PromptChoice choice)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Chenglie:{}r}f||!ChenglieChoices(f).Any(c=>AssistedChoicesEqual([c],[choice])))throw new InvalidOperationException("Chenglie requires its original chooser, exact current secret entity or native target.");
        var expected=r.Stage==ChenglieStage.Gift?RequireChenglieParent(f).ChenglieLease!.Cards[r.Index].HolderSeat:f.OwnerSeat;if(_pendingDecision?.PlayerSeat!=expected)throw new InvalidOperationException("Only Chenglie's genuine current participant may answer.");AssertMaCheng(f);ClearPendingDecision();ReplaceRuntimeTop(f=f with{Step=ResolutionFrameStep.ResolvingEffect});var branch=choice.Parameters["branch"];
        if(branch=="cancel"){FinishChenglie(f);return;}
        if(branch=="target")
        {var seat=choice.Targets.Single();AdvanceEventRulesAndQueueFact(new ChenglieTargetSlotEvent(f.Id,true,r.Added.Count,seat));ReplaceRuntimeTop(f=f with{Chenglie=r with{Added=r.Added.Append(seat).ToArray()}});if(f.Chenglie!.Added.Count==2||ChenglieLegalTargets(RequireChenglieParent(f),f.Chenglie.Added).Count==0)ApplyChenglieTargets(f);else PublishChenglie(f);return;}
        if(branch=="targets-done"){ApplyChenglieTargets(f);return;}
        if(branch=="exchange")
        {
            var hand=choice.Cards[0];var top=choice.Cards[1];AdvanceEventRulesAndQueueFact(new ChenglieSwapSelectedEvent(f.Id,hand,top));ReplaceRuntimeTop(f=f with{Chenglie=r with{SwapHand=hand,SwapTop=top}});StartChenglieMovement(ref f,ChenglieStage.ExchangeChildren,f.OwnerSeat);
            MoveCard(GetAdvancedCard(hand),CardLocation.Hand(f.OwnerSeat),CardLocation.Processing,new(ChenglieSwapOutReason),m=>RecordChenglieMove(f.Id,m));MoveCard(GetAdvancedCard(top),CardLocation.Processing,CardLocation.Hand(f.OwnerSeat),new(ChenglieSwapInReason),m=>RecordChenglieMove(f.Id,m));
            var current=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(current with{Chenglie=current.Chenglie! with{Pool=r.Pool.Select(id=>id==top?hand:id).ToArray()}});FinishChenglieMovement(f.Id);return;
        }
        if(branch=="assign"){PlaceChenglieAttachment(f,choice.Cards.Single());return;}
        if(branch=="give")
        {
            if(!Enum.TryParse<CardZoneKind>(choice.Parameters.GetValueOrDefault("source-zone"),out var zone)||!int.TryParse(choice.Parameters.GetValueOrDefault("slot-index"),out var slot))throw new InvalidOperationException("Chenglie gift lost its original owned slot.");
            var from=new CardLocation(zone,expected);var card=_cardZones.CardsAt(from)[slot];ReplaceRuntimeTop(f=f with{Chenglie=r with{GiftId=card.Id,GiftFrom=from}});StartChenglieMovement(ref f,ChenglieStage.GiftChildren,expected);MoveCard(card, from, CardLocation.Hand(f.OwnerSeat), new(ChenglieGiftReason), NativeGiveCompletion(f.Id,expected,f.OwnerSeat,[card.Id],_movementSequence,m=>RecordChenglieMove(f.Id,m)));FinishChenglieMovement(f.Id);return;
        }
        throw new InvalidOperationException("Unknown Chenglie choice.");
    }
    private void ApplyChenglieTargets(ProgramSkillFrame f)
    {
        var r=f.Chenglie!;var use=RequireChenglieParent(f);if(r.Added.Count is<1 or>2||r.Added.Any(s=>!ChenglieLegalTargets(use,[]).Contains(s)))throw new InvalidOperationException("Chenglie requires one or two still-legal additional targets.");
        var targets=r.Original.Concat(r.Added).ToArray();var a=CloneDesignatedExtraTargetAction(use.Action!,targets);var lease=new ChenglieLease(f.Id,use.Id,a.ActionId,r.Source,r.GameplayHash,a.ActorSeat,a.ProviderSeat){Original=r.Original,Added=r.Added};
        ReplaceRuntimeTop(f=f with{Chenglie=r with{Paid=true}});AdvanceEventRulesAndQueueFact(new ChenglieTargetsAppliedEvent(f.Id,use.Id,a.ActionId,r.Source,r.GameplayHash,a.ActorSeat,a.ProviderSeat,r.Original.Count,r.Added.Count));
        var rebase=RebaseChenglieSlashTargets(use,targets);UpdateProgramRoleCardUse(rebase with{TargetSeats=targets,Action=a,ChenglieLease=lease,TargetsAdjusted=true,Enhancements=use.Enhancements|CurrentCardEnhancement.ExtraTarget},a);RefreshCurrentUseAddedTargetCandidates(LifecycleCardUse(use.Id)!,r.Added);
        StartChenglieMovement(ref f,ChenglieStage.RevealChildren,f.OwnerSeat);var cards=new List<Card>();
        for(var i=0;i<targets.Length;i++)
        {
            if(DrawOneToProcessing(new(ChenglieRevealReason))is not{}card)break;cards.Add(card);var m=_cardMovements.Last(x=>x.CardId==card.Id&&x.To==CardLocation.Processing&&x.Reason.Value==ChenglieRevealReason);RecordChenglieMove(f.Id,m);AdvanceEventRulesAndQueueFact(new ChenglieRevealedCardEvent(f.Id,i,card.Id,card.Kind,card.Suit,card.Rank));
        }
        var active=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(active with{Chenglie=active.Chenglie! with{Revealed=cards.Select(c=>c.Id).ToArray(),Pool=cards.Select(c=>c.Id).ToArray()}});if(cards.Count>0)AdvanceEventRulesAndQueueFact(new ProgramCardsRevealedEvent(f.Id,f.SkillId,GetProgramBindingId(f),f.OwnerSeat,"chenglie-top",Array.AsReadOnly(cards.Select(ToSnapshot).ToArray())));FinishChenglieMovement(f.Id);
    }
    private CardUseFrame RebaseChenglieSlashTargets(CardUseFrame use,IReadOnlyList<int> targets)
    {
        if(use.CardAttack is not{}original)return use;
        CardAttackState Fresh(int seat)=>new(){Active=original.Active,SourceSeat=use.SourceSeat,CardUserSeat=use.SourceSeat,TargetSeat=seat,DamageAmount=use.ProgramAdjustedSlashBaseDamage??use.Continuations.FangtianHalberd?.DamageAmount??original.DamageAmount,IgnoresArmor=use.IgnoresArmor||HasCardArmorBypass(_players[use.SourceSeat],_players[seat],use.CardKind),EffectiveCardKind=use.CardKind,IsDelayedJudgmentDamage=original.IsDelayedJudgmentDamage,IsSourceLess=original.IsSourceLess,ProgramJudgmentFrameId=original.ProgramJudgmentFrameId,ProgramSkillFrameId=original.ProgramSkillFrameId,ProgramSkillCardUseFrameId=original.ProgramSkillCardUseFrameId,DelayedJudgmentSeat=original.DelayedJudgmentSeat,DamageNatureOverride=original.DamageNatureOverride,ConversionSource=original.ConversionSource,CardId=original.CardId,AppearanceKind=original.AppearanceKind,AppearanceSuit=original.AppearanceSuit,AppearanceRank=original.AppearanceRank,PhysicalCardIds=Array.AsReadOnly(original.PhysicalCardIds.ToArray()),AdjustedSlashReturn=original.AdjustedSlashReturn};
        IReadOnlyList<CardAttackState>? prepared=use.PreparedTargetAttacks is{}old?Array.AsReadOnly(targets.Select(seat=>old.FirstOrDefault(x=>x.TargetSeat==seat)??Fresh(seat)).ToArray()):null;
        return use with{TargetIndex=0,PreparedTargetAttacks=prepared,CardAttack=prepared is{Count:>0}?prepared[0]:original,Continuations=use.Continuations with{FangtianHalberd=use.Continuations.FangtianHalberd is{Active:true}multi?multi with{TargetSeats=Array.AsReadOnly(targets.ToArray()),CurrentAttackOwnerId=use.Id}:use.Continuations.FangtianHalberd}};
    }
    private void StartChenglieMovement(ref ProgramSkillFrame f,ChenglieStage stage,int participant)
    {
        foreach(var p in _players)_observedSkillGrantRevisions.TryAdd(p.Seat,p.SkillGrants.Revision);var r=f.Chenglie!;ReplaceRuntimeTop(f=f with{Chenglie=r with{Stage=stage,Paid=true,Before=_movementSequence,After=_movementSequence,Count=0},PendingMovementContinuation=new(participant,0,null)});
    }
    private void RecordChenglieMove(long id,CardMovementRecord m)
    {
        var f=GetActiveProgramFrame(id);var r=f.Chenglie!;AdvanceEventRulesAndQueueFact(new ChenglieCardMovedEvent(id,r.Stage,r.Index,m.CardId,m.From,m.To,m.Reason,m.Sequence));
    }
    private void FinishChenglieMovement(long id)
    {
        var f=GetActiveProgramFrame(id);var r=f.Chenglie!;var count=ProgramEventHistory<ChenglieCardMovedEvent>().Count(e=>e.FrameId==id&&e.Stage==r.Stage&&e.Index==r.Index&&e.Sequence>r.Before&&e.Sequence<=_movementSequence);ReplaceRuntimeTop(f=f with{Chenglie=r with{After=_movementSequence,Count=count}});AdvanceEventRulesAndQueueFact(new ChenglieMovementPaidEvent(id,r.Stage,r.Index,r.Before,_movementSequence,count));ContinueChenglie(id);
    }
    private void PlaceChenglieAttachment(ProgramSkillFrame f,int cardId)
    {
        var r=f.Chenglie!;var use=RequireChenglieParent(f);var lease=use.ChenglieLease!;var target=r.Original.Concat(r.Added).ElementAt(r.Index);var card=GetAdvancedCard(cardId);var location=new CardLocation(CardZoneKind.PrivatePersistentPile,target,$"chenglie:{use.Id}:{r.SetupFrameId}:{r.Index}");_cardZones.EnsurePrivatePersistentPile(location);
        StartChenglieMovement(ref f,ChenglieStage.AssignmentChildren,target);MoveCard(card,CardLocation.Processing,location,new(ChenglieAssignReason),m=>
        {RecordChenglieMove(f.Id,m);var attachment=new ChenglieAttachment(r.Index,target,card.Id,card.Kind,card.Suit,IsRedSuit(card.Suit),location,m.Sequence);AdvanceEventRulesAndQueueFact(new ChenglieAttachmentPlacedEvent(r.SetupFrameId,use.Id,r.ActionId,r.Index,target,card.Id,card.Kind,card.Suit,attachment.Red,location,m.Sequence));UpdateLifecycleCardUse(use.Id,u=>u with{ChenglieLease=u.ChenglieLease! with{Cards=u.ChenglieLease.Cards.Append(attachment).ToArray()}});});
        if(!_players[target].IsAlive)MoveCard(card,location,CardLocation.DiscardPile,new(ChenglieCleanupReason),m=>RecordChenglieMove(f.Id,m));var now=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(now with{Chenglie=now.Chenglie! with{Pool=r.Pool.Where(id=>id!=cardId).ToArray()}});FinishChenglieMovement(f.Id);
    }
    private bool DrainChenglie(ProgramSkillFrame f)=>TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginAdvancedSkillsChanged(f.Id)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id);
    private void ContinueChenglie(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Chenglie:{}r}f||f.Id!=id)return;AssertMaCheng(f);
        if(f.PendingMovementContinuation is not null){if(DrainChenglie(f))return;f=GetActiveProgramFrame(id);r=f.Chenglie!;ReplaceRuntimeTop(f=f with{PendingMovementContinuation=null});}
        if(r.Stage==ChenglieStage.CleanupChildren){SettleChenglieLease(f);FinishChenglie(GetActiveProgramFrame(id));return;}
        if(!r.Outcome&&!_players[f.OwnerSeat].IsAlive&&r.Paid){BeginChenglieCleanup(f);return;}
        if(r.Stage==ChenglieStage.Targets){if(_pendingDecision is null)PublishChenglie(f);return;}
        if(r.Stage==ChenglieStage.RevealChildren)
        {ReplaceRuntimeTop(f=f with{Chenglie=r with{Stage=GetHand(_players[f.OwnerSeat]).Count>0&&r.Pool.Count>0?ChenglieStage.Exchange:ChenglieStage.Assignment,Index=0}});ContinueChenglie(id);return;}
        if(r.Stage==ChenglieStage.Exchange)
        {if(ChenglieChoices(f).Count>0){if(_pendingDecision is null)PublishChenglie(f);return;}ReplaceRuntimeTop(f=f with{Chenglie=r with{Stage=ChenglieStage.Assignment}});ContinueChenglie(id);return;}
        if(r.Stage==ChenglieStage.ExchangeChildren){ReplaceRuntimeTop(f=f with{Chenglie=r with{Stage=ChenglieStage.Assignment,Index=0}});ContinueChenglie(id);return;}
        if(r.Stage==ChenglieStage.AssignmentChildren){ReplaceRuntimeTop(f=f with{Chenglie=r with{Stage=ChenglieStage.Assignment,Index=r.Index+1}});ContinueChenglie(id);return;}
        if(r.Stage==ChenglieStage.Assignment)
        {
            if(r.Pool.Count==0||r.Index>=r.Original.Count+r.Added.Count){UpdateLifecycleCardUse(r.UseId,u=>u with{ChenglieLease=u.ChenglieLease! with{Ready=true}});var lease=LifecycleCardUse(r.UseId)!.ChenglieLease!;AdvanceEventRulesAndQueueFact(new ChenglieSetupReadyEvent(r.SetupFrameId,r.UseId,r.ActionId,lease.Cards.Count));FinishChenglie(f);return;}
            if(!_players[r.Original.Concat(r.Added).ElementAt(r.Index)].IsAlive){PlaceChenglieAttachment(f,r.Pool[0]);return;}if(_pendingDecision is null)PublishChenglie(f);return;
        }
        if(r.Stage is ChenglieStage.GiftChildren or ChenglieStage.RecoveryChildren){CompleteChenglieOutcome(f);return;}
        if(r.Stage==ChenglieStage.Gift){if(ChenglieChoices(f).Count>0){if(_pendingDecision is null)PublishChenglie(f);return;}CompleteChenglieOutcome(f);return;}
        if(r.Stage==ChenglieStage.Outcomes)
        {
            var lease=RequireChenglieParent(f).ChenglieLease!;if(r.Index>=lease.Cards.Count||!_players[f.OwnerSeat].IsAlive){BeginChenglieCleanup(f);return;}var card=lease.Cards[r.Index];var responded=ChenglieResponded(lease,card.HolderSeat);AdvanceEventRulesAndQueueFact(new ChenglieOutcomeIssuedEvent(f.Id,r.SetupFrameId,r.Index,card.HolderSeat,card.Red,responded));
            if(!card.Red||!_players[card.HolderSeat].IsAlive){CompleteChenglieOutcome(f);return;}
            if(responded){ReplaceRuntimeTop(f=f with{Chenglie=r with{Stage=ChenglieStage.Gift}});ContinueChenglie(id);return;}
            StartChenglieMovement(ref f,ChenglieStage.RecoveryChildren,card.HolderSeat);new ProgramSkillHost(this).Recover(f.Id,f.OwnerSeat,card.HolderSeat,1,null,null);FinishChenglieMovement(id);return;
        }
        throw new InvalidOperationException("Chenglie lost its typed continuation stage.");
    }
    private void CompleteChenglieOutcome(ProgramSkillFrame f)
    {
        var r=f.Chenglie!;AdvanceEventRulesAndQueueFact(new ChenglieOutcomeCompletedEvent(f.Id,r.SetupFrameId,r.Index));ReplaceRuntimeTop(f=f with{Chenglie=r with{Stage=ChenglieStage.Outcomes,Index=r.Index+1,GiftId=null,GiftFrom=null}});ContinueChenglie(f.Id);
    }
    private void BeginChenglieCleanup(ProgramSkillFrame f)
    {
        var r=f.Chenglie!;var use=RequireChenglieParent(f);StartChenglieMovement(ref f,ChenglieStage.CleanupChildren,f.OwnerSeat);
        foreach(var card in use.ChenglieLease!.Cards)if(_cardZones.GetLocation(card.CardId)==card.Location)MoveCard(GetAdvancedCard(card.CardId),card.Location,CardLocation.DiscardPile,new(ChenglieCleanupReason),m=>RecordChenglieMove(f.Id,m));
        foreach(var id in r.Pool)if(_cardZones.GetLocation(id)==CardLocation.Processing)MoveCard(GetAdvancedCard(id),CardLocation.Processing,CardLocation.DiscardPile,new(ChenglieCleanupReason),m=>RecordChenglieMove(f.Id,m));FinishChenglieMovement(f.Id);
    }
    private void SettleChenglieLease(ProgramSkillFrame f)
    {
        var r=f.Chenglie!;var use=RequireChenglieParent(f);if(use.ChenglieLease!.Cards.Any(c=>_cardZones.GetLocation(c.CardId)==c.Location)||r.Pool.Any(id=>_cardZones.GetLocation(id)==CardLocation.Processing))throw new InvalidOperationException("Chenglie cannot complete while exact temporary entities remain.");UpdateLifecycleCardUse(use.Id,u=>u with{ChenglieLease=u.ChenglieLease! with{Ready=true,Settled=true}});AdvanceEventRulesAndQueueFact(new ChenglieLeaseSettledEvent(f.Id,r.SetupFrameId,r.UseId));
    }
    private void FinishChenglie(ProgramSkillFrame f){var r=f.Chenglie!;AdvanceEventRulesAndQueueFact(new ChenglieCompletedEvent(f.Id,r.SetupFrameId,r.Outcome));ReplaceRuntimeTop(f=f with{Chenglie=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    [ProgramContinuation(ProgramContinuationKind.Resume,27560)]private bool ResumeChenglie(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{Chenglie:not null}f||f.Id!=id)return false;ContinueChenglie(id);return true;}
    [ProgramContinuation(ProgramContinuationKind.Movement,27560)]private bool ReturnChenglieMovement(ProgramSkillFrame f){if(f.Chenglie is null)return false;ContinueChenglie(f.Id);return true;}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27560)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27560)]private bool ContinuePaidChenglie(ProgramSkillFrame f)=>ShouldAllowChenglie(f);
    private bool ShouldAllowChenglie(ProgramSkillFrame f)
    {
        if(f.Chenglie is{Paid:true}){AssertMaCheng(f);return true;}if(f.Chenglie is not null||f.TriggerId!="red-holder-outcomes"||f.WindowContext is not{}c||f.InstructionIndex is not(0 or 1))return false;return IsIssuedChenglieCandidate(new(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,0),c);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveChenglieExtraTargetsAndHiddenCards,SkillProgramEffectOp.ResolveChenglieRedHolderOutcome)]private PromptChoice ChooseChenglie(PendingDecision p,ProgramSkillFrame f)
    {
        var r=f.Chenglie!;if(r.Stage==ChenglieStage.Targets)return p.Choices.FirstOrDefault(c=>c.Parameters.GetValueOrDefault("branch")=="target")??p.Choices[0];
        if(r.Stage==ChenglieStage.Gift)return p.Choices.OrderBy(c=>c.Cards.Count==0?0:GetKeepValue(GetAdvancedCard(c.Cards[0]),_players[p.PlayerSeat])).First();return p.Choices[0];
    }
}
