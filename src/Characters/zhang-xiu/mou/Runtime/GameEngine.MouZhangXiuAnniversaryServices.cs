namespace CardGame.Core;
public sealed partial class GameEngine
{
    private MouZhangXiuAnniversaryRuntime MouZhangXiuAnniversaryRules => GetRuntimeModule<MouZhangXiuAnniversaryRuntime>();
    private bool HasMouZhangXiuAnniversary => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouZhangXiuHaoyi);
    private bool ValidMouZhangXiuAnniversary(ProgramSkillFrame f) => MouZhangXiuAnniversaryRules.Valid(f);
    private void ContinueMouZhangXiuAnniversary(long id) => MouZhangXiuAnniversaryRules.Continue(id);
    [ProgramCapability] private sealed class MouZhangXiuAnniversaryFacts(GameEngine e) : IMouZhangXiuAnniversaryFacts
    {
        public int Turn => e._turnNumber; public long Sequence => e._movementSequence; public bool Finished => e._winner != Winner.None;
        public bool Alive(int s) => e.IsValidPlayerSeat(s) && e._players[s].IsAlive; public bool Human(int s) => e._players[s].IsHuman; public string Name(int s) => e._players[s].Name;
        public bool Friendly(int owner, int other) => e._aiBrains[owner].ScoreProgramTarget(e.CreateSnapshot(owner), other, new(0, 0, 0, 1, 0, 0, false, false)) > 0;
        public int Keep(int owner, int card) => GetKeepValue(e.GetAdvancedCard(card), e._players[owner]);
        public SkillProgram Program(string id) => e._contentRegistry.GetSkill(id).Program!;
        public IEnumerable<T> History<T>() where T : IGameEvent => e.ProgramEventHistory<T>();
        public bool Largest(int seat) => Alive(seat) && e._players.Where(p => p.IsAlive).All(p => e.GetHand(p).Count <= e.GetHand(e._players[seat]).Count);
        public int PlayActor(ProgramSkillWindowContext w) => w.Window == SkillProgramTriggerWindow.PlayPhaseStarting && e._resolutionStack.SingleOrDefault(f => f.Id == w.ParentFrameId) is PlayPhaseStartingBoundaryFrame p && w.SourceSeat == p.OwnerSeat && w.TargetSeat == p.OwnerSeat && e._currentSeat == p.OwnerSeat && e._phase == TurnPhase.Play ? p.OwnerSeat : -1;
        public IReadOnlyList<int> Others(int owner) => e._players.Where(p => p.IsAlive && p.Seat != owner).Select(p => p.Seat).ToArray();
        public IReadOnlyList<MouZhangXiuAnniversaryCard> Entities(IEnumerable<int> ids) => Array.AsReadOnly(ids.Distinct().Select(id => { var c = e.GetAdvancedCard(id); return new MouZhangXiuAnniversaryCard(c.Id, c.Kind, e._cardZones.GetLocation(id), c.DisplayName); }).ToArray());
        public IReadOnlyList<MouZhangXiuAnniversaryCard> Cards(int actor, int holder, bool discard) => !Alive(actor) || !Alive(holder) ? [] : Array.AsReadOnly(new[] { CardZoneKind.Hand, CardZoneKind.Equipment }.SelectMany(z => e._cardZones.CardsAt(new(z, holder)).Where(c => !e.IsForeignEquipmentDiscardPrevented(actor, c, new(z, holder), discard ? OwnedCardMoveIntent.Discard : OwnedCardMoveIntent.Transfer) && (actor != holder || !e.IsSelfHandCategoryDiscardForbidden(holder, c, new(z, holder), discard ? OwnedCardMoveIntent.Discard : OwnedCardMoveIntent.Transfer))).Select(c => new MouZhangXiuAnniversaryCard(c.Id, c.Kind, new(z, holder), c.DisplayName))).ToArray());
        public bool Parent(ProgramSkillFrame f)
        {
            if (f.TriggerId != f.ActivationId || f.WindowContext is not { } w || w.OwnerSeat != f.OwnerSeat) return false;
            var n = e._resolutionStack.FindIndex(x => x.Id == f.Id); if (n < 1 || e._resolutionStack[n - 1].Id != w.ParentFrameId) return false;
            return e._resolutionStack[n - 1] switch {
                PlayPhaseStartingBoundaryFrame p => f.SkillId == MouZhangXiuAnniversaryRuntime.Fuxi && f.TriggerId == "other-largest-hand" && w.Window == SkillProgramTriggerWindow.PlayPhaseStarting && PlayActor(w) == p.OwnerSeat && p.OwnerSeat != f.OwnerSeat && p.ItemIndex >= 0 && p.ItemIndex < p.Items.Count && MountObserverCandidateMatches(f, p.Items[p.ItemIndex].Candidate),
                TurnEndingBoundaryFrame p => f.SkillId == MouZhangXiuAnniversaryRuntime.Haoyi && f.TriggerId == "turn-damage-card-claim" && w.Window == SkillProgramTriggerWindow.TurnEnding && p.OwnerSeat == f.OwnerSeat && p.ItemIndex >= 0 && p.ItemIndex < p.Items.Count && MountObserverCandidateMatches(f, p.Items[p.ItemIndex].Candidate), _ => false };
        }
    }
    [ProgramCapability] private sealed class MouZhangXiuAnniversaryNative(GameEngine e) : IMouZhangXiuAnniversaryNative
    {
        public void PublishState() => e.AdvanceRulesAndPublishState(); public void Finish(ProgramSkillFrame f) => e.FinishProgramSkill(f, true);
        public bool Drain(ProgramSkillFrame f) => e.TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || e.TryBeginAdvancedSkillsChanged(f.Id) || e.TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCardsMovedProgramWindow(f.Id);
        public bool CanSlash(ProgramSkillFrame f) => e.CanMouZhangXiuAnniversarySlash(f); public void Slash(ProgramSkillFrame f) => e.IssueMouZhangXiuAnniversarySlash(f);
        public MouZhangXiuAnniversaryCard RandomHand(ProgramSkillFrame f, IReadOnlyList<MouZhangXiuAnniversaryCard> cards)
        {
            if (!e.ValidMouZhangXiuAnniversary(f) || f.MouZhangXiuAnniversary is not { Stage: MouZhangXiuAnniversaryStage.DiscardCard } r || cards.Count == 0 || cards.Any(c => c.From != CardLocation.Hand(r.Target))) throw new InvalidOperationException("Fuxi requires the exact opposing anonymous-Hand selection.");
            var before = e._random.State; var card = cards[e._random.Next(cards.Count)]; e.AdvanceEventRulesAndQueueFact(new MouZhangXiuAnniversaryHandChosenEvent(f.Id, r.Target, card.Id, before, e._random.State)); return card;
        }
        public void Move(ProgramSkillFrame f)
        {
            if (!e.ValidMouZhangXiuAnniversary(f) || f.MouZhangXiuAnniversary is not { Cards.Count: > 0 } r || r.Stage is not (MouZhangXiuAnniversaryStage.GiveChildren or MouZhangXiuAnniversaryStage.DiscardChildren or MouZhangXiuAnniversaryStage.ObtainChildren or MouZhangXiuAnniversaryStage.DistributionChildren)) throw new InvalidOperationException("Mou Zhang Xiu requires his exact issued physical movement segment.");
            var discard = r.Stage == MouZhangXiuAnniversaryStage.DiscardChildren; var obtain = r.Stage == MouZhangXiuAnniversaryStage.ObtainChildren; var distribute = r.Stage == MouZhangXiuAnniversaryStage.DistributionChildren;
            var recipient = distribute ? r.Recipient : obtain ? f.OwnerSeat : r.Target;
            var destination = discard ? CardLocation.DiscardPile : CardLocation.Hand(recipient);
            if (r.Cards.Any(c => e._cardZones.GetLocation(c.Id) != c.From) || obtain && r.Cards.Any(c => c.From != CardLocation.DiscardPile) || distribute && (r.Cards.Count != 1 || r.Cards.Any(c => c.From != CardLocation.Hand(f.OwnerSeat) || !r.Gained.Contains(c.Id) || r.Given.Contains(c.Id))) || !obtain && !distribute && r.Cards.Any(c => !new MouZhangXiuAnniversaryFacts(e).Cards(f.OwnerSeat, discard ? r.Target : f.OwnerSeat, discard).Contains(c))) throw new InvalidOperationException("Mou Zhang Xiu cannot pay with another entity, unacquired card or different region.");
            var ids = r.Cards.Select(c => c.Id).ToArray();
            void Paid(long batch, IReadOnlyList<CardMovementRecord> moves)
            {
                if (moves.Any(m => m.To != destination && m.To != CardLocation.OutsideGame)) throw new InvalidOperationException("Mou Zhang Xiu native movement changed its actual destination.");
                e.MouZhangXiuAnniversaryRules.MovesPaid(f.Id, moves);
                if (!obtain && !discard) e.RecordNativeGivenCards(f.Id, f.OwnerSeat, recipient, ids, r.Before);
            }
            var reason = new CardMoveReason(discard ? MouZhangXiuAnniversaryRuntime.DiscardReason : obtain ? MouZhangXiuAnniversaryRuntime.ClaimReason : distribute ? MouZhangXiuAnniversaryRuntime.DistributionReason : MouZhangXiuAnniversaryRuntime.GiveReason);
            if (discard) e.MoveProgramLogicalDiscard(f, r.Target, ids, reason, Paid);
            else e.MoveProgramCardsFromMultipleSources(ids, destination, reason, Paid);
        }
    }
}
