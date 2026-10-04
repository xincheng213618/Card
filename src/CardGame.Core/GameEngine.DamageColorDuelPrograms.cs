namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool CanIssueSelectedActorDuel(int ownerSeat,int actorSeat) =>
        IsValidPlayerSeat(ownerSeat) && IsValidPlayerSeat(actorSeat) && ownerSeat!=actorSeat &&
        _players[ownerSeat].IsAlive && _players[actorSeat].IsAlive &&
        !IsCardUseForbidden(actorSeat,CardKind.Duel,CardActionType.Use) &&
        !HasTurnCardTargetRestriction(actorSeat,SkillProgramCardTargetRestriction.SelfOnly) &&
        !IsCardTargetProhibited(_players[ownerSeat],CardKind.Duel,Suit.None) &&
        !IsDirectedCardTargetProhibited(actorSeat,ownerSeat,CardKind.Duel);

    private SkillProgramStepOutcome BeginSelectedActorDuel(ProgramSkillFrame input)
    {
        var f=GetActiveProgramFrame(input.Id);
        var plan=ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!);
        if(f.TriggerId is not null || f.WindowContext is not null || f.InstructionIndex!=1 || f.SelectedCardIds.Count!=0 ||
            f.SelectedTargetSeats is not [var actor] || plan.Instructions.Count!=1 || plan.Instructions[0].Op!=SkillProgramEffectOp.UseSelectedActorDuel ||
            plan.Activation is not {MinCards:0,MaxCards:0,MinTargets:1,MaxTargets:1,UsesPerPhase:2,UsesPerTurn:null} ||
            _phase!=TurnPhase.Play || _currentSeat!=f.OwnerSeat || ActiveCardAttack is not null || ActiveDuel is not null)
            throw new InvalidOperationException("Selected actor Duel lost its exact owning zero-card Play activation.");
        if(!CanIssueSelectedActorDuel(f.OwnerSeat,actor))return SkillProgramStepOutcome.Continue;
        var origin=new ProgramSelectedActorDuelOrigin(f.Id,f.InstructionIndex,f.OwnerSeat,actor,f.SkillId,f.ActivationId,f.SkillInstanceId,f.GameplayHash,_turnNumber);
        var card=new Card(0,CardKind.Duel,Suit.None,0);
        var id=BeginCardUse(card,actor,[f.OwnerSeat],CardKind.Duel,physicalCardIds:[],selectedActorDuelOrigin:origin);
        BeginJizhiOrNullificationWindow(id,card,actor,[f.OwnerSeat],LegalActionKind.Duel,playedCardKind:CardKind.Duel);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private bool IsSelectedActorDuelUse(long id) => LifecycleCardUse(id) is {SelectedActorDuelOrigin:not null,CardId:0,CardKind:CardKind.Duel,PhysicalCardIds.Count:0};
    private Card GetTrickRepresentation(long frameId,int cardId,bool requireProcessing=false) =>
        cardId==0 && IsTieredRoundZeroUse(frameId) ? TieredRoundZeroRepresentation(frameId) :
        cardId==0 && IsIssuedZeroEntityDuel(frameId) ? new Card(0,CardKind.Duel,Suit.None,0) :
            _cardZones.CardsAt(requireProcessing ? CardLocation.Processing : _cardZones.GetLocation(cardId)).Single(c=>c.Id==cardId);
    private bool MatchesSelectedActorDuelAction(long frameId,CardActionContext action,int cardId) =>
        cardId==0 && IsIssuedZeroEntityDuel(frameId) && LifecycleCardUse(frameId)?.Action?.ActionId==action.ActionId &&
        action.Type==CardActionType.Use && action.EffectiveKind==CardKind.Duel && action.PhysicalCards.Count==0 && action.ConversionChain.Count==0;

    private void ReturnSelectedActorDuel(CardUseFrame use)
    {
        if(use.SelectedActorDuelOrigin is not {AttackStarted:false} origin)return;
        if(_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id!=origin.ParentProgramFrameId ||
            f.InstructionIndex!=origin.InstructionIndex || f.OwnerSeat!=origin.OwnerSeat || f.SkillId!=origin.SkillId ||
            f.ActivationId!=origin.ActivationId || f.SkillInstanceId!=origin.SkillInstanceId || f.GameplayHash!=origin.GameplayHash)
            throw new InvalidOperationException("Selected actor Duel lost its typed Program return.");
        AdvanceRuntimeProgram(f.Id);
    }

    private void AssertSelectedActorDuels()
    {
        foreach(var use in _resolutionStack.OfType<CardUseFrame>().Where(u=>u.SelectedActorDuelOrigin is not null))
        {
            var o=use.SelectedActorDuelOrigin!;
            var index=_resolutionStack.FindIndex(x=>x.Id==use.Id);
            if(index<1 || _resolutionStack[index-1] is not ProgramSkillFrame f || f.Id!=o.ParentProgramFrameId ||
                f.OwnerSeat!=o.OwnerSeat || f.SkillId!=o.SkillId || f.ActivationId!=o.ActivationId || f.SkillInstanceId!=o.SkillInstanceId ||
                f.GameplayHash!=o.GameplayHash || f.InstructionIndex!=o.InstructionIndex || o.InstructionIndex!=1 ||
                f.SelectedCardIds.Count!=0 || f.SelectedTargetSeats is not [var initialActor] || initialActor!=o.InitialActorSeat ||
                o.OwnerSeat==o.InitialActorSeat || !IsValidPlayerSeat(o.InitialActorSeat) || o.TurnNumber!=_turnNumber ||
                use.CardId!=0 || use.CardKind!=CardKind.Duel || use.PhysicalCardIds?.Count!=0 ||
                use.Action is not {Type:CardActionType.Use,EffectiveKind:CardKind.Duel,PhysicalCards.Count:0,ConversionChain.Count:0,EffectiveSuit:Suit.None,EffectiveIsRed:false} ||
                ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).Instructions is not [{Op:SkillProgramEffectOp.UseSelectedActorDuel}])
                throw new InvalidOperationException("Selected actor Duel has an invalid issued origin or typed owner.");
            // Initial actor/target are issuance facts. Mature action rewrites need not keep these initial seats.
        }
    }

    private SkillProgramStepOutcome BeginDamageAppearanceDraw(ProgramSkillFrame input)
    {
        var f=GetActiveProgramFrame(input.Id);
        if(f.DamageAppearanceReceipt is not null || f.InstructionIndex!=1 || f.WindowContext is not {Window:SkillProgramTriggerWindow.DamageAppliedBeforeDying} context ||
            _resolutionStack.Count<2 || _resolutionStack[^2] is not DamageTriggerWindowFrame window || window.Id!=context.ParentFrameId ||
            window.TriggerWindow!=context.Window || window.ParentFrameId!=context.DamageFrameId || window.TargetSeat!=f.OwnerSeat || context.TargetSeat!=f.OwnerSeat)
            throw new InvalidOperationException("Damage appearance Draw requires the exact applied damage target window.");
        var attempt=GetDamageTriggerAttack(window);
        if(!attempt.DamageWasApplied || attempt.DamageAmount<=0 || attempt.TargetSeat!=f.OwnerSeat)
            throw new InvalidOperationException("Damage appearance Draw requires positive actual applied damage.");
        var action=LifecycleCardUse(attempt.ResolutionId)?.Action;
        var hasCard=action is not null || attempt.EffectiveCardKind is not null || attempt.Card is not null;
        var red=hasCard && (action is not null ? action.EffectiveIsRed ?? (action.EffectiveSuit is Suit.Heart or Suit.Diamond) : attempt.Card?.Suit is Suit.Heart or Suit.Diamond);
        var source=attempt.IsSourceLess || attempt.IsDelayedJudgmentDamage ? (int?)null : attempt.SourceSeat;
        var recipient=hasCard ? red ? source : f.OwnerSeat : null;
        var draw=recipient is { } seat && IsValidPlayerSeat(seat) && _players[seat].IsAlive;
        ReplaceRuntimeTop(f with {DamageAppearanceReceipt=new(f.InstructionIndex,window.Id,window.ParentFrameId,f.OwnerSeat,source,
            action?.EffectiveKind??attempt.EffectiveCardKind??attempt.Card?.Kind,hasCard,red,recipient,draw)});
        if(draw)DrawProgramCards(f.Id,recipient!.Value,1,null,null,SkillProgramCardSetVisibility.Private,CardMoveReasons.Draw);
        return SkillProgramStepOutcome.Continue;
    }

    private void AssertDamageAppearanceReceipt(ProgramSkillFrame f,ProgramExecutionPlan plan)
    {
        if(f.DamageAppearanceReceipt is not { } p)return;
        if(f.TriggerId is null || plan.Instructions is not [{Op:SkillProgramEffectOp.DrawByDamageCardColor}] ||
            f.InstructionIndex!=1 || p.InstructionIndex!=1 || p.OwnerSeat!=f.OwnerSeat ||
            f.WindowContext is not {Window:SkillProgramTriggerWindow.DamageAppliedBeforeDying} c || c.ParentFrameId!=p.DamageWindowFrameId ||
            c.DamageFrameId!=p.DamageFrameId || c.OwnerSeat!=p.OwnerSeat || c.TargetSeat!=p.OwnerSeat || c.SourceSeat!=p.SourceSeat || c.Amount<=0 ||
            !_resolutionStack.OfType<DamageTriggerWindowFrame>().Any(w=>w.Id==p.DamageWindowFrameId&&w.ParentFrameId==p.DamageFrameId&&w.TargetSeat==p.OwnerSeat&&w.TriggerWindow==c.Window) ||
            p.HasCard!=(p.EffectiveCardKind is not null) || p.IsRed&&!p.HasCard ||
            p.RecipientSeat!=(p.HasCard ? p.IsRed ? p.SourceSeat : p.OwnerSeat : null) ||
            p.SourceSeat is { } source && !IsValidPlayerSeat(source) || p.DrawIssued && p.RecipientSeat is null)
            throw new InvalidOperationException("Damage appearance receipt lost its exact producer, source or finite claim.");
    }

    private ProgramSkillFrame? DamageAppearanceDrawObserverRoot(long windowId)
    {
        for(var index=1;index+1<_resolutionStack.Count;index++)
        {
            if(_resolutionStack[index] is not ProgramSkillFrame f || f.DamageAppearanceReceipt is not {DrawIssued:true,RecipientSeat:{ } recipient} p ||
                f.WindowContext is not {Window:SkillProgramTriggerWindow.DamageAppliedBeforeDying} c || c.ParentFrameId!=windowId ||
                p.DamageWindowFrameId!=windowId || p.DamageFrameId!=c.DamageFrameId || p.OwnerSeat!=f.OwnerSeat || c.TargetSeat!=f.OwnerSeat ||
                _resolutionStack[index-1] is not DamageTriggerWindowFrame w || w.Id!=windowId || w.ParentFrameId!=p.DamageFrameId || w.TargetSeat!=f.OwnerSeat ||
                f.InstructionIndex!=1 || p.InstructionIndex!=1 ||
                ProgramInstructionResolver.Default.Resolve(f,_contentRegistry.GetSkill(f.SkillId).Program!).Instructions is not [{Op:SkillProgramEffectOp.DrawByDamageCardColor}] ||
                _resolutionStack[index+1] is not CardsMovedTriggerWindowFrame movement ||
                movement.Batch.ParentFrameId!=f.Id || movement.Batch.AwaitingProgramFrameId is { } awaiting && awaiting!=f.Id ||
                movement.Batch.OriginOwnerSeat!=f.OwnerSeat || movement.Batch.OriginSkillId!=f.SkillId || movement.Batch.OriginSkillInstanceId!=f.SkillInstanceId ||
                movement.Batch.Movements is not [var actual] || actual.From!=CardLocation.DrawPile || actual.To!=CardLocation.Hand(recipient) || actual.Reason!=CardMoveReasons.Draw ||
                !_cardMovements.Any(m=>m==actual))continue;
            return f;
        }
        return null;
    }
    private bool HasDamageAppearanceDrawObserver(long windowId)
    {
        if(DamageAppearanceDrawObserverRoot(windowId) is not { } root)return false;
        var index=_resolutionStack.Count-1;
        while(index>=1 && _resolutionStack[index].Id!=root.Id &&
            (DamageFrameRidesOn(_resolutionStack[index],_resolutionStack[index-1]) ||
             DamageObserverRidesOn(_resolutionStack[index],_resolutionStack[index-1]) ||
             PileEquipmentFrameRidesOn(_resolutionStack[index],_resolutionStack[index-1]) ||
             RandomEquipmentFrameRidesOn(_resolutionStack[index],_resolutionStack[index-1])))index--;
        return _resolutionStack[index].Id==root.Id;
    }

    private bool IsDamageAppearanceDrawProgramDying()
    {
        if (ActiveDamageTrigger is not { } trigger ||
            ActiveDying is not { ResumesProgramSkill: true } dying ||
            DamageAppearanceDrawObserverRoot(trigger.Id) is not { } root ||
            _resolutionStack.OfType<DyingFrame>().SingleOrDefault(frame => frame.Id == dying.FrameId) is not { } dyingFrame ||
            dyingFrame.ParentFrameId != dying.ParentFrameId)
            return false;
        var parentIndex = -1;
        for (var index = 0; index < _resolutionStack.Count; index++)
            if (_resolutionStack[index] is ProgramSkillFrame parent && parent.Id == dying.ParentFrameId)
                parentIndex = index;
        if (parentIndex < 0 || parentIndex + 1 >= _resolutionStack.Count ||
            _resolutionStack[parentIndex + 1].Id != dyingFrame.Id)
            return false;
        var top = _resolutionStack.LastOrDefault();
        if (!(top is DyingFrame topDying && topDying.Id == dyingFrame.Id ||
              top is ProgramSkillFrame { WindowContext: { } response } && response.ParentFrameId == dyingFrame.Id &&
              response.Window is SkillProgramTriggerWindow.DyingResponse or SkillProgramTriggerWindow.SelfDyingResponse ||
              IsAvailableBoundPeachRescueRide(parentIndex + 1, dyingFrame)))
            return false;
        while (parentIndex >= 1 &&
               (DamageFrameRidesOn(_resolutionStack[parentIndex], _resolutionStack[parentIndex - 1]) ||
                DamageObserverRidesOn(_resolutionStack[parentIndex], _resolutionStack[parentIndex - 1]) ||
                PileEquipmentFrameRidesOn(_resolutionStack[parentIndex], _resolutionStack[parentIndex - 1]) ||
                RandomEquipmentFrameRidesOn(_resolutionStack[parentIndex], _resolutionStack[parentIndex - 1])))
            parentIndex--;
        return _resolutionStack[parentIndex].Id == root.Id;
    }

    private sealed partial class ProgramSkillHost:IDamageColorDuelProgramHost
    {
        public SkillProgramStepOutcome DrawByDamageCardColor(ProgramSkillFrame frame)=>engine.BeginDamageAppearanceDraw(frame);
        public SkillProgramStepOutcome UseSelectedActorDuel(ProgramSkillFrame frame)=>engine.BeginSelectedActorDuel(frame);
    }
}
