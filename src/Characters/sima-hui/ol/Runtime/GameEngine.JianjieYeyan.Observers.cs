using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IsSimaHuiYeyanSelectedPayment(ProgramSkillFrame f)=>f.SimaHui is{Stage:"yeyan-paid",OriginalCards.Count:4}r && ValidSimaHui(f) && f.InstructionIndex==2 && SimaHuiPausedEffect(f) is{Op:SkillProgramEffectOp.DiscardSelected,Amount:4} &&
        f.SelectedCardPayment is{InstructionIndex:2,Operation:SkillProgramEffectOp.DiscardSelected,MovementCommitted:true}p && p.RecipientSeat==f.OwnerSeat && p.CardIds.SequenceEqual(r.OriginalCards) &&
        r.OriginalCards.All(id=>_cardMovements.Count(m=>m.Sequence>r.MovementBefore && m.CardId==id && m.From==CardLocation.Hand(f.OwnerSeat) && m.To==CardLocation.DiscardPile && m.Reason.Value==$"skill-program.{f.SkillId}.{SkillProgramEffectOp.DiscardSelected}")==1);
    private bool SimaHuiYeyanFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        var r=f.SimaHui!;
        if(child is CardsMovedTriggerWindowFrame moved)return IsSimaHuiYeyanSelectedPayment(f) && f.SelectedCardPayment!.ActiveChildFrameId==moved.Id && moved.Batch.ParentFrameId==f.Id && moved.Batch.AwaitingProgramFrameId==f.Id &&
            moved.Batch.OriginOwnerSeat==f.OwnerSeat && moved.Batch.OriginSkillId==f.SkillId && moved.Batch.OriginSkillInstanceId==f.SkillInstanceId && moved.Batch.Movements.Select(m=>m.CardId).SequenceEqual(r.OriginalCards) &&
            moved.Batch.Movements.All(m=>_cardMovements.Contains(m) && m.Sequence>r.MovementBefore && m.From==CardLocation.Hand(f.OwnerSeat) && m.To==CardLocation.DiscardPile && m.Reason.Value==$"skill-program.{f.SkillId}.{SkillProgramEffectOp.DiscardSelected}");
        if(child is ProgramLifecycleTriggerWindowFrame skills)return skills.Window==SkillProgramTriggerWindow.SkillsChanged && skills.ResumeProgramFrameId==f.Id && skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram && f.InstructionIndex is >=1 and <=4;
        if(child is DyingFrame dying)return f.InstructionIndex==3 && SimaHuiPausedEffect(f) is{Op:SkillProgramEffectOp.LoseHpUnclamped,Amount:3} && f.SelectedCardPaymentResult is{Completed:true,InstructionIndex:2} &&
            dying.ParentFrameId==f.Id && dying.Continuation==DyingContinuationKind.ProgramSkill && dying.VictimSeat==f.OwnerSeat && dying.KillerSeat is null &&
            CompleteProgramEventHistory().OfType<ProgramSkillHpLostEvent>().Count(e=>e.FrameId==f.Id && e.SkillId==f.SkillId && e.TargetSeat==f.OwnerSeat && e.Amount==3 && e.RemainingHp<=0)==1;
        if(child is HpChangedTriggerWindowFrame hp)return f.InstructionIndex==3 && SimaHuiPausedEffect(f) is{Op:SkillProgramEffectOp.LoseHpUnclamped,Amount:3} && hp.ResumeFrameId==f.Id && hp.Continuation==PostEventContinuation.Program &&
            hp.Change.ParentFrameId==f.Id && hp.Change.Kind==HpChangeKind.Loss && hp.Change.TargetSeat==f.OwnerSeat && hp.Change.Amount==3;
        if(f.AttackAttempt is not{}attack || SimaHuiPausedEffect(f) is not{Op:SkillProgramEffectOp.DamageParticipants,DamageNature:DamageNature.Fire} || attack.SourceSeat!=f.OwnerSeat || !f.SelectedTargetSeats.Contains(attack.TargetSeat))return false;
        if(child is DamageFrame damage)return damage.ParentFrameId==f.Id && damage.SourceSeat==attack.SourceSeat && damage.TargetSeat==attack.TargetSeat && damage.Amount==attack.DamageAmount && damage.Nature==DamageNature.Fire &&
            CompleteProgramEventHistory().OfType<DamageRequestedEvent>().Count(e=>e.ResolutionId==damage.Id && e.SourceSeat==damage.SourceSeat && e.TargetSeat==damage.TargetSeat && e.Amount==damage.Amount && e.Nature==damage.Nature && !e.SourceLess)==1;
        return child is BeforeDamageProgramWindowFrame before && (before.ContinuationAttackResolutionId??before.ParentFrameId)==f.Id && before.SourceSeat==attack.SourceSeat && before.TargetSeat==attack.TargetSeat && before.Amount==attack.DamageAmount && before.Nature==DamageNature.Fire;
    }
}
