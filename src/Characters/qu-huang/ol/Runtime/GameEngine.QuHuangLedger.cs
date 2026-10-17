namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasQuHuangCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveQiejianLastHandLoss)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveNishouEquipmentLossChoice);
    private static CardConversionSource QuHuangSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    private bool IsQuHuangMovementTrigger(SkillProgramTrigger t)=>t.Window==SkillProgramTriggerWindow.CardsMoved&&t.Effects is[{ }e]&&e.Op is SkillProgramEffectOp.ResolveQiejianLastHandLoss or SkillProgramEffectOp.ResolveNishouEquipmentLossChoice;
    private bool QiejianBlocked(int owner,int target)=>ProgramEventHistory<QiejianRoundBlockedEvent>().Any(e=>e.OwnerSeat==owner&&e.TargetSeat==target&&e.RoundNumber==_roundNumber);
    private void CaptureQuHuangEquipmentBatch(CardMovementBatchContext batch,ProgramSkillFrame? originProgram)
    {
        if(!HasQuHuangCapability)return;
        foreach(var owner in batch.Movements.Where(m=>m.From.Zone==CardZoneKind.Equipment&&m.To==CardLocation.DiscardPile).Select(m=>m.From.OwnerSeat!.Value).Distinct())
        {
            if(!HasRuntimeSkill(_players[owner],"ol:nishou"))continue;
            var cards=batch.Movements.Where(m=>m.From==CardLocation.Equipment(owner)&&m.To==CardLocation.DiscardPile&&_cardMovements.Contains(m)&&EquipmentCatalog.IsEquipment(m.CardKind)).ToArray();if(cards.Length==0)continue;
            var selected=(originProgram?.SelectedCardPayment?.CardIds??originProgram?.SelectedCardIds??[]).Where(id=>cards.Any(m=>m.CardId==id)).ToArray();
            // A peer chooses its own matching-recast materials in receipt order;
            // the program's ordinary SelectedCardIds remain the initiating player's cost.
            if(originProgram is{MatchingRecast:{Stage:MatchingRecastStage.PeerCostChildren,Peer:{CostIssued:true,DrawIssued:false}peer}}paid&&
                peer.Seat==owner&&batch.ParentFrameId==paid.Id&&batch.AwaitingProgramFrameId==paid.Id&&
                batch.Movements.Count==peer.Materials.Count&&batch.Movements.Zip(peer.Materials).All(pair=>
                    pair.First.Sequence>peer.CostBefore&&pair.First.CardId==pair.Second.CardId&&pair.First.CardKind==pair.Second.PrintedKind&&
                    pair.First.From==pair.Second.From&&pair.First.To==pair.Second.Destination&&pair.First.Reason==CardMoveReasons.RecastDiscard))
                selected=peer.Materials.Select(m=>m.CardId).Where(id=>cards.Any(m=>m.CardId==id)).ToArray();
            long Enter(CardMovementRecord m)=>_cardMovements.Where(x=>x.CardId==m.CardId&&x.To==m.From&&x.Sequence<m.Sequence).Select(x=>(long)x.Sequence).DefaultIfEmpty().Max();
            var chosen=selected.Length>0?cards.Single(m=>m.CardId==selected[0]):cards.OrderBy(Enter).ThenBy(m=>m.Sequence).First();var card=GetAdvancedCard(chosen.CardId);
            AdvanceEventRulesAndQueueFact(new NishouEquipmentLostEvent(batch.Id,owner,card.Id,card.Kind,card.Suit,card.Rank,chosen.Sequence,batch.DiscardRecoveryPhase));
        }
    }
    private IEnumerable<ProgramTriggerCandidate> CollectQuHuangMovementCandidates(CardMovementBatchContext batch)
    {
        if(!HasQuHuangCapability)yield break;
        foreach(var owner in _players.Where(p=>p.IsAlive))foreach(var c in CollectProgramTriggerCandidates(owner,SkillProgramTriggerWindow.CardsMoved))
        {
            var t=GetProgramTrigger(c);if(!IsQuHuangMovementTrigger(t))continue;
            if(t.Effects[0].Op==SkillProgramEffectOp.ResolveQiejianLastHandLoss)
            {
                foreach(var count in batch.SourceCounts.Where(n=>n.Location.Zone==CardZoneKind.Hand&&n.CountBefore>0&&n.CountAfter==0&&n.Location.OwnerSeat is{}s&&_players[s].IsAlive))
                {var seat=count.Location.OwnerSeat!.Value;if(QiejianBlocked(owner.Seat,seat)||GetHand(_players[seat]).Count!=0)continue;var index=batch.Movements.ToList().FindIndex(m=>m.From==count.Location&&m.To!=m.From);if(index>=0)yield return c with{OccurrenceIndex=index};}
            }
            else if(ProgramEventHistory<NishouEquipmentLostEvent>().SingleOrDefault(e=>e.BatchId==batch.Id&&e.OwnerSeat==owner.Seat)is{}lost)
            {var index=batch.Movements.ToList().FindIndex(m=>m.Sequence==lost.Sequence&&m.CardId==lost.CardId);if(index>=0)yield return c with{OccurrenceIndex=index};}
        }
    }
    private ProgramSkillWindowContext CreateQuHuangMovementContext(CardsMovedTriggerWindowFrame w,ProgramTriggerCandidate c)
    {if(w.Contexts is{}contexts){var at=w.Candidates.ToList().FindIndex(item=>item==c);if(at<0)throw new InvalidOperationException("Qu Huang movement lost its frozen candidate context.");return contexts[at];}var m=w.Batch.Movements[c.OccurrenceIndex];var count=w.Batch.SourceCounts.Single(n=>n.Location==m.From);return new(SkillProgramTriggerWindow.CardsMoved,w.Id,c.OwnerSeat,SourceSeat:m.From.OwnerSeat,TargetSeat:m.From.OwnerSeat,OccurrenceIndex:c.OccurrenceIndex,Facts:CaptureCardsMovedTriggerFacts(_players[c.OwnerSeat],1,count,SkillProgramTriggerWindow.CardsMoved,w.Batch.MovementTiming),MovementBatch:w.Batch,MovementIndex:c.OccurrenceIndex);}
    private bool QuHuangMovementCandidateCurrent(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(context.Window!=SkillProgramTriggerWindow.CardsMoved||context.MovementBatch is not{}batch||context.MovementIndex is not{}index||index<0||index>=batch.Movements.Count||index!=c.OccurrenceIndex||_resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId)is not{}w||w.Batch.Id!=batch.Id||w.CandidateIndex<0||w.CandidateIndex>=w.Candidates.Count||w.Candidates[w.CandidateIndex]!=c||CreateQuHuangMovementContext(w,c)!=context)return false;
        var op=GetProgramTrigger(c).Effects.Single().Op;var m=batch.Movements[index];var source=m.From.OwnerSeat;
        return op==SkillProgramEffectOp.ResolveQiejianLastHandLoss?source is{}s&&_players[s].IsAlive&&GetHand(_players[s]).Count==0&&LostLastHandCard(batch,s)&&!QiejianBlocked(c.OwnerSeat,s):op==SkillProgramEffectOp.ResolveNishouEquipmentLossChoice&&source==c.OwnerSeat&&ProgramEventHistory<NishouEquipmentLostEvent>().Any(e=>e.BatchId==batch.Id&&e.OwnerSeat==c.OwnerSeat&&e.Sequence==m.Sequence&&e.CardId==m.CardId);
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27420)]
    private bool QualifyQuHuang(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext context)=>!t.Effects.Any(e=>QuHuangComposition.Handles(e.Op))||(t.Effects[0].Op==SkillProgramEffectOp.ResolveNishouIssuedCurrentPhaseSwap?IsIssuedNishouCandidate(c,context):QuHuangMovementCandidateCurrent(c,context));
    private bool NishouSwapAvailable(int owner,ActualDiscardRecoveryPhaseKey? phase)=>phase is{}p&&IsRecordedActualDiscardRecoveryPhase(p)&&p==CurrentActualDiscardRecoveryPhase()&&!ProgramEventHistory<ActualPhaseEndedEvent>().Any(e=>e.Phase==p)&&!ProgramEventHistory<NishouSwapDebtIssuedEvent>().Any(e=>e.Source.OwnerSeat==owner&&e.Phase.TurnNumber==p.TurnNumber);
    private IReadOnlyList<ActualPhaseEndItem> CollectNishouPhaseEndCandidates(ActualDiscardRecoveryPhaseKey phase)
    {
        if(!HasQuHuangCapability)return [];
        return Array.AsReadOnly(ProgramEventHistory<NishouSwapDebtIssuedEvent>().Where(e=>e.Phase==phase&&_players[e.Source.OwnerSeat].IsAlive&&!ProgramEventHistory<NishouSwapDebtConsumedEvent>().Any(d=>d.DebtId==e.DebtId)).OrderBy(e=>e.DebtId).Select(e=>{var t=_contentRegistry.GetSkill(e.Source.SkillId).Program!.Triggers.Single(t=>t.Id=="issued-current-phase-swap");return new ActualPhaseEndItem(new(e.Source.OwnerSeat,e.Source.SkillId,t.Id,e.Source.SkillInstanceId,e.GameplayHash,t.Priority),e.DebtId);}).ToArray());
    }
    private ActualPhaseEndFrame AppendNishouLatePhaseItems(ActualPhaseEndFrame frame)
    {if(!HasQuHuangCapability||!frame.ItemsCaptured)return frame;var extra=CollectNishouPhaseEndCandidates(frame.Phase).Where(i=>!frame.Items.Any(old=>old.EntitlementId==i.EntitlementId)).ToArray();return extra.Length==0?frame:frame with{Items=Array.AsReadOnly(frame.Items.Concat(extra).ToArray())};}
    private bool IsIssuedNishouCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(c.SkillId!="ol:nishou"||c.BindingId!="issued-current-phase-swap"||!_players[c.OwnerSeat].IsAlive||!IsExactActualPhaseEndCandidate(c,context)||context.ActualPhaseEnd is not{}due)return false;
        return ProgramEventHistory<NishouSwapDebtIssuedEvent>().Any(e=>e.DebtId==due.EntitlementId&&e.Source.OwnerSeat==c.OwnerSeat&&e.Source.SkillId==c.SkillId&&e.Source.SkillInstanceId==c.SkillInstanceId&&e.GameplayHash==c.GameplayHash&&e.Phase==due.Phase&&!ProgramEventHistory<NishouSwapDebtConsumedEvent>().Any(x=>x.DebtId==e.DebtId));
    }
}
