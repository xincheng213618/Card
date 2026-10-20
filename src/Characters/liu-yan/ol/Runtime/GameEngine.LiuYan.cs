namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramTargetWindow(ProgramTargetWindowStage.NormalizeImplicitDrawTwo, 30)]
    private bool LiuYanNormalizeImplicitDrawTwoTargetWindow(ProgramTargetCardWindowQuery query) =>
        HasLiuYanCapability;


    private const string LiuYanDrawReason="skill-program.liu-yan.tushe.draw";
    private bool HasLiuYanCapability=>_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.UseLimuSelfIndulgenceAndRecover)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DrawTusheForActualNonEquipmentTargets);
    private static CardConversionSource LiuYanSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    private static bool LiuYanBasic(CardKind k)=>IsSlashCard(k)||k is CardKind.Dodge or CardKind.Peach or CardKind.Alcohol;
    private bool LiuYanNoBasicHand(CharacterState p)=>GetHand(p).All(c=>!LiuYanBasic(AdvancedEffectiveHandKind(p,c)));
    private CardUseFrame? LiuYanTusheUse(int owner,ProgramSkillWindowContext context)
    {
        if(context.Window!=SkillProgramTriggerWindow.CardUseTargetsFinalized||context.OwnerSeat!=owner||context.CardUse is not{}identity||
           _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId)is not{}window||window.ParentFrameId!=identity.ParentCardUseFrameId||GetCardActionWindow(window)!=context.Window||
           LifecycleCardUse(identity.ParentCardUseFrameId)is not{Action:{Type:CardActionType.Use}a}use||a.ActionId!=identity.CardActionId||a.ActorSeat!=owner||use.SourceSeat!=owner||a.EffectiveKind!=use.CardKind||
           CardCatalog.Get(use.CardKind).CategoryName=="装备牌"||a.EffectiveDesignatedTargetSeats.Count==0)return null;
        return use;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27230,SkillProgramEffectOp.DrawTusheForActualNonEquipmentTargets)]
    private bool QualifyLiuYanTushe(CharacterState owner,ProgramSkillWindowContext context)=>LiuYanTusheUse(owner.Seat,context)is not null&&LiuYanNoBasicHand(owner);
    private bool LiuYanLimuMaterialLegal(CharacterState owner,Card card,string skill,string? instance=null)
    {
        var from=_cardZones.GetLocation(card.Id);
        return owner.IsAlive&&from.OwnerSeat==owner.Seat&&from.Zone is(CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.WoodenOxGrain)&&
            (from.Zone!=CardZoneKind.WoodenOxGrain||GetPlayableCards(owner).Any(c=>c.Id==card.Id))&&!card.IsGeneralWeapon&&
            !IsActiveProgramSourceEquipmentCard(owner.Seat,skill,instance??GetRuntimeSkillInstanceId(owner,skill),card)&&EffectiveSuit(owner,card)==Suit.Diamond&&
            !IsTurnHandCardRestricted(owner,card)&&!IsPlayPhasePhysicalCardRestricted(owner,card)&&!IsTurnPhysicalUseForbidden(owner.Seat,[card.Id])&&!IsTurnSuitUseForbidden(owner.Seat,Suit.Diamond)&&
            !owner.JudgmentAreaAbolished&&!HasJudgmentEffectiveCard(owner,CardKind.Indulgence)&&!HasCardPolicy(owner,SkillProgramCardPolicyKind.ProhibitDelayedTrickTarget)&&
            !IsCardUseForbidden(owner.Seat,CardKind.Indulgence,CardActionType.Use)&&!IsDirectedCardTargetProhibited(owner.Seat,owner.Seat,CardKind.Indulgence)&&
            !IsSelfTargetForbiddenAction(owner,CardKind.Indulgence,[owner.Seat])&&!IsCardTargetProhibited(owner,CardKind.Indulgence,Suit.Diamond,true)&&!HasBeneficiarySuitShield(owner.Seat,owner.Seat,Suit.Diamond);
    }
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart,27231,SkillProgramEffectOp.UseLimuSelfIndulgenceAndRecover)]
    private bool CanActivateLiuYanLimu(CharacterState owner,string skillId)=>GetPlayableCards(owner).Concat(GetEquipment(owner)).Any(c=>LiuYanLimuMaterialLegal(owner,c,skillId));
    private bool HasLiuYanLimuTargetAllowance(CharacterState actor,CharacterState target,int? excludedEquipmentId=null,EquipmentSlot? excludedEquipmentSlot=null)=>
        HasLiuYanCapability&&actor.IsAlive&&target.IsAlive&&actor.Seat!=target.Seat&&HasRuntimeSkill(actor,"ol:limu")&&GetJudgment(actor).Count>0&&
        (IsGameFactionAttackRangeTarget(actor.Seat,target.Seat)||ConvertRuleValue(EvaluateDistance(actor,target,excludedEquipmentId,excludedEquipmentSlot))<=ConvertRuleValue(EvaluateAttackRange(actor,excludedEquipmentId,excludedEquipmentSlot)));
    private bool HasLiuYanLimuQuotaAllowance(CharacterState actor,CharacterState target,Card? physical,IReadOnlyList<int>? ids,int? excludedId,EquipmentSlot? excludedSlot)
    {
        if(!HasLiuYanCapability)return false;
        var materials=(ids??(physical is {Id:>0}?[physical.Id]:[])).ToHashSet();
        var equipment=GetEquipment(actor).Where(c=>materials.Contains(c.Id)).ToArray();
        excludedId??=equipment.FirstOrDefault(c=>EquipmentCatalog.Get(c.Kind).Slot==EquipmentSlot.Weapon)?.Id;
        if(excludedSlot is null&&equipment.Any(c=>EquipmentCatalog.Get(c.Kind).Slot==EquipmentSlot.OffensiveHorse))excludedSlot=EquipmentSlot.OffensiveHorse;
        return HasLiuYanLimuTargetAllowance(actor,target,excludedId,excludedSlot);
    }
    [ProgramCapability]private sealed class LiuYanCapability(GameEngine e):ILiuYanHost,ILiuYanSelectionQuery
    {public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginLiuYan(f,op);public bool CanUse(int owner,Card card,string skill)=>e.LiuYanLimuMaterialLegal(e._players[owner],card,skill);}
    private SkillProgramStepOutcome BeginLiuYan(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(f.LiuYan is not null||f.InstructionIndex!=1||!_players[f.OwnerSeat].IsAlive||!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId))throw new InvalidOperationException("Liu Yan requires his exact living, unissued native operation.");
        if(op==SkillProgramEffectOp.DrawTusheForActualNonEquipmentTargets)
        {
            if(f.SkillId!="ol:tushe"||f.TriggerId!="actual-targets"||f.WindowContext is not{}context||LiuYanTusheUse(f.OwnerSeat,context)is not{}use||!LiuYanNoBasicHand(_players[f.OwnerSeat])||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0)throw new InvalidOperationException("Tushe requires an actual non-equipment designation and no effective basic hand cards after payment.");
            var r=new LiuYanReceipt(LiuYanSource(f),f.GameplayHash,1,_turnNumber,_currentSeat,op,LiuYanStage.TusheDrawChildren){Before=_movementSequence,After=_movementSequence,ParentWindowId=context.ParentFrameId,OriginalUseId=use.Id,ActionId=use.Action!.ActionId,Targets=use.Action.EffectiveDesignatedTargetSeats.Distinct().ToArray()};
            ReplaceRuntimeTop(f=f with{LiuYan=r});RecordLiuYanStart(f);
            var count=DrawCards(_players[f.OwnerSeat],r.Targets.Count,true,new(LiuYanDrawReason)).Count;
            f=GetActiveProgramFrame(f.Id);ReplaceRuntimeTop(f=f with{LiuYan=f.LiuYan! with{DrawCount=count,After=_movementSequence}});
            AdvanceEventRulesAndQueueFact(new LiuYanTusheDrawnEvent(f.Id,f.OwnerSeat,r.Targets.Count,count,r.Before,_movementSequence));ContinueLiuYan(f.Id);return SkillProgramStepOutcome.AwaitChild;
        }
        if(f.SkillId!="ol:limu"||f.TriggerId is not null||f.ActivationId!="self-indulgence"||f.WindowContext is not null||f.OwnerSeat!=_currentSeat||_phase!=TurnPhase.Play||f.SelectedCardIds is not[var id]||f.SelectedTargetSeats.Count!=0)throw new InvalidOperationException("Limu requires one real diamond entity in the owner's actual Play.");
        var card=EntityAtCurrentLocation(id);var actor=_players[f.OwnerSeat];if(!LiuYanLimuMaterialLegal(actor,card,f.SkillId,f.SkillInstanceId))throw new InvalidOperationException("Limu's original diamond entity or self delayed target is illegal.");
        var origin=new LiuYanLimuUseOrigin(f.Id,_resolutionSequence+1,_cardActionSequence+1,LiuYanSource(f),f.GameplayHash,_turnNumber,_cardUseDebitPhaseInstanceId,f.OwnerSeat,id,card.Kind,_cardZones.GetLocation(id),EffectiveSuit(actor,card),card.Rank);
        var receipt=new LiuYanReceipt(origin.Source,f.GameplayHash,1,_turnNumber,_currentSeat,op,LiuYanStage.LimuNative){Before=_movementSequence,After=_movementSequence,Use=origin,OriginalUseId=origin.CardUseFrameId,ActionId=origin.ActionId,Targets=[f.OwnerSeat],Auxiliaries=card.Kind==CardKind.WoodenOx&&origin.From==CardLocation.Equipment(f.OwnerSeat)?GetWoodenOxGrain(actor).Select(c=>new LiuYanMaterial(c.Id,c.Kind,CardLocation.WoodenOxGrain(f.OwnerSeat))).ToArray():[]};
        ReplaceRuntimeTop(f=f with{LiuYan=receipt});RecordLiuYanStart(f);AdvanceEventRulesAndQueueFact(new LiuYanLimuIssuedEvent(origin));BeginLiuYanLimu(f,origin,card);return SkillProgramStepOutcome.AwaitChild;
    }
    private void RecordLiuYanStart(ProgramSkillFrame f)
    {var r=f.LiuYan!;AdvanceEventRulesAndQueueFact(new LiuYanStartedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.TurnOwner,r.Operation,r.ParentWindowId,r.OriginalUseId,r.ActionId,string.Join(',',r.Targets)));}
    private bool DrainLiuYan(long id)=>TryBeginQueuedRecoveryReplacement(id,PostEventContinuation.Program)||TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.Program)||TryBeginCardsMovedProgramWindow(id)||TryBeginAdvancedSkillsChanged(id);
    private void ContinueLiuYan(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{LiuYan:{}r}f||f.Id!=id)return;AssertLiuYan(f);
        if(r.Stage==LiuYanStage.LimuNative)return;
        if(DrainLiuYan(id))return;f=GetActiveProgramFrame(id);r=f.LiuYan!;
        if(r.Stage==LiuYanStage.LimuReturnedChildren)
        {
            ReplaceRuntimeTop(f=f with{LiuYan=r with{Stage=LiuYanStage.LimuRecoveryChildren,RecoveryIssued=_winner==Winner.None&&_players[f.OwnerSeat].IsAlive}});
            if(f.LiuYan!.RecoveryIssued)
            {
                AdvanceEventRulesAndQueueFact(new LiuYanLimuRecoveryIssuedEvent(f.Id,f.OwnerSeat,1));new ProgramSkillHost(this).Recover(f.Id,f.OwnerSeat,f.OwnerSeat,1,null,null);
                if(DrainLiuYan(id))return;f=GetActiveProgramFrame(id);
            }
        }
        FinishLiuYan(f);
    }
    private void FinishLiuYan(ProgramSkillFrame f)
    {var r=f.LiuYan!;AdvanceEventRulesAndQueueFact(new LiuYanCompletedEvent(f.Id,r.Operation,r.DrawCount,r.RecoveryIssued));ReplaceRuntimeTop(f=f with{LiuYan=null});FinishProgramSkill(f,true);}
    [ProgramContinuation(ProgramContinuationKind.Resume,27230)]private bool ResumeLiuYan(long id)
    {if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{LiuYan:not null}f||f.Id!=id)return false;ContinueLiuYan(id);return true;}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27230)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27230)]
    private bool ContinuePaidLiuYan(ProgramSkillFrame f)=>f.LiuYan is not null&&ValidLiuYan(f);

    [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption, 80)]
    private bool DistanceCardUseExemption80HasLiuYanLimuTargetAllowance(ProgramCardDistanceQuery q) =>
        !IsSlashCard(q.Kind) && HasLiuYanLimuTargetAllowance(q.Actor,q.Target,q.ExcludedEquipmentId,q.ExcludedEquipmentSlot);

}

public sealed partial class GameEngine
{
    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaBeforeLimit, 80)]
    private bool LiuYanLimuSlashQuotaBeforeLimit(ProgramSlashQuotaQuery query) =>
        HasLiuYanLimuQuotaAllowance(query.Actor,query.Target,query.PhysicalCard,query.PhysicalCardIds,query.ExcludedEquipmentId,query.ExcludedEquipmentSlot);
}
