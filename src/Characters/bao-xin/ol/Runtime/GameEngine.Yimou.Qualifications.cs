namespace CardGame.Core;
public sealed partial class GameEngine
{
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
}
