namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,27330)]
    private void AssertSunHao(ProgramSkillFrame f)
    {
        if(f.SunHaoCanshi is not{} r)return;
        if(r.Source!=SunHaoSource(f)||r.GameplayHash!=f.GameplayHash||f.InstructionIndex!=1||f.TriggerId!="issued-turn-use-discard"||GetPausedPrivateOfferEffect(f)?.Op!=SkillProgramEffectOp.ResolveSunHaoCanshiUseDiscard||
            f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||f.WindowContext is not{} c||c.ParentFrameId!=r.WindowId||c.CardUse?.CardActionId!=r.ActionId||r.ActualTurn!=_turnNumber||r.TurnOwner!=_currentSeat||
            !Enum.IsDefined(r.Stage)||ProgramEventHistory<SunHaoCanshiStartedEvent>().Where(e=>e.FrameId==f.Id).ToArray() is not[var start]||
            start!=new SunHaoCanshiStartedEvent(f.Id,r.Source,r.GameplayHash,r.GrantId,r.ActualTurn,r.TurnOwner,r.WindowId,r.ActionId,r.UseId,r.OriginalTargets.Count,r.GrantIds.Count)||
            ProgramEventHistory<SunHaoCanshiGrantedEvent>().SingleOrDefault(e=>e.FrameId==r.GrantId) is not{} grant||grant.Source.SkillId!=f.SkillId||grant.Source.SkillInstanceId!=f.SkillInstanceId||grant.Source.OwnerSeat!=f.OwnerSeat||
            grant.GameplayHash!=f.GameplayHash||grant.ActualTurn!=r.ActualTurn||grant.TurnOwner!=r.TurnOwner||grant.X<1||
            r.GrantIds.Count==0||r.GrantIds[0]!=r.GrantId||r.GrantIds.Distinct().Count()!=r.GrantIds.Count||r.Cursor<0||r.Cursor>r.GrantIds.Count||
            !ProgramEventHistory<SunHaoCanshiFrozenGrantEvent>().Where(e=>e.FrameId==f.Id).OrderBy(e=>e.Index).Select(e=>e.GrantId).SequenceEqual(r.GrantIds)||
            r.GrantIds.Any(id=>!ProgramEventHistory<SunHaoCanshiGrantedEvent>().Any(g=>g.FrameId==id&&g.Source==grant.Source&&g.GameplayHash==r.GameplayHash&&g.ActualTurn==r.ActualTurn&&g.TurnOwner==r.TurnOwner))||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w=>w.Id==r.WindowId) is not{} w||w.Action.ActionId!=r.ActionId||w.Action.ActorSeat!=f.OwnerSeat||!SunHaoCanshiUseKind(w.Action)||
            w.CandidateIndex<0||w.CandidateIndex>=w.Candidates.Count||!MountObserverCandidateMatches(f,ToSharedCandidate(w.Candidates[w.CandidateIndex]))||
            !ProgramEventHistory<SunHaoCanshiOriginalTargetEvent>().Where(e=>e.FrameId==f.Id).OrderBy(e=>e.Index).Select(e=>e.TargetSeat).SequenceEqual(r.OriginalTargets))
            throw new InvalidOperationException("Canshi lost its exact original grant, actual turn, committed native action or scalar target invoices.");
        if(r.UseId is{} id ? LifecycleCardUse(id) is not{} use||use.Id!=w.ParentFrameId||use.Action?.ActionId!=r.ActionId||use.SourceSeat!=f.OwnerSeat||!r.OriginalTargets.SequenceEqual(use.TargetSeats) :
            r.OriginalTargets.Count!=0||w.CompletedResponseReturn is not{Kind:ProgramCompletedResponseKind.Nullification,IsCommitted:true}||!IsCompletedResponseUse(w.Action,ProgramCardContinuation.NullificationResponse))
            throw new InvalidOperationException("Canshi changed the whole native Use or its genuine committed Nullification return.");
        if(r.Stage==SunHaoCanshiStage.SelectDiscard ? r.Payment is not null||r.WoodenOxGrain.Count!=0||f.PendingMovementContinuation is not null :
            r.Payment is not{} paid||f.PendingMovementContinuation is not{BeforeCount:0,CoverageResultBind:null} pending||pending.SubjectSeat!=f.OwnerSeat||
            paid.FrameId!=f.Id||paid.Index!=r.Cursor||paid.From.OwnerSeat!=f.OwnerSeat||paid.From.Zone is not(CardZoneKind.Hand or CardZoneKind.Equipment)||
            paid.To!=(paid.Kind==CardKind.GeneralWeapon&&paid.From.Zone==CardZoneKind.Equipment?CardLocation.OutsideGame:CardLocation.DiscardPile)||
            paid.Before<0||paid.After<=paid.Before||paid.After>_movementSequence||ProgramEventHistory<SunHaoCanshiDiscardedEvent>().Count(e=>e==paid)!=1||
            _cardMovements.Count(m=>m.Sequence>paid.Before&&m.Sequence<=paid.After&&m.CardId==paid.CardId&&m.From==paid.From&&m.To==paid.To&&m.Reason.Value==SunHaoCanshiDiscardReason)!=1||
            _cardMovements.Where(m=>m.Sequence>paid.Before&&m.Sequence<=paid.After).Any(m=>!IsSunHaoPaymentMovement(f,m)))
            throw new InvalidOperationException("Canshi lost its one real legal HE discard, exact General Weapon removal or Wooden Ox spill.");
        var payments=ProgramEventHistory<SunHaoCanshiDiscardedEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        var skipped=ProgramEventHistory<SunHaoCanshiSkippedEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        if(payments.Any(e=>e.Index<0||e.Index>=r.GrantIds.Count)||skipped.Any(e=>e.Index<0||e.Index>=r.Cursor||e.GrantId!=r.GrantIds[e.Index])||
            payments.Select(e=>e.Index).Distinct().Count()!=payments.Length||skipped.Select(e=>e.Index).Distinct().Count()!=skipped.Length||
            Enumerable.Range(0,r.Cursor).Any(i=>payments.Count(e=>e.Index==i)+skipped.Count(e=>e.Index==i)!=1)||
            payments.Any(e=>e.Index>r.Cursor||e.Index==r.Cursor&&r.Stage!=SunHaoCanshiStage.DiscardChildren))
            throw new InvalidOperationException("Canshi cannot skip, duplicate or re-pay an independent draw-phase obligation.");
        if(_resolutionStack.LastOrDefault()?.Id==f.Id&&_pendingDecision is{} prompt&&r.Stage==SunHaoCanshiStage.SelectDiscard&&
            (prompt.PlayerSeat!=f.OwnerSeat||!prompt.IsPrivate||prompt.SkillPrompt?.SkillId!=f.SkillId||!AssistedChoicesEqual(prompt.Choices,SunHaoChoices(f))))
            throw new InvalidOperationException("Canshi changed its current private discard menu.");
    }
    private bool IsSunHaoPaymentMovement(ProgramSkillFrame f,CardMovementRecord m)=>f.SunHaoCanshi is{Payment:{} p} r&&_cardMovements.Contains(m)&&m.Sequence>p.Before&&m.Sequence<=p.After&&
        (m.CardId==p.CardId&&m.From==p.From&&m.To==p.To&&m.Reason.Value==SunHaoCanshiDiscardReason||
            p.Kind==CardKind.WoodenOx&&p.From==CardLocation.Equipment(f.OwnerSeat)&&r.WoodenOxGrain.Contains(m.CardId)&&m.From==CardLocation.WoodenOxGrain(f.OwnerSeat)&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.WoodenOxGrainDiscard);
    private bool IsSunHaoMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)=>f.SunHaoCanshi is{Stage:SunHaoCanshiStage.DiscardChildren,Payment:not null}&&
        e?.Op==SkillProgramEffectOp.ResolveSunHaoCanshiUseDiscard&&p==f.PendingMovementContinuation&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null;
    private bool SunHaoLion(ProgramSkillFrame f)=>f.SunHaoCanshi is{Payment:{Kind:CardKind.SilverLion,From.Zone:CardZoneKind.Equipment}};
    private bool IsSunHaoRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)=>SunHaoLion(f)&&RecoveryReplacementFrameRidesOn(recovery,f)&&
        recovery.Return.Continuation==PostEventContinuation.AwaitedProgramMovement&&recovery.Attempt.SourceSeat==f.OwnerSeat&&recovery.Attempt.TargetSeat==f.OwnerSeat&&recovery.Attempt.Amount==1&&
        recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&recovery.Attempt.Completion.MoveReason?.Value==SunHaoCanshiDiscardReason;
    private bool SunHaoFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.SunHaoCanshi is not{Stage:SunHaoCanshiStage.DiscardChildren} r)return false;AssertSunHao(f);
        if(child is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.SkillsChanged} skills)return skills.ResumeProgramFrameId==f.Id&&skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
        if(child is RecoveryReplacementFrame recovery)return IsSunHaoRecoveryParent(f,recovery);
        if(child is HpChangedTriggerWindowFrame hp)return SunHaoLion(f)&&hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&hp.Continuation==PostEventContinuation.AwaitedProgramMovement&&
            hp.Change.Kind==HpChangeKind.Recovery&&hp.Change.SourceSeat==f.OwnerSeat&&hp.Change.TargetSeat==f.OwnerSeat&&hp.Change.Amount==1;
        return child is CardsMovedTriggerWindowFrame moved&&ProgramMovementReturnMatches(f,moved)&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>IsSunHaoPaymentMovement(f,m));
    }
    private ProgramSkillFrame? SunHaoObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.SunHaoFirstChild(f,c),static(e,i)=>e.HasObserverSuffix(i,
        static(x,n)=>x.IsMovementObserverEdge(n)||x.IsYanPuDyingExitEdge(n)||RecoveryReplacementFrameRidesOn(x._resolutionStack[n],x._resolutionStack[n-1]),
        static(x,n,d)=>x.IsSuspendedOrPaidRescueObserverSuffix(n,d)));
    private bool HasSunHaoDamageObserver(long id)=>SunHaoObserverRoot() is{} root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is DamageTriggerWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool HasSunHaoBeforeDamageObserver(long id)=>SunHaoObserverRoot() is{} root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool IsSunHaoDying()=>ActiveDying is{} dying&&SunHaoObserverRoot() is{} root&&_resolutionStack.FindIndex(f=>f.Id==dying.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool AllowsSunHaoNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if(sourceLess||f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||SunHaoObserverRoot() is not{} root||root.Id==f.Id)return false;
        var e=GetPausedPrivateOfferEffect(f);return e is{Op:SkillProgramEffectOp.Damage}&&e.Amount==amount&&e.ActorReference==source&&e.DamageNature==nature&&
            target==(e.TargetReference is{} reference?ResolveProgramParticipant(f,reference):ResolveProgramEffectTarget(f,e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,27330)] private bool AdvanceSunHaoObserver()=>_pendingDecision is null&&SunHaoObserverRoot() is not null&&TryAdvanceObserverSubtree(true);
}
