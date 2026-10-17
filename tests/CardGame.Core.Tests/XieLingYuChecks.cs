using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class XieLingYuChecks
{
    private const string Owner = "fixture:xly-owner", Mode = "identity:classic-xie-ling-yu",
        Driver = "fixture:xly-driver", Quiet = "fixture:xly-quiet", Watch = "fixture:xly-watch",
        Conditional = "fixture:xly-conditional", Yuandi = "ol:yuandi", Xinyou = "ol:xinyou",
        Zenrun = "ol:zenrun", Obtained = "fixture:xly-obtained", Assault = "fixture:xly-assault";
    private const string YDraw = "skill-program.xie-ling-yu.yuandi.draw",
        XDraw = "skill-program.xie-ling-yu.xinyou.draw", XCost = "skill-program.xie-ling-yu.xinyou.discard";
    private static string Scene = "";
    private static GameEngine? DiagnosticContext;

    public static void YuandiFirstCompleteDiscardGroupAndPermanentDeletionCold()
    {
        var (g, r) = Start("deletions"); Stock(g);
        var three = new[] { CardKind.Slash, CardKind.Dodge, CardKind.Peach }.Select(k => Hand(g, k)).ToArray();
        var before = V(g).HandCount; Use(g, "drop-three", three);
        Reach(g, () => Branch(g, "remove-name"));
        var paid = E<YuandiPaidEvent>(g).Single(); var parent = Frame(g, Yuandi);
        Need(paid is { NonUse: true, NamesMask: 7, ActualTurn: 1, TurnOwner: 0 } &&
            parent.XieLingYu is { Paid: true, Stage: XieLingYuStage.RemoveName, Requests: 1 } &&
            V(g).HandCount == before - 3 + 1 && Moves(g, YDraw).Length == 1 &&
            three.All(id => Zone(g, id) == CardLocation.DiscardPile) &&
            E<YuandiDiscardEnteredEvent>(g).Where(e => three.Contains(e.CardId)).Select(e => e.Name).Order().SequenceEqual(
                new[] { CardKind.Slash, CardKind.Dodge, CardKind.Peach }.Order()),
            "One genuine atomic S/D/P discard completes the first actual-turn group, draws once, then privately removes one name.");
        var p = P(g)!; var state = State(g);
        Need(!g.Submit(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, g.Revision)).Accepted && State(g) == state &&
            !g.Submit(new AnswerPromptCommand(0, p.PromptId, new ChoiceId("xly.unpublished-name"), g.Revision)).Accepted && State(g) == state,
            "Neither another seat nor an unpublished name can answer the mandatory source-owned removal.");
        g = Cold(g, r); Remove(g, CardKind.Peach); Play(g);
        Need(E<YuandiNameRemovedEvent>(g) is [{ Name: CardKind.Peach, BeforeMask: 7, AfterMask: 3 }] &&
            E<XieLingYuCompletedEvent>(g).Count(e => e.FrameId == paid.FrameId && e.Paid && e.Requests == 1) == 1,
            "The completed first benefit permanently changes only the current name group and returns once.");
        var repeat = new[] { Hand(g, CardKind.Slash), Hand(g, CardKind.Dodge) };
        Use(g, "drop-two", repeat); Play(g);
        Need(E<YuandiPaidEvent>(g).Length == 1 && Moves(g, YDraw).Length == 1,
            "A second complete reduced group in the same actual turn cannot issue a second benefit.");
        NextOwnTurn(g); Stock(g);
        Use(g, "drop-two", [Hand(g, CardKind.Slash), Hand(g, CardKind.Dodge)]);
        Reach(g, () => Branch(g, "remove-name"));
        var second = E<YuandiPaidEvent>(g).Last();
        Need(second.ActualTurn > paid.ActualTurn && second.NamesMask == 3 && second.NonUse &&
            P(g)!.Choices.Where(c => c.Parameters.GetValueOrDefault("branch") == "remove-name")
                .Select(c => c.Parameters["name"]).Order().SequenceEqual(new[] { "Dodge", "Slash" }),
            "The next actual turn uses the permanent two-name group; Peach cannot reappear as a removal choice.");
        g = Cold(g, r); Remove(g, CardKind.Dodge); Play(g);
        Need(E<YuandiNameRemovedEvent>(g).Last() is { BeforeMask: 3, AfterMask: 1, Name: CardKind.Dodge },
            "The second real benefit permanently reduces the group to one name.");
        NextOwnTurn(g); Use(g, "drop-one", [Hand(g, CardKind.Slash)]); Play(g);
        Need(E<YuandiPaidEvent>(g).Length == 3 && E<YuandiPaidEvent>(g).Last().NamesMask == 1 &&
            E<YuandiNameRemovedEvent>(g).Length == 2 && Moves(g, YDraw).Length == 3 &&
            E<YuandiPaidEvent>(g).Select(e => e.ActualTurn).Distinct().Count() == 3,
            "The final singleton still rewards once in its own actual turn but can never remove its last name.");
        _ = Cold(g, r);
    }

    public static void YuandiActualTurnResetNativeUsesAndPlayedBoundariesCold()
    {
        {
            var (g, r) = Start("true-uses"); Stock(g); Use(g, "wound"); Play(g);
            var slash = Hand(g, CardKind.Slash); Native(g, a => a.CardId == slash && a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1])); Play(g);
            var peach = Hand(g, CardKind.Peach); Native(g, a => a.CardId == peach && a.Kind == LegalActionKind.Peach); Play(g);
            var dodge = Hand(g, CardKind.Dodge); Incoming(g);
            Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
            var incoming = g.ResolutionStack.OfType<CardUseFrame>().Last(f => f.CardKind == CardKind.Slash && f.TargetSeats.Contains(0));
            var incomingAction = incoming.Action!.ActionId; var hp = V(g).Hp;
            g = Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([dodge])); Play(g);
            var actual = E<YuandiDiscardEnteredEvent>(g).Where(e => new[] { slash, peach, dodge }.Contains(e.CardId)).ToArray();
            Need(actual.Length == 3 && actual.All(e => e.FromTrueUse) && E<YuandiPaidEvent>(g) is [{ NonUse: false }] &&
                E<YuandiNameRemovedEvent>(g).Length == 0 && Moves(g, YDraw).Length == 1 &&
                E<CardRespondedEvent>(g).Count(e => e.CardId == dodge && e.ResponderSeat == 0 && e.SourceSeat == incoming.SourceSeat && e.EffectiveCardKind == CardKind.Dodge) == 1 &&
                E<CardActionAcceptedEvent>(g).Count(e => e.Action.Type == CardActionType.Response && e.Action.EffectiveKind == CardKind.Dodge &&
                    e.Action.ActorSeat == 0 && e.Action.ProviderSeat == 0 && e.Action.ResponderSeat == 0 && e.Action.RequesterSeat is null &&
                    e.Action.ParentActionId == incomingAction && e.Action.OpponentSeat == incoming.SourceSeat &&
                    e.Action.PhysicalCards is [{ CardId: var id, From: var from }] && id == dodge && from == CardLocation.Hand(0)) == 1 &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == incoming.Id) == 1 && V(g).Hp == hp,
                "Physical Slash, recovery Peach and genuine defensive Dodge complete one all-Use group: Draw1, with no name removal.");
            _ = Cold(g, r);
        }
        {
            var (g, r) = Start("played"); Stock(g); var slash = Hand(g, CardKind.Slash);
            Use(g, "duel", targets: [1]); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
            var duel = g.ResolutionStack.OfType<CardUseFrame>().Last(f => f.CardKind == CardKind.Duel);
            Need(duel.SourceSeat == 1 && duel.TargetSeats.Contains(0), "The actual foreign Duel requests a played Slash from the human defender.");
            g = Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([slash])); Play(g);
            Need(E<DuelResponseEvent>(g).Any(e => e.ResolutionId == duel.Id && e.ResponderSeat == 0 && e.UsedSlash && e.SlashCardId == slash) &&
                E<YuandiDiscardEnteredEvent>(g).Single(e => e.CardId == slash).FromTrueUse == false,
                "A real Duel Slash enters Discard as Played, not as another card Use.");
            Use(g, "drop-two", [Hand(g, CardKind.Dodge), Hand(g, CardKind.Peach)]);
            Reach(g, () => Branch(g, "remove-name"));
            Need(E<YuandiPaidEvent>(g) is [{ NonUse: true }] && Moves(g, YDraw).Length == 1,
                "The first full group containing the genuine played Slash admits mandatory removal after its native draw.");
            g = Cold(g, r); Remove(g, CardKind.Peach); Play(g);
        }
        {
            var (g, r) = Start("partial-turn"); Stock(g);
            Use(g, "drop-two", [Hand(g, CardKind.Slash), Hand(g, CardKind.Dodge)]); Play(g);
            Need(E<YuandiPaidEvent>(g).Length == 0, "Two printed names alone do not prematurely complete the first group.");
            var old = g.State.TurnNumber; NextOwnTurn(g); Use(g, "drop-one", [Hand(g, CardKind.Peach)]); Play(g);
            Need(g.State.TurnNumber > old && E<YuandiPaidEvent>(g).Length == 0,
                "Old-turn Slash/Dodge history does not combine with a new-turn Peach.");
            Stock(g, "stock-rest"); Use(g, "drop-two", [Hand(g, CardKind.Slash), Hand(g, CardKind.Dodge)]);
            Reach(g, () => Branch(g, "remove-name"));
            Need(E<YuandiPaidEvent>(g).Single().ActualTurn == g.State.TurnNumber && E<YuandiPaidEvent>(g).Single().NamesMask == 7,
                "The complete current actual-turn trio independently creates its first opportunity.");
            Remove(g, CardKind.Dodge); Play(g); _ = Cold(g, r);
        }
        ConvertedAndGroupResponseOrigins();
    }

    private static void ConvertedAndGroupResponseOrigins()
    {
        {
            var (g, r) = Start("converted-null"); Stock(g); var material = Hand(g, CardKind.Dodge);
            Use(g, "duel", targets: [1]); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.Nullification });
            var duel = g.ResolutionStack.OfType<CardUseFrame>().Last(f => f.CardKind == CardKind.Duel);
            var parentAction = duel.Action!.ActionId;
            var prompt = P(g)!;
            Need(prompt.Choices.Any(c => c.Cards.SequenceEqual([material]) && c.Parameters.GetValueOrDefault("conversion-skill-id") == Driver),
                "The real native counterspell window publishes this original black Basic's allowed conversion.");
            g = Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([material]) && c.Parameters.GetValueOrDefault("conversion-skill-id") == Driver); Play(g);
            Need(E<YuandiDiscardEnteredEvent>(g).Single(e => e.CardId == material) is { PrintedKind: CardKind.Dodge, Name: CardKind.Dodge, FromTrueUse: true } &&
                g.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Nullification) == 1 &&
                g.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.NullificationFinished) == 1 &&
                E<CardRespondedEvent>(g).Count(e => e.CardId == material && e.ResponderSeat == 0 && e.SourceSeat == duel.SourceSeat && e.EffectiveCardKind == CardKind.Nullification) == 1 &&
                E<CardActionAcceptedEvent>(g).Count(e => e.Action.Type == CardActionType.Response && e.Action.EffectiveKind == CardKind.Nullification &&
                    e.Action.ActorSeat == 0 && e.Action.ProviderSeat == 0 && e.Action.ParentActionId == parentAction &&
                    e.Action.PhysicalCards is [{ CardId: var id, CardKind: CardKind.Dodge }] && id == material) == 1 &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == duel.Id) == 1 &&
                E<DuelResponseEvent>(g).All(e => e.ResolutionId != duel.Id),
                "The single native Nullification's late response action still proves a true Use of its original printed Dodge material.");
            _ = Cold(g, r);
        }
        foreach (var recast in new[] { false, true })
        {
            var (g, r) = Start(recast ? "chain-recast" : "chain-use"); Stock(g); var material = Hand(g, CardKind.Dodge);
            var action = g.GetHumanLegalActions().First(a => a.CardId == material && a.ConversionSource?.SkillId == "classic:lianhuan" &&
                a.Kind == (recast ? LegalActionKind.Recast : LegalActionKind.IronChain) && (recast || a.TargetSeats.SequenceEqual([1])));
            if (recast) Accept(g, new RecastCardCommand(0, material, g.Revision, P(g)!.PromptId) { ConversionSource = action.ConversionSource });
            else Native(g, a => a.CardId == material && a.Kind == LegalActionKind.IronChain &&
                a.ConversionSource == action.ConversionSource && a.TargetSeats.SequenceEqual(action.TargetSeats));
            Play(g);
            Need(E<YuandiDiscardEnteredEvent>(g).Single(e => e.CardId == material) is { PrintedKind: CardKind.Dodge, Name: CardKind.Dodge } entered &&
                entered.FromTrueUse == !recast &&
                (recast ? E<CardRecastEvent>(g).Any(e => e.CardId == material) && E<CardUseDeclaredEvent>(g).All(e => e.CardId != material) :
                    E<CardUseDeclaredEvent>(g).Any(e => e.CardId == material && e.CardKind == CardKind.IronChain) &&
                    g.CardMovements.Any(m => m.CardId == material && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.IronChainUse)),
                "The same allowed printed Basic/club Lianhuan route distinguishes actual IronChainUse from genuine recast rather than guessing from card name.");
            _ = Cold(g, r);
        }
        {
            var (g, r) = Start("arrows"); Stock(g); var material = Hand(g, CardKind.Dodge); End(g);
            Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
            var group = g.ResolutionStack.OfType<CardUseFrame>().Last(f => f.CardKind == CardKind.ArrowBarrage);
            Need(group.SourceSeat != 0 && g.ResolutionStack.OfType<ResponseWindowFrame>().Last().IncomingCard == CardKind.ArrowBarrage,
                "The actual foreign ArrowBarrage asks for a played Dodge, not the defender's own use against a Slash.");
            g = Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([material]));
            Reach(g, () => E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == group.Id));
            Need(E<YuandiDiscardEnteredEvent>(g).Single(e => e.CardId == material).FromTrueUse == false &&
                E<GroupResponseEvent>(g).Any(e => e.ResolutionId == group.Id && e.ResponderSeat == 0 && e.UsedResponse && e.ResponseCardId == material),
                "The real AOE's physical Dodge is marked as Played even though its printed name matches a true Slash-defense Dodge.");
            _ = Cold(g, r);
        }
    }

    public static void XinyouActualDrawNamesFiveLimitAndExemptHandCardsCold()
    {
        {
            var (g, r) = Start("new-name"); Stock(g); Stock(g, "stock-rest");
            Use(g, "drop-one", [Hand(g, CardKind.Peach)]); Play(g);
            var top = Hand(g, CardKind.Peach); Use(g, "top", [top]); Play(g);
            var cost = Hand(g, CardKind.Slash); BeginXinyou(g); var p = P(g)!; var unchanged = State(g);
            Need(!Branch(g, "pay") && !g.Submit(new AnswerPromptCommand(0, p.PromptId, new ChoiceId("xly.unpublished-pay"), g.Revision)).Accepted &&
                State(g) == unchanged && E<XinyouPaidEvent>(g).Length == 0,
                "Opening the draft does not pay a quota, and zero selected Basics cannot forge the unpublished payment.");
            AssertPrivate(g, 0); g = Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "cancel"); Play(g);
            Need(E<XinyouPaidEvent>(g).Length == 0 && g.GetHumanLegalActions().Any(a => a.SkillId == Xinyou),
                "A real unpaid cancellation preserves both original entities and the current Play allowance.");
            BeginXinyou(g); Cost(g, cost); Pay(g); Play(g);
            var paid = E<XinyouPaidEvent>(g).Single(); var draws = E<XieLingYuDrawReturnedEvent>(g).Where(e => e.FrameId == paid.FrameId).ToArray();
            Need(paid.MaterialCount == 1 && paid.NamesMask == 1 && draws is [{ Request: 1, ActualCount: 1, ReplacementCount: 0, Stop: true }] &&
                E<XinyouGainedEntityEvent>(g).Where(e => e.FrameId == paid.FrameId).Select(e => e.CardId).SequenceEqual([top]) &&
                Zone(g, top) == CardLocation.Hand(0) && CostMoves(g, paid.FrameId).Select(m => m.CardId).SequenceEqual([cost]) &&
                !g.GetHumanLegalActions().Any(a => a.SkillId == Xinyou),
                "A real newly named Peach stops after one actual Draw1; a prior external Peach discard cannot enlarge this activation's frozen Slash-only cost set.");
            _ = Cold(g, r);
        }
        {
            var (g, r) = Start("five"); Stock(g, "stock-six"); var original = V(g).Hand.Select(c => c.Id).ToArray();
            BeginXinyou(g); Cost(g, original[0]); Cost(g, original[1]); g = Cold(g, r); Pay(g); Play(g);
            var paid = E<XinyouPaidEvent>(g).Single(); var gains = E<XinyouGainedEntityEvent>(g).Where(e => e.FrameId == paid.FrameId).ToArray();
            Need(paid.MaterialCount == 2 && paid.NamesMask == 2 && CostMoves(g, paid.FrameId).Length == 2 &&
                E<XieLingYuDrawReturnedEvent>(g).Where(e => e.FrameId == paid.FrameId).Select(e => e.Request).SequenceEqual([1, 2, 3, 4, 5]) &&
                gains.Length == 5 && gains.All(e => !e.Replacement && e.PrintedKind == CardKind.Dodge) && V(g).HandCount == 9 &&
                E<XieLingYuCompletedEvent>(g).Single(e => e.FrameId == paid.FrameId) is { Paid: true, Requests: 5 },
                "Two originals pay one atomic initial batch; repeated same-name cards cause five Draw1 requests without any repeated discard cost.");
            var turn = g.State.TurnNumber; var beforeDiscard = g.CardMovements.Last().Sequence; var handLimit = V(g).HandLimit;
            Need(handLimit == 4, "The real classic Lord has its native HP-based four-card Hand limit before any discard boundary.");
            g = Cold(g, r); End(g);
            Reach(g, () => E<TurnEndedEvent>(g).Any(e => e.TurnNumber == turn && e.ActorSeat == 0));
            Need(V(g).HandCount == 9 && V(g).HandCount > handLimit!.Value &&
                !g.CardMovements.Any(m => m.Sequence > beforeDiscard && m.From == CardLocation.Hand(0) && m.Reason == CardMoveReasons.HandLimitDiscard) &&
                gains.All(e => Zone(g, e.CardId) == CardLocation.Hand(0)),
                "The five exact obtained entities genuinely do not count at this turn's discard boundary; the four older Hand cards still do.");
            Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } && g.State.TurnNumber > turn);
            End(g); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards });
            Need(P(g)!.RequiredCardCount == 5 && gains.All(e => P(g)!.ValidCardIds.Contains(e.CardId)),
                "The same still-held entities count again in the next actual turn without moving away or losing the skill.");
            g = Cold(g, r); var discard = P(g)!;
            Accept(g, new DiscardCardsCommand(0, discard.ValidCardIds.Take(discard.RequiredCardCount).ToArray(), discard.PromptId, g.Revision));
        }
        {
            var (g, r) = Start("nonbasic"); Stock(g, "stock-thirteen"); var top = V(g).Hand.First(c => c.Kind == CardKind.Crossbow).Id;
            Use(g, "top", [top]); Play(g); BeginXinyou(g); Cost(g, Hand(g, CardKind.Dodge)); Pay(g); Play(g);
            var paid = E<XinyouPaidEvent>(g).Single(); var returned = E<XieLingYuDrawReturnedEvent>(g).Where(e => e.FrameId == paid.FrameId).ToArray();
            Need(returned.Length == 5 && returned[0] is { ActualCount: 1, Stop: false } &&
                E<XinyouGainedEntityEvent>(g).First(e => e.FrameId == paid.FrameId).CardId == top &&
                E<XinyouGainedEntityEvent>(g).Where(e => e.FrameId == paid.FrameId).All(e => e.PrintedKind is CardKind.Dodge or CardKind.Crossbow),
                "A real nonbasic draw is obtained and exempted but cannot satisfy the new-Basic-name stopping condition.");
            _ = Cold(g, r);
        }
    }

    public static void XinyouPaidChildrenReplacementSourceLossDeathAndInputPrivacyCold()
    {
        foreach (var scene in new[] { "paid-child", "paid-loss", "paid-death" })
        {
            var (g, r) = Start(scene); Stock(g, "stock-six"); var cost = Hand(g, CardKind.Dodge);
            BeginXinyou(g); Cost(g, cost); Pay(g); Reach(g, () => Option(g, Watch, "paid"));
            var paid = E<XinyouPaidEvent>(g).Single(); var frame = Frame(g, Xinyou);
            Need(frame.XieLingYu is { Paid: true, Stage: XieLingYuStage.CostChildren, Requests: 0 } receipt &&
                receipt.Materials is System.Collections.IList { IsReadOnly: true } && receipt.Materials.Single().CardId == cost &&
                CostMoves(g, paid.FrameId).Length == 1 && E<XieLingYuDrawReturnedEvent>(g).All(e => e.FrameId != paid.FrameId),
                "The original atomic Basic discard owns its frozen receipt before a real movement child and before any Draw1 request.");
            AssertPrivate(g, 0); g = Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "paid");
            if (scene == "paid-child")
            {
                for (var request = 1; request <= 5; request++)
                {
                    Reach(g, () => Option(g, Watch, "gained"));
                    var returned = E<XieLingYuDrawReturnedEvent>(g).Where(e => e.FrameId == paid.FrameId).ToArray();
                    Need(returned.Length == request && returned.Last().Request == request &&
                        E<XinyouGainedEntityEvent>(g).Count(e => e.FrameId == paid.FrameId) == request &&
                        E<XieLingYuCompletedEvent>(g).All(e => e.FrameId != paid.FrameId) && CostMoves(g, paid.FrameId).Length == 1,
                        "Each real gain child finishes before another request or completion; the paid initial cost is never repeated.");
                    AssertPrivate(g, 0); g = Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "gained");
                }
                Play(g);
            }
            else Reach(g, () => E<XieLingYuCompletedEvent>(g).Any(e => e.FrameId == paid.FrameId));
            var complete = E<XieLingYuCompletedEvent>(g).Single(e => e.FrameId == paid.FrameId);
            Need(complete.Paid && CostMoves(g, paid.FrameId).Length == 1 && !g.ResolutionStack.Any(f => f.Id == paid.FrameId),
                "The exact paid parent drains its native descendants and returns once, even across cold recovery.");
            if (scene == "paid-loss") Need(V(g).IsAlive && !Has(g, Xinyou) && complete.Requests == 5 &&
                E<XinyouGainedEntityEvent>(g).Count(e => e.FrameId == paid.FrameId) == 5,
                "Genuine conditional source loss does not revoke the already-paid frozen loop.");
            if (scene == "paid-death") Need(!V(g).IsAlive && complete.Requests == 0 &&
                E<XinyouGainedEntityEvent>(g).All(e => e.FrameId != paid.FrameId) && Zone(g, cost) == CardLocation.DiscardPile,
                "Real cost-child death keeps its one payment but starts no draw or fresh private choice for the deceased owner.");
            _ = Cold(g, r);
        }
        {
            var (g, r) = Start("replacement"); Stock(g);
            var given = new[] { Hand(g, CardKind.Dodge), Hand(g, CardKind.Peach) }; Use(g, "give", given, [1]); Play(g);
            var cost = Hand(g, CardKind.Slash); BeginXinyou(g); Cost(g, cost); Pay(g);
            Reach(g, () => Optional(g, Zenrun)); var original = Frame(g, Xinyou).Id;
            var attempt = g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(a => a.Context.ParentFrameId == original);
            Need(attempt.Context.RequestedCount == 1 && attempt.Context.Reason.Value == XDraw && Moves(g, XDraw).Length == 0,
                "Production Zenrun is offered on Xinyou's genuine first native Draw1, before DrawPile material is moved.");
            g = Cold(g, r); Activate(g); Reach(g, () => Branch(g, "target"));
            Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "target" && c.Targets.SequenceEqual([1]));
            Reach(g, () => Branch(g, "pick"));
            Need(P(g)!.Choices.Where(c => c.Parameters.GetValueOrDefault("branch") == "pick").All(c => c.Cards.Count == 0) &&
                g.CreateSnapshot(0).Players[1].Hand.Count == 0,
                "The replacement offers actual foreign Hand slots without revealing their faces to the requester.");
            Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "pick" && c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
            Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "take");
            Reach(g, () => Option(g, Obtained, "obtained"));
            var replacement = g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(a => a.Id == attempt.Id).Replacement!;
            Need(replacement.GainedIds is [var id] && given.Contains(id) && Moves(g, XDraw).Length == 0 &&
                E<XieLingYuDrawReturnedEvent>(g).All(e => e.FrameId != original),
                "The real obtained entity and its child remain separate from the unpaid original DrawPile material result.");
            AssertPrivate(g, 0); g = Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "obtained");
            Reach(g, () => g.CreateSnapshot(1).PendingDecision?.SkillPrompt?.SkillId == Zenrun);
            AssertPrivate(g, 1); g = Cold(g, r); Accept(g, new AdvanceOneStepCommand(g.Revision)); Play(g);
            var returned = E<XieLingYuDrawReturnedEvent>(g).Single(e => e.FrameId == original);
            var gained = E<XinyouGainedEntityEvent>(g).Single(e => e.FrameId == original);
            Need(returned is { Request: 1, ActualCount: 0, ReplacementCount: 1, Stop: true } && gained.Replacement &&
                replacement.GainedIds.SequenceEqual([gained.CardId]) && given.Contains(gained.CardId) &&
                gained.PrintedKind is CardKind.Dodge or CardKind.Peach && Zone(g, gained.CardId) == CardLocation.Hand(0) &&
                Moves(g, XDraw).Length == 0 && CostMoves(g, original).Select(m => m.CardId).SequenceEqual([cost]) &&
                E<NativeDrawAttemptDecidedEvent>(g).Count(e => e.AttemptFrameId == attempt.Id && e.Replaced) == 1 &&
                E<XieLingYuCompletedEvent>(g).Single(e => e.FrameId == original) is { Paid: true, Requests: 1 },
                "One real replacement Basic stops the Slash-cost loop and receives its actual-gain identity without forging an original draw ID or repeating either payment.");
            End(g); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards });
            Need(P(g)!.RequiredCardCount == 1 && !P(g)!.ValidCardIds.Contains(gained.CardId),
                "The actual replacement-obtained entity is also excluded from this turn's genuine discard payment.");
            _ = Cold(g, r);
        }
    }

    private static CardKind Name(CardKind kind) => kind is CardKind.FireSlash or CardKind.ThunderSlash ? CardKind.Slash : kind;
    private static int Hand(GameEngine g, CardKind kind) => V(g).Hand.FirstOrDefault(c => Name(c.Kind) == kind)?.Id ??
        throw new InvalidOperationException("Missing actual Hand " + kind + " " + Diagnostic(g));
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static CardLocation Zone(GameEngine g, int id) => g.CreateCardZoneDiagnostics().Single(z => z.CardId == id).Location;
    private static bool Has(GameEngine g, string skill) => V(g).Skills!.Any(s => s.Id == skill);
    private static ProgramSkillFrame Frame(GameEngine g, string skill) => g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.SkillId == skill && f.XieLingYu is not null);
    private static CardMovementRecord[] Moves(GameEngine g, string reason) => g.CardMovements.Where(m => m.Reason.Value == reason && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)).ToArray();
    private static CardMovementRecord[] CostMoves(GameEngine g, long frame) => g.CardMovements.Where(m => m.Reason.Value == XCost &&
        E<XinyouMaterialDiscardedEvent>(g).Any(e => e.FrameId == frame && e.CardId == m.CardId && e.MovementSequence == m.Sequence)).ToArray();
    private static bool Branch(GameEngine g, string branch) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == branch) == true;
    private static bool Optional(GameEngine g, string skill) => P(g)?.SkillPrompt?.SkillId == skill && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool Option(GameEngine g, string skill, string option) => P(g) is { } p && p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == option);
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Cost(GameEngine g, int id) => Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "cost" && c.Cards.SequenceEqual([id]));
    private static void Pay(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "pay");
    private static void Remove(GameEngine g, CardKind kind) => Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "remove-name" && c.Parameters.GetValueOrDefault("name") == kind.ToString());
    private static void BeginXinyou(GameEngine g)
    {
        var action = g.GetHumanLegalActions().Single(a => a.SkillId == Xinyou);
        Accept(g, new UseProgramSkillCommand(0, Xinyou, action.ProgramActivationId!, [], [], g.Revision, P(g)!.PromptId));
        Reach(g, () => Branch(g, "cost"));
    }
    private static void Stock(GameEngine g, string id = "stock-nine") { Use(g, id); Play(g); }
    private static void Use(GameEngine g, string id, int[]? cards = null, int[]? targets = null) =>
        Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Native(GameEngine g, Func<LegalAction, bool> match)
    {
        var action = g.GetHumanLegalActions().FirstOrDefault(match) ?? throw new InvalidOperationException("Missing physical native action " + Diagnostic(g));
        Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId, action.PlayedCardKind, action.TargetCardId)
        { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources ?? [] });
    }
    private static void Incoming(GameEngine g)
    {
        Use(g, "incoming", targets: [1]);
        Reach(g, () => P(g) is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("offer-option") == "target-0"));
        Answer(g, c => c.Targets.SequenceEqual([0]));
    }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void NextOwnTurn(GameEngine g) { var turn = g.State.TurnNumber; End(g); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } && g.State.TurnNumber > turn); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> test)
    {
        var prompt = P(g) ?? throw new InvalidOperationException("No published decision " + Diagnostic(g));
        var choice = prompt.Choices.FirstOrDefault(test);
        Need(prompt.PlayerSeat == 0 && choice is not null, "Only the human's actual published choice can be answered.");
        Accept(g, new AnswerPromptCommand(0, prompt.PromptId, choice!.Id, g.Revision));
    }
    private static void AssertPrivate(GameEngine g, int seat) => Need(g.CreateSnapshot(seat).PendingDecision is { IsPrivate: true } p && p.PlayerSeat == seat &&
        Enumerable.Range(0, 4).Where(s => s != seat).All(s => g.CreateSnapshot(s).PendingDecision is null), "Every pending private selection belongs only to its real chooser.");
    private static void Reach(GameEngine g, Func<bool> done, [CallerMemberName] string caller = "", [CallerLineNumber] int line = 0)
    {
        for (var i = 0; i < 180; i++)
        {
            if (done()) return;
            Need(g.State.Winner == Winner.None, "Ended before " + caller + ":" + line);
            var prompt = P(g);
            if (prompt is null || prompt.PlayerSeat != 0) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (prompt.Kind == DecisionKind.PlayCard) End(g);
            else if (prompt.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification)
                Answer(g, c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("response") is not ("faction-defense-request" or "faction-slash-request"));
            else if (prompt.Kind == DecisionKind.RescueDying) Answer(g, c => c.Parameters.GetValueOrDefault("response") is "let-die" or "pass");
            else if (prompt.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else throw new InvalidOperationException("Unexpected " + caller + ":" + line + " " + Diagnostic(g));
        }
        throw new InvalidOperationException("Missing bounded " + caller + ":" + line + " " + Diagnostic(g));
    }
    private static void Accept(GameEngine g, GameCommand command)
    {
        try { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, "Rejected " + command.GetType().Name + ": " + JsonSerializer.Serialize(result)); }
        catch (Exception error) { throw new InvalidOperationException("scene=" + Scene + " command=" + command.GetType().Name + " cause=" + error + " " + Diagnostic(g), error); }
    }
    private static void Need(bool yes, string text)
    { if (!yes) throw new InvalidOperationException("scene=" + Scene + " " + text + (DiagnosticContext is { } g ? " " + Diagnostic(g) : "")); }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { Scene, g.State.TurnNumber, g.State.CurrentSeat, g.State.Phase,
        Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).Hp, V(g, s).MaxHp, V(g, s).IsAlive, V(g, s).HandCount }),
        Prompt = P(g), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
        Paid = E<XinyouPaidEvent>(g), Draws = E<XieLingYuDrawReturnedEvent>(g), Yuan = E<YuandiPaidEvent>(g) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), g.CardMovements, Zones = g.CreateCardZoneDiagnostics(),
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
        Need(State(g) == State(restored), "Cold replay preserves all private drafts, printed-name history, paid native draws and exact material cursors.");
        DiagnosticContext = restored; return restored;
    }
    private static (GameEngine, ContentRegistry) Start(string scene)
    {
        Scene = scene; DiagnosticContext = null; ContentRegistry registry;
        try { registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene)); }
        catch (Exception error) { throw new InvalidOperationException("scene=" + scene + " fixture cause=" + error, error); }
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0,
            HumanRole = scene == "paid-death" ? Role.Renegade : Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = scene is "five" or "replacement", AdvanceAfterHumanCommands = false, MaxTurns = 14 }, registry);
        DiagnosticContext = g; Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-xie-ling-yu", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:xie-ling-yu");
            if (scene == "replacement") RegisterProductionSkill(b, "ol-sun-hong", Zenrun);
            if (scene is "chain-use" or "chain-recast") CharacterFixtureContent.Register(b, "classic:pang-tong");
            static JsonNode N(string json) => JsonNode.Parse(json)!;
            static JsonObject A(string id, string effects, int cards = 0, int targets = 0, int? maximumCards = null)
            {
                var activation = new JsonObject { ["id"] = id, ["minCards"] = cards, ["maxCards"] = maximumCards ?? cards,
                    ["minTargets"] = targets, ["maxTargets"] = targets, ["targetKind"] = "anyLiving", ["usesPerTurn"] = null,
                    ["effects"] = N("[" + effects + "]") };
                if ((maximumCards ?? cards) > 0) activation["sourceZones"] = N("[\"hand\"]");
                return activation;
            }
            var definitions = new JsonArray();
            var duel = A("duel", """{"op":"useSelectedActorDuel","target":"owner"}""", targets: 1);
            duel["targetKind"] = "otherLiving"; duel["usesPerPhase"] = 2;
            var driver = new JsonObject { ["id"] = Driver, ["revision"] = 1, ["activations"] = new JsonArray(
                A("stock-nine", """{"op":"draw","target":"owner","amount":9}"""), A("stock-rest", """{"op":"draw","target":"owner","amount":3}"""),
                A("stock-six", """{"op":"draw","target":"owner","amount":6}"""), A("stock-thirteen", """{"op":"draw","target":"owner","amount":13}"""),
                A("drop-one", """{"op":"discardSelected","target":"owner","amount":1}""", 1), A("drop-two", """{"op":"discardSelected","target":"owner","amount":2}""", 2),
                A("drop-three", """{"op":"discardSelected","target":"owner","amount":3}""", 3),
                A("top", """{"op":"captureSelectedCards","target":"owner","resultBind":"top"},{"op":"moveBoundCards","target":"owner","sourceBind":"top","destination":"drawPileTop","awaitMovementTriggers":true}""", 1),
                A("wound", """{"op":"loseHp","target":"owner","amount":2}"""),
                A("give", """{"op":"giveSelected","target":"selectedTarget"}""", 2, 1),
                A("incoming", """{"op":"offerVirtualSlashOrDraw","target":"selectedTarget"}""", targets: 1), duel) };
            if (scene is not ("five" or "replacement")) driver["modifiers"] = N("""[{"id":"keep","query":"handLimit","operation":"add","value":30,"priority":0}]""");
            if (scene == "converted-null") driver["viewAs"] = N("""[{"id":"native-counterspell","inputKinds":["dodge"],"inputSuits":["club"],"sourceZones":["hand"],"outputKind":"nullification","forPlay":false,"forResponse":true}]""");
            definitions.Add(driver);
            definitions.Add(N("{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"));
            if (scene == "paid-loss") definitions.Add(N("{\"id\":\"" + Conditional + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"healthy-source\",\"skillId\":\"ol:xinyou\",\"minimumHp\":9}]}"));
            if (scene.StartsWith("paid-", StringComparison.Ordinal))
            {
                var effects = new JsonArray(N("""{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"paid"}]}"""));
                if (scene != "paid-child") effects.Add(new JsonObject { ["op"] = "loseHp", ["target"] = "owner", ["amount"] = scene == "paid-death" ? 20 : 1 });
                var triggers = new JsonArray(new JsonObject { ["id"] = "real-paid-cost", ["window"] = "cardsMoved", ["subject"] = "owner",
                    ["sourceZones"] = N("[\"hand\"]"), ["movementOccurrence"] = "perBatch", ["movementReasons"] = new JsonArray(XCost), ["optional"] = false, ["effects"] = effects });
                if (scene == "paid-child") triggers.Add(new JsonObject { ["id"] = "real-draw-child", ["window"] = "cardsGained", ["subject"] = "owner",
                    ["destinationZones"] = N("[\"hand\"]"), ["movementOccurrence"] = "perBatch", ["movementReasons"] = new JsonArray(XDraw), ["optional"] = false,
                    ["effects"] = N("""[{"op":"chooseOption","target":"owner","resultBind":"gained","options":[{"id":"gained"}]}]""") });
                definitions.Add(new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = triggers });
            }
            if (scene == "replacement") definitions.Add(new JsonObject { ["id"] = Obtained, ["revision"] = 1, ["triggers"] = new JsonArray(new JsonObject {
                ["id"] = "real-replacement-gain", ["window"] = "cardsGained", ["subject"] = "owner", ["destinationZones"] = N("[\"hand\"]"),
                ["movementOccurrence"] = "perBatch", ["movementReasons"] = N("[\"skill-program.sun-hong.zenrun.obtain\"]"), ["optional"] = false,
                ["effects"] = N("""[{"op":"chooseOption","target":"owner","resultBind":"obtained","options":[{"id":"obtained"}]}]""") }) });
            if (scene == "arrows") definitions.Add(new JsonObject { ["id"] = Assault, ["revision"] = 1, ["triggers"] = N("""[{"id":"actual-native-arrows","window":"turnStartBeforeNormalFlow","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"owner"},"targetRef":{"kind":"owner"},"outputKind":"arrowBarrage"}]}]""") });
            var labels = definitions.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "真实原生入弃/连续Draw1行为夹具",
                optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray())
                    .SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(x => x, x => x) });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "真实元嫡/心幽夹具") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            var own = new List<string> { Driver, Yuandi, scene == "paid-loss" ? Conditional : Xinyou };
            if (scene.StartsWith("paid-", StringComparison.Ordinal)) own.Add(Watch);
            if (scene == "replacement") { own.Add(Zenrun); own.Add(Obtained); }
            if (scene is "chain-use" or "chain-recast") own.Add("classic:lianhuan");
            b.AddSkill(new("fixture:xly-own-pick", "本人", "固定公开选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => -100000000d) });
            b.AddGeneral(new(Owner, "真实谢灵毓机制", "ol-xie-ling-yu", "fixture:xly-own-pick", "wu",
                scene == "five" ? 3 : scene == "replacement" ? 4 : scene.StartsWith("paid-", StringComparison.Ordinal) ? 8 : 12, own, GeneralGender.Female));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:xly-peer-" + i).ToArray();
            for (var i = 0; i < peers.Length; i++)
            {
                var pick = "fixture:xly-peer-pick-" + i;
                b.AddSkill(new(pick, "其他角色", "固定公开选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 1000000d * (3 - i)) });
                b.AddGeneral(new(peers[i], "真实其他角色", "supporter", pick, "shu", 12, scene == "arrows" ? [Quiet, Assault] : [Quiet], GeneralGender.Male));
            }
            ContentDeckPhysicalCard[] deck = scene == "nonbasic"
                ? Enumerable.Range(0, 24).Select(i => new ContentDeckPhysicalCard(i < 12 ? "standard:dodge" : "standard:crossbow", Suit.Heart, 7)).ToArray()
                : scene == "five" || scene.StartsWith("paid-", StringComparison.Ordinal)
                    ? Enumerable.Range(0, 24).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, 7)).ToArray()
                    : Enumerable.Range(0, 12).Select(i => new ContentDeckPhysicalCard(i < 4 ? "standard:slash" : i < 8 ? "standard:dodge" : "standard:peach",
                        scene is "converted-null" or "chain-use" or "chain-recast" ? Suit.Club : Suit.Heart, 7)).ToArray();
            b.AddDeck(new("fixture:xly-deck", "元嫡/心幽固定小实体牌堆", 0, 0, []) { PhysicalCards = deck });
            var roles = scene == "paid-death" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 }
                : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            b.AddMode(new(Mode, "谢灵毓真实四人边界", 4, 4, roles, "fixture:xly-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }

        private static void RegisterProductionSkill(IContentRegistryBuilder builder, string bundle, string skillId)
        {
            string Read(string suffix)
            {
                using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream(
                    "CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json")
                    ?? throw new InvalidOperationException("Missing production resource " + bundle + "." + suffix);
                using var reader = new StreamReader(stream); return reader.ReadToEnd();
            }
            var catalog = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
            var presentation = catalog.Presentations[skillId];
            builder.AddSkill(new(skillId, presentation.Name, presentation.Description)
            { Program = catalog.Programs[skillId], ProgramPresentation = presentation });
        }
    }
}
