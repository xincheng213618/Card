namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,27020)]
    private void AssertHanFu(ProgramSkillFrame f)
    {
        if(f.HanFu is not { } r) return;
        if(r.Source!=HanFuSource(f) || r.GameplayHash!=f.GameplayHash || f.InstructionIndex!=1 || r.InstructionIndex!=f.InstructionIndex || r.ActualTurn!=_turnNumber ||
            !HanFuComposition.Handles(r.Op) || GetPausedPrivateOfferEffect(f)?.Op!=r.Op || !IsValidPlayerSeat(r.TargetSeat) || !Enum.IsDefined(r.Stage) || r.Movements is not { Count:>0 } ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash!=r.GameplayHash || ProgramEventHistory<HanFuStartedEvent>().Where(e=>e.FrameId==f.Id).ToArray() is not [var started] ||
            started.Source!=r.Source || started.GameplayHash!=r.GameplayHash || started.Op!=r.Op || started.ActualTurn!=r.ActualTurn || started.PhaseInstance!=r.PhaseInstance ||
            started.TargetSeat!=r.TargetSeat || started.ParentId!=r.ParentId || started.DamageFrameId!=r.DamageFrameId || started.DamageAmount!=r.DamageAmount)
            throw new InvalidOperationException("HanFu lost its original exact source, first instruction, actual turn or single start invoice.");
        var shuzi=r.Op==SkillProgramEffectOp.ResolveShuziExchange;
        if(shuzi)
        {
            if(f.TriggerId is not null || f.WindowContext is not null || r.ParentId!=0 || r.DamageFrameId is not null || r.DamageAmount!=0 || r.PhaseInstance!=_cardUseDebitPhaseInstanceId ||
                f.SelectedTargetSeats is not [var target] || target!=r.TargetSeat || r.Gifts is not { Count:2 } gifts || gifts.Select(m=>m.CardId).Distinct().Count()!=2 ||
                !gifts.Select(m=>m.CardId).SequenceEqual(f.SelectedCardIds) || gifts.Any(m=>m.From.OwnerSeat!=f.OwnerSeat || m.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) ||
                r.Stage is HanFuStage.DrawChildren or HanFuStage.DiscardChoice or HanFuStage.DiscardChildren || r.HistoryCutoff is not null || r.DamagedSeats is not null || r.DiscardCandidates is not null || r.RequiredDiscard is not null || r.SelectedDiscard is not null ||
                (r.Stage is HanFuStage.GiftChildren or HanFuStage.ReturnChoice ? r.Returned is not null || r.Movements.Count!=1 : r.Returned is null || r.Movements.Count!=(r.Stage==HanFuStage.BoardChildren?3:2)))
                throw new InvalidOperationException("Shuzi lost its accepted two-HE activation, original recipient or ordered exact gift/return cursor.");
            if(r.Stage==HanFuStage.BoardChildren ? r.Board is not { } board || board.From.OwnerSeat==r.TargetSeat || board.From.Zone is not (CardZoneKind.Equipment or CardZoneKind.Judgment) : r.Board is not null)
                throw new InvalidOperationException("Shuzi board movement must originate in a different person's actual public field.");
            if(r.Stage is HanFuStage.Benefit or HanFuStage.BoardChoice or HanFuStage.BoardChildren or HanFuStage.DamageIssued && !r.Gifts!.Any(m=>m.Name==r.Returned!.Name))
                throw new InvalidOperationException("Shuzi cannot grant a bonus without matching the two exact names frozen at their original payments.");
        }
        else
        {
            if(f.SelectedCardIds.Count!=0 || f.SelectedTargetSeats.Count!=0 || r.Gifts is not null || r.Returned is not null || r.Board is not null || r.TargetSeat!=f.OwnerSeat || r.DamageAmount<1 ||
                f.WindowContext is not { Window:SkillProgramTriggerWindow.AfterDamageApplied } context || context.ParentFrameId!=r.ParentId || context.DamageFrameId!=r.DamageFrameId || context.TargetSeat!=f.OwnerSeat || context.Amount!=r.DamageAmount ||
                _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w=>w.Id==r.ParentId) is not { } window || window.ParentFrameId!=r.DamageFrameId || window.TargetSeat!=f.OwnerSeat || window.TriggerWindow!=context.Window ||
                window.CandidateIndex<0 || window.CandidateIndex>=window.Candidates.Count || !MountObserverCandidateMatches(f,window.Candidates[window.CandidateIndex].ToProgramCandidate()) ||
                !_resolutionStack.OfType<DamageFrame>().Any(d=>d.Id==r.DamageFrameId && d.TargetSeat==f.OwnerSeat && d.Amount==r.DamageAmount) ||
                r.Stage is not (HanFuStage.DrawChildren or HanFuStage.DiscardChoice or HanFuStage.DiscardChildren) || r.Movements.Count!=(r.Stage==HanFuStage.DiscardChildren?2:1))
                throw new InvalidOperationException("Kuangshou lost its actual whole-damage parent or one native Draw3/discard cursor.");
            if(r.Stage==HanFuStage.DrawChildren)
            { if(r.HistoryCutoff is not null || r.DamagedSeats is not null || r.RequiredDiscard is not null || r.DiscardCandidates is not null || r.SelectedDiscard is not null) throw new InvalidOperationException("Kuangshou cannot freeze its later discard quota before the Draw3 children return."); }
            else if(r.HistoryCutoff is not { } cutoff || cutoff<1 || cutoff>CompleteProgramEventHistory().Count() || r.DamagedSeats is not { } seats || !seats.SequenceEqual(KuangshouDamagedSeats(cutoff)) ||
                r.DiscardCandidates is not { } candidates || candidates.Select(m=>m.CardId).Distinct().Count()!=candidates.Count || candidates.Any(m=>m.From.OwnerSeat!=f.OwnerSeat || m.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) ||
                r.RequiredDiscard!=Math.Min(seats.Count,candidates.Count) || r.SelectedDiscard is not { } selected || selected.Distinct().Count()!=selected.Count || selected.Any(id=>!candidates.Any(m=>m.CardId==id)) || selected.Count>r.RequiredDiscard ||
                r.Stage==HanFuStage.DiscardChildren && selected.Count!=r.RequiredDiscard || ProgramEventHistory<KuangshouQuotaFrozenEvent>().Where(e=>e.FrameId==f.Id).ToArray() is not [var quota] ||
                quota.HistoryCutoff!=cutoff || quota.DamagedSeatCount!=seats.Count || quota.RequiredDiscard!=r.RequiredDiscard)
                throw new InvalidOperationException("Kuangshou lost its exact post-draw history cutoff, distinct actually damaged people, private eligible HE set or frozen discard requirement.");
        }
        var children=r.Stage is HanFuStage.GiftChildren or HanFuStage.ReturnChildren or HanFuStage.BoardChildren or HanFuStage.DrawChildren or HanFuStage.DiscardChildren;
        if(children ? f.PendingMovementContinuation is not { BeforeCount:0,CoverageResultBind:null } p || p.SubjectSeat!=f.OwnerSeat : f.PendingMovementContinuation is not null)
            throw new InvalidOperationException("HanFu's paid movement cursor has lost its exact owning continuation.");
        var issued=ProgramEventHistory<HanFuMovementIssuedEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        if(issued.Length!=r.Movements.Count) throw new InvalidOperationException("HanFu's movement cursor differs from its immutable issued invoices.");
        for(var i=0;i<r.Movements.Count;i++)
        {
            var m=r.Movements[i]; var reason=shuzi?i switch {0=>ShuziGiftReason,1=>ShuziReturnReason,_=>ShuziBoardReason}:i==0?KuangshouDrawReason:KuangshouDiscardReason;
            if(m.Index!=i || issued[i].Movement!=m || m.Reason!=reason || m.Before<0 || m.After<m.Before || m.After>_movementSequence || m.ActualCount<0 || i>0 && m.Before<r.Movements[i-1].After)
                throw new InvalidOperationException("HanFu lost an ordered actual physical-movement invoice.");
            var moves=_cardMovements.Where(move=>move.Sequence>m.Before && move.Sequence<=m.After && move.Reason.Value==reason).ToArray();
            if(moves.Length!=m.ActualCount) throw new InvalidOperationException("HanFu cannot fabricate, omit or repay a physical primary movement.");
            if(reason==KuangshouDrawReason)
            { if(m.ActualCount>3 || moves.Any(move=>move.From!=CardLocation.DrawPile || move.To!=CardLocation.Hand(f.OwnerSeat))) throw new InvalidOperationException("Kuangshou requires at most three genuine native draw entities."); continue; }
            IReadOnlyList<HanFuMaterial> materials=reason==ShuziGiftReason?r.Gifts!:reason==ShuziReturnReason?[r.Returned!]:reason==ShuziBoardReason?[r.Board!]:r.SelectedDiscard!.Select(id=>r.DiscardCandidates!.Single(c=>c.CardId==id)).ToArray();
            var paid=ProgramEventHistory<HanFuMaterialPaidEvent>().Where(e=>e.FrameId==f.Id && e.Stage==m.Stage).ToArray();
            if(paid.Length!=materials.Count) throw new InvalidOperationException("HanFu lost its exact frozen payment materials.");
            for(var j=0;j<materials.Count;j++)
            {
                var material=materials[j]; var fact=paid[j]; var to=reason==ShuziGiftReason?CardLocation.Hand(r.TargetSeat):reason==ShuziReturnReason?CardLocation.Hand(f.OwnerSeat):reason==KuangshouDiscardReason?(material.From.Zone==CardZoneKind.Equipment&&material.IsGeneralWeapon?CardLocation.OutsideGame:CardLocation.DiscardPile):
                    material.From.Zone==CardZoneKind.Judgment?CardLocation.Judgment(r.TargetSeat):CardLocation.Equipment(r.TargetSeat);
                to=NormalizeDiouCarrierDestination(GetAdvancedCard(material.CardId),material.From,to);
                if(fact.Index!=j || fact.Material!=material || fact.To!=to || (fact.MovementSequence is { } sequence ?
                    moves.Count(move=>move.Sequence==sequence && move.CardId==material.CardId && move.CardKind==material.PrintedKind && move.From==material.From && move.To==to)!=1 :
                    reason is not (ShuziGiftReason or ShuziReturnReason) || r.TargetSeat!=f.OwnerSeat || material.From!=CardLocation.Hand(f.OwnerSeat) || to!=material.From ||
                    _cardMovements.LastOrDefault(move=>move.Sequence<=m.Before && move.CardId==material.CardId)?.To!=material.From))
                    throw new InvalidOperationException("HanFu lost a real original entity transfer, or fabricated a same-Hand loss/gain.");
            }
        }
        var damageFacts=ProgramEventHistory<ShuziDamageIssuedEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        if(r.Stage==HanFuStage.DamageIssued ? damageFacts is not [var d] || d.SourceSeat!=f.OwnerSeat || d.TargetSeat!=r.TargetSeat || d.Amount!=1 : damageFacts.Length!=0)
            throw new InvalidOperationException("Shuzi damage must be issued once for the exact matched original recipient.");
    }
    private bool HasPaidShuziInitialSelection(ProgramSkillFrame f)
    { if(f.HanFu is not { Op:SkillProgramEffectOp.ResolveShuziExchange,InstructionIndex:1,Movements.Count:>0 }) return false; AssertHanFu(f); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27020)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27020)]
    private bool ContinuePaidHanFu(ProgramSkillFrame f) { if(f.HanFu is not { Movements.Count:>0 }) return false; AssertHanFu(f); return true; }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 220)]
    private bool IsHanFuMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)
    { if(f.HanFu is not { Movements.Count:>0 } r || e?.Op!=r.Op || p!=f.PendingMovementContinuation) return false; AssertHanFu(f); return true; }
    private bool HanFuLionProof(ProgramSkillFrame f,int seat,CardMoveReason? reason)
    { var m=f.HanFu!.Movements![^1]; return reason is not null && _cardMovements.Any(move=>move.Sequence>m.Before && move.Sequence<=m.After && move.CardKind==CardKind.SilverLion && move.From==CardLocation.Equipment(seat) && move.Reason==reason); }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 760)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 700)]
    private bool IsHanFuRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)
    {
        if(f.HanFu is not { Movements.Count:>0 } || f.PendingMovementContinuation is null) return false; AssertHanFu(f);
        return RecoveryReplacementFrameRidesOn(recovery,f) && recovery.Return.Continuation==PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.Amount==1 &&
            recovery.Attempt.SourceSeat==recovery.Attempt.TargetSeat && recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion && HanFuLionProof(f,recovery.Attempt.TargetSeat,recovery.Attempt.Completion.MoveReason);
    }
    private bool HanFuFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.HanFu is not { } r) return false; AssertHanFu(f);
        if(r.Stage==HanFuStage.DamageIssued) return f.AttackAttempt is { SourceLess:false } attack && attack.SourceSeat==f.OwnerSeat && attack.TargetSeat==r.TargetSeat && f.AttackReturn is not null && DyingSuitsStructuralEdge(f,child);
        if(f.PendingMovementContinuation is null) return false; var m=r.Movements![^1];
        if(child is CardsMovedTriggerWindowFrame moved) return ProgramMovementReturnMatches(f,moved) && moved.Batch.OriginOwnerSeat==f.OwnerSeat && moved.Batch.OriginSkillId==f.SkillId && moved.Batch.OriginSkillInstanceId==f.SkillInstanceId &&
            moved.Batch.Movements.Count>0 && moved.Batch.Movements.All(move=>_cardMovements.Contains(move) && move.Sequence>m.Before && move.Sequence<=m.After);
        if(child is RecoveryReplacementFrame replacement) return IsHanFuRecoveryParent(f,replacement);
        if(child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId==f.Id && hp.ResumeFrameId==f.Id && hp.Continuation==PostEventContinuation.AwaitedProgramMovement && hp.Change.Kind==HpChangeKind.Recovery &&
            hp.Change.Amount==1 && hp.Change.SourceSeat==hp.Change.TargetSeat && hp.Change.TargetSeat is var seat && _cardMovements.Any(move=>move.Sequence>m.Before && move.Sequence<=m.After && move.CardKind==CardKind.SilverLion && move.From==CardLocation.Equipment(seat));
        return child is ProgramLifecycleTriggerWindowFrame { Window:SkillProgramTriggerWindow.SkillsChanged } skills && skills.ResumeProgramFrameId==f.Id && skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? HanFuObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.HanFuFirstChild(f,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 570)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 680)]
    private bool HasHanFuDamageObserver(long id)=>HanFuObserverRoot() is { } root && _resolutionStack.Any(f=>f.Id==id && f is DamageTriggerWindowFrame) && _resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 710)]
    private bool HasHanFuBeforeDamageObserver(long id)=>HanFuObserverRoot() is { } root && _resolutionStack.Any(f=>f.Id==id && f is BeforeDamageProgramWindowFrame) && _resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 530)]
    private bool IsHanFuDying()=>ActiveDying is { } dying && HanFuObserverRoot() is { } root && _resolutionStack.FindIndex(f=>f.Id==dying.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1170)]
    private bool AllowsHanFuNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if(sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id!=f.Id) return false;
        if(f.HanFu is { Stage:HanFuStage.DamageIssued } r) { AssertHanFu(f); return target==r.TargetSeat && amount==1 && source is null && nature is null; }
        if(HanFuObserverRoot() is not { } root || root.Id==f.Id) return false; var e=GetPausedPrivateOfferEffect(f);
        return e is { Op:SkillProgramEffectOp.Damage } && e.Amount==amount && e.ActorReference==source && e.DamageNature==nature && target==(e.TargetReference is { } reference?ResolveProgramParticipant(f,reference):ResolveProgramEffectTarget(f,e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,27020)]
    private bool AdvanceHanFuSubtree()=>_pendingDecision is null && HanFuObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
