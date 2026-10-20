namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedInstantTrickRequirement, 60)]
    private bool QirangTargetFinalizedInstantTrickRequirementTargetWindow(ProgramTargetCardWindowQuery query) =>
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.AddTargetForQirangPhysicalTrick);


    private const string QirangGainReason="skill-program.qirang.deck-trick";
    private static CardConversionSource ZhugeGuoSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    private bool TryQirangEquipmentParent(int owner,ProgramSkillWindowContext context,out CardUseFrame use)
    {
        use=null!;
        if(context.Window!=SkillProgramTriggerWindow.CardUseTargetsFinalized || context.CardUse is not{}identity ||
            LifecycleCardUse(identity.ParentCardUseFrameId)is not{Action:{Type:CardActionType.Use} action} actual || action.ActionId!=identity.CardActionId || action.ActorSeat!=owner || actual.SourceSeat!=owner || !EquipmentCatalog.IsEquipment(action.EffectiveKind) || actual.CardKind!=action.EffectiveKind || action.PhysicalCards is not[var physical] || physical.CardId!=actual.CardId || _cardZones.GetLocation(physical.CardId)!=CardLocation.Processing || actual.QirangEquipmentOrigin is not{}origin || origin.CardUseFrameId!=actual.Id || !SameOverflowAction(origin.OriginalAction,action) || CompleteProgramEventHistory().OfType<QirangEquipmentOriginCapturedEvent>().Count(e=>e==new QirangEquipmentOriginCapturedEvent(actual.Id,action.ActionId,owner,physical.CardId,origin.MovementBefore))!=1 || _cardMovements.Count(m=>m.CardId==physical.CardId && m.From==physical.From && m.To==CardLocation.Processing && m.Sequence>origin.MovementBefore && m.Reason==CardMoveReasons.EquipmentUse)!=1 ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault(w=>w.Id==context.ParentFrameId)is not{}window || window.ParentFrameId!=actual.Id || window.Action.ActionId!=action.ActionId)return false;
        use=actual;return true;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 370)]
    private bool CanOfferZhugeGuo(ProgramTriggerCandidate candidate,SkillProgramTrigger trigger,ProgramSkillWindowContext context)
    {
        if(ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.ObtainDeckTrickForActualEquipmentUse))return TryQirangEquipmentParent(candidate.OwnerSeat,context,out _);
        if(ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.AddTargetForQirangPhysicalTrick))return TryQirangExtraParent(candidate.OwnerSeat,candidate.SkillId,candidate.SkillInstanceId,context,out _);
        return true;
    }
    private SkillProgramStepOutcome BeginZhugeGuo(ProgramSkillFrame supplied,SkillProgramEffect e)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(f.ZhugeGuo is not null || f.InstructionIndex!=1 || f.WindowContext is not{}context || !HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId))throw new InvalidOperationException("ZhugeGuo requires its clean actual source window.");
        var r=new ZhugeGuoReceipt(ZhugeGuoSource(f),f.GameplayHash,f.InstructionIndex,_turnNumber,context.ParentFrameId,ZhugeGuoStage.GainChildren);
        if(e.Op==SkillProgramEffectOp.InspectYuhuaTop)
        {
            if(f.OwnerSeat!=_currentSeat || context.Window is not(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or SkillProgramTriggerWindow.TurnEnding))throw new InvalidOperationException("Yuhua requires actual own prepare or ending.");
            var count=_players[f.OwnerSeat].YuhuaActiveQuota is{ActualTurnNumber:var turn}quota && turn==_turnNumber?quota.Count:1;
            _=EnsureDrawPile();var viewed=_cardZones.CardsAt(CardLocation.DrawPile).TakeLast(Math.Min(count,_cardZones.Count(CardLocation.DrawPile))).Reverse().Select(c=>c.Id).ToArray();
            r=r with{Stage=ZhugeGuoStage.YuhuaInspect,RequestedPeek=count,Viewed=viewed};ReplaceRuntimeTop(f=f with{ZhugeGuo=r});AdvanceEventRulesAndQueueFact(new YuhuaPrivatePeekStartedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.WindowId,count,viewed.Length));
            if(viewed.Length==0){FinishZhugeGuo(f);return SkillProgramStepOutcome.AwaitChild;}
            var result=BeginProgramTopReorder(f,count,null);f=GetActiveProgramFrame(f.Id);var order=f.TopReorder!;ReplaceRuntimeTop(f with{TopReorder=order with{ViewedCardIds=Array.AsReadOnly(order.ViewedCardIds.ToArray()),TopCardIds=Array.AsReadOnly(order.TopCardIds.ToArray()),BottomCardIds=Array.AsReadOnly(order.BottomCardIds.ToArray())}});return result;
        }
        if(e.Op==SkillProgramEffectOp.AddTargetForQirangPhysicalTrick)
        {
            if(!TryQirangExtraParent(f.OwnerSeat,f.SkillId,f.SkillInstanceId,context,out var use))throw new InvalidOperationException("Qirang extra target lacks its exact acquired physical Use.");
            r=r with{Stage=ZhugeGuoStage.ExtraTargetChoice,UseId=use.Id,ActionId=use.Action!.ActionId,OriginalAction=use.Action,Qualification=use.QirangQualification};ReplaceRuntimeTop(f=f with{ZhugeGuo=r});PublishQirangExtraChoice(f);return SkillProgramStepOutcome.AwaitChoice;
        }
        if(e.Op!=SkillProgramEffectOp.ObtainDeckTrickForActualEquipmentUse || !TryQirangEquipmentParent(f.OwnerSeat,context,out var equipment))throw new InvalidOperationException("Qirang gain requires actual equipment Use payment.");
        var trick=_cardZones.CardsAt(CardLocation.DrawPile).LastOrDefault(c=>GetProgramCardCategory(c.Kind)==SkillProgramCardCategory.Trick);
        if(trick is null){FinishProgramSkill(f,true);return SkillProgramStepOutcome.AwaitChild;}
        r=r with{UseId=equipment.Id,ActionId=equipment.Action!.ActionId,OriginalAction=equipment.Action,GainedCardId=trick.Id,GainedKind=trick.Kind,Before=_movementSequence,After=_movementSequence};ReplaceRuntimeTop(f with{ZhugeGuo=r,PendingMovementContinuation=new(f.OwnerSeat,0,null)});
        MoveProgramCardsFromMultipleSources([trick.Id],CardLocation.Hand(f.OwnerSeat),new(QirangGainReason),(_,records)=>
        {
            var active=GetActiveProgramFrame(f.Id);var movement=records.Single();var paid=r with{After=_movementSequence};ReplaceRuntimeTop(active with{ZhugeGuo=paid});
            AdvanceEventRulesAndQueueFact(new QirangGainPaidEvent(f.Id,r.Source,r.GameplayHash,equipment.Id,equipment.Action.ActionId,trick.Id,trick.Kind,r.Before,paid.After));IssueQirangAcquiredBenefit(active,paid,movement);
        });AdvanceRuntimeProgram(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }
    private void IssueQirangAcquiredBenefit(ProgramSkillFrame f,ZhugeGuoReceipt r,CardMovementRecord movement)
    {
        _hasIssuedZhugeGuoState=true;var owner=_players[f.OwnerSeat];
        if(IsOrdinaryTrick(r.GainedKind!.Value))
        {
            var lease=new QirangEntityLease(r.Source,r.GameplayHash,f.OwnerSeat,r.GainedCardId!.Value,r.GainedKind.Value,r.UseId!.Value,r.ActionId!.Value,movement.Sequence);owner.QirangEntityLeases=(owner.QirangEntityLeases??[]).Append(lease).ToArray();AdvanceEventRulesAndQueueFact(new QirangEntityLeaseIssuedEvent(lease));
        }
        else if(IsDelayedCard(r.GainedKind!.Value) && (owner.YuhuaDelayDebts?.Count??0)<4)
        {
            var debt=new YuhuaDelayDebt(r.Source,r.GameplayHash,f.OwnerSeat,_turnNumber,r.UseId!.Value,r.ActionId!.Value,r.GainedCardId!.Value,movement.Sequence);owner.YuhuaDelayDebts=(owner.YuhuaDelayDebts??[]).Append(debt).ToArray();AdvanceEventRulesAndQueueFact(new YuhuaDelayedDebtIssuedEvent(debt));
        }
    }
    private bool TryQirangExtraParent(int owner,string skill,string instance,ProgramSkillWindowContext context,out CardUseFrame use)
    {
        use=null!;
        if(!TryGetDesignatedExtraTargetUse(owner,context,out var actual,out _) || !IsOrdinaryTrick(actual.CardKind) || actual.QirangQualification is not{}q || q.CardUseFrameId!=actual.Id || q.Lease.Source.SkillId!=skill || q.Lease.Source.SkillInstanceId!=instance || q.Lease.OwnerSeat!=owner || actual.Action is not{Type:CardActionType.Use}action || action.ActorSeat!=owner || action.ProviderSeat!=owner || action.PhysicalCards is not[var physical] || physical.CardId!=q.Lease.CardId || physical.From!=CardLocation.Hand(owner) || physical.CardKind!=q.Lease.PrintedKind || action.EffectiveKind!=q.Lease.PrintedKind || action.ActionId!=q.OriginalAction.ActionId || !SameOverflowAction(q.OriginalAction,action) || !ValidQirangQualification(q) || RoundGainedOriginalTargets(actual).Count!=(actual.CardKind==CardKind.BorrowedSword?2:1))return false;
        use=actual;return true;
    }
    private IReadOnlyList<PromptChoice> QirangExtraChoices(ProgramSkillFrame f)
    {
        var use=LifecycleCardUse(f.ZhugeGuo!.UseId!.Value)!;var originals=RoundGainedOriginalTargets(use);var choices=new List<PromptChoice>();var primary=originals.Where((_,index)=>use.CardKind!=CardKind.BorrowedSword||index%2==0).ToHashSet();
        foreach(var target in _players.Where(p=>!primary.Contains(p.Seat)&&CanBeUniqueLeaderTrickTarget(use,p)).OrderBy(p=>p.Seat))
            if(use.CardKind==CardKind.BorrowedSword){foreach(var victim in _players.Where(p=>p.IsAlive&&p.Seat!=target.Seat&&IsLegalBorrowedSwordSlashTarget(target,p)))choices.Add(QirangExtraChoice(f,[target.Seat,victim.Seat]));}
            else choices.Add(QirangExtraChoice(f,[target.Seat]));
        choices.Add(QirangExtraChoice(f,[]));return Array.AsReadOnly(choices.ToArray());
    }
    private PromptChoice QirangExtraChoice(ProgramSkillFrame f,IReadOnlyList<int> targets)=>new(new($"qirang-extra.{f.Id}.{string.Join('-',targets)}"),targets.Count==0?"不增加目标":$"增加目标：{string.Join('、',targets.Select(s=>_players[s].Name))}",[],targets,new Dictionary<string,string>{{"program-action","qirang-extra-target"}});
    private void PublishQirangExtraChoice(ProgramSkillFrame f)
    {
        var choices=QirangExtraChoices(f);var skill=_contentRegistry.GetSkill(f.SkillId);_pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"可为此祈禳获得的实体锦囊增加一个合法目标。",[],choices.SelectMany(c=>c.Targets).Distinct().ToArray(),f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=false,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};_status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;
    }
    [ProgramAnswer("qirang-extra-target")]
    private void ResolveQirangExtraChoice(PromptChoice choice)
    {
        var f=(ProgramSkillFrame)_resolutionStack.Last();RequireZhugeGuo(f);var r=f.ZhugeGuo!;
        if(_pendingDecision?.PlayerSeat!=f.OwnerSeat || !AssistedChoicesEqual([choice],QirangExtraChoices(f).Where(c=>c.Id==choice.Id).ToArray()) || !TryQirangExtraParent(f.OwnerSeat,f.SkillId,f.SkillInstanceId,f.WindowContext!,out var use))throw new InvalidOperationException("Qirang changed its original physical Use or legal target pair.");
        ClearPendingDecision();if(choice.Targets.Count>0){var targets=RoundGainedOriginalTargets(use).Concat(choice.Targets).ToArray();var action=CloneDesignatedExtraTargetAction(use.Action!,targets);UpdateProgramRoleCardUse(use with{TargetSeats=Array.AsReadOnly(targets),Action=action,TargetsAdjusted=true,Enhancements=use.Enhancements|CurrentCardEnhancement.ExtraTarget},action);AdvanceEventRulesAndQueueFact(new ProgramCardUseTargetAddedEvent(f.Id,f.SkillId,f.OwnerSeat,use.Id,choice.Targets[0]));}
        AdvanceEventRulesAndQueueFact(new QirangExtraTargetResolvedEvent(f.Id,use.Id,r.ActionId!.Value,choice.Targets.FirstOrDefault(-1) is var target && target>=0?target:null));FinishZhugeGuo(f);
    }
    private void FinishZhugeGuo(ProgramSkillFrame f){ReplaceRuntimeTop(f=f with{ZhugeGuo=null,PendingMovementContinuation=null});FinishProgramSkill(f,true);}
    [ProgramContinuation(ProgramContinuationKind.Resume,23000)]
    private bool ResumeZhugeGuo(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{ZhugeGuo:{}r}f||f.Id!=id||r.Stage==ZhugeGuoStage.ExtraTargetChoice)return false;RequireZhugeGuo(f);
        if(r.Stage==ZhugeGuoStage.YuhuaInspect){if(f.TopReorder is not null)return false;if(!r.PlacementIssued&&r.Viewed.Count>0)throw new InvalidOperationException("Yuhua cannot complete an unpaid private placement.");FinishZhugeGuo(f);return true;}
        if(TryBeginQueuedRecoveryReplacement(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCharacterStateProgramWindow(f.Id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(f.Id,PostEventContinuation.AwaitedProgramMovement)||TryBeginCardsMovedProgramWindow(f.Id)||TryBeginAdvancedSkillsChanged(f.Id))return true;FinishZhugeGuo(f);return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement,23000)]
    private bool ReturnZhugeGuoMovement(ProgramSkillFrame f)=>f.ZhugeGuo is not null&&ResumeZhugeGuo(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 130)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 130)]
    private bool CanContinueZhugeGuo(ProgramSkillFrame f){if(f.ZhugeGuo is not{Stage:ZhugeGuoStage.GainChildren or ZhugeGuoStage.YuhuaInspect})return false;RequireZhugeGuo(f);return true;}
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 140)]
    private bool IsZhugeGuoMovement(ProgramSkillFrame f,SkillProgramEffect?e,ProgramMovementContinuation p)=>f.ZhugeGuo is{Stage:ZhugeGuoStage.GainChildren}&&e?.Op==SkillProgramEffectOp.ObtainDeckTrickForActualEquipmentUse&&CanContinueZhugeGuo(f)&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null;
    private void RequireZhugeGuo(ProgramSkillFrame f)
    {
        if(f.ZhugeGuo is not{}r||r.Source!=ZhugeGuoSource(f)||r.GameplayHash!=f.GameplayHash||r.InstructionIndex!=f.InstructionIndex||r.ActualTurn!=_turnNumber||r.WindowId!=f.WindowContext?.ParentFrameId||r.Before>r.After||r.After>_movementSequence)throw new InvalidOperationException("ZhugeGuo lost its exact original source/window invoice.");
        if(r.Stage==ZhugeGuoStage.GainChildren)
        {
            if(r.GainedCardId is not{}id||r.GainedKind is not{}kind||r.UseId is not{}use||r.ActionId is not{}action||r.OriginalAction is not{Type:CardActionType.Use}original||original.ActionId!=action||original.ActorSeat!=f.OwnerSeat||!EquipmentCatalog.IsEquipment(original.EffectiveKind)||CompleteProgramEventHistory().OfType<QirangGainPaidEvent>().Count(e=>e==new QirangGainPaidEvent(f.Id,r.Source,r.GameplayHash,use,action,id,kind,r.Before,r.After))!=1||_cardMovements.Count(m=>m.CardId==id&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Sequence>r.Before&&m.Sequence<=r.After&&m.Reason.Value==QirangGainReason)!=1)throw new InvalidOperationException("Qirang gain lacks its once-paid native equipment and acquired entity.");
        }
        else if(r.Stage==ZhugeGuoStage.ExtraTargetChoice){if(r.Qualification is not{}q||!ValidQirangQualification(q)||!TryQirangExtraParent(f.OwnerSeat,f.SkillId,f.SkillInstanceId,f.WindowContext!,out _))throw new InvalidOperationException("Qirang extra target lost its original native designation.");}
        else
        {
            if(r.RequestedPeek is <1 or >5||r.Viewed.Count>r.RequestedPeek||r.Viewed.Distinct().Count()!=r.Viewed.Count||CompleteProgramEventHistory().OfType<YuhuaPrivatePeekStartedEvent>().Count(e=>e==new YuhuaPrivatePeekStartedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.WindowId,r.RequestedPeek,r.Viewed.Count))!=1||f.TopReorder is{}top&&!top.ViewedCardIds.SequenceEqual(r.Viewed))throw new InvalidOperationException("Yuhua lost its exact private viewed entities and once-issued quota.");
            if(r.PlacementIssued&&(r.PlacedTop.Count+r.PlacedBottom.Count!=r.Viewed.Count||r.PlacedTop.Concat(r.PlacedBottom).Distinct().Count()!=r.Viewed.Count||CompleteProgramEventHistory().OfType<YuhuaPrivatePeekPlacedEvent>().Count(e=>e==new YuhuaPrivatePeekPlacedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.WindowId,r.PlacedTop.Count,r.PlacedBottom.Count))!=1))throw new InvalidOperationException("Yuhua placed receipt lacks its once-issued native partition.");
        }
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 190)]
    private void AssertRegisteredZhugeGuo(ProgramSkillFrame frame)
    {
        if (frame.ZhugeGuo is not null) RequireZhugeGuo(frame);
    }
    [ProgramCapability]
    private sealed class ZhugeGuoProgramCapability(GameEngine engine) : IZhugeGuoProgramHost
    {public SkillProgramStepOutcome BeginZhugeGuo(ProgramSkillFrame f,SkillProgramEffect e)=>engine.BeginZhugeGuo(f,e);}
}
