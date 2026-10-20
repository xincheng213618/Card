using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class WuLuKangPindianChecks
{
    private const string Discard = "skill-program.wu-lu-kang.shenduan.discard", Reserve = "skill-program.wu-lu-kang.shenduan.reserve", Bottom = "skill-program.wu-lu-kang.shenduan.bottom", Draw = "skill-program.wu-lu-kang.shenduan.draw";
    private static ShanheStartingCard C(CardKind kind, int rank = 7, Suit suit = Suit.Spade) => WuLuKangScene.C(kind, rank, suit);
    private static bool Replacement(WuLuKangScene s) => s.Action("wu-lu-kang-shenduan") && s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "discard");
    private static void DeclineReplacement(WuLuKangScene s) => s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "decline");
    private static bool KegouBranch(WuLuKangScene s, string branch) => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "wu-lu-kang-kegou" && c.Parameters.GetValueOrDefault("branch") == branch) == true;
    private static void KegouOpponent(WuLuKangScene s, int target) => s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "opponent" && c.Targets.SequenceEqual([target]));
    private static void ExactEndedCutoff(WuLuKangScene s, WuLuKangKegouQualification q)
    {
        var history = s.G.Events.Select(e => e.Payload).ToArray();
        var endIndex = Array.FindLastIndex(history, e => e is TurnEndedEvent t && t.TurnNumber == q.ActualTurn && t.ActorSeat == q.TurnOwner);
        s.Need(endIndex >= 0 && q.HistoryCutoff == endIndex + 1 &&
            history.Take(checked((int)q.HistoryCutoff)).Last() is TurnEndedEvent ended && ended.TurnNumber == q.ActualTurn && ended.ActorSeat == q.TurnOwner,
            "Turn-end qualification is frozen at the matching real TurnEnded fact, excluding any later same-window children or activation facts.");
    }
    public static void ShenduanRealPairGlobalGroupPaymentsAndCold()
    {
        foreach (var equipment in new[] { false, true })
        {
            var s = WuLuKangScene.Start(new("maximum-replacement-" + equipment) { Shenduan = true, Hand = [C(CardKind.Dodge, 2)],
                Equipment = equipment ? [C(CardKind.SilverLion, 8)] : [], WatchReasons = [Discard] });
            var maxima = s.Stock().Where(c => s.StockRank(c) == 13).ToArray(); var chosen = maxima.First().CardId;
            if (!equipment) s.Need(s.StockRank(s.Stock().First()) < 13, "The fixed native stock has a genuinely deeper maximum, distinguishing highest-card replacement from merely taking top.");
            var original = s.V().Hand.Single().Id; var cost = equipment ? s.V().Equipment.Single().Id : original;
            s.BeginPair(2); s.Reach(() => Replacement(s)); s.Private(); s.RejectPrompt(); s.Cold();
            s.Need(Enumerable.Range(0, 4).All(v => s.G.CreateSnapshot(v).PublicRevealedCards.All(c => c.Id != chosen)), "The current maximum remains hidden from every player view before its actual reveal.");
            s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "discard" && c.Cards.SequenceEqual([cost]) && c.Parameters.GetValueOrDefault("zone") == (equipment ? "equipment" : "hand"));
            s.Reach(() => s.Hold()); var paid = s.E<WuLuKangShenduanDiscardPaidEvent>().Single();
            s.Need(paid.CardId == cost && paid.From == (equipment ? CardLocation.Equipment(0) : CardLocation.Hand(0)) && paid.To == CardLocation.DiscardPile &&
                s.G.CardMovements.Count(m => m.CardId == cost && m.Sequence == paid.Sequence && m.Reason.Value == Discard) == 1 &&
                s.E<WuLuKangMaximumReservedEvent>().Length == 0 && s.E<PindianResultDeterminedEvent>().Length == 0,
                "Actual Hand or Equipment discard, including the last Hand card, pays once and drains its real children before selecting any maximum entity.");
            s.Private(); s.RejectPrompt(); s.Cold(); s.Continue(); s.Play();
            var result = s.E<PindianResultDeterminedEvent>().Single().Result; var reveal = s.E<WuLuKangPindianInputRevealedEvent>().Single(e => e.Seat == 0);
            s.Need(result is { SourceRank: 13, SourceWon: true } && result.SourceCardId == chosen && reveal.CardId == chosen && reveal.Replaced &&
                s.G.CardMovements.Count(m => m.CardId == chosen && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing && m.Reason.Value == Reserve) == 1 &&
                !s.G.CardMovements.Any(m => m.CardId == original && m.Reason == CardMoveReasons.PindianReveal),
                "One genuine current top-nearest maximum entity supplies the native final comparison; neither the discarded last Hand nor an extra ordinary source material is paid again.");
            var bottom = s.E<WuLuKangWinnerBottomedEvent>().Single(); var draw = s.E<WuLuKangKDrawReturnedEvent>().Single();
            s.Need(bottom.CardId == chosen && bottom.WinnerSeat == 0 && s.G.CardMovements.Single(m => m.Sequence == bottom.MovementSequence) is { From.Zone: CardZoneKind.Processing, To.Zone: CardZoneKind.DrawPile } &&
                s.G.CardMovements.Single(m => m.Sequence == bottom.MovementSequence).Reason.Value == Bottom && draw is { RecipientSeat: 0, ActualDrawCount: 1, ReplacementCount: 0 } &&
                s.G.CardMovements.Single(m => m.Reason.Value == Draw && m.To == CardLocation.Hand(0)).Sequence > bottom.MovementSequence &&
                !s.G.CardMovements.Any(m => m.CardId == chosen && m.Reason == CardMoveReasons.PindianFinish),
                "The real winner material is bottomed once before the final-K participant's genuine native minimum draw, without a duplicate PindianFinish discard."); s.Cold();
        }
        var emptyHand = WuLuKangScene.Start(new("empty-Hand-equipment-paid-native-Gushe") { Shenduan = true, Hand = [], Equipment = [C(CardKind.SilverLion, 8)] });
        var equipmentOnly = emptyHand.V().Equipment.Single().Id;
        emptyHand.Need(emptyHand.V().Hand.Count == 0 && emptyHand.Actions().Any(a => a.ProgramSkillId == WuLuKangScene.Gushe),
            "A real empty-Hand participant with a payable Equipment and Shenduan retains native simultaneous-pindian activation admission.");
        emptyHand.BeginGroup([2], null); emptyHand.Reach(() => Replacement(emptyHand)); emptyHand.Private(); emptyHand.RejectPrompt(); emptyHand.Cold();
        emptyHand.Answer(c => c.Parameters.GetValueOrDefault("branch") == "discard" && c.Cards.SequenceEqual([equipmentOnly]) && c.Parameters.GetValueOrDefault("zone") == "equipment"); emptyHand.Play();
        emptyHand.Need(emptyHand.E<WuLuKangContestStartedEvent>().Single().Simultaneous &&
            emptyHand.E<PindianResultDeterminedEvent>().Single().Result is { SourceRank: 13, SourceWon: true } &&
            emptyHand.E<WuLuKangShenduanDiscardPaidEvent>() is [{ CardId: var actualEquipment }] && actualEquipment == equipmentOnly &&
            emptyHand.E<WuLuKangPindianInputRevealedEvent>().Single(e => e.Seat == 0).Replaced &&
            emptyHand.G.CardMovements.Count(m => m.CardId == equipmentOnly && m.Reason.Value == Discard) == 1 &&
            emptyHand.E<WuLuKangPindianCancelledEvent>().Length == 0,
            "Native cards=[] admission pays the actual Equipment once and supplies the real maximum entity, without a fabricated Hand selection, second material or cancellation."); emptyHand.Cold();

        var legacy = WuLuKangScene.Start(new("legacy-Pindian-empty-selected-source") { Shenduan = true, Hand = [], Equipment = [C(CardKind.SilverLion, 8)] });
        var legacyEquipment = legacy.V().Equipment.Single().Id;
        var legacyDefinition = legacy.G.ContentRegistry.GetSkill(WuLuKangScene.Driver).Program!.Activations.Single(a => a.Id == "legacy-pair");
        legacy.Need(legacy.Actions().Any(a => a.ProgramSkillId == WuLuKangScene.Driver && a.ProgramActivationId == "legacy-pair" && a.MinCardCount == 0 && a.SelectableTargetSeats.Contains(2)),
            "The existing selected-source native Pindian activation publishes its legal empty-card admission only because real Equipment can pay Shenduan. " +
            JsonSerializer.Serialize(new { Definition = new { legacyDefinition.MinCards, legacyDefinition.MaxCards, legacyDefinition.MinTargets, legacyDefinition.MaxTargets,
                legacyDefinition.TargetKind, legacyDefinition.SourceZones, legacyDefinition.CardCountExpression, legacyDefinition.CardKinds, legacyDefinition.CardSuits,
                legacyDefinition.CardCategories, legacyDefinition.EquipmentSlots, legacyDefinition.SelectedCardsSameSuit, legacyDefinition.SelectedCardsDistinctSuits,
                Effects = legacyDefinition.Effects.Select(e => new { e.Op, e.Target, Condition = e.Condition.Kind, e.Amount }) },
                Published = legacy.Actions().Where(a => a.ProgramSkillId == WuLuKangScene.Driver && a.ProgramActivationId == "legacy-pair")
                    .Select(a => new { a.MinCardCount, a.MaxCardCount, a.MinTargetCount, a.MaxTargetCount, a.TargetSeats, a.SelectableTargetSeats, a.SelectableCardIds }) }));
        legacy.Use("legacy-pair", [2]); legacy.Reach(() => Replacement(legacy)); legacy.Private(); legacy.RejectPrompt(); legacy.Cold();
        legacy.Answer(c => c.Parameters.GetValueOrDefault("branch") == "discard" && c.Cards.SequenceEqual([legacyEquipment])); legacy.Play();
        legacy.Need(legacy.E<WuLuKangPindianSelectionAdmittedEvent>() is [{ Admission.ProgramSource.BindingId: "legacy-pair" }] &&
            legacy.E<WuLuKangContestStartedEvent>().Single().Simultaneous == false &&
            legacy.E<PindianResultDeterminedEvent>().Single().Result is { SourceRank: 13, SourceWon: true } &&
            legacy.E<WuLuKangShenduanDiscardPaidEvent>() is [{ CardId: var legacyPaid }] && legacyPaid == legacyEquipment &&
            legacy.G.CardMovements.Count(m => m.CardId == legacyEquipment && m.Reason.Value == Discard) == 1 &&
            legacy.E<WuLuKangPindianCancelledEvent>().Length == 0,
            "Legacy Pindian receives one exact cards=[] admission, actually pays Equipment once, and resumes its genuine winning native result instead of fabricating or double-paying a selected Hand card."); legacy.Cold();

        var doubleEmpty = WuLuKangScene.Start(new("two-empty-Hand-maximum-native-group") { Shenduan = true, OtherShenduanSeats = [2],
            Hand = [], Equipment = [C(CardKind.SilverLion, 8)], ForeignDuel = true,
            Peers = [[C(CardKind.Dodge, 3)], [C(CardKind.SilverLion, 6)], [C(CardKind.Dodge, 9)]] });
        // NPC setup has explicit Hands only. Its sole real card is naturally equipped
        // during its genuine Play, producing the second empty-Hand/Equipment source.
        var opponentEquipment = doubleEmpty.V(2).Hand.Single().Id; doubleEmpty.End(); doubleEmpty.Play();
        var equipPath = doubleEmpty.G.CardMovements.Where(m => m.CardId == opponentEquipment && m.Reason is var reason &&
            (reason == CardMoveReasons.EquipmentUse || reason == CardMoveReasons.EquipmentEnter)).OrderBy(m => m.Sequence).ToArray();
        var equipUses = doubleEmpty.E<CardUseDeclaredEvent>().Where(e => e.SourceSeat == 2 && e.CardId == opponentEquipment && e.CardKind == CardKind.SilverLion).ToArray();
        doubleEmpty.Need(doubleEmpty.V().Hand.Count == 0 && doubleEmpty.V(2).Hand.Count == 0 && doubleEmpty.V(2).Equipment.Any(c => c.Id == opponentEquipment) &&
            equipPath.Length == 2 && equipPath[0].From == CardLocation.Hand(2) && equipPath[0].To == CardLocation.Processing && equipPath[0].Reason == CardMoveReasons.EquipmentUse &&
            equipPath[1].From == CardLocation.Processing && equipPath[1].To == CardLocation.Equipment(2) && equipPath[1].Reason == CardMoveReasons.EquipmentEnter && equipPath[0].Sequence < equipPath[1].Sequence &&
            equipUses.Length == 1 && doubleEmpty.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == equipUses[0].ResolutionId && e.CardId == opponentEquipment) == 1 &&
            doubleEmpty.Actions().Any(act => act.ProgramSkillId == WuLuKangScene.Gushe && act.SelectableTargetSeats.Contains(2)),
            "Two real empty-Hand participants retain payable Equipment and live maximum policies; the other's one completed native Equipment Use paid Hand-to-Processing then Processing-to-Equipment exactly once in that order.");
        var sourceEquipment = doubleEmpty.V().Equipment.Single().Id;
        doubleEmpty.BeginGroup([2], null); doubleEmpty.Reach(() => Replacement(doubleEmpty)); doubleEmpty.Private(); doubleEmpty.Cold();
        doubleEmpty.Answer(c => c.Parameters.GetValueOrDefault("branch") == "discard" && c.Cards.SequenceEqual([sourceEquipment])); doubleEmpty.Play();
        var twoCosts = doubleEmpty.E<WuLuKangShenduanDiscardPaidEvent>();
        doubleEmpty.Need(twoCosts.Length == 2 && twoCosts.Select(e => e.ParticipantSeat).Order().SequenceEqual([0, 2]) &&
            twoCosts.Select(e => e.CardId).ToHashSet().SetEquals([sourceEquipment, opponentEquipment]) &&
            twoCosts.All(e => e.From == CardLocation.Equipment(e.ParticipantSeat) && e.To == CardLocation.DiscardPile &&
                doubleEmpty.G.CardMovements.Count(m => m.CardId == e.CardId && m.Sequence == e.Sequence && m.Reason.Value == Discard) == 1) &&
            doubleEmpty.E<WuLuKangMaximumReservedEvent>().Length == 2 &&
            doubleEmpty.E<WuLuKangPindianInputRevealedEvent>().Length == 2 &&
            doubleEmpty.E<PindianResultDeterminedEvent>().Single().Result is { SourceRank: 13, OpponentRank: 13, SourceWon: false } &&
            doubleEmpty.E<WuLuKangPindianCancelledEvent>().Length == 0,
            "Paying the first genuine Equipment cannot invalidate the other participant's still-unvisited maximum source; both native payments reserve and reveal different real Kings before one genuine tie result."); doubleEmpty.Cold();

        foreach (var rank in new[] { 3, 7, 9 })
        {
            var s = WuLuKangScene.Start(new("unreplaced-win-loss-tie-" + rank) { Shenduan = true, Hand = [C(CardKind.Slash, rank)], Peers = [[C(CardKind.Dodge, 3)], [C(CardKind.Dodge, 7)], [C(CardKind.Dodge, 9)]] });
            var source = s.V().Hand.Single().Id; s.BeginPair(2); s.Reach(() => Replacement(s)); s.Private(); s.Cold(); DeclineReplacement(s); s.Play();
            var result = s.E<PindianResultDeterminedEvent>().Single().Result;
            s.Need(result.SourceCardId == source && result.SourceRank == rank && result.OpponentRank == 7 && result.SourceWon == (rank > 7) && s.E<WuLuKangShenduanDiscardPaidEvent>().Length == 0 && s.E<WuLuKangKDrawReturnedEvent>().Length == 0,
                "Declining replacement preserves genuine ordinary Hand selection and native strict win/loss/tie ranks, with no hidden payment or unearned K reward.");
            s.Need(rank == 7 ? s.E<WuLuKangWinnerBottomedEvent>().Length == 0 : s.E<WuLuKangWinnerBottomedEvent>() is [{ CardId: var winner }] && winner == (rank > 7 ? result.SourceCardId : result.OpponentCardId),
                "An exact equality creates no winner; an ordinary opponent victory bottoms the opponent's own physical entity rather than treating every non-source-win as a tie."); s.Cold();
        }
        var effective = WuLuKangScene.Start(new("effective-heart-one-is-K") { Shenduan = true, Tianbian = true, Hand = [C(CardKind.Slash, 1, Suit.Heart)] });
        var heart = effective.V().Hand.Single().Id; effective.BeginPair(2); effective.Reach(() => Replacement(effective)); DeclineReplacement(effective); effective.Play();
        effective.Need(effective.E<PindianResultDeterminedEvent>().Single().Result is { SourceRank: 13 } && effective.E<WuLuKangKDrawReturnedEvent>() is [{ RecipientSeat: 0, ActualDrawCount: 1 }] &&
            effective.E<WuLuKangWinnerBottomedEvent>().Single().CardId == heart, "Native Tianbian final comparison rank13 qualifies as used K even though its paid Heart entity is printed one."); effective.Cold();

        var group = WuLuKangScene.Start(new("true-group-shared-K-two-global-sources") { Shenduan = true, OtherShenduanSeats = [1], Hand = [C(CardKind.Slash, 13), C(CardKind.Dodge, 2)],
            Peers = [[C(CardKind.Dodge, 3)], [C(CardKind.Dodge, 7)], [C(CardKind.Dodge, 13)]] });
        var shared = group.V().Hand.Single(c => c.Rank == 13).Id; group.BeginGroup([1, 2, 3], shared); group.Reach(() => Replacement(group)); group.Private(); group.Cold(); DeclineReplacement(group); group.Play();
        var comparisons = group.E<WuLuKangComparisonSettledEvent>(); var id = group.E<WuLuKangContestStartedEvent>().Single().ContestId;
        group.Need(comparisons.Length == 3 && comparisons.All(e => e.ContestId == id && e.Result.SourceCardId == shared && e.Result.SourceRank == 13) && comparisons.Select(e => e.ComparisonIndex).SequenceEqual([0, 1, 2]) &&
            group.E<PindianCardsRevealedEvent>().Count(e => e.ParentFrameId == id) == 1 && group.E<WuLuKangWinnerBottomedEvent>().Count(e => e.CardId == shared) == 1,
            "One genuine common source King participates in three ordered native comparisons and one common reveal; only a still-Processing actual winner entity is bottomed once.");
        foreach (var comparison in comparisons)
        {
            var recipients = new List<int> { comparison.Result.SourceSeat }; if (comparison.Result.OpponentRank == 13) recipients.Add(comparison.Result.OpponentSeat);
            foreach (var owner in new[] { 0, 1 }) group.Need(group.E<WuLuKangKDrawIssuedEvent>().Where(e => e.ContestId == id && e.ComparisonIndex == comparison.ComparisonIndex && e.Source.OwnerSeat == owner).Select(e => e.RecipientSeat).Order().SequenceEqual(recipients.Order()),
                "Each independent Shenduan source triggers separately for every real comparison, including the same shared source K in all three comparisons.");
        }
        group.Need(group.E<WuLuKangKDrawReturnedEvent>().Length == group.E<WuLuKangKDrawIssuedEvent>().Length && group.E<WuLuKangKDrawReturnedEvent>().All(e => e.ActualDrawCount == 1 && e.ReplacementCount == 0),
            "All per-comparison per-source minimum rewards are genuine once-returned Draw1 invoices, rather than a whole-group dedup or fake repeated pair."); group.Cold();

        var replaced = WuLuKangScene.Start(new("minimum-real-native-draw-replacement") { Shenduan = true, Zenrun = true, Hand = [C(CardKind.Slash, 13)] });
        var minimum = replaced.Stock().First(c => replaced.StockRank(c) == 1).CardId; replaced.BeginPair(2); replaced.Reach(() => Replacement(replaced)); DeclineReplacement(replaced);
        replaced.Reach(() => replaced.Optional("ol:zenrun")); replaced.Private(); replaced.Cold(); replaced.Activate(); replaced.Reach(() => replaced.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "target") == true); replaced.Answer(c => c.Parameters.GetValueOrDefault("branch") == "target" && c.Targets.SequenceEqual([3]));
        replaced.Reach(() => replaced.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "pick") == true); replaced.Private(); replaced.Answer(c => c.Parameters.GetValueOrDefault("branch") == "pick" && c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)); replaced.Answer(c => c.Parameters.GetValueOrDefault("branch") == "take"); replaced.Play();
        replaced.Need(replaced.E<WuLuKangKDrawReturnedEvent>() is [{ ActualDrawCount: 0, ReplacementCount: 1 }] && replaced.Location(minimum) == CardLocation.DrawPile && replaced.G.CardMovements.All(m => m.Reason.Value != Draw),
            "Native replacement returns one obtained foreign entity without stealing the previously eligible current minimum or forging a stock Draw1."); replaced.Cold();

        foreach (var death in new[] { false, true })
        {
            var s = WuLuKangScene.Start(new("paid-replacement-participant-" + death) { Shenduan = true, ConditionalSources = !death, Hand = [C(CardKind.Dodge, 2), C(CardKind.Slash, 9)], WatchReasons = [Discard], ChildLoss = death ? 4 : 1 });
            s.BeginPair(2); s.Reach(() => Replacement(s)); var cost = s.V().Hand.Single(c => c.Rank == 2).Id; s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "discard" && c.Cards.SequenceEqual([cost])); s.Reach(() => s.Hold());
            var paid = s.E<WuLuKangShenduanDiscardPaidEvent>().Single(); s.Private(); s.Cold(); s.Continue(); s.Reach(() => !s.G.ResolutionStack.Any(f => f.WuLuKangContest?.ContestId == paid.ContestId));
            s.Need(s.E<WuLuKangShenduanDiscardPaidEvent>().Length == 1 && s.G.CardMovements.Count(m => m.CardId == cost && m.Sequence == paid.Sequence && m.Reason.Value == Discard) == 1 &&
                (death ? !s.V().IsAlive && s.G.State.Winner == Winner.None && s.E<WuLuKangPindianCancelledEvent>().Count(e => e.ContestId == paid.ContestId) == 1 && !s.E<PindianResultDeterminedEvent>().Any() :
                    s.V().IsAlive && !s.Has(WuLuKangScene.Shenduan) && s.E<PindianResultDeterminedEvent>().Single().Result.SourceRank == 13),
                "An accepted real discard survives source loss exactly once; true participant death returns a typed cancellation without a fabricated tie or repeated cost."); s.Cold();
        }
    }

    public static void KegouActualBoundariesDistinctMinimaRepeatAndNativeSlashCold()
    {
        var s = WuLuKangScene.Start(new("play-start-distinct-minima") { Kegou = true, Hand = [C(CardKind.Slash, 9), C(CardKind.Dodge, 2)], InsertPlay = true }, stopBeforePlay: true);
        s.Reach(() => s.Optional(WuLuKangScene.Kegou)); s.Private(); s.RejectPrompt(); s.Cold(); s.Activate(); s.Reach(() => KegouBranch(s, "opponent"));
        var expected = s.Stock().GroupBy(s.StockRank).OrderBy(g => g.Key).Take(2).Select(g => g.First().CardId).ToArray(); s.Private(); s.Cold(); KegouOpponent(s, 1);
        s.Reach(() => s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("action") is "pindian-source-card" or "pindian-card")); s.PindianCard(9); s.Play();
        var first = s.E<WuLuKangKegouStartedEvent>().Single(); var gains = s.E<WuLuKangKegouGainPaidEvent>().Where(e => e.FrameId == first.FrameId).ToArray();
        s.Need(first.Qualification is { Used: false, Played: false } && gains.Length == 2 && gains.Select(e => e.PrintedRank).SequenceEqual([1, 3]) && gains.Select(e => e.CardId).SequenceEqual(expected) &&
            gains.All(e => s.G.CardMovements.Count(m => m.CardId == e.CardId && m.Sequence == e.Sequence && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)) == 1),
            "A genuine actual Play-start needs no previous Use; winning obtains one physical entity for each of the two lowest distinct rank values, preserving obtain rather than Draw semantics."); s.Cold();
        var turn = s.G.State.TurnNumber; s.End(); s.Reach(() => s.Optional(WuLuKangScene.Kegou));
        s.Need(s.G.State.TurnNumber == turn && s.E<WuLuKangKegouStartedEvent>().Length == 1, "An actual inserted Play returns to the same turn's normal Play with its own start opportunity."); s.Decline(); s.Play();
        var endBefore = s.E<WuLuKangKegouStartedEvent>().Length; s.End(); s.Play(); s.Need(s.E<WuLuKangKegouStartedEvent>().Length == endBefore,
            "Pindian payments and obtains alone are not genuine Used or Played cards, so the no-action actual turn has no invented turn-end opportunity.");

        var repeat = WuLuKangScene.Start(new("declined-slash-still-retargets-repeat") { Kegou = true, Hand = [C(CardKind.Dodge, 2), C(CardKind.Slash, 13), C(CardKind.Dodge, 4)] }, stopBeforePlay: true);
        repeat.Reach(() => repeat.Optional(WuLuKangScene.Kegou)); repeat.Activate(); repeat.Reach(() => KegouBranch(repeat, "opponent")); KegouOpponent(repeat, 2); repeat.Reach(() => repeat.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "pindian-source-card")); repeat.PindianCard(2);
        repeat.Reach(() => KegouBranch(repeat, "repeat")); var root = repeat.E<WuLuKangKegouStartedEvent>().Single().FrameId;
        repeat.Need(repeat.E<WuLuKangKegouPindianReturnedEvent>().Single().Result.SourceWon == false && repeat.E<WuLuKangKegouSlashDecidedEvent>().Single() is { Accepted: false, OpponentSeat: 2 } && repeat.E<WuLuKangKegouSlashIssuedEvent>().Length == 0,
            "The real far opponent has no native legal Slash and declines it; a genuine nonwin still permits the owner's separate repeat.");
        repeat.Private(); repeat.Cold(); repeat.Answer(c => c.Parameters.GetValueOrDefault("branch") == "repeat"); repeat.Reach(() => KegouBranch(repeat, "opponent")); KegouOpponent(repeat, 3); repeat.Reach(() => repeat.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "pindian-source-card")); repeat.PindianCard(13); repeat.Play();
        repeat.Need(repeat.E<WuLuKangKegouPindianIssuedEvent>().Where(e => e.FrameId == root).Select(e => e.OpponentSeat).SequenceEqual([2, 3]) && repeat.E<WuLuKangKegouPindianReturnedEvent>().Last().Result.SourceWon &&
            repeat.E<WuLuKangKegouGainPaidEvent>().Count(e => e.FrameId == root) == 2 && repeat.E<WuLuKangKegouCompletedEvent>().Single(e => e.FrameId == root).Iterations == 2,
            "A separate optional repeat chooses a new real other participant and returns its second actual contest, without re-paying or replaying the first comparison."); repeat.Cold();

        var slash = WuLuKangScene.Start(new("enemy-real-optional-Slash-return") { Kegou = true, Hand = [C(CardKind.Dodge, 2), C(CardKind.Slash, 4), C(CardKind.Dodge, 6)], Peers = [[C(CardKind.Dodge, 3)], [C(CardKind.Dodge, 7)], [C(CardKind.Dodge, 2)]] }, stopBeforePlay: true);
        slash.Reach(() => slash.Optional(WuLuKangScene.Kegou)); slash.Activate(); slash.Reach(() => KegouBranch(slash, "opponent")); KegouOpponent(slash, 3); slash.Reach(() => slash.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "pindian-source-card")); slash.PindianCard(2);
        slash.Reach(() => slash.P is { PlayerSeat: 3 } && KegouBranch(slash, "slash"));
        slash.Need(KegouBranch(slash, "decline") && slash.P!.Choices.All(c => c.Cards.Count == 0), "The actual opponent independently owns both legal-use and decline choices, with no fake card payment."); slash.Private(); slash.Cold();
        slash.Reach(() => slash.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }); var issued = slash.E<WuLuKangKegouSlashIssuedEvent>().Single().Origin;
        var use = slash.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == issued.UseId);
        slash.Need(use.Action is { Type: CardActionType.Use, ActorSeat: 3, ProviderSeat: 3, PhysicalCards.Count: 0, EffectiveSuit: Suit.None, EffectiveRank: 0, EffectiveIsRed: null } && use.WuLuKangKegouSlash == issued && issued.TargetSeat == 0,
            "The real hostile AI accepts one native zero-material ordinary Slash with exact current opponent/parent provenance; it retains normal target range and response flow.");
        slash.Private(); slash.RejectPrompt(); slash.Cold(); slash.Answer(c => c.Parameters.GetValueOrDefault("response") == "take-damage"); slash.Reach(() => KegouBranch(slash, "repeat"));
        slash.Need(slash.V().Hp == 3 && slash.E<WuLuKangKegouSlashReturnedEvent>().Count(e => e.UseId == issued.UseId) == 1 && slash.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == issued.UseId) == 1,
            "One actual Slash damage and typed child return precede repeat choice without replaying the comparison or issuing another Slash."); slash.Answer(c => c.Parameters.GetValueOrDefault("branch") == "repeat-decline"); slash.Play(); slash.Cold();

        var far = WuLuKangScene.Start(new("nonwin-far-native-Slash-forbidden") { Kegou = true, Hand = [C(CardKind.Dodge, 2), C(CardKind.Slash, 9)] }, stopBeforePlay: true);
        far.Reach(() => far.Optional(WuLuKangScene.Kegou)); far.Activate(); far.Reach(() => KegouBranch(far, "opponent")); KegouOpponent(far, 2); far.Reach(() => far.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "pindian-source-card")); far.PindianCard(2); far.Reach(() => KegouBranch(far, "repeat"));
        far.Need(far.E<WuLuKangKegouSlashIssuedEvent>().Length == 0 && far.E<WuLuKangKegouSlashDecidedEvent>().Single().Accepted == false, "An actual far opponent has no extra distance exemption for the optional Slash, yet a nonwin still offers repeat."); far.Answer(c => c.Parameters.GetValueOrDefault("branch") == "repeat-decline"); far.Play(); far.Cold();

        var paidGain = WuLuKangScene.Start(new("paid-Kegou-gain-child-source-loss") { Kegou = true, ConditionalSources = true,
            Hand = [C(CardKind.Slash, 9), C(CardKind.Dodge, 2)], WatchGainReason = "skill-program.wu-lu-kang.kegou.gain", ChildLoss = 1 }, stopBeforePlay: true);
        paidGain.Reach(() => paidGain.Optional(WuLuKangScene.Kegou)); paidGain.Activate(); paidGain.Reach(() => KegouBranch(paidGain, "opponent")); KegouOpponent(paidGain, 1);
        paidGain.Reach(() => paidGain.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "pindian-source-card")); paidGain.PindianCard(9); paidGain.Reach(() => paidGain.Hold());
        var gainRoot = paidGain.E<WuLuKangKegouStartedEvent>().Single().FrameId;
        paidGain.Need(paidGain.E<WuLuKangKegouGainPaidEvent>().Count(e => e.FrameId == gainRoot) == 2 && paidGain.E<WuLuKangKegouCompletedEvent>().Length == 0,
            "Both actual distinct-rank obtains are paid before their real gain observer returns to the owning Kegou frame.");
        paidGain.Private(); paidGain.Cold(); paidGain.Continue(); paidGain.Play();
        paidGain.Need(!paidGain.Has(WuLuKangScene.Kegou) && paidGain.V().Hp == 3 &&
            paidGain.E<WuLuKangKegouCompletedEvent>().Count(e => e.FrameId == gainRoot) == 1 &&
            paidGain.E<WuLuKangKegouGainPaidEvent>().Count(e => e.FrameId == gainRoot) == 2 &&
            !paidGain.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == gainRoot),
            "Real source loss in the paid obtain child neither refunds nor repeats the contest, two physical gains or exact parent completion."); paidGain.Cold();

        var ownUse = WuLuKangScene.Start(new("own-actual-turn-Use-qualified-end") { Kegou = true, Hand = [C(CardKind.IronChain, 5), C(CardKind.Dodge, 2)] });
        ownUse.Native(CardKind.IronChain, [0]); ownUse.Play(); ownUse.End(); ownUse.Reach(() => ownUse.Optional(WuLuKangScene.Kegou)); ownUse.Private(); ownUse.Cold(); ownUse.Activate(); ownUse.Reach(() => KegouBranch(ownUse, "opponent"));
        var ownQualification = ownUse.E<WuLuKangKegouStartedEvent>().Last().Qualification;
        ownUse.Need(ownQualification is { Used: true }, "A true own Use qualifies the same whole actual turn's end even though Pindian itself is not a card Use."); ExactEndedCutoff(ownUse, ownQualification);
        KegouOpponent(ownUse, 2); ownUse.Reach(() => KegouBranch(ownUse, "repeat-decline"));
        ownUse.Need(ownUse.V().Hand.Count == 0 && !KegouBranch(ownUse, "repeat"), "A genuine nonwin with no remaining Hand or payable source cannot publish an impossible repeat.");
        ownUse.Answer(c => c.Parameters.GetValueOrDefault("branch") == "repeat-decline"); ownUse.Play(); ownUse.Cold();

        var played = WuLuKangScene.Start(new("foreign-turn-pure-Played-qualified") { Kegou = true, ForeignDuel = true, Hand = [C(CardKind.Slash, 9), C(CardKind.Dodge, 2)],
            Peers = [[C(CardKind.Dodge, 3)], [C(CardKind.Duel, 7)], [C(CardKind.Dodge, 9)]] });
        played.End(); played.Reach(() => played.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash }); played.Private(); played.Cold(); var material = played.V().Hand.Single(c => c.Kind == CardKind.Slash).Id; played.Answer(c => c.Cards.SequenceEqual([material]));
        played.Reach(() => played.Optional(WuLuKangScene.Kegou)); played.Activate(); played.Reach(() => KegouBranch(played, "opponent")); var qualified = played.E<WuLuKangKegouStartedEvent>().Last().Qualification;
        played.Need(qualified is { Played: true, Used: false, TurnOwner: 2 } && !played.E<CardUseDeclaredEvent>().Any(e => e.SourceSeat == 0) && played.E<CardRespondedEvent>().Any(e => e.ResponderSeat == 0 && e.SourceSeat == 2 && e.CardId == material),
            "A genuine Duel response is pure Played in the foreign actual turn, independently qualifying its real end without inventing an owner Use or inheriting another turn's history.");
        ExactEndedCutoff(played, qualified); played.Private(); played.Cold(); KegouOpponent(played, 1); played.Reach(() => KegouBranch(played, "repeat-decline"));
        played.Need(played.V().Hand.Count == 0 && !KegouBranch(played, "repeat"), "Paying its last real Pindian card leaves only the owner's independent end choice, not an invented repeat source.");
        played.Answer(c => c.Parameters.GetValueOrDefault("branch") == "repeat-decline"); played.Play(); played.Cold();
    }
}

internal sealed record WuLuKangSpec(string Name)
{
    public bool Shenduan { get; init; }
    public bool Kegou { get; init; }
    public bool Dixian { get; init; }
    public bool ConditionalSources { get; init; }
    public bool InsertPlay { get; init; }
    public bool ForeignDuel { get; init; }
    public bool Zenrun { get; init; }
    public bool Tianbian { get; init; }
    public int[] OtherShenduanSeats { get; init; } = [];
    public int OwnerHp { get; init; } = 4;
    public ShanheStartingCard[] Hand { get; init; } = [WuLuKangScene.C(CardKind.Slash, 9), WuLuKangScene.C(CardKind.Dodge, 2), WuLuKangScene.C(CardKind.Dodge, 4)];
    public ShanheStartingCard[] Equipment { get; init; } = [];
    public ShanheStartingCard[][] Peers { get; init; } = [[WuLuKangScene.C(CardKind.Dodge, 3)], [WuLuKangScene.C(CardKind.Dodge, 7)], [WuLuKangScene.C(CardKind.Dodge, 9)]];
    public ShanheStartingCard[] Stock { get; init; } = Enumerable.Range(0, 4).SelectMany(_ => new[] { WuLuKangScene.C(CardKind.Dodge, 1), WuLuKangScene.C(CardKind.Slash, 3), WuLuKangScene.C(CardKind.Peach, 5), WuLuKangScene.C(CardKind.Indulgence, 13) }).ToArray();
    public string[] WatchReasons { get; init; } = [];
    public int ChildLoss { get; init; }
    public bool WatchDraw { get; init; }
    public int DrawChildLoss { get; init; }
    public string? WatchGainReason { get; init; }
    public bool PauseUse { get; init; }

    public bool Rescue { get; init; }
}

internal sealed class WuLuKangScene(WuLuKangSpec spec, GameEngine game, ContentRegistry registry)
{
    public const string Shenduan = "wumiao:shenduan", Kegou = "wumiao:kegou", Dixian = "wumiao:dixian", Gushe = "ol:gushe";
    public const string Driver = "fixture:wlk-driver", Quiet = "fixture:wlk-quiet", Watch = "fixture:wlk-watch", Conditional = "fixture:wlk-sources";
    private const string Owner = "fixture:wlk-owner", Ally = "fixture:wlk-ally", EnemyA = "fixture:wlk-enemy-a", EnemyB = "fixture:wlk-enemy-b", Deck = "fixture:wlk-deck";
    public static ShanheStartingCard C(CardKind kind, int rank = 7, Suit suit = Suit.Spade) => new(kind, suit, rank);
    public GameEngine G { get; private set; } = game;
    public WuLuKangSpec Spec => spec;
    public PendingDecision? P => Enumerable.Range(0, 4).Select(s => G.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
    public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
    public bool Has(string skill, int seat = 0) => V(seat).Skills?.Any(s => s.ContentId == skill) == true;
    public bool Action(string action) => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action) == true;
    public bool Optional(string skill) => P?.SkillPrompt?.SkillId == skill && Action("activate");
    public bool Hold(string option = "continue") => P?.SkillPrompt?.SkillId == Watch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == option);
    public CardZoneDiagnostic[] Stock() => G.CreateCardZoneDiagnostics().Where(c => c.Location == CardLocation.DrawPile).OrderByDescending(c => c.ZoneIndex).ToArray();
    public int StockRank(CardZoneDiagnostic card)
    {
        var ranks = spec.Stock.Where(c => c.Kind == card.CardKind).Select(c => c.Rank).Distinct().ToArray();
        Need(ranks.Length == 1, "The explicit stock fixture assigns one printed rank to each stock kind."); return ranks.Single();
    }
    public CardLocation Location(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
    public void Use(string id, int[]? targets = null, int[]? cards = null) => Accept(new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
    public void BeginPair(int target) => Use("pair", [target]);
    public void BeginGroup(int[] targets, int? card) => Accept(new UseProgramSkillCommand(0, Gushe, "simultaneous-pindian", card is { } id ? [id] : [], targets, G.Revision, P!.PromptId));
    public void BeginDixian()
    {
        Accept(new UseProgramSkillCommand(0, Dixian, "declare-rank", [], [], G.Revision, P!.PromptId)); Reach(() => Action("wu-lu-kang-dixian"));
    }
    public void ChooseRank(int rank) => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "wu-lu-kang-dixian" && c.Parameters.GetValueOrDefault("rank") == rank.ToString(System.Globalization.CultureInfo.InvariantCulture));
    public LegalAction[] Actions() => G.GetHumanLegalActions().ToArray();
    public void Native(CardKind kind, int[] targets, int? rank = null)
    {
        var a = Actions().FirstOrDefault(a => a.CardId is { } id && V().Hand.Any(c => c.Id == id && c.Kind == kind && (rank is null || c.Rank == rank)) &&
            a.Kind != LegalActionKind.Recast && a.ConversionSource is null && a.TargetSeats.SequenceEqual(targets));
        Need(a is not null, "Expected a genuine legal physical " + kind + " at native targets."); Native(a!);
    }
    public void Native(LegalAction a) => Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId)
        { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
    public void Answer(Func<PromptChoice, bool> predicate)
    {
        var p = P; var choice = p?.Choices.FirstOrDefault(predicate); Need(p is { PlayerSeat: 0 } && choice is not null, "Only a currently published human choice may be answered.");
        Accept(new AnswerPromptCommand(0, p!.PromptId, choice!.Id, G.Revision));
    }
    public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
    public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    public void Decline() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip" || c.Parameters.GetValueOrDefault("branch") is "decline" or "repeat-decline" || c.Parameters.GetValueOrDefault("choice") == "decline");
    public void PindianCard(int? rank = null)
    {
        Answer(c => c.Parameters.GetValueOrDefault("action") is "pindian-source-card" or "pindian-card" && c.Cards.Count == 1 &&
            (rank is null || V().Hand.Any(h => h.Id == c.Cards[0] && h.Rank == rank)));
    }
    public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
    {
        for (var n = 0; n < 300; n++)
        {
            if (done()) return; Need(G.State.Winner == Winner.None, "Native terminal cutoff before expected boundary " + line);
            var p = P;
            if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
            else if (p.Kind == DecisionKind.PlayCard) End();
            else if (p.SkillPrompt?.SkillId == Watch && p.Choices.Any(c => c.Parameters.ContainsKey("option-id"))) Continue();
            else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
            else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "simultaneous-pindian" && c.Parameters.GetValueOrDefault("branch") == "draw")) Answer(c => c.Parameters.GetValueOrDefault("branch") == "draw");
            else if (Action("skip") || p.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") is "decline" or "repeat-decline" || c.Parameters.GetValueOrDefault("choice") == "decline")) Decline();
            else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("action") is "pindian-source-card" or "pindian-card")) PindianCard();
            else throw new InvalidOperationException("Unexpected actual boundary " + line + " " + Diagnostic());
        }
        throw new InvalidOperationException("Bounded actual scene did not reach boundary " + line + " " + Diagnostic());
    }
    public void Private()
    {
        var p = P!; Need(p.IsPrivate && Enumerable.Range(0, 4).Where(v => v != p.PlayerSeat).All(v => G.CreateSnapshot(v).PendingDecision is null) &&
            Enumerable.Range(0, 4).All(v => G.CreateSnapshot(v).Players.Where(s => s.Seat != v).All(s => s.Hand.Count == 0)), "Only the actual chooser sees its prompt; all other Hands stay opaque.");
        Frozen(p.Choices); foreach (var choice in p.Choices) { Frozen(choice.Cards); Frozen(choice.Targets); }
    }
    public void Frozen<T>(IReadOnlyList<T> values)
    {
        if (values is IList list && list.Count != 0) { var before = JsonSerializer.Serialize(values); var rejected = false; try { list[0] = list[0]; } catch (NotSupportedException) { rejected = true; } Need(rejected && before == JsonSerializer.Serialize(values), "Prepared nested collections are immutable."); }
    }
    public string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
        Zones = G.CreateCardZoneDiagnostics(), G.CardMovements, Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
    public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold replay retains all safe views, exact owning frames, private drafts, RNG, materials, movements, events and accepted commands."); }
    public void RejectPrompt()
    {
        var p = P!; Reject(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision));
        Reject(new AnswerPromptCommand(0, p.PromptId, new("forged-wu-lu-kang-choice"), G.Revision));
        Reject(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, G.Revision - 1));
    }
    public void Reject(GameCommand command) { var before = State(); Need(!G.Submit(command).Accepted && before == State(), "Invalid actor, stale or unpublished input changes no state, history, stock or RNG."); }
    public void Accept(GameCommand command)
    { try { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Code + "/" + result.Error?.Message); } catch (Exception e) { throw new InvalidOperationException(spec.Name + ": " + e.Message + " " + Diagnostic(), e); } }
    public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Diagnostic()); }
    private string Diagnostic() => JsonSerializer.Serialize(new { spec.Name, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner, Prompt = P,
        Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).IsAlive, V(i).HandCount, V(i).Equipment, V(i).Judgment }),
        StockTopFirst = Stock().Select(c => new { c.CardId, Kind = c.CardKind, c.ZoneIndex,
            PrintedRank = c.CardId > 0 && c.CardId <= spec.Stock.Length ? (int?)spec.Stock[c.CardId - 1].Rank : null }),
        Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = G.Events.Where(e => e.Payload.GetType().Name.Contains("WuLuKang", StringComparison.Ordinal)).TakeLast(24).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Moves = G.CardMovements.TakeLast(14) });
    public static SkillProgramCatalog Bundle(string bundle)
    {
        string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json") ?? throw new InvalidOperationException("Missing complete bundle " + bundle); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
        return SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
    }
    public static WuLuKangScene Start(WuLuKangSpec spec, bool stopBeforePlay = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(spec));
        var setup = new ShanheBattleSetup(7, 1, "wlk-" + spec.Name, Owner, spec.OwnerHp, 0, 0, 0, 0, [], spec.Hand, spec.Equipment,
            [new(EnemyA, Hp: 12) { StartingHand = spec.Peers[1] }, new(EnemyB, Hp: 12) { StartingHand = spec.Peers[2] }], [new(Ally, Hp: 12) { StartingHand = spec.Peers[0] }], new string('a', 64));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
            ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 16 }, registry);
        var scene = new WuLuKangScene(spec, g, registry); scene.Accept(new StartGameCommand()); if (!stopBeforePlay) scene.Play(); return scene;
    }
    private sealed class Fixture(WuLuKangSpec spec) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-wu-lu-kang-native", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            void Add(SkillProgramCatalog catalog, params string[] ids)
            { foreach (var id in ids) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id] }); }
            var real = Bundle("wumiao-lu-kang"); Add(real, Shenduan, Kegou, Dixian); Add(Bundle("ol-wang-lang"), Gushe);
            if (spec.Zenrun) Add(Bundle("ol-sun-hong"), "ol:zenrun");
            if (spec.Tianbian) Add(Bundle("classic-qin-mi"), "classic:tianbian");
            var kinds = spec.Stock.Concat(spec.Hand).Concat(spec.Equipment).Concat(spec.Peers.SelectMany(x => x)).Select(c => c.Kind)
                .Concat(new[] { CardKind.Slash, CardKind.Dodge, CardKind.Peach, CardKind.Alcohol, CardKind.Indulgence, CardKind.IronChain, CardKind.SilverLion, CardKind.StoneAxe }).Distinct();
            foreach (var kind in kinds) { var card = CardCatalog.Get(kind); b.AddCard(new("fixture:wlk-card-" + kind, card.DisplayName, card.CategoryName, card.Description, kind)); }
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static string Pause() => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"held\",\"options\":[{\"id\":\"continue\"}]}";
            static JsonNode A(string id, string effects, int targets = 0, int cards = 0) => N("{\"id\":\"" + id + "\",\"minCards\":" + cards + ",\"maxCards\":" + cards + ",\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,\"effects\":[" + effects + "]}");
            var acts = new JsonArray(A("pair", "{\"op\":\"startPindian\",\"target\":\"owner\",\"opponentRef\":{\"kind\":\"selectedTarget\"},\"resultBind\":\"contest\",\"visibility\":\"public\"}", 1),
                A("lose-one", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}"), A("recover-one", "{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}"),
                A("draw20", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":20}"), A("discard", "{\"op\":\"discardSelected\",\"target\":\"owner\",\"amount\":1}", cards: 1),
                A("damage", "{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":1}", 1));
            var selectedPair = (JsonObject)A("legacy-pair", "{\"op\":\"pindian\",\"target\":\"selectedTarget\",\"amount\":1}", 1, 1);
            selectedPair["targetKind"] = "otherLivingWithHand"; selectedPair["sourceZones"] = N("[\"hand\"]"); acts.Add(selectedPair);
            var defs = new JsonArray(new JsonObject { ["id"] = Driver, ["revision"] = 1, ["activations"] = acts, ["modifiers"] = N("[{\"id\":\"keep\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":20,\"priority\":0}]") },
                N("{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"));
            var soleDuelTarget = spec.ForeignDuel && spec.Peers[1].Any(c => c.Kind == CardKind.Duel);
            if (soleDuelTarget) defs.Add(N("{\"id\":\"fixture:wlk-duel-shield\",\"revision\":1,\"cardPolicies\":[{\"id\":\"native-only-human-Duel-target\",\"kind\":\"prohibitTarget\",\"cardKinds\":[\"duel\"]}]}"));
            var watches = new JsonArray();
            if (spec.WatchReasons.Length != 0)
                foreach (var zone in new[] { "hand", "equipment", "judgment" })
                    watches.Add(new JsonObject { ["id"] = "paid-movement-" + zone, ["window"] = "cardsMoved", ["subject"] = "owner", ["sourceZones"] = JsonSerializer.SerializeToNode(new[] { zone }), ["movementReasons"] = JsonSerializer.SerializeToNode(spec.WatchReasons),
                        ["movementOccurrence"] = "perBatch", ["usageScope"] = "game", ["usageLimit"] = 1, ["namedUsageGroup"] = "paid-movement", ["optional"] = false, ["priority"] = 1000, ["effects"] = N("[" + Pause() + (spec.ChildLoss > 0 ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + spec.ChildLoss + "}" : "") + "]") });
            if (spec.WatchDraw) watches.Add(N("{\"id\":\"actual-draw\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementReasons\":[\"skill-program.wumiao:dixian.draw\"],\"movementOccurrence\":\"perBatch\",\"priority\":1000,\"optional\":false,\"effects\":[" + Pause() + (spec.DrawChildLoss > 0 ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + spec.DrawChildLoss + "}" : "") + "]}"));
            if (spec.WatchGainReason is { } gainReason) watches.Add(new JsonObject { ["id"] = "paid-gain", ["window"] = "cardsGained", ["subject"] = "owner", ["destinationZones"] = N("[\"hand\"]"), ["movementReasons"] = JsonSerializer.SerializeToNode(new[] { gainReason }),
                ["movementOccurrence"] = "perBatch", ["usageScope"] = "game", ["usageLimit"] = 1, ["optional"] = false, ["priority"] = 1000, ["effects"] = N("[" + Pause() + (spec.ChildLoss > 0 ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + spec.ChildLoss + "}" : "") + "]") });
            if (spec.PauseUse) watches.Add(N("{\"id\":\"actual-use\",\"window\":\"cardUseCompleted\",\"ownerRelation\":\"actor\",\"singleActionInstance\":true,\"priority\":1000,\"optional\":false,\"effects\":[" + Pause() + "]}"));
            if (watches.Count > 0) defs.Add(new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = watches });
            if (spec.ConditionalSources) defs.Add(new JsonObject { ["id"] = Conditional, ["revision"] = 1, ["hpSkillGrants"] = new JsonArray(new[] { Shenduan, Kegou, Dixian }.Where(id => id == Shenduan && spec.Shenduan || id == Kegou && spec.Kegou || id == Dixian && spec.Dixian)
                .Select(id => (JsonNode)new JsonObject { ["id"] = "healthy-" + id, ["skillId"] = id, ["minimumHp"] = 4 }).ToArray()) });
            if (spec.InsertPlay) defs.Add(N("{\"id\":\"fixture:wlk-insert\",\"revision\":1,\"triggers\":[{\"id\":\"insert\",\"window\":\"turnStartBeforeNormalFlow\",\"subject\":\"owner\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"insertPhase\",\"target\":\"owner\",\"phase\":\"play\",\"phaseContinuation\":\"beforeNormalPreparation\"}]}]}"));
            if (spec.Rescue) defs.Add(N("{\"id\":\"fixture:wlk-rescue\",\"revision\":1,\"triggers\":[{\"id\":\"recover\",\"window\":\"selfDyingResponse\",\"subject\":\"owner\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"recoverTo\",\"target\":\"owner\",\"numberExpression\":\"integerConstant\",\"minimumValue\":2,\"clampToMaxHp\":true}]}]}"));
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "武陆抗真实原生边界检查", optionLabels = n!["id"]!.GetValue<string>() == Watch ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() });
            var local = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in local.Programs) b.AddSkill(new(id, labels[id].name, labels[id].description) { Program = program, ProgramPresentation = local.Presentations[id] });
            string[] Skills(int seat)
            {
                var skills = new List<string>(); if (watches.Count > 0) skills.Add(Watch);
                if (seat == 0)
                {
                    skills.Add(Driver); skills.Add(Gushe); if (spec.ConditionalSources) skills.Add(Conditional);
                    else { if (spec.Shenduan) skills.Add(Shenduan); if (spec.Kegou) skills.Add(Kegou); if (spec.Dixian) skills.Add(Dixian); }
                    if (spec.Tianbian) skills.Add("classic:tianbian"); if (spec.InsertPlay) skills.Add("fixture:wlk-insert"); if (spec.Zenrun) skills.Add("ol:zenrun"); if (spec.Rescue) skills.Add("fixture:wlk-rescue");
                }
                else { if (spec.OtherShenduanSeats.Contains(seat)) skills.Add(Shenduan); if (!(spec.ForeignDuel && seat == 2)) skills.Add(Quiet);
                    if (soleDuelTarget && seat is 1 or 3) skills.Add("fixture:wlk-duel-shield"); }
                return skills.ToArray();
            }
            b.AddGeneral(new(Owner, "真实武陆抗检查人", "", "standard:none", "wu", 4, Skills(0)));
            b.AddGeneral(new(Ally, "真实同行", "", "standard:none", "wei", 12, Skills(1))); b.AddGeneral(new(EnemyA, "真实对手甲", "", "standard:none", "wei", 12, Skills(2))); b.AddGeneral(new(EnemyB, "真实对手乙", "", "standard:none", "wei", 12, Skills(3)));
            b.AddDeck(new(Deck, "固定点数实体", 0, 0, []) { PhysicalCards = spec.Stock.Select(c => new ContentDeckPhysicalCard("fixture:wlk-card-" + c.Kind, c.Suit, c.Rank)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "武陆抗原生小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
