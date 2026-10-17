namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidCaiMao(ProgramSkillFrame f)
    {
        if(f.CaiMao is not{}r||!HasCaiMaoCapability||r.Source!=CaiMaoSource(f)||r.GameplayHash!=f.GameplayHash||f.InstructionIndex!=1||
            GetPausedPrivateOfferEffect(f)?.Op!=r.Op||r.ActualTurn!=_turnNumber||r.TurnOwner!=_turnProgression.OwnerSeat||r.FrozenHp<0||!Enum.IsDefined(r.Stage)||r.Op is not(SkillProgramEffectOp.ResolveZuolianWholeRevealExchange or SkillProgramEffectOp.ResolveJingzhouBeforeDamageToggles)||
            f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||f.PendingMovementContinuation is not null||r.Targets.Count>r.FrozenHp||r.Targets.Distinct().Count()!=r.Targets.Count||r.Targets.Any(s=>!IsValidPlayerSeat(s))||
            r.Before<0||r.After<r.Before||r.After>_movementSequence||r.Cursor<0||r.Cursor>r.Targets.Count||
            ProgramEventHistory<CaiMaoStartedEvent>().Where(e=>e.FrameId==f.Id).ToArray()is not[var start]||
            start!=new CaiMaoStartedEvent(f.Id,r.Source,r.GameplayHash,r.Op,r.ActualTurn,r.TurnOwner,r.FrozenHp,r.ParentId))return false;
        var targets=ProgramEventHistory<CaiMaoTargetChosenEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        if(targets.Length!=r.Targets.Count||targets.Where((e,i)=>e.Index!=i).Any()||!targets.Select(e=>e.TargetSeat).Order().SequenceEqual(r.Targets.Order()))return false;
        var committed=ProgramEventHistory<CaiMaoTargetsCommittedEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        if(r.Committed?committed is not[var commit]||commit.Count!=r.Targets.Count:committed.Length!=0)return false;
        if(r.Stage==CaiMaoStage.Targets)return !r.Committed&&r.Reveals.Count==0&&r.Exchanges.Count==0&&r.ExchangeAccepted is null&&r.Cursor==0&&
            (r.Op==SkillProgramEffectOp.ResolveZuolianWholeRevealExchange?r.ParentId==0&&f.WindowContext is null:f.WindowContext is{}initialContext&&CaiMaoDamageParentMatches(f,initialContext));
        if(!r.Committed)return false;
        if(r.Op==SkillProgramEffectOp.ResolveJingzhouBeforeDamageToggles)
        {
            if(f.SkillId!="ol:jingzhou"||f.TriggerId!="before-own-damage"||f.ActivationId!=f.TriggerId||f.WindowContext is not{}context||context.ParentFrameId!=r.ParentId||!CaiMaoDamageParentMatches(f,context)||
                r.Stage!=CaiMaoStage.ToggleChildren||r.Reveals.Count!=0||r.Exchanges.Count!=0||r.ExchangeAccepted is not null)return false;
            var toggles=ProgramEventHistory<CaiMaoToggleIssuedEvent>().Where(e=>e.FrameId==f.Id).ToArray();
            return toggles.Length==r.Cursor&&!toggles.Where((e,i)=>e.Index!=i||e.TargetSeat!=r.Targets[i]||e.Requested==e.Before).Any();
        }
        if(r.Op!=SkillProgramEffectOp.ResolveZuolianWholeRevealExchange||f.SkillId!="ol:zuolian"||f.ActivationId!="whole-exchange"||f.TriggerId is not null||f.WindowContext is not null||r.ParentId!=0||r.Cursor!=0||r.Stage is not(CaiMaoStage.Exchange or CaiMaoStage.ExchangeChildren)||
            r.Reveals.Count>r.Targets.Count||r.Reveals.Select(e=>e.HolderSeat).Distinct().Count()!=r.Reveals.Count||r.Reveals.Select(e=>e.CardId).Distinct().Count()!=r.Reveals.Count)return false;
        var reveals=ProgramEventHistory<CaiMaoHandRevealedEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        if(reveals.Length!=r.Reveals.Count||reveals.Where((e,i)=>e.Index!=i||e.Entity!=r.Reveals[i]).Any())return false;
        for(var i=0;i<r.Reveals.Count;i++)
        {
            var e=r.Reveals[i];
            if(!r.Targets.Contains(e.HolderSeat)||e.HandEntry<0||e.Bind!=CaiMaoRevealPrefix+i||
                f.CardSetBindings.SingleOrDefault(b=>b.Name==e.Bind)is not{Visibility:SkillProgramCardSetVisibility.Public}binding||!binding.CardIds.SequenceEqual([e.CardId])||!binding.SourceLocations.SequenceEqual([CardLocation.Hand(e.HolderSeat)])||
                ProgramEventHistory<ProgramCardsRevealedEvent>().Count(v=>v.FrameId==f.Id&&v.SkillId==f.SkillId&&v.BindingId==GetProgramBindingId(f)&&v.Bind==e.Bind&&v.Cards is[var card]&&card.Id==e.CardId&&card.Kind==e.PrintedKind)!=1)return false;
        }
        var choices=ProgramEventHistory<CaiMaoExchangeChosenEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        if(r.ExchangeAccepted is{}accepted?choices is not[var chosen]||chosen.Accepted!=accepted:choices.Length!=0)return false;
        if(r.Stage==CaiMaoStage.Exchange)return r.ExchangeAccepted is null&&r.Exchanges.Count==0;
        if(r.ExchangeAccepted!=true||r.Exchanges.Count>r.Reveals.Count||r.Exchanges.SelectMany(e=>new[]{e.OldCardId,e.NewCardId}).Distinct().Count()!=r.Exchanges.Count*2)return false;
        var frozen=ProgramEventHistory<CaiMaoExchangeFrozenEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        if(frozen.Length!=r.Exchanges.Count||frozen.Where((e,i)=>e.Index!=i||e.Entity!=r.Exchanges[i]).Any())return false;
        foreach(var e in r.Exchanges)
            if(e.Supply!=CardLocation.DrawPile&&e.Supply!=CardLocation.DiscardPile||e.NewKind is not(CardKind.FireSlash or CardKind.ThunderSlash)||
                !r.Reveals.Any(v=>v.HolderSeat==e.HolderSeat&&v.CardId==e.OldCardId&&v.PrintedKind==e.OldKind&&v.HandEntry==e.HandEntry)||
                _cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.CardId==e.OldCardId&&m.CardKind==e.OldKind&&m.From==CardLocation.Hand(e.HolderSeat)&&m.To==e.Supply&&m.Reason.Value==CaiMaoExchangeReason)!=1||
                _cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.CardId==e.NewCardId&&m.CardKind==e.NewKind&&m.From==e.Supply&&m.To==CardLocation.Hand(e.HolderSeat)&&m.Reason.Value==CaiMaoExchangeReason)!=1)return false;
        var pays=ProgramEventHistory<CaiMaoExchangePaidEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        return pays is[var pay]&&pay.Before==r.Before&&pay.After==r.After&&pay.Count==r.Exchanges.Count&&(pay.BatchId>0)==(r.Exchanges.Count>0)&&
            _cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.Reason.Value==CaiMaoExchangeReason)==r.Exchanges.Count*2;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,27250)]private void AssertCaiMao(ProgramSkillFrame f)
    {if(f.CaiMao is not null&&!ValidCaiMao(f))throw new InvalidOperationException("Cai Mao lost his actual HP budget, exact public original Hand entities, whole-roster decision or native before-damage return.");}
    private bool CaiMaoFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.CaiMao is not{Stage:CaiMaoStage.ExchangeChildren or CaiMaoStage.ToggleChildren}r||!ValidCaiMao(f))return false;
        if(child is CardsMovedTriggerWindowFrame moved)
        {
            var pay=ProgramEventHistory<CaiMaoExchangePaidEvent>().SingleOrDefault(e=>e.FrameId==f.Id);
            return r.Stage==CaiMaoStage.ExchangeChildren&&pay is not null&&moved.Batch.Id==pay.BatchId&&moved.Batch.ParentFrameId==f.Id&&moved.ResumeProgramFrameId==f.Id&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&
                moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After&&m.Reason.Value==CaiMaoExchangeReason&&r.Exchanges.Any(e=>
                    m.CardId==e.OldCardId&&m.CardKind==e.OldKind&&m.From==CardLocation.Hand(e.HolderSeat)&&m.To==e.Supply||m.CardId==e.NewCardId&&m.CardKind==e.NewKind&&m.From==e.Supply&&m.To==CardLocation.Hand(e.HolderSeat)));
        }
        if(child is ProgramLifecycleTriggerWindowFrame state&&state.Continuation==ProgramLifecycleContinuation.ResumeCharacterStateChange)
            return r.Stage==CaiMaoStage.ToggleChildren&&state.ResumeProgramFrameId==f.Id&&state.CharacterStateContinuation==CharacterStateContinuation.Program&&
                ProgramEventHistory<CharacterStateChangedEvent>().Any(e=>e.Change.Id==state.Id&&e.Change.ParentFrameId==f.Id&&e.Change.TargetSeat==state.OwnerSeat&&e.Change.Window==state.Window)&&
                ProgramEventHistory<CaiMaoToggleIssuedEvent>().Any(e=>e.FrameId==f.Id&&e.TargetSeat==state.OwnerSeat&&e.Actual&&!e.Before);
        return child is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.SkillsChanged}skills&&skills.ResumeProgramFrameId==f.Id&&skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? CaiMaoObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.CaiMaoFirstChild(f,c),static(e,i)=>e.HasObserverSuffix(i,static(x,n)=>x.IsMovementObserverEdge(n)||x.RecoveryReplacementFrameRidesOn(x._resolutionStack[n],x._resolutionStack[n-1]),static(x,n,d)=>x.IsSuspendedOrPaidRescueObserverSuffix(n,d)));
    private bool HasCaiMaoDamageObserver(long id)=>CaiMaoObserverRoot()is{}root&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool HasCaiMaoBeforeDamageObserver(long id)=>HasCaiMaoDamageObserver(id);
    private bool IsCaiMaoDying()=>ActiveDying is{}dying&&CaiMaoObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==dying.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool AllowsCaiMaoNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if(sourceLess||f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||CaiMaoObserverRoot()is not{}root||root.Id==f.Id)return false;
        var e=GetPausedPrivateOfferEffect(f);return e is{Op:SkillProgramEffectOp.Damage}&&e.Amount==amount&&e.ActorReference==source&&e.DamageNature==nature&&target==(e.TargetReference is{}p?ResolveProgramParticipant(f,p):ResolveProgramEffectTarget(f,e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,27250)]private bool AdvanceCaiMaoChildren()=>_pendingDecision is null&&CaiMaoObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
}
