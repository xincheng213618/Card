namespace CardGame.Core;
public sealed partial class GameEngine
{
    private static bool IsMouCaFengmin(SkillProgramTrigger t)=>t.Effects.Any(e=>e.Op==SkillProgramEffectOp.ResolveMouCaoAngFengmin);
    private IEnumerable<ProgramTriggerCandidate> CollectMouCaEquipmentCandidates(CardMovementBatchContext batch)
    {
        if(!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouCaoAngFengmin))yield break;
        var losses=batch.Movements.Select((m,i)=>(m,i)).Where(x=>x.m.From.Zone==CardZoneKind.Equipment&&x.m.From!=x.m.To&&x.m.From.OwnerSeat is{}seat&&IsValidPlayerSeat(seat)&&_cardMovements.Contains(x.m)).GroupBy(x=>x.m.From.OwnerSeat!.Value).Select(g=>g.First().i).ToArray();
        foreach(var owner in _players.Where(p=>p.IsAlive))
        foreach(var candidate in CollectProgramTriggerCandidates(owner,SkillProgramTriggerWindow.CardsMoved))
        {
            if(!IsMouCaFengmin(GetProgramTrigger(candidate))||MouCaoAng.Used(owner.Seat,candidate.SkillInstanceId))continue;
            foreach(var i in losses)yield return candidate with{OccurrenceIndex=i};
        }
    }
    private ProgramSkillWindowContext CreateMouCaEquipmentContext(CardsMovedTriggerWindowFrame frame,ProgramTriggerCandidate candidate)
    {
        var m=frame.Batch.Movements[candidate.OccurrenceIndex];var loser=m.From.OwnerSeat!.Value;
        if(m.From.Zone!=CardZoneKind.Equipment||m.From==m.To||!_cardMovements.Contains(m))throw new InvalidOperationException("Fengmin requires a real equipment departure.");
        return new(SkillProgramTriggerWindow.CardsMoved,frame.Id,candidate.OwnerSeat,SourceSeat:loser,TargetSeat:candidate.OwnerSeat,OccurrenceIndex:candidate.OccurrenceIndex,Facts:CaptureProgramTriggerFacts(_players[candidate.OwnerSeat]),MovementBatch:frame.Batch,MovementIndex:candidate.OccurrenceIndex);
    }
    private bool CanRunMouCaEquipment(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(c.SkillId!=MouCaoAngRuntime.Fengmin||!IsMouCaFengmin(GetProgramTrigger(c))||context.Window!=SkillProgramTriggerWindow.CardsMoved||context.OwnerSeat!=c.OwnerSeat||context.TargetSeat!=c.OwnerSeat||context.OccurrenceIndex!=c.OccurrenceIndex||context.MovementIndex!=c.OccurrenceIndex||context.MovementBatch is not{}batch||batch.Id!=context.ParentFrameId||c.OccurrenceIndex<0||c.OccurrenceIndex>=batch.Movements.Count||_resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(x=>x.Id==batch.Id)is not{}parent||parent.CandidateIndex<0||parent.CandidateIndex>=parent.Candidates.Count||parent.Candidates[parent.CandidateIndex]!=c||!parent.Batch.Movements.SequenceEqual(batch.Movements)||MouCaoAng.Used(c.OwnerSeat,c.SkillInstanceId))return false;
        var m=batch.Movements[c.OccurrenceIndex];return m.From.Zone==CardZoneKind.Equipment&&m.From.OwnerSeat==context.SourceSeat&&m.From!=m.To&&_cardMovements.Contains(m);
    }
}
