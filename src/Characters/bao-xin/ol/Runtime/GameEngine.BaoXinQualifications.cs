namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasBaoXinCapability=>_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveBaoXinMutao)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveBaoXinYimou);
    private static CardConversionSource BaoXinSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    private BaoXinYimouQualification? BaoXinQualification(ProgramTriggerCandidate candidate,ProgramSkillWindowContext context)
    {
        if(!HasBaoXinCapability||candidate.SkillId!="ol:yimou"||candidate.BindingId!="near-damage"||context.Window!=SkillProgramTriggerWindow.AfterDamageApplied||context.OwnerSeat!=candidate.OwnerSeat||context.OccurrenceIndex!=candidate.OccurrenceIndex||context.Amount<=0||_resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId)is not{}window||window.ParentFrameId!=context.DamageFrameId||window.TriggerWindow!=context.Window||window.CandidateIndex<0||window.CandidateIndex>=window.Candidates.Count||window.Candidates[window.CandidateIndex].ToProgramCandidate()!=candidate||_resolutionStack.OfType<DamageFrame>().SingleOrDefault(d=>d.Id==window.ParentFrameId)is not{}damage||damage.TargetSeat!=context.TargetSeat||damage.Amount!=context.Amount)return null;
        var q=window.BaoXinYimouQualifications?.SingleOrDefault(q=>q.Source==new CardConversionSource(candidate.SkillId,candidate.BindingId,candidate.OwnerSeat,candidate.SkillInstanceId)&&q.GameplayHash==candidate.GameplayHash&&q.OccurrenceIndex==candidate.OccurrenceIndex);
        return q is not null&&q.WindowId==window.Id&&q.DamageFrameId==damage.Id&&q.AttackId==damage.ParentFrameId&&q.VictimSeat==window.TargetSeat&&q.VictimSeat==context.TargetSeat&&q.SourceSeat==context.SourceSeat&&q.Amount==damage.Amount&&q.Distance>=0&&q.Distance<=1&&context.Facts?.OwnerEventTargetDistance==q.Distance?q:null;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27450)]private bool CanOfferBaoXin(ProgramTriggerCandidate c,SkillProgramTrigger trigger,ProgramSkillWindowContext context)=>!trigger.Effects.Any(e=>e.Op==SkillProgramEffectOp.ResolveBaoXinYimou)||BaoXinQualification(c,context)is{}q&&_players[c.OwnerSeat].IsAlive&&_players[q.VictimSeat].IsAlive;
}
