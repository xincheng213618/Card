namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasBaoXinCapability=>_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveBaoXinMutao)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveBaoXinYimou);
    private static CardConversionSource BaoXinSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    private DamageTriggerWindowFrame CaptureBaoXinYimouWindow(DamageTriggerWindowFrame frame,IDamageAttempt attack)
    {
        if(!HasBaoXinCapability||frame.TriggerWindow!=SkillProgramTriggerWindow.AfterDamageApplied)return frame;var found=new List<BaoXinYimouQualification>();
        foreach(var candidate in frame.Candidates.Where(c=>c.ProgramId=="ol:yimou"&&c.ProgramTriggerId=="near-damage"))
        {
            var source=new CardConversionSource(candidate.ProgramId,candidate.ProgramTriggerId,candidate.OwnerSeat,candidate.SkillInstanceId);var distance=GetCombatDistance(candidate.OwnerSeat,frame.TargetSeat);
            found.Add(new(source,candidate.GameplayHash,frame.Id,frame.ParentFrameId,attack.ResolutionId,frame.TargetSeat,attack.DamageAmount,distance,attack.IsSourceLess?null:attack.SourceSeat,candidate.OccurrenceIndex));
        }
        return found.Count==0?frame:frame with{BaoXinYimouQualifications=found};
    }
    private ProgramSkillWindowContext CreateBaoXinYimouContext(DamageTriggerWindowFrame frame,ProgramTriggerCandidate candidate,ProgramSkillWindowContext ordinary)
    {
        if(candidate.SkillId!="ol:yimou"||candidate.BindingId!="near-damage")return ordinary;
        var q=frame.BaoXinYimouQualifications?.SingleOrDefault(q=>q.Source==new CardConversionSource(candidate.SkillId,candidate.BindingId,candidate.OwnerSeat,candidate.SkillInstanceId)&&q.GameplayHash==candidate.GameplayHash&&q.OccurrenceIndex==candidate.OccurrenceIndex)??throw new InvalidOperationException("Yimou lost its collection-time native distance.");
        return ordinary with{Facts=ordinary.Facts! with{OwnerEventTargetDistance=q.Distance}};
    }
    private BaoXinYimouQualification? BaoXinQualification(ProgramTriggerCandidate candidate,ProgramSkillWindowContext context)
    {
        if(!HasBaoXinCapability||candidate.SkillId!="ol:yimou"||candidate.BindingId!="near-damage"||context.Window!=SkillProgramTriggerWindow.AfterDamageApplied||context.OwnerSeat!=candidate.OwnerSeat||context.OccurrenceIndex!=candidate.OccurrenceIndex||context.Amount<=0||_resolutionStack.OfType<DamageTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId)is not{}window||window.ParentFrameId!=context.DamageFrameId||window.TriggerWindow!=context.Window||window.CandidateIndex<0||window.CandidateIndex>=window.Candidates.Count||window.Candidates[window.CandidateIndex].ToProgramCandidate()!=candidate||_resolutionStack.OfType<DamageFrame>().SingleOrDefault(d=>d.Id==window.ParentFrameId)is not{}damage||damage.TargetSeat!=context.TargetSeat||damage.Amount!=context.Amount)return null;
        var q=window.BaoXinYimouQualifications?.SingleOrDefault(q=>q.Source==new CardConversionSource(candidate.SkillId,candidate.BindingId,candidate.OwnerSeat,candidate.SkillInstanceId)&&q.GameplayHash==candidate.GameplayHash&&q.OccurrenceIndex==candidate.OccurrenceIndex);
        return q is not null&&q.WindowId==window.Id&&q.DamageFrameId==damage.Id&&q.AttackId==damage.ParentFrameId&&q.VictimSeat==window.TargetSeat&&q.VictimSeat==context.TargetSeat&&q.SourceSeat==context.SourceSeat&&q.Amount==damage.Amount&&q.Distance>=0&&q.Distance<=1&&context.Facts?.OwnerEventTargetDistance==q.Distance?q:null;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27450)]private bool CanOfferBaoXin(ProgramTriggerCandidate c,SkillProgramTrigger trigger,ProgramSkillWindowContext context)=>!trigger.Effects.Any(e=>e.Op==SkillProgramEffectOp.ResolveBaoXinYimou)||BaoXinQualification(c,context)is{}q&&_players[c.OwnerSeat].IsAlive&&_players[q.VictimSeat].IsAlive;
    private bool IsIssuedBaoXinMutaoPile(PublicPersistentPileSource source)=>_resolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.BaoXin is{Paid:true,Pile:{}pile}r&&pile==source&&r.Op==SkillProgramEffectOp.ResolveBaoXinMutao&&r.Source==BaoXinSource(f)&&r.GameplayHash==f.GameplayHash&&f.InstructionIndex==1&&f.SkillId=="ol:mutao"&&r.Original.Count>0&&_publicPersistentPiles.GetValueOrDefault((source.OwnerSeat,source.SkillId,source.SkillInstanceId))==source&&ProgramEventHistory<BaoXinStartedEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.Holder==r.Holder)==1&&r.Original.Select((c,i)=>(c,i)).All(x=>ProgramEventHistory<BaoXinOriginalEntityEvent>().Count(e=>e.FrameId==f.Id&&e.Index==x.i&&e.CardId==x.c.CardId&&e.Kind==x.c.Kind&&e.EffectiveKind==x.c.EffectiveKind&&e.From==x.c.From)==1));
}
