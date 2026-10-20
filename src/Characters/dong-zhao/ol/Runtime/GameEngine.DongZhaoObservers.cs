namespace CardGame.Core;
public sealed partial class GameEngine
{
    private ProgramSkillFrame? DongZhaoObserverRoot() => FindProgramObserverRoot(static (engine,root,child)=>engine.DongZhaoFirstChild(root,child),
        static (engine,index)=>engine.HasMovementObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1560)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1670)]
    private bool HasDongZhaoDamageObserver(long id) => DongZhaoObserverRoot() is {} root && _resolutionStack.Any(f=>f.Id==id && f is DamageTriggerWindowFrame) &&
        _resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 10, group: 220)]
    private bool IsDongZhaoDying() => ActiveDying is {} dying && DongZhaoObserverRoot() is {} root &&
        _resolutionStack.FindIndex(f=>f.Id==dying.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 700)]
    private bool AllowsDongZhaoNestedDamage(ProgramSkillFrame observer,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if(sourceLess || observer.AttackAttempt is not null || observer.InstructionIndex<1 || _resolutionStack.LastOrDefault()?.Id!=observer.Id ||
            DongZhaoObserverRoot() is not {} root || root.Id==observer.Id || observer.WindowContext?.Window is not
            (SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.DiscardPileReceived or
             SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged))return false;
        var e=GetPausedPrivateOfferEffect(observer);
        return e is { Op:SkillProgramEffectOp.Damage } && e.Amount==amount && e.ActorReference==source && e.DamageNature==nature &&
            target==(e.TargetReference is {} reference ? ResolveProgramParticipant(observer,reference):ResolveProgramEffectTarget(observer,e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 430)]
    private bool TryAdvanceDongZhaoSubtree()=>_pendingDecision is null && DongZhaoObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    private bool ValidIssuedZaowang(ProgramSkillFrame f)
    {
        if(f.ZaowangPaidTargetSeat is not {} seat || !IsValidPlayerSeat(seat) || f.SelectedTargetSeats is not [var selected] || selected!=seat ||
            f.TriggerId is not null || f.InstructionIndex is <1 or >3)return false;
        var program=_contentRegistry!.GetSkill(f.SkillId).Program;
        var a=program is null?null:ProgramInstructionResolver.Default.FindActivation(program,f.ActivationId);
        if(a?.Effects is not [{ Op:SkillProgramEffectOp.ZaowangResolve },{ Op:SkillProgramEffectOp.Recover,Amount:1 },{ Op:SkillProgramEffectOp.Draw,Amount:3 }])return false;
        var facts=CompleteProgramEventHistory().OfType<ZaowangGrantedEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        return facts is [var e] && e.OwnerSeat==f.OwnerSeat && e.TargetSeat==seat && e.SkillId==f.SkillId && e.SkillInstanceId==f.SkillInstanceId && e.GameplayHash==f.GameplayHash;
    }
    private bool ZaowangFirstChild(ProgramSkillFrame root,ResolutionFrame child)
    {
        if(!ValidIssuedZaowang(root) || root.InstructionIndex!=2 || root.ZaowangPaidTargetSeat is not {} target)return false;
        var grant=CompleteProgramEventHistory().OfType<ZaowangGrantedEvent>().Single(e=>e.FrameId==root.Id);
        if(child is HpChangedTriggerWindowFrame hp)
            return hp.Continuation==PostEventContinuation.Program && hp.ResumeFrameId==root.Id && hp.Change.ParentFrameId==root.Id &&
                hp.Change.Kind==HpChangeKind.Recovery && hp.Change.SourceSeat==root.OwnerSeat && hp.Change.TargetSeat==target && hp.Change.Amount==1 &&
                hp.Change.HpBefore==grant.HpBeforeRecovery && hp.Change.HpAfter==grant.HpBeforeRecovery+1 &&
                CompleteProgramEventHistory().OfType<RecoveryAppliedEvent>().Any(e=>e.SourceSeat==root.OwnerSeat && e.TargetSeat==target && e.Amount==1 && e.RemainingHp==hp.Change.HpAfter);
        if(child is RecoveryReplacementFrame recovery && TypedRecoveryPausedEffect(root) is {} effect && effect.CompiledInstruction is RecoverProgramInstruction instruction)
            return recovery.Attempt.Amount==1 && recovery.Attempt.TargetSeat==target &&
                DirectProgramRecoveryReturnMatches(root,effect,instruction,recovery);
        return false;
    }
    private void RequireDongZhao(ProgramSkillFrame f)
    {
        if(f.ZaowangPaidTargetSeat is not null && !ValidIssuedZaowang(f) || f.XianluePending is {} r &&
            (r.SkillId.Length>0 ? !ValidIssuedXianlue(f) : r.Stage!=XianlueStage.Record || f.TriggerId is null ||
                f.WindowContext?.Window!=SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || GetProgramTrigger(f).Effects is not [{ Op:SkillProgramEffectOp.XianlueRecord }]))
            throw new InvalidOperationException("Dong Zhao lost its native source, exact boundary or once-paid invoice.");
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 440)]
    private void AssertRegisteredDongZhao(ProgramSkillFrame frame)
    {
        if (frame.XianluePending is not null || frame.ZaowangPaidTargetSeat is not null) RequireDongZhao(frame);
    }
}
