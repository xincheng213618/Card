namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,27210)]
    private void AssertWangGuan(ProgramSkillFrame f)
    {
        if(f.WangGuan is not{}r)return;
        if(r.Source!=WangGuanSource(f)||r.GameplayHash!=f.GameplayHash||f.InstructionIndex!=1||!WangGuanComposition.Handles(r.Op)||
            GetPausedPrivateOfferEffect(f)?.Op!=r.Op||r.ActualTurn!=_turnNumber||r.TurnOwner!=_turnProgression.OwnerSeat||
            f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||!Enum.IsDefined(r.Stage)||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash!=r.GameplayHash||f.WindowContext is not{}context||context.ParentFrameId!=r.ParentId||
            ProgramEventHistory<WangGuanStartedEvent>().Where(e=>e.FrameId==f.Id).ToArray()is not[var started]||
            started!=new WangGuanStartedEvent(f.Id,r.Source,r.GameplayHash,r.Op,r.ActualTurn,r.TurnOwner,r.ParentId,r.NativeId,r.FrozenHp))
            throw new InvalidOperationException("Wang Guan lost his exact native first instruction, source, turn or immutable start invoice.");
        var miuyan=r.Op==SkillProgramEffectOp.ResolveMiuyanCompletedUse;
        if(miuyan)
        {
            var c=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority);
            if(r.Use is not{PreviousState:SkillPolarity.Yang}invoice||MiuyanCandidateUse(c,context)is not{}use||use.MiuyanUse!=invoice||r.NativeId!=invoice.UseId||r.FrozenHp!=0||
                r.Stage!=WangGuanStage.MiuyanGainChildren||r.HolderSeat is not null||!use.CausedDamage||
                !ProgramEventHistory<CompletedUndamagedUseDamageRecordedEvent>().Any(e=>e.CardUseFrameId==use.Id&&e.Amount>0)||
                r.Entities.Select(e=>e.CardId).Distinct().Count()!=r.Entities.Count||
                r.Entities.Any(e=>!IsValidPlayerSeat(e.HolderSeat)||e.HolderSeat==f.OwnerSeat||e.Slot<0||e.Entry<0||
                    !ProgramEventHistory<MiuyanPhaseHandRevealedEvent>().Any(v=>v.Phase==invoice.Phase&&v.CardId==e.CardId))||
                ProgramEventHistory<MiuyanClaimEntityFrozenEvent>().Where(e=>e.FrameId==f.Id).ToArray()is not{}frozen||frozen.Length!=r.Entities.Count||
                frozen.Where((e,i)=>e.Index!=i||e.Entity!=r.Entities[i]).Any())
                throw new InvalidOperationException("Yang Miuyan lost its genuine completed damaging Use, phase history or frozen current hand entities.");
            var paid=ProgramEventHistory<MiuyanEntityObtainedEvent>().Where(e=>e.FrameId==f.Id).ToArray();
            if(paid.Length!=r.Entities.Count||paid.Where((e,i)=>e.Index!=i||e.Entity!=r.Entities[i]||
                !_cardMovements.Any(m=>m.Sequence==e.MovementSequence&&m.Sequence>r.Before&&m.Sequence<=r.After&&m.CardId==e.Entity.CardId&&m.CardKind==e.Entity.PrintedKind&&
                    m.From==CardLocation.Hand(e.Entity.HolderSeat)&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==MiuyanGainReason)).Any())
                throw new InvalidOperationException("Miuyan cannot replace or repay any originally frozen hand entity.");
        }
        else
        {
            if(r.Use is not null||r.FrozenHp<0||context.Window!=SkillProgramTriggerWindow.AfterDamageApplied||context.Amount<1||context.TargetSeat!=f.OwnerSeat||context.DamageFrameId!=r.NativeId||
                _resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w=>w.Id==r.ParentId)is not{}window||window.ParentFrameId!=r.NativeId||window.TargetSeat!=f.OwnerSeat||
                window.TriggerWindow!=context.Window||window.CandidateIndex<0||window.CandidateIndex>=window.Candidates.Count||
                !MountObserverCandidateMatches(f,window.Candidates[window.CandidateIndex].ToProgramCandidate())||
                !_resolutionStack.OfType<DamageFrame>().Any(d=>d.Id==r.NativeId&&d.TargetSeat==f.OwnerSeat&&d.Amount==context.Amount)||
                r.Stage==WangGuanStage.MiuyanGainChildren||r.ActualCount>r.FrozenHp)
                throw new InvalidOperationException("Shilu lost its true PerDamage parent, frozen execution-time HP or exact native draw.");
            if(r.Stage==WangGuanStage.ShiluCard)
            {
                var holders=ProgramEventHistory<ShiluHolderChosenEvent>().Where(e=>e.FrameId==f.Id).ToArray();
                var candidates=ProgramEventHistory<ShiluCandidateFrozenEvent>().Where(e=>e.FrameId==f.Id).ToArray();
                if(r.HolderSeat is not{}holder||!IsValidPlayerSeat(holder)||holder==f.OwnerSeat||holders is not[var chosen]||chosen.HolderSeat!=holder||
                    r.Entities.Count==0||candidates.Length!=r.Entities.Count||r.Entities.Select(e=>e.CardId).Distinct().Count()!=r.Entities.Count||
                    candidates.Where((e,i)=>e.Index!=i||e.Entity!=r.Entities[i]||e.Entity.Slot!=i||e.Entity.HolderSeat!=holder||e.Entity.Entry<0||
                        CurrentHandEntry(holder,e.Entity.CardId)!=e.Entity.Entry).Any()||
                    !GetHand(_players[holder]).Select(c=>c.Id).SequenceEqual(r.Entities.Select(e=>e.CardId)))
                    throw new InvalidOperationException("Shilu lost its exact post-draw holder or original opaque hand slots.");
            }
            else if(r.HolderSeat is not null||r.Entities.Count!=0||ProgramEventHistory<ShiluHolderChosenEvent>().Any(e=>e.FrameId==f.Id))
                throw new InvalidOperationException("Shilu cannot freeze a hand before all actual draw children return.");
        }
        var movement=ProgramEventHistory<WangGuanMovementIssuedEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        var paidStage=miuyan?WangGuanStage.MiuyanGainChildren:WangGuanStage.ShiluDrawChildren;
        if(movement is not[var issued]||issued.Stage!=paidStage||issued.Before!=r.Before||issued.After!=r.After||issued.Count!=r.ActualCount||
            r.Before<0||r.After<r.Before||r.After>_movementSequence||r.ActualCount<0||
            _cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.Reason.Value==(miuyan?MiuyanGainReason:ShiluDrawReason))!=r.ActualCount||
            miuyan&&r.ActualCount!=r.Entities.Count)
            throw new InvalidOperationException("Wang Guan lost his exact once-paid native movement interval.");
        var children=r.Stage is WangGuanStage.MiuyanGainChildren or WangGuanStage.ShiluDrawChildren;
        if(children?f.PendingMovementContinuation is not{BeforeCount:0,CoverageResultBind:null}p||p.SubjectSeat!=f.OwnerSeat:f.PendingMovementContinuation is not null)
            throw new InvalidOperationException("Wang Guan lost his owning typed movement continuation.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27210)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27210)]
    private bool ContinueIssuedWangGuan(ProgramSkillFrame f)
    {
        if(f.WangGuan is not null){AssertWangGuan(f);return true;}
        if(f.InstructionIndex is not(0 or 1)||f.SkillId!="ol:miuyan"||f.TriggerId is null||f.WindowContext is not{}context)return false;
        return CanRunIssuedMiuyanCandidate(new(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority),context);
    }
    private bool IsWangGuanMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)
    {
        if(f.WangGuan is not{Stage:WangGuanStage.MiuyanGainChildren or WangGuanStage.ShiluDrawChildren}r||e?.Op!=r.Op||p!=f.PendingMovementContinuation)return false;
        AssertWangGuan(f);return true;
    }
    private bool WangGuanFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.WangGuan is not{Stage:WangGuanStage.MiuyanGainChildren or WangGuanStage.ShiluDrawChildren}r)return false;AssertWangGuan(f);
        if(child is CardsMovedTriggerWindowFrame moved)return ProgramMovementReturnMatches(f,moved)&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&
            moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&m.Sequence>r.Before&&m.Sequence<=r.After&&
                (r.Op==SkillProgramEffectOp.ResolveShiluDrawRevealIdentity?
                    m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==ShiluDrawReason||m.Reason==CardMoveReasons.Reshuffle:
                    r.Entities.Any(e=>e.CardId==m.CardId&&m.From==CardLocation.Hand(e.HolderSeat))&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==MiuyanGainReason));
        return child is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.SkillsChanged}skills&&skills.ResumeProgramFrameId==f.Id&&skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? WangGuanObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.WangGuanFirstChild(f,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
    private bool HasWangGuanDamageObserver(long id)=>WangGuanObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is DamageTriggerWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool HasWangGuanBeforeDamageObserver(long id)=>WangGuanObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool IsWangGuanDying()=>ActiveDying is{}dying&&WangGuanObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==dying.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool AllowsWangGuanNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if(sourceLess||f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||WangGuanObserverRoot()is not{}root||root.Id==f.Id)return false;
        var e=GetPausedPrivateOfferEffect(f);return e is{Op:SkillProgramEffectOp.Damage}&&e.Amount==amount&&e.ActorReference==source&&e.DamageNature==nature&&target==(e.TargetReference is{}reference?ResolveProgramParticipant(f,reference):ResolveProgramEffectTarget(f,e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,27210)]
    private bool AdvanceWangGuanSubtree()=>_pendingDecision is null&&WangGuanObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
}
