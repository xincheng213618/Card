namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasMiuyanCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMiuyanCompletedUse);
    private bool HasShiluCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveShiluDrawRevealIdentity);
    private (long Base,int Round,int Count) _miuyanQualificationDependencies;
    private long _miuyanQualificationStamp;
    private long CaptureMiuyanQualificationStamp(long underlying)
    {
        if(!HasMiuyanCapability)return underlying;
        var next=(underlying,_roundNumber,ProgramEventHistory<MiuyanRoundDisabledEvent>().Count);
        if(_miuyanQualificationStamp==0||next!=_miuyanQualificationDependencies)
        {_miuyanQualificationDependencies=next;_miuyanQualificationStamp++;}
        return long.MinValue+_miuyanQualificationStamp;
    }
    private bool IsMiuyanGrantQualified(CharacterState owner,SkillGrant grant)=>!HasMiuyanCapability||grant.SkillId!="ol:miuyan"||
        !ProgramEventHistory<MiuyanRoundDisabledEvent>().Any(e=>e.Source.OwnerSeat==owner.Seat&&e.Source.SkillId==grant.SkillId&&e.RoundNumber==_roundNumber);

    private void IssueMiuyanUse(long useId,CardActionContext? action)
    {
        if(!HasMiuyanCapability||action is not{Type:CardActionType.Use,EffectiveKind:CardKind.FireAttack}||
            action.ConversionChain.SingleOrDefault(s=>s.SkillId=="ol:miuyan"&&s.BindingId=="black-fire-attack")is not{}source)return;
        var program=_contentRegistry.GetSkill(source.SkillId).Program!;
        WangGuanComposition.ValidateProgram(source.SkillId,program);
        if(LifecycleCardUse(useId)is not{}use||use.MiuyanUse is not null||use.Action?.ActionId!=action.ActionId||
            use.SourceSeat!=action.ActorSeat||source.OwnerSeat!=action.ActorSeat||action.ProviderSeat!=action.ActorSeat||
            action.PhysicalCards is not[var material]||material.From.OwnerSeat!=source.OwnerSeat||
            material.From.Zone is not(CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.WoodenOxGrain)||
            _cardZones.GetLocation(material.CardId)!=material.From||EffectiveSuit(_players[source.OwnerSeat],GetAdvancedCard(material.CardId))is not(Suit.Spade or Suit.Club)||
            !HasRuntimeSkillInstance(_players[source.OwnerSeat],source.SkillId,source.SkillInstanceId)||
            CurrentActualDiscardRecoveryPhase()is not{}phase||!IsRecordedActualDiscardRecoveryPhase(phase)||
            ProgramEventHistory<MiuyanUseIssuedEvent>().Any(e=>e.Invoice.UseId==useId))
            throw new InvalidOperationException("Miuyan lost its unique real accepted black Fire Attack material or actual phase.");
        var previous=_skillRuntimeState.GetConversionState(source.OwnerSeat,source.SkillId);
        var current=_skillRuntimeState.ToggleConversionState(source.OwnerSeat,source.SkillId);
        var invoice=new MiuyanUseInvoice(useId,action.ActionId,source,program.GameplayHash,_turnNumber,_turnProgression.OwnerSeat,_roundNumber,phase,previous,current,
            action.ActorSeat,action.ProviderSeat,material,_movementSequence);
        UpdateLifecycleCardUse(useId,u=>u with{MiuyanUse=invoice});
        AdvanceEventRulesAndQueueFact(new MiuyanUseIssuedEvent(invoice));
    }
    private bool MiuyanUseProof(CardUseFrame use)
    {
        if(use.MiuyanUse is not{}r||use.Action is not{Type:CardActionType.Use,EffectiveKind:CardKind.FireAttack}action||
            r.UseId!=use.Id||r.ActionId!=action.ActionId||use.CardKind!=CardKind.FireAttack||use.CardId!=r.Material.CardId||
            use.PhysicalCardIds is not[var id]||id!=r.Material.CardId||action.PhysicalCards is not[var cost]||cost!=r.Material||
            action.ProviderSeat!=r.ProviderSeat||!action.ConversionChain.Contains(r.Source)||!ShownEntityUseActorMatches(use,r.OriginalActor,r.ProviderSeat)||
            r.Source.OwnerSeat!=r.OriginalActor||r.Source.SkillId!="ol:miuyan"||r.Source.BindingId!="black-fire-attack"||
            _contentRegistry.GetSkill(r.Source.SkillId).Program?.GameplayHash!=r.GameplayHash||
            r.ActualTurn!=_turnNumber||r.TurnOwner!=_turnProgression.OwnerSeat||r.RoundNumber!=_roundNumber||!IsRecordedActualDiscardRecoveryPhase(r.Phase)||
            r.Phase.TurnNumber!=r.ActualTurn||r.Phase.ActualTurnOwnerSeat!=r.TurnOwner||!Enum.IsDefined(r.PreviousState)||
            r.CurrentState==(r.PreviousState)||ProgramEventHistory<MiuyanUseIssuedEvent>().Count(e=>e.Invoice==r)!=1||
            ProgramEventHistory<MiuyanUseIssuedEvent>().Count(e=>e.Invoice.UseId==use.Id)!=1||
            ProgramEventHistory<CardUseDeclaredEvent>().Count(e=>e.ResolutionId==use.Id&&e.SourceSeat==r.OriginalActor&&e.CardId==r.Material.CardId&&e.CardKind==CardKind.FireAttack)!=1)return false;
        return _cardMovements.Count(m=>m.Sequence>r.BeforePayment&&m.CardId==r.Material.CardId&&m.CardKind==r.Material.CardKind&&m.From==r.Material.From&&
            (m.To==CardLocation.Processing||m.To==CardLocation.OutsideGame&&GetAdvancedCard(m.CardId).IsGeneralWeapon)&&m.Reason==CardMoveReasons.Use)==1;
    }
    private CardUseFrame? MiuyanCandidateUse(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(!IsIssuedMiuyanCandidate(c)||context.Window!=SkillProgramTriggerWindow.CardUseCompleted||context.OwnerSeat!=c.OwnerSeat||
            context.CardUse is not{}card||LifecycleCardUse(card.ParentCardUseFrameId)is not{Step:ResolutionFrameStep.Completed,MiuyanUse:{}r,Action:{}action}use||
            !MiuyanUseProof(use)||card.CardActionId!=r.ActionId||r.Source.OwnerSeat!=c.OwnerSeat||r.Source.SkillId!=c.SkillId||r.Source.SkillInstanceId!=c.SkillInstanceId||r.GameplayHash!=c.GameplayHash||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId)is not{}window||window.ParentFrameId!=use.Id||window.Action.ActionId!=action.ActionId||
            window.CandidateIndex<0||window.CandidateIndex>=window.Candidates.Count)return null;
        var current=window.Candidates[window.CandidateIndex];
        return current.OwnerSeat==c.OwnerSeat&&current.SkillId==c.SkillId&&current.TriggerId==c.BindingId&&current.SkillInstanceId==c.SkillInstanceId&&current.GameplayHash==c.GameplayHash&&
            CreateCardActionProgramContext(window,current)==context&&ProgramEventHistory<CardUseFinishedEvent>().Count(e=>e.ResolutionId==use.Id)==1?use:null;
    }
    private bool IsIssuedMiuyanCandidate(ProgramTriggerCandidate c)=>HasMiuyanCapability&&c.SkillId=="ol:miuyan"&&
        _contentRegistry.GetSkill(c.SkillId).Program?.Triggers.Any(t=>t.Id==c.BindingId&&t.Effects is[{Op:SkillProgramEffectOp.ResolveMiuyanCompletedUse}])==true;
    private bool CanRunIssuedMiuyanCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext context)=>MiuyanCandidateUse(c,context)is{}use&&
        !ProgramEventHistory<WangGuanStartedEvent>().Any(e=>e.Op==SkillProgramEffectOp.ResolveMiuyanCompletedUse&&e.NativeId==use.Id);
    private void CollectIssuedMiuyanCandidates(CardActionContext action,SkillProgramTriggerWindow window,List<ProgramCardTriggerCandidate> result)
    {
        if(window!=SkillProgramTriggerWindow.CardUseCompleted||_resolutionStack.LastOrDefault()is not CardUseFrame{MiuyanUse:{}r}use||action.ActionId!=r.ActionId||!MiuyanUseProof(use))return;
        var t=_contentRegistry.GetSkill(r.Source.SkillId).Program!.Triggers.Single();
        if(result.Any(c=>c.OwnerSeat==r.Source.OwnerSeat&&c.SkillId==r.Source.SkillId&&c.SkillInstanceId==r.Source.SkillInstanceId&&c.TriggerId==t.Id))return;
        result.Add(new(r.Source.OwnerSeat,r.Source.OwnerSeat,r.Source.SkillId,t.Id,r.GameplayHash,r.Source.SkillInstanceId,t.Priority,
            CreateCardActionProgramContext(action,window,0,r.Source.OwnerSeat,r.Source.OwnerSeat,CaptureProgramTriggerFacts(_players[r.Source.OwnerSeat],action))));
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27210)]
    private bool QualifyWangGuan(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext context)=>t.Effects is not[{Op:SkillProgramEffectOp.ResolveMiuyanCompletedUse}]||CanRunIssuedMiuyanCandidate(c,context);

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,27210)]
    private void ObserveMiuyanPhaseHandReveal(IGameEvent fact)
    {
        if(!HasMiuyanCapability||CurrentActualDiscardRecoveryPhase()is not{}phase||IsMiuyanShandaoMovementReveal(fact)||IsMiuyanCategoryGiftMovementReveal(fact))return;
        var(frame,ids)=fact switch
        {
            ProgramCardsRevealedEvent e=>(e.FrameId,e.Cards.Select(c=>c.Id)),
            CardsRevealedEvent e=>(e.ResolutionId,e.Cards.Select(c=>c.Id)),
            FireAttackCardRevealedEvent e=>(e.ResolutionId,new[]{e.CardId}.AsEnumerable()),
            ProgramHandComparisonSourceRevealedEvent e=>(e.FrameId,new[]{e.CardId}.AsEnumerable()),
            ProgramHandComparisonCardRevealedEvent e=>(e.FrameId,new[]{e.CardId}.AsEnumerable()),
            ProgramHandDraftRevealedEvent e=>(e.FrameId,MiuyanDraftHandIds(e)),
            ProgramQingliangRevealEvent e=>(e.FrameId,MiuyanQingliangHandIds(e)),
            ProgramJuanxiaTrickUsedEvent{TrickKind:nameof(CardKind.FireAttack),CardId:>0}e=>(e.FrameId,MiuyanJuanxiaHandIds(e)),
            _=>(0L,Enumerable.Empty<int>())
        };
        foreach(var id in ids.Distinct().ToArray())
            if(_cardZones.GetLocation(id)is{Zone:CardZoneKind.Hand,OwnerSeat:{}holder}&&CurrentHandEntry(holder,id)is{}entry&&
                !ProgramEventHistory<MiuyanPhaseHandRevealedEvent>().Any(e=>e.Phase==phase&&e.CardId==id))
                AdvanceEventRulesAndQueueFact(new MiuyanPhaseHandRevealedEvent(phase,frame,id,holder,entry,fact.GetType().Name));
    }
    private bool IsMiuyanShandaoMovementReveal(IGameEvent fact)
    {
        if(fact is not CardsRevealedEvent{Cards:[var card]}e||
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==e.ResolutionId)is not{SkillId:"ol:shandao",ActivationId:"top-and-two-tricks",TriggerId:null,InstructionIndex:1,TianChouShandao:{Stage:TianChouShandaoStage.TopChildren,TargetsFrozen:true}r}f||
            GetPausedPrivateOfferEffect(f)?.Op!=SkillProgramEffectOp.ResolveShandaoTopAndGlobalTricks||r.Source!=TianChouSource(f)||
            r.Cursor<0||r.Cursor>=r.Targets.Count)return false;
        return _cardZones.GetLocation(card.Id)==CardLocation.Hand(r.Targets[r.Cursor]);
    }
    private bool IsMiuyanCategoryGiftMovementReveal(IGameEvent fact)
    {
        if(fact is not ProgramCardsRevealedEvent{SkillId:"ol:zhaosong",BindingId:"give-hand-category-mark",Bind:"category-mark-gift",Cards:[var card]}e||
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==e.FrameId)is not{SkillId:"ol:zhaosong",TriggerId:"give-hand-category-mark",InstructionIndex:1,
                RecipientCategoryMark:{Operation:SkillProgramEffectOp.GiveHandAndGrantCategoryMark,Stage:RecipientCategoryMarkStage.ChoosingGift}r}f||
            GetPausedPrivateOfferEffect(f)?.Op!=r.Operation||r.Source!=RecipientMarkSource(f)||r.HolderSeat!=e.OwnerSeat)return false;
        return r.Materials.Any(m=>m.CardId==card.Id&&m.From==CardLocation.Hand(r.HolderSeat))&&_cardZones.GetLocation(card.Id)==CardLocation.Hand(r.HolderSeat);
    }
    private IEnumerable<int> MiuyanQingliangHandIds(ProgramQingliangRevealEvent e)
    {
        if(_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==e.FrameId)is not{}f||f.SkillId!=e.SkillId||
            GetProgramBindingId(f)!=e.BindingId||f.OwnerSeat!=e.OwnerSeat||
            f.CardSetBindings.SingleOrDefault(b=>b.Name==QingliangRevealBinding)is not{Visibility:SkillProgramCardSetVisibility.Public}set||set.CardIds.Count!=e.CardCount)
            throw new InvalidOperationException("Miuyan lost Qingliang's exact original public Hand binding.");
        return set.CardIds;
    }
    private IEnumerable<int> MiuyanDraftHandIds(ProgramHandDraftRevealedEvent e)
    {
        if(_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==e.FrameId)is not{PublicHandDraft:{Stage:2}draft}f||
            f.SkillId!=e.SkillId||f.OwnerSeat!=e.OwnerSeat||!draft.CardIds.SequenceEqual(e.CardIds)||!draft.RecipientSeats.SequenceEqual(e.RecipientSeats))
            throw new InvalidOperationException("Miuyan lost the native draft's exact original public Hand entities.");
        return e.CardIds;
    }
    private IEnumerable<int> MiuyanJuanxiaHandIds(ProgramJuanxiaTrickUsedEvent e)
    {
        if(_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==e.FrameId)is not{JuanxiaLaunch:{Stage:JuanxiaStage.Fire,PendingKind:nameof(CardKind.FireAttack)}state}f||
            f.OwnerSeat!=e.OwnerSeat||f.SkillId!=e.SkillId||GetProgramBindingId(f)!=e.BindingId||GetPausedPrivateOfferEffect(f)?.Op!=SkillProgramEffectOp.JuanxiaDeclareTricks||
            state.TargetSeat!=e.TargetSeat||state.RevealedCardId!=e.CardId||!f.SelectedTargetSeats.SequenceEqual([state.TargetSeat]))
            throw new InvalidOperationException("Miuyan lost Juanxia's actual original public fire-reveal entity.");
        return [e.CardId];
    }
    private IReadOnlyList<WangGuanHandEntity> MiuyanClaimEntities(MiuyanUseInvoice use,int owner)=>Array.AsReadOnly(ProgramEventHistory<MiuyanPhaseHandRevealedEvent>()
        .Where(e=>e.Phase==use.Phase).Select(e=>e.CardId).Distinct().Order().Select(id=>(Id:id,From:_cardZones.GetLocation(id)))
        .Where(x=>x.From is{Zone:CardZoneKind.Hand,OwnerSeat:{}holder}&&holder!=owner&&_players[holder].IsAlive)
        .Select(x=>new WangGuanHandEntity(x.Id,GetAdvancedCard(x.Id).Kind,x.From.OwnerSeat!.Value,_cardZones.CardsAt(x.From).ToList().FindIndex(c=>c.Id==x.Id),CurrentHandEntry(x.From.OwnerSeat!.Value,x.Id)!.Value)).ToArray());

    private ProgramCardIdentityMatch? ShiluIdentity(CharacterState owner,Card card)
    {
        if(!HasShiluCapability||_cardZones.GetLocation(card.Id)!=CardLocation.Hand(owner.Seat)||CurrentHandEntry(owner.Seat,card.Id)is not{}entry)return null;
        var e=ProgramEventHistory<ShiluHandIdentityGrantedEvent>().LastOrDefault(e=>e.HolderSeat==owner.Seat&&e.CardId==card.Id&&e.HandEntry==entry&&
            !_cardMovements.Any(m=>m.CardId==card.Id&&m.Sequence>e.MovementCutoff&&m.From==CardLocation.Hand(owner.Seat)&&m.To!=m.From));
        if(e is null)return null;
        AssertShiluIdentity(e);
        var p=_contentRegistry.GetSkill(e.Source.SkillId).Program!;
        return new(p,new SkillProgramCardIdentity(e.Source.BindingId,[CardZoneKind.Hand],[],[],CardKind.Slash,new(SkillProgramConditionKind.Always,0,[])),e.Source);
    }
    private void AssertShiluIdentity(ShiluHandIdentityGrantedEvent e)
    {
        if(e.Source.SkillId!="ol:shilu"||_contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash!=e.GameplayHash||
            ProgramEventHistory<ShiluHandIdentityGrantedEvent>().Count(v=>v.FrameId==e.FrameId)!=1||
            ProgramEventHistory<WangGuanStartedEvent>().Where(v=>v.FrameId==e.FrameId).ToArray()is not[var started]||started.Source!=e.Source||started.GameplayHash!=e.GameplayHash||
            started.NativeId!=e.DamageFrameId||started.Op!=SkillProgramEffectOp.ResolveShiluDrawRevealIdentity||
            ProgramEventHistory<ShiluCandidateFrozenEvent>().Count(v=>v.FrameId==e.FrameId&&v.Entity.CardId==e.CardId&&v.Entity.HolderSeat==e.HolderSeat&&v.Entity.Entry==e.HandEntry)!=1||
            ProgramEventHistory<ShiluHolderChosenEvent>().Count(v=>v.FrameId==e.FrameId&&v.HolderSeat==e.HolderSeat&&v.MovementCutoff==e.MovementCutoff)!=1||
            ProgramEventHistory<ProgramCardsRevealedEvent>().Count(v=>v.FrameId==e.FrameId&&v.SkillId==e.Source.SkillId&&v.BindingId==e.Source.BindingId&&v.Bind==ShiluRevealBind&&v.Cards is[var card]&&card.Id==e.CardId)!=1)
            throw new InvalidOperationException("Shilu's persistent hand identity lost its exact actual damage, private entity selection or genuine public reveal.");
    }
}
