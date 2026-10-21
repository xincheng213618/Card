namespace CardGame.Core;
public sealed partial class GameEngine
{
    private void RecordMouLuSuOwnedTransferProducer(CardMovementRecord m,int recipient,long producer)
    {
        if (!HasMouLuSuAnniversary || m.From.OwnerSeat is not { } owner || owner == recipient || m.To != CardLocation.Processing || !IsValidPlayerSeat(recipient) || producer <= 0 || !_cardMovements.Contains(m)) return;
        AdvanceEventRulesAndQueueFact(new MouLuSuAnniversaryTransferInvoiceEvent(m.Sequence,"native-transfer:" + producer,owner,recipient));
    }
    private void CaptureMouLuSuNativeTransferProducer(CardMovementRecord m)
    {
        if (!HasMouLuSuAnniversary || m.From.OwnerSeat is not { } owner || m.To != CardLocation.Processing) return;
        if (m.Reason == CardMoveReasons.Snatch && _resolutionStack.LastOrDefault() is CardUseFrame { Action: { Type: CardActionType.Use, EffectiveKind: CardKind.Snatch } a } use && a.ActorSeat == use.SourceSeat && a.ActorSeat != owner && a.EffectiveDesignatedTargetSeats.Contains(owner)) RecordMouLuSuOwnedTransferProducer(m,a.ActorSeat,use.Id);
        if (_resolutionStack.LastOrDefault() is ProgramSkillFrame p && GetPausedPrivateOfferEffect(p) is { Op: SkillProgramEffectOp.SelectAndMoveOwnedCard, Destination: SkillProgramCardDestination.SelectedTargetHand, CardOwnerRef: { } holder, TargetReference: { } recipient } effect && effect.Zones.Contains(m.From.Zone) && ResolveProgramParticipant(p,holder) == owner && m.Reason.Value == $"skill-program.{p.SkillId}.{effect.Op}") RecordMouLuSuOwnedTransferProducer(m,ResolveProgramParticipant(p,recipient),p.Id);
    }
    private int? MouLuSuTransferSource(CardMovementRecord m)
    {
        if (m.To.Zone != CardZoneKind.Hand || m.To.OwnerSeat is not { } recipient || m.From == m.To) return null;
        if (m.From.OwnerSeat is { } direct) return direct != recipient ? direct : null;
        if (m.From != CardLocation.Processing) return null;
        var entry = _cardMovements.LastOrDefault(e => e.CardId == m.CardId && e.Sequence < m.Sequence);
        return entry is { From.OwnerSeat: { } owner } && entry.To == CardLocation.Processing && owner != recipient && ProgramEventHistory<MouLuSuAnniversaryTransferInvoiceEvent>().Any(e => e.EntrySequence == entry.Sequence && e.Owner == owner && e.Recipient == recipient) ? owner : null;
    }
    private static bool IsMouLuSuGainTrigger(SkillProgramTrigger t) => t.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveMouLuSuMengmou);
    private (int Peer,int Index)[] MouLuSuGainPeers(CardMovementBatchContext batch,int owner) => batch.Movements.Select((m,i) => (Move:m,Index:i,Source:MouLuSuTransferSource(m)))
        .Where(x => x.Source is not null && (x.Source == owner || x.Move.To.OwnerSeat == owner))
        .Select(x => (Peer:x.Source == owner ? x.Move.To.OwnerSeat!.Value : x.Source!.Value,x.Index)).Where(x => x.Peer != owner && _players[x.Peer].IsAlive).DistinctBy(x => x.Peer).ToArray();
    private IEnumerable<ProgramTriggerCandidate> CollectMouLuSuGainCandidates(CardMovementBatchContext batch)
    {
        if (!HasMouLuSuAnniversary) return [];
        return _players.Where(p => p.IsAlive).SelectMany(owner => CollectProgramTriggerCandidates(owner,SkillProgramTriggerWindow.CardsGained).Where(c => IsMouLuSuGainTrigger(GetProgramTrigger(c)))
            .SelectMany(c => MouLuSuGainPeers(batch,owner.Seat).Select(p => c with { OccurrenceIndex = p.Index }))).ToArray();
    }
    private int? MouLuSuGainPeer(ProgramTriggerCandidate c,ProgramSkillWindowContext w)
    {
        if (w.Window != SkillProgramTriggerWindow.CardsGained || w.OwnerSeat != c.OwnerSeat || w.TargetSeat != c.OwnerSeat || w.MovementBatch is not { } batch || w.ParentFrameId != batch.Id || w.MovementIndex != c.OccurrenceIndex || w.OccurrenceIndex != c.OccurrenceIndex || _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(p => p.Id == batch.Id) is not { } p || p.CandidateIndex < 0 || p.CandidateIndex >= p.Candidates.Count || p.Candidates[p.CandidateIndex] != c || !p.Batch.Movements.SequenceEqual(batch.Movements)) return null;
        var peer = MouLuSuGainPeers(batch,c.OwnerSeat).Where(x => x.Index == c.OccurrenceIndex).ToArray(); return peer is [var one] && w.SourceSeat == one.Peer ? one.Peer : null;
    }
    private ProgramSkillWindowContext CreateMouLuSuGainContext(CardsMovedTriggerWindowFrame p,ProgramTriggerCandidate c)
    {
        var peer = MouLuSuGainPeers(p.Batch,c.OwnerSeat).Single(x => x.Index == c.OccurrenceIndex);
        return new(SkillProgramTriggerWindow.CardsGained,p.Id,c.OwnerSeat,SourceSeat:peer.Peer,TargetSeat:c.OwnerSeat,OccurrenceIndex:c.OccurrenceIndex,Facts:CaptureProgramTriggerFacts(_players[c.OwnerSeat]),MovementBatch:p.Batch,MovementIndex:peer.Index);
    }
}
