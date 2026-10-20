using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private int MouXiahouDunQingjianCount(int seat) => PublicPileSources(seat).Where(p => p.SkillId == MouXiahouDunQingjian).Sum(p => PublicPileCards(p).Count);
    private int MouXiahouDunQingjianCapacity(int seat) => _players[seat].HasHpValue ? Math.Max(1, _players[seat].Hp - 1) : 0;
    private PublicPersistentPileSource? MouXiahouDunPile(int seat, string instance) => PublicPileSources(seat).SingleOrDefault(p => p.SkillId == MouXiahouDunQingjian && p.SkillInstanceId == instance);
    private void RecordMouXiahouDunDiscardOrigins(CardMovementBatchContext batch)
    {
        if (!HasMouXiahouDunCapability) return;
        // Capture while the native Use/response producer is still alive.
        // Discard-trigger candidates may be consumed after that producer retires.
        foreach (var move in batch.Movements.Where(m => m.To == CardLocation.DiscardPile && m.From != m.To))
            AdvanceEventRulesAndQueueFact(new MouXiahouDunMobileDiscardOriginEvent(batch.Id, move.Sequence, move.CardId, IsNativeTrueUseDiscardMovement(batch, move)));
    }
    private int[] MatchingMouXiahouDunDiscardIndexes(CardMovementBatchContext batch) => batch.Movements.Select((m, i) => (m, i)).Where(x =>
        x.m.To == CardLocation.DiscardPile && x.m.From != x.m.To && ProgramEventHistory<MouXiahouDunMobileDiscardOriginEvent>().Count(e =>
            e == new MouXiahouDunMobileDiscardOriginEvent(batch.Id, x.m.Sequence, x.m.CardId, false)) == 1).Select(x => x.i).ToArray();
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 31200, SkillProgramEffectOp.CollectMouXiahouDunQingjian, SkillProgramEffectOp.DistributeMouXiahouDunQingjian)]
    private bool CanOfferMouXiahouDunQingjian(ProgramTriggerLegalityContext gate)
    {
        if (gate.Context.Window == SkillProgramTriggerWindow.PlayEnding) return MouXiahouDunPile(gate.Owner.Seat, gate.Candidate.SkillInstanceId) is { } pile && PublicPileCards(pile).Count > 0;
        return gate.Context is { Window: SkillProgramTriggerWindow.DiscardPileReceived, MovementBatch: { } batch, MovementIndex: { } index } && index >= 0 && index < batch.Movements.Count &&
            MatchingMouXiahouDunDiscardIndexes(batch).Contains(index) && _cardZones.GetLocation(batch.Movements[index].CardId) == CardLocation.DiscardPile &&
            MouXiahouDunQingjianCount(gate.Owner.Seat) < MouXiahouDunQingjianCapacity(gate.Owner.Seat);
    }
}
