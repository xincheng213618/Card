using System.Runtime.CompilerServices;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class LiFengChecks
{
    private const string Owner = "fixture:lf-owner", Mode = "identity:classic-li-feng";
    private const string Driver = "fixture:lf-driver", Quiet = "fixture:lf-quiet", Watch = "fixture:lf-watch";
    private const string Jiyun = "ol:jiyun", Shuliang = "ol:shuliang", Conditional = "fixture:lf-conditional";
    private const string Initial = "skill-program.li-feng.jiyun.initial", PrepDraw = "skill-program.li-feng.jiyun.draw",
        Collect = "skill-program.li-feng.jiyun.collect", Gift = "skill-program.li-feng.shuliang.gift";
    private static string Scene = "";
    private static GameEngine? Context;

    public static void JiyunInitialCategoriesPreparationLeasesAndEndingDistributionCold()
    {
        {
            var (g, r) = Start("prep", beforePrep: true);
            var initial = Pile(g).ToArray();
            Need(initial.Length == 3 && initial.Select(c => CardCatalog.Get(c.Kind).CategoryName).Distinct().Count() == 3,
                "The genuine GameStarting operation stores one original entity of each of the three categories.");
            Need(g.CardMovements.Count(m => m.Reason.Value == Initial && m.From == CardLocation.DrawPile &&
                m.To.Zone == CardZoneKind.PublicPersistentPile && initial.Any(c => c.Id == m.CardId)) == 3,
                "Each public original grain has one actual Deck-to-pile initial invoice.");
            AssertPublicPile(g, initial.Select(c => c.Id));
            AssertPrivate(g, 0); g = Cold(g, r);
            Prepare(g, [1, 0]);
            Reach(g, () => Option(g, "prep-gained"));
            var gains = g.CardMovements.Where(m => m.Reason.Value == PrepDraw && m.From == CardLocation.DrawPile &&
                m.To.Zone == CardZoneKind.Hand).ToArray();
            Need(gains.Length == 2 && gains.Select(m => m.To.OwnerSeat).Order().SequenceEqual(new int?[] { 0, 1 }) &&
                V(g).Hand.Count == 1 && V(g, 1).Hand.Count == 1,
                "Two distinct selected recipients each receive exactly one actual draw before the final gain child returns.");
            var ownMarked = V(g).Hand.Single();
            var peerMarked = V(g, 1).Hand.Single();
            Need(Marked(ownMarked) && Marked(peerMarked), "Only the exact actual Prep draw entities receive continuous Hand leases.");
            Need(Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).Players[0].Hand.Count == 0),
                "A public grain pile does not expose its recipient's private newly drawn Hand card.");
            g = Cold(g, r); Continue(g); Play(g);
            var grainSlash = initial.Single(c => c.Kind == CardKind.Slash);
            PlayOriginalGrain(g, grainSlash.Id, [1]); Play(g);
            AssertOriginalUse(g, grainSlash, 0);
            Need(Pile(g).Count == 2, "A genuine original grain Use first frees one real pile slot.");
            PlayHand(g, ownMarked.Id, [1]); Play(g);
            Need(Zone(g, ownMarked.Id).Zone == CardZoneKind.PublicPersistentPile &&
                g.CardMovements.Count(m => m.CardId == ownMarked.Id && m.Reason.Value == Collect &&
                    m.From == CardLocation.DiscardPile && m.To.Zone == CardZoneKind.PublicPersistentPile) == 1 &&
                Pile(g).Count == 3,
                "Only after the marked Hand card's whole Slash Use finishes is its exact discarded original collected once.");
            Need(g.CardMovements.Count(m => m.CardId == ownMarked.Id && m.From == CardLocation.Hand(0) &&
                m.To == CardLocation.Processing) == 1, "Collection cannot repay or substitute the original Use material.");
            var give = Pile(g).Take(2).Select(c => c.Id).ToArray();
            var retained = Pile(g).Single(c => !give.Contains(c.Id)).Id;
            End(g); Reach(g, () => Optional(g, Shuliang)); Activate(g); Reach(g, () => Branch(g, "give"));
            AssertPrivate(g, 0); g = Cold(g, r);
            GiveGrain(g, give[0], 1); Reach(g, () => Branch(g, "give"));
            g = Cold(g, r); GiveGrain(g, give[1], 1);
            Reach(g, () => Branch(g, "finish")); Answer(g, c => B(c) == "finish");
            Reach(g, () => Pile(g).Select(c => c.Id).SequenceEqual([retained]) && !Branch(g, "give") && !Branch(g, "finish"));
            Need(g.CardMovements.Count(m => give.Contains(m.CardId) && m.Reason.Value == Gift &&
                m.From.Zone == CardZoneKind.PublicPersistentPile && m.To == CardLocation.Hand(1)) == 2 &&
                give.All(id => V(g, 1).Hand.Any(c => c.Id == id && !Marked(c))),
                "Ending distribution permits an arbitrary subset and the same living recipient twice; gift gains are not Prep leases.");
            var oldTurn = g.State.TurnNumber;
            Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } && g.State.TurnNumber > oldTurn);
            Need(V(g, 1).Hand.Any(c => c.Id == peerMarked.Id && Marked(c)),
                "An untouched original recipient's lease survives subsequent actual turns.");
            Use(g, "take", targets: [1]); Reach(g, () => Action(g, "select-and-move-owned-card"));
            var peerHand = V(g, 1).Hand.Select(c => c.Id).ToArray();
            var index = Array.IndexOf(peerHand, peerMarked.Id);
            Need(index >= 0 && P(g)!.Choices.All(c => c.Cards.Count == 0),
                "The real foreign-Hand acquisition chooses an opaque slot, never a leaked original id.");
            Answer(g, c => c.Parameters.GetValueOrDefault("slot-index") == index.ToString()); Play(g);
            Need(V(g).Hand.Any(c => c.Id == peerMarked.Id && !Marked(c)),
                "The original recipient's first departure retires its lease even if another player later owns that same entity.");
            var before = Pile(g).Count;
            PlayHand(g, peerMarked.Id, [1]); Play(g);
            Need(Zone(g, peerMarked.Id) == CardLocation.DiscardPile && Pile(g).Count == before &&
                !g.CardMovements.Any(m => m.CardId == peerMarked.Id && m.Reason.Value == Collect),
                "A transferred and reused former marked card cannot be reclaimed as the original recipient's Use.");
            _ = Cold(g, r);
        }
        {
            var (g, r) = Start("prep-replacement", beforePrep: true);
            var originalVictimHand = V(g, 1).Hand.Single().Id;
            Prepare(g, [0]); Reach(g, () => Optional(g, "ol:zenrun"));
            Need(!E<JiyunLeaseIssuedEvent>(g).Any() && !g.CardMovements.Any(m => m.Reason.Value == PrepDraw),
                "The real Jiyun draw waits for the native pre-draw replacement before any Deck material or Hand lease exists.");
            AssertPrivate(g, 0); g = Cold(g, r); Activate(g);
            Reach(g, () => P(g)?.Choices.Any(c => B(c) == "target") == true);
            Answer(g, c => B(c) == "target" && c.Targets.SequenceEqual([1]));
            Reach(g, () => P(g)?.Choices.Any(c => B(c) == "pick") == true);
            Need(P(g)!.Choices.Where(c => B(c) == "pick").All(c => c.Cards.Count == 0),
                "The actual replacement obtains a foreign opaque Hand slot rather than publishing its face.");
            Answer(g, c => B(c) == "pick" && c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
            Answer(g, c => B(c) == "take");
            Reach(g, () => g.CreateSnapshot(1).PendingDecision?.SkillPrompt?.SkillId == "ol:zenrun");
            AssertPrivate(g, 1); g = Cold(g, r); Play(g);
            Need(E<JiyunPreparationDrawReturnedEvent>(g) is [{ RecipientSeat: 0, ActualCount: 0, ReplacementCount: 1 }] &&
                !E<JiyunLeaseIssuedEvent>(g).Any() && !g.CardMovements.Any(m => m.Reason.Value == PrepDraw) &&
                V(g).Hand.Any(c => c.Id == originalVictimHand && !Marked(c)),
                "Real replacement gains finish the Prep queue but never masquerade as actual Draw IDs or acquire collection leases.");
            _ = Cold(g, r);
        }
        {
            var (g, r) = Start("initial-short", beforePrep: true);
            Need(Pile(g).Count == 2 && g.CardMovements.Count(m => m.Reason.Value == Initial) == 2,
                "A truly short initial Deck stores only the two available categories without inventing a third entity.");
            Prepare(g, [0]); Play(g);
            Need(V(g).HandCount == 0 && !g.CardMovements.Any(m => m.Reason.Value == PrepDraw),
                "A real shortfall Draw1 issues no fake Hand card or collection lease.");
            End(g); Reach(g, () => Optional(g, Shuliang)); Activate(g);
            Reach(g, () => Branch(g, "finish")); g = Cold(g, r); Answer(g, c => B(c) == "finish");
            Need(Pile(g).Count == 2 && !g.CardMovements.Any(m => m.Reason.Value == Gift),
                "The genuine Ending choice may finish with no distribution while retaining all original grain.");
        }
    }

    public static void ShuliangOriginalPlayKindsAndWholeUseCollectionCold()
    {
        foreach (var scene in new[] { "play-slash", "play-trick", "play-chain", "play-equipment", "play-delayed", "converted-lease" })
        {
            var (g, r) = Start(scene, beforePrep: true);
            Prepare(g, [0]); Play(g);
            var marked = V(g).Hand.Single();
            var original = Pile(g).Single(c => c.Kind == (scene == "converted-lease" ? CardKind.DrawTwo : PrimaryKind(scene)));
            Need(Marked(marked) && marked.Kind == PrimaryKind(scene), "The real one-card Prep lease has this scene's exact printed primary kind.");
            var raw = g.GetHumanLegalActions().First(a => a.CardId == original.Id && a.NativePublicPileSource is not null);
            Need(raw.ConversionSource is null && (raw.AdditionalConversionSources?.Count ?? 0) == 0,
                "Direct grain authorization remains separate from optional ViewAs conversion sources.");
            if (scene == "play-chain")
            {
                var state = State(g);
                Need(!g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Recast && a.CardId == original.Id) &&
                    !g.Submit(new RecastCardCommand(0, original.Id, g.Revision, P(g)!.PromptId)).Accepted && State(g) == state,
                    "A real IronChain grain is usable in its native direction but never leaks into Hand-only recast costs.");
            }
            g = Cold(g, r);
            PlayOriginalGrain(g, original.Id, NativeTargets(original.Kind, 1));
            Reach(g, () => Option(g, "original-live"));
            var liveOriginal = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.CardId == original.Id);
            var originalAction = liveOriginal.Action;
            Need(originalAction is not null && !E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == liveOriginal.Id),
                "The real committed observer freezes the original typed Action before its native Use finishes.");
            g = Cold(g, r); Continue(g); Play(g);
            AssertOriginalUse(g, original, 0, originalAction);
            var target = scene == "play-delayed" ? 3 : 1;
            CardActionContext? convertedAction = null;
            if (scene == "converted-lease")
            {
                var converted = g.GetHumanLegalActions().First(a => a.CardId == marked.Id && a.Kind == LegalActionKind.IronChain &&
                    a.ConversionSource?.SkillId == "classic:lianhuan" && a.TargetSeats.SequenceEqual([target]));
                Accept(g, new PlayCardCommand(0, marked.Id, converted.TargetSeats, g.Revision, P(g)!.PromptId,
                    converted.PlayedCardKind, converted.TargetCardId) { ConversionSource = converted.ConversionSource,
                    AdditionalConversionSources = converted.AdditionalConversionSources ?? [] });
                Reach(g, () => Option(g, "converted-live"));
                convertedAction = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.CardId == marked.Id).Action;
                g = Cold(g, r); Continue(g);
            }
            else PlayHand(g, marked.Id, NativeTargets(marked.Kind, target));
            Play(g);
            var handUse = E<CardUseDeclaredEvent>(g).Single(e => e.CardId == marked.Id);
            Need(E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == handUse.ResolutionId) == 1,
                "A marked original Hand entity reaches a complete native Use before any collection decision.");
            if (scene == "play-delayed")
                Need(Zone(g, marked.Id) == CardLocation.Judgment(target) &&
                    !g.CardMovements.Any(m => m.CardId == marked.Id && m.Reason.Value == Collect) && Pile(g).Count == 2,
                    "A finished delayed-card Use leaves its original in Judgment; Jiyun cannot collect from Judgment.");
            else
                Need(Zone(g, marked.Id).Zone == CardZoneKind.PublicPersistentPile &&
                    g.CardMovements.Count(m => m.CardId == marked.Id && m.Reason.Value == Collect &&
                        m.From.Zone == (scene == "play-equipment" ? CardZoneKind.Equipment : CardZoneKind.DiscardPile)) == 1 &&
                    Pile(g).Count == 3,
                    "Whole-use collection retrieves only the exact marked entity from native Equipment or Discard.");
            Need(g.CardMovements.Count(m => m.CardId == marked.Id && m.From == CardLocation.Hand(0) &&
                m.To == CardLocation.Processing) == 1 &&
                !g.CardMovements.Any(m => m.Reason.Value == Collect && m.CardId != marked.Id),
                "Other actual native gains or replacement equipment are not mistaken for the Prep-marked Use material.");
            if (scene == "converted-lease")
                Need(handUse.CardKind == CardKind.IronChain && convertedAction is { EffectiveKind: CardKind.IronChain } &&
                    convertedAction.PhysicalCards.Any(c => c.CardId == marked.Id && c.CardKind == CardKind.Dodge) &&
                    convertedAction.ConversionChain.Any(s => s.SkillId == "classic:lianhuan"),
                    "A genuine Lianhuan Use collects its original printed marked Dodge, not an invented IronChain entity.");
            _ = Cold(g, r);
        }
        foreach (var scene in new[] { "lease-dodge", "lease-null", "lease-played" })
        {
            var (g, r) = Start(scene, beforePrep: true);
            Prepare(g, [0]); Play(g);
            var marked = V(g).Hand.Single();
            var opening = Pile(g).Single(c => c.Kind == (scene == "lease-null" ? CardKind.Crossbow : CardKind.DrawTwo));
            PlayOriginalGrain(g, opening.Id, []); Play(g);
            if (scene == "lease-dodge") Incoming(g);
            else Use(g, "duel", targets: [1]);
            Reach(g, () => P(g) is { PlayerSeat: 0 } p && p.Kind == (scene == "lease-null" ? DecisionKind.Nullification :
                scene == "lease-dodge" ? DecisionKind.RespondDodge : DecisionKind.RespondSlash));
            var parent = g.ResolutionStack.OfType<CardUseFrame>().Last();
            Need(P(g)!.Choices.Any(c => c.Cards.SequenceEqual([marked.Id]) &&
                !c.Parameters.ContainsKey("native-original-public-pile")),
                "The marked entity responds from its real Hand, independently of the already-spent original grain quota.");
            g = Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([marked.Id])); Play(g);
            Need(E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == parent.Id) == 1 &&
                E<ShuliangGrainPaidEvent>(g).Length == 1,
                "The original response parent finishes once; the normal marked Hand response does not pay another Shuliang quota.");
            if (scene == "lease-played")
                Need(Zone(g, marked.Id) == CardLocation.DiscardPile &&
                    !E<JiyunClaimedEntityEvent>(g).Any(e => e.CardId == marked.Id) &&
                    E<DuelResponseEvent>(g).Any(e => e.ResolutionId == parent.Id && e.ResponderSeat == 0 && e.UsedSlash && e.SlashCardId == marked.Id),
                    "A marked but purely played Duel Slash cannot trigger true-Use collection.");
            else
                Need(Zone(g, marked.Id).Zone == CardZoneKind.PublicPersistentPile &&
                    E<JiyunClaimedEntityEvent>(g).Count(e => e.CardId == marked.Id &&
                        e.ResponseKind == (scene == "lease-null" ? ProgramCompletedResponseKind.Nullification : ProgramCompletedResponseKind.Dodge)) == 1 &&
                    g.CardMovements.Count(m => m.CardId == marked.Id && m.Reason.Value == Collect && m.From == CardLocation.DiscardPile) == 1,
                    "A genuine marked Hand Dodge/Nullification is reclaimed once through its native completed-response return.");
            _ = Cold(g, r);
        }
    }

    public static void ShuliangNativeResponsesPlayedQuotaAndFactionSupplyCold()
    {
        foreach (var scene in new[] { "dodge-quota", "duel-quota" })
        {
            var (g, r) = Start(scene);
            Use(g, "stock-all"); Play(g);
            Need(V(g).Hand.Any(c => c.Kind == CardKind.Nullification), "The exact extra native Hand Nullification keeps the later counterspell menu reachable even when grain is blocked.");
            var original = Pile(g).Single(c => c.Kind == (scene == "dodge-quota" ? CardKind.Dodge : CardKind.Slash));
            var counter = Pile(g).Single(c => c.Kind == CardKind.Nullification);
            var turn = g.State.TurnNumber;
            if (scene == "dodge-quota") Incoming(g);
            else { Use(g, "duel", targets: [1]); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash }); }
            Reach(g, () => P(g) is { PlayerSeat: 0 } p && p.Kind == (scene == "dodge-quota" ? DecisionKind.RespondDodge : DecisionKind.RespondSlash));
            var parent = g.ResolutionStack.OfType<CardUseFrame>().Last();
            var choice = OriginalChoice(g, original.Id);
            AssertPrivate(g, 0); g = Cold(g, r);
            Answer(g, c => c.Id == choice.Id); Play(g);
            var first = E<ShuliangGrainPaidEvent>(g).Single();
            Need(first.ProviderSeat == 0 && first.CardId == original.Id && first.ActualTurn == turn &&
                first.Played == (scene == "duel-quota") &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == parent.Id) == 1,
                "A genuine defensive Use or pure played Duel response pays one original grain and one shared actual-turn invoice.");
            if (scene == "dodge-quota")
                Need(E<CardRespondedEvent>(g).Any(e => e.CardId == original.Id && e.ResponderSeat == 0 && e.EffectiveCardKind == CardKind.Dodge),
                    "The native incoming Slash truly receives its exact original grain Dodge.");
            else
                Need(E<DuelResponseEvent>(g).Any(e => e.ResolutionId == parent.Id && e.ResponderSeat == 0 &&
                    e.UsedSlash && e.SlashCardId == original.Id), "The genuine Duel consumes that original grain as Played, without a new Slash Use.");
            Use(g, "duel", targets: [1]);
            Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.Nullification });
            Need(P(g)!.Choices.All(c => !c.Cards.Contains(counter.Id)) && Zone(g, counter.Id).Zone == CardZoneKind.PublicPersistentPile &&
                E<ShuliangGrainPaidEvent>(g).Length == 1,
                "The same actual turn cannot spend a second grain through a different native Use/Played request.");
            var stable = State(g);
            Need(!g.Submit(new AnswerPromptCommand(0, P(g)!.PromptId, choice.Id, g.Revision)).Accepted && State(g) == stable,
                "A choice from the already-completed native request cannot bypass the shared quota in a new prompt.");
            Play(g);
            NextOwnTurn(g);
            Need(g.State.TurnNumber > turn, "The quota restoration uses another genuine actual turn.");
            Use(g, "duel", targets: [1]); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.Nullification });
            var next = OriginalChoice(g, counter.Id); g = Cold(g, r); Answer(g, c => c.Id == next.Id); Play(g);
            Need(E<ShuliangGrainPaidEvent>(g).Length == 2 &&
                E<ShuliangGrainPaidEvent>(g).Select(e => e.ActualTurn).Distinct().Count() == 2 &&
                E<ShuliangGrainPaidEvent>(g).Last().CardId == counter.Id,
                "The still-owned original grain becomes available under a fresh actual-turn budget, rather than being permanently disabled.");
        }
        {
            var (g, r) = Start("null");
            var original = Pile(g).Single(c => c.Kind == CardKind.Nullification);
            Use(g, "duel", targets: [1]); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.Nullification });
            var native = g.ResolutionStack.OfType<CardUseFrame>().Last(f => f.CardKind == CardKind.Duel);
            var choice = OriginalChoice(g, original.Id); AssertPrivate(g, 0); g = Cold(g, r);
            Answer(g, c => c.Id == choice.Id); Play(g);
            Need(E<CardRespondedEvent>(g).Any(e => e.CardId == original.Id && e.ResponderSeat == 0 && e.EffectiveCardKind == CardKind.Nullification) &&
                E<CardActionAcceptedEvent>(g).Any(e => e.Action.Type == CardActionType.Response && e.Action.EffectiveKind == original.Kind &&
                    e.Action.ParentActionId == native.Action!.ActionId && e.Action.PhysicalCards is [var cost] &&
                    cost.CardId == original.Id && cost.CardKind == original.Kind && cost.From.Zone == CardZoneKind.PublicPersistentPile) &&
                g.CardMovements.Count(m => m.CardId == original.Id && m.From.Zone == CardZoneKind.PublicPersistentPile &&
                    m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Nullification) == 1 &&
                g.CardMovements.Count(m => m.CardId == original.Id && m.From == CardLocation.Processing &&
                    m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.NullificationFinished) == 1 &&
                E<ShuliangGrainPaidEvent>(g) is [{ Played: false }] &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == native.Id) == 1,
                "A single original-pile Nullification keeps the native late Response action, exact two-reason physical cleanup and one true-Use quota.");
        }
        {
            var (g, r) = Start("rescue");
            var original = Pile(g).Single(c => c.Kind == CardKind.Peach);
            Use(g, "wound-peer", targets: [1]); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RescueDying });
            var dying = g.ResolutionStack.OfType<DyingFrame>().Last();
            Need(dying.VictimSeat == 1 && V(g, 1).Hp == 0, "A real original Dying frame, not a fabricated Peach prompt, owns the rescue.");
            var choice = OriginalChoice(g, original.Id); AssertPrivate(g, 0); g = Cold(g, r);
            Answer(g, c => c.Id == choice.Id);
            Reach(g, () => E<DyingResolvedEvent>(g).Any(e => e.ResolutionId == dying.Id));
            Need(E<DyingResponseEvent>(g).Count(e => e.ResolutionId == dying.Id && e.ResponderSeat == 0 &&
                    e.UsedPeach && e.PeachCardId == original.Id) == 1 &&
                E<DyingResolvedEvent>(g).Single(e => e.ResolutionId == dying.Id).Survived && V(g, 1).Hp == 1 &&
                E<ShuliangGrainPaidEvent>(g) is [{ Played: false }] &&
                g.CardMovements.Count(m => m.CardId == original.Id && m.From.Zone == CardZoneKind.PublicPersistentPile &&
                    m.To == CardLocation.Processing) == 1 &&
                g.CardMovements.Count(m => m.CardId == original.Id && m.From == CardLocation.Processing &&
                    m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.UseFinished) == 1,
                "Third-party rescue really uses the original grain Peach, restores the original victim, cleans its physical material and returns once.");
            _ = Cold(g, r);
        }
        foreach (var scene in new[] { "barbarian", "arrows" })
        {
            var (g, r) = Start(scene);
            var original = Pile(g).Single(c => c.Kind == (scene == "arrows" ? CardKind.Dodge : CardKind.Slash));
            End(g); Reach(g, () => P(g) is { PlayerSeat: 0 } p &&
                p.Kind == (scene == "arrows" ? DecisionKind.RespondDodge : DecisionKind.RespondSlash));
            var native = g.ResolutionStack.OfType<CardUseFrame>().Last(f =>
                f.CardKind == (scene == "arrows" ? CardKind.ArrowBarrage : CardKind.BarbarianAssault));
            var response = g.ResolutionStack.OfType<ResponseWindowFrame>().Last();
            Need(response.ParentFrameId == native.Id && response.ResponderSeat == 0 && response.IncomingCard == native.CardKind,
                "The original foreign group-card response window owns this pure played-card request.");
            var choice = OriginalChoice(g, original.Id); g = Cold(g, r); Answer(g, c => c.Id == choice.Id);
            Reach(g, () => E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == native.Id));
            Need(E<GroupResponseEvent>(g).Count(e => e.ResolutionId == native.Id && e.ResponderSeat == 0 &&
                    e.UsedResponse && e.ResponseCardId == original.Id) == 1 &&
                E<ShuliangGrainPaidEvent>(g) is [{ Played: true }] &&
                !E<CardUseDeclaredEvent>(g).Any(e => e.CardId == original.Id) &&
                g.CardMovements.Count(m => m.CardId == original.Id && m.From.Zone == CardZoneKind.PublicPersistentPile) == 1,
                "AOE Played accepts the actual printed grain kind, consumes the shared payment and never invents a new Slash/Dodge Use.");
            _ = Cold(g, r);
        }
        {
            var (g, r) = Start("forced");
            var original = Pile(g).Single(c => c.Kind == CardKind.FireSlash);
            Use(g, "poke", targets: [1]);
            Reach(g, () => P(g) is { PlayerSeat: 0 } p && p.Choices.Any(c =>
                c.Cards.SequenceEqual([original.Id]) && c.Parameters.GetValueOrDefault("program-action") == "request-slash"));
            var request = g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.SkillId == "fixture:lf-request");
            Need(request.OwnerSeat == 1 && request.SelectedTargetSeats.SequenceEqual([0]),
                "A real damaged foreign issuer requests the Li Feng actor's Slash in its own original program frame.");
            AssertPrivate(g, 0); g = Cold(g, r);
            Answer(g, c => c.Cards.SequenceEqual([original.Id]) && c.Parameters.GetValueOrDefault("program-action") == "request-slash");
            Reach(g, () => Option(g, "forced"));
            var use = g.ResolutionStack.OfType<CardUseFrame>().Last(u => u.CardId == original.Id);
            Need(use.SourceSeat == 0 && use.CardKind == CardKind.FireSlash && use.Action is { ActorSeat: 0, ProviderSeat: 0 } action &&
                action.PhysicalCards is [var cost] && cost.CardId == original.Id && cost.CardKind == CardKind.FireSlash &&
                cost.From.Zone == CardZoneKind.PublicPersistentPile && action.EffectiveKind == CardKind.FireSlash &&
                action.EffectiveSuit == original.Suit && action.EffectiveRank == original.Rank &&
                g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == request.Id && f.OwnerSeat == 1),
                "The genuine requested Use retains its original FireSlash kind, suit/rank, exact public-pile payment and foreign parent.");
            g = Cold(g, r); Continue(g); Play(g);
            AssertOriginalUse(g, original, 0);
            Need(E<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == request.Id && e.Activated && e.Completed) == 1 &&
                E<ShuliangGrainPaidEvent>(g) is [{ Played: false }],
                "Whole Slash completion returns once to the original foreign request without changing it to pure Played or repaying its grain.");
        }
        foreach (var scene in new[] { "faction-use", "faction-played" })
        {
            var (g, r) = Start(scene);
            var provider = Enumerable.Range(1, 3).Single(s => V(g, s).GeneralId == "fixture:lf-peer-1");
            var original = Pile(g, provider).Single(c => c.Kind == CardKind.Slash);
            Need(V(g, provider).Role == Role.Loyalist && r.Generals[V(g, provider).GeneralId!].FactionId == "shu" &&
                V(g, provider).HandCount == 0, "The real allied provider has only his genuine original Slash grain as supply.");
            if (scene == "faction-use")
            {
                var request = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.UseProgramSkill && a.ProgramSkillId == "classic:jijiang");
                var target = new[] { 1, 3 }.First(s => s != provider);
                Accept(g, new UseProgramSkillCommand(0, "classic:jijiang", request.ProgramActivationId!, [], [target], g.Revision, P(g)!.PromptId));
            }
            else
            {
                var enemy = Enumerable.Range(1, 3).First(s => s != provider);
                Use(g, "duel", targets: [enemy]); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
                Answer(g, c => c.Parameters.GetValueOrDefault("response") == "faction-slash-request");
            }
            Reach(g, () => P(g) is { Kind: DecisionKind.RespondSlash } p && p.PlayerSeat == provider);
            var published = OriginalChoice(g, original.Id); AssertPrivate(g, provider);
            var state = State(g);
            Need(!g.Submit(new AnswerPromptCommand(0, P(g)!.PromptId, published.Id, g.Revision)).Accepted && State(g) == state,
                "The requester cannot answer or spend the provider's private original-pile selection.");
            g = Cold(g, r);
            if (scene == "faction-use")
            {
                Reach(g, () => Option(g, "supply-paid"));
                var supply = E<CardSupplyCompletedEvent>(g).Single();
                Need(P(g)!.PlayerSeat == provider && supply.ProviderSeat == provider && supply.RequesterSeat == 0 &&
                    supply.EffectiveKind == original.Kind &&
                    g.ResolutionStack.OfType<CardUseFrame>().Any(u => u.Id == supply.ParentFrameId &&
                        u.Action?.ActionId == supply.ActionId) &&
                    !E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == supply.ParentFrameId),
                    "The genuine provider completion observer runs after payment and before the principal's whole supplied Slash finishes.");
                AssertPrivate(g, provider); g = Cold(g, r);
                Reach(g, () => Option(g, "supplied"));
                var use = g.ResolutionStack.OfType<CardUseFrame>().Last(u => u.CardKind == CardKind.Slash && u.SourceSeat == 0);
                Need(use.Action is { ActorSeat: 0, RequesterSeat: 0 } a && a.ProviderSeat == provider &&
                    a.PhysicalCards is [var cost] && cost.CardId == original.Id && cost.CardKind == original.Kind &&
                    cost.From.Zone == CardZoneKind.PublicPersistentPile &&
                    a.Type == CardActionType.Use && a.EffectiveKind == original.Kind && a.EffectiveSuit == original.Suit && a.EffectiveRank == original.Rank &&
                    supply.ParentFrameId == use.Id && supply.ActionId == a.ActionId &&
                    E<CardSupplyCompletedEvent>(g).Count(e => e.ActionId == a.ActionId) == 1,
                    "The principal's true Slash Use retains actual user, requester, provider and the original pile invoice; the provider merely plays its material.");
                var id = use.Id; var suppliedActionId = use.Action!.ActionId;
                g = Cold(g, r); Continue(g); Play(g);
                Need(E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == id) == 1 &&
                    E<CardSupplyCompletedEvent>(g).Count(e => e.ActionId == suppliedActionId && e.ProviderSeat == provider && e.RequesterSeat == 0) == 1,
                    "The whole supplied original Use completes once after its genuine provider-completion child, preserving one supply fact and exact faction return.");
            }
            else Play(g);
            Need(E<ShuliangGrainPaidEvent>(g).Count(e => e.ProviderSeat == provider && e.CardId == original.Id &&
                    e.Played) == 1 &&
                g.CardMovements.Count(m => m.CardId == original.Id && m.From.Zone == CardZoneKind.PublicPersistentPile &&
                    m.From.OwnerSeat == provider) == 1 &&
                V(g, provider).HandCount == 0,
                "The faction provider pays one Played grain invoice in both requests; the principal separately performs the true Use or pure response.");
            _ = Cold(g, r);
        }
        {
            var (g, r) = Start("ai-play");
            var actor = Enumerable.Range(1, 3).Single(s => V(g, s).GeneralId == "fixture:lf-peer-1");
            Use(g, "stock-two"); Play(g);
            var donated = V(g).Hand.First().Id;
            Use(g, "give", [donated], [actor]); Play(g);
            Need(Zone(g, donated) == CardLocation.Hand(actor), "The AI really receives an original Hand card before its genuine turn.");
            End(g); Reach(g, () => g.State.CurrentSeat == actor && g.State.Phase == TurnPhase.Play && g.ResolutionStack.Count == 0);
            var available = Pile(g, actor).ToArray();
            Need(!V(g, actor).IsHuman && V(g, actor).HandCount == 0 && V(g, actor).Equipment.Count == 0 && available.Length == 3 &&
                g.CardMovements.Count(m => m.CardId == donated && m.From == CardLocation.Hand(actor) && m.To == CardLocation.DiscardPile) == 1 &&
                E<ProgramBindingResolvedEvent>(g).Any(e => e.SkillId == "fixture:lf-ai-empty" && e.OwnerSeat == actor && e.Activated && e.Completed),
                "A real AfterNormalDraw discard empties the AI Hand; its only non-End card actions are its three natural original grains.");
            Need(!E<ShuliangGrainPaidEvent>(g).Any(), "No prior supplied or human-selected grain has substituted for the AI Play decision.");
            var turn = g.State.TurnNumber; g = Cold(g, r);
            Accept(g, new AdvanceOneStepCommand(g.Revision));
            Need(E<ShuliangGrainPaidEvent>(g) is [var paid] && paid.ProviderSeat == actor && paid.ActualTurn == turn && !paid.Played &&
                available.Any(c => c.Id == paid.CardId) && E<NativePublicPileOriginalPaidEvent>(g).Any(e => e.CardId == paid.CardId &&
                    e.ActorSeat == actor && e.ProviderSeat == actor && e.Direction == NativePublicPileDirection.Use &&
                    e.From.Zone == CardZoneKind.PublicPersistentPile && e.EntrySequence == paid.MovementSequence) &&
                E<NativePublicPileOriginalBoundEvent>(g).Count(e => e.CardId == paid.CardId && e.ActionId == paid.ActionId &&
                    e.EntrySequence == paid.MovementSequence) == 1 &&
                E<CardUseDeclaredEvent>(g).Any(e => e.CardId == paid.CardId && e.SourceSeat == actor &&
                    available.Any(c => c.Id == e.CardId && c.Kind == e.CardKind)) &&
                g.CardMovements.Count(m => m.CardId == paid.CardId && m.From.Zone == CardZoneKind.PublicPersistentPile) == 1,
                "RunOneAiPlayDecision really scores and pays one original-pile Use without a Hand Single lookup or a human answer.");
            _ = Cold(g, r);
        }
        {
            var (g, r) = Start("ai-null");
            var actor = Enumerable.Range(1, 3).Single(s => V(g, s).GeneralId == "fixture:lf-peer-1");
            var original = Pile(g, actor).Single(c => c.Kind == CardKind.Nullification);
            Need(!V(g, actor).IsHuman && V(g, actor).Role == Role.Rebel && V(g, actor).HandCount == 0,
                "The actual enemy AI has no Hand counterspell before the human's genuine Duel.");
            Use(g, "stock-two"); Play(g);
            var duel = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Duel &&
                a.ConversionSource?.SkillId == Driver && a.TargetSeats.SequenceEqual([actor]));
            var material = duel.CardId!.Value;
            Accept(g, new PlayCardCommand(0, material, duel.TargetSeats, g.Revision, P(g)!.PromptId, duel.PlayedCardKind, duel.TargetCardId)
            { ConversionSource = duel.ConversionSource, AdditionalConversionSources = duel.AdditionalConversionSources ?? [] });
            Reach(g, () => g.CreateSnapshot(actor).PendingDecision is { Kind: DecisionKind.Nullification });
            var use = g.ResolutionStack.OfType<CardUseFrame>().Last(f => f.CardKind == CardKind.Duel && f.CardId == material);
            var prompt = g.CreateSnapshot(actor).PendingDecision!;
            Need(prompt.Choices.Count(c => c.Cards.Count != 0) == 1 &&
                prompt.Choices.Any(c => c.Cards.SequenceEqual([original.Id]) &&
                    c.Parameters.GetValueOrDefault("native-original-public-pile") == "true") && V(g, actor).HandCount == 0,
                "Only the actual AI viewer's private native prompt authorizes this one visible original grain counterspell.");
            AssertPrivate(g, actor); g = Cold(g, r);
            Accept(g, new AdvanceOneStepCommand(g.Revision)); Play(g);
            Need(E<CardRespondedEvent>(g).Count(e => e.CardId == original.Id && e.ResponderSeat == actor &&
                    e.EffectiveCardKind == CardKind.Nullification) == 1 &&
                E<ShuliangGrainPaidEvent>(g).Count(e => e.CardId == original.Id && e.ProviderSeat == actor && !e.Played) == 1 &&
                g.CardMovements.Count(m => m.CardId == original.Id && m.From.Zone == CardZoneKind.PublicPersistentPile &&
                    m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Nullification) == 1 &&
                g.CardMovements.Count(m => m.CardId == original.Id && m.From == CardLocation.Processing &&
                    m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.NullificationFinished) == 1 &&
                E<NullificationResolvedEvent>(g).Any(e => e.ResolutionId == use.Id && e.EffectCardId == material && e.EffectNullified) &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1,
                "The real AI chooses its grain-only Nullification, pays/cleans it once and returns through the original nullified Duel.");
            _ = Cold(g, r);
        }
    }

    public static void LiFengPaidChildrenSourceLossDeathAndInputPrivacyCold()
    {
        {
            var (g, r) = Start("prep-loss", beforePrep: true);
            var oldGrain = Pile(g).Select(c => c.Id).ToArray();
            Prepare(g, [0, 1]); Reach(g, () => Option(g, "prep-paid"));
            var paid = g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.LiFeng is not null);
            var frameId = paid.Id;
            Need(paid.LiFeng is { Paid: true, Stage: LiFengStage.PreparationChildren } receipt &&
                receipt.Recipients.SequenceEqual([0, 1]) && receipt.Recipients is System.Collections.IList { IsReadOnly: true } &&
                E<JiyunPreparationDrawReturnedEvent>(g).Count(e => e.FrameId == frameId) == 1,
                "The real first gain child owns a frozen, paid two-recipient Prep queue before actual skill loss.");
            g = Cold(g, r); Continue(g); Play(g);
            Need(V(g).IsAlive && !V(g).Skills!.Any(s => s.ContentId == Jiyun) &&
                E<JiyunPreparationDrawReturnedEvent>(g).Count(e => e.FrameId == frameId && e.ActualCount == 1) == 2 &&
                E<LiFengCompletedEvent>(g).Count(e => e.FrameId == frameId && e.Paid) == 1 &&
                E<JiyunLeaseIssuedEvent>(g).Count(e => e.FrameId == frameId) == 2,
                "Source loss after the first actual draw cannot cancel or replay the already-issued second recipient and its original lease.");
            Need(Pile(g).Count == 0 && oldGrain.All(id => g.CardMovements.Count(m => m.CardId == id &&
                    m.From.Zone == CardZoneKind.PublicPersistentPile && m.To == CardLocation.DiscardPile &&
                    m.Reason.Value == "skill-program.public-pile.source-lost") == 1) &&
                !g.GetHumanLegalActions().Any(a => a.NativePublicPileSource is not null),
                "A paid Prep queue does not preserve unrelated old grain or grant a fresh original-pile Use after the real Jiyun source is lost.");
            _ = Cold(g, r);
        }
        foreach (var scene in new[] { "paid-loss", "paid-death" })
        {
            var (g, r) = Start(scene);
            var original = Pile(g).Single(c => c.Kind == CardKind.DrawTwo);
            var action = g.GetHumanLegalActions().Single(a => a.CardId == original.Id && a.NativePublicPileSource is not null);
            var state = State(g);
            Need(!g.Submit(new UseProgramSkillCommand(0, Driver, "give", [original.Id], [1], g.Revision, P(g)!.PromptId)).Accepted &&
                State(g) == state, "Original public grain is excluded from ordinary Hand gift/cost selection.");
            PlayOriginalGrain(g, original.Id, action.TargetSeats.ToArray());
            Reach(g, () => Option(g, "paid"));
            var use = g.ResolutionStack.OfType<CardUseFrame>().Last(f => f.CardId == original.Id);
            var invoice = E<ShuliangGrainPaidEvent>(g).Single(e => e.CardId == original.Id);
            Need(invoice.ParentFrameId == use.Id && Zone(g, original.Id) == CardLocation.Processing &&
                !E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == use.Id) &&
                g.CardMovements.Count(m => m.CardId == original.Id && m.From.Zone == CardZoneKind.PublicPersistentPile) == 1,
                "The committed native Use owns the actual paid original grain before its real source-loss/death child and before DrawTwo effects.");
            var old = P(g)!; AssertPrivate(g, 0); state = State(g);
            Need(!g.Submit(new AnswerPromptCommand(1, old.PromptId, old.Choices[0].Id, g.Revision)).Accepted && State(g) == state,
                "A peer cannot answer the original actor's paid native observer.");
            g = Cold(g, r); Continue(g);
            Reach(g, () => E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == use.Id));
            Need(E<ShuliangGrainPaidEvent>(g).Count(e => e.CardId == original.Id) == 1 &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1 &&
                !g.ResolutionStack.Any(f => f.Id == use.Id) &&
                g.CardMovements.Count(m => m.CardId == original.Id && m.From.Zone == CardZoneKind.PublicPersistentPile) == 1,
                "Cold continuation cannot repay the original grain and must return the whole already-paid native Use exactly once.");
            if (scene == "paid-loss")
                Need(V(g).IsAlive && !V(g).Skills!.Any(s => s.ContentId == Shuliang) &&
                    g.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) &&
                        m.Sequence > invoice.MovementSequence && m.Reason == CardMoveReasons.Draw) == 2,
                    "Actual loss of the authorizing skill does not revoke an already-paid original DrawTwo.");
            else
                Need(!V(g).IsAlive && E<PlayerDiedEvent>(g).Any(e => e.VictimSeat == 0) && P(g)?.PlayerSeat != 0 &&
                    !g.CardMovements.Any(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) &&
                        m.Sequence > invoice.MovementSequence && m.Reason == CardMoveReasons.Draw),
                    "Cost-child death completes the paid native source without a fresh draw or a dead actor's private choice.");
            _ = Cold(g, r);
        }
        foreach (var scene in new[] { "lion", "ox" })
        {
            var (g, r) = Start(scene);
            Use(g, "stock-two"); Play(g);
            var equipped = V(g).Hand.First().Id;
            PlayHand(g, equipped, []); Play(g);
            int? held = null;
            if (scene == "lion") { Use(g, "wound"); Play(g); }
            else
            {
                held = V(g).Hand.Single().Id;
                Accept(g, new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [held.Value], [], g.Revision, P(g)!.PromptId)); Play(g);
                Need(V(g).WoodenOxGrain is [{ Id: var heldCardId }] && heldCardId == held &&
                    Enumerable.Range(1, 3).All(s => (g.CreateSnapshot(s).Players[0].WoodenOxGrain?.Count ?? 0) == 0),
                    "The real existing Ox holds its separate private grain before original-pile replacement.");
            }
            var original = Pile(g).Single(c => c.Kind == (scene == "lion" ? CardKind.SilverLion : CardKind.WoodenOx));
            var hp = V(g).Hp;
            PlayOriginalGrain(g, original.Id, []);
            Reach(g, () => Option(g, scene));
            var use = g.ResolutionStack.OfType<CardUseFrame>().Last(f => f.CardId == original.Id);
            Need(E<ShuliangGrainPaidEvent>(g).Count(e => e.CardId == original.Id) == 1 &&
                !E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == use.Id) &&
                g.CardMovements.Count(m => m.CardId == equipped && m.From == CardLocation.Equipment(0)) == 1,
                "A real native replacement child runs before the one original-pile Equipment Use completes.");
            if (scene == "lion")
                Need(V(g).Hp == hp + 1 && E<RecoveryAppliedEvent>(g).Any(e => e.TargetSeat == 0 && e.Amount == 1),
                    "The removed old Silver Lion triggers actual recovery inside the original equipment-use subtree.");
            else
                Need(held is { } spillCardId && g.CardMovements.Count(m => m.CardId == spillCardId &&
                    m.From == CardLocation.WoodenOxGrain(0) && m.To == CardLocation.DiscardPile &&
                    m.Reason.Value == "equipment.wooden-ox.grain-discard") == 1 &&
                    !E<ShuliangGrainPaidEvent>(g).Any(e => e.CardId == spillCardId),
                    "Only the original public grain pays Shuliang; native Ox spill is a separate real child invoice.");
            g = Cold(g, r); Continue(g); Play(g);
            Need(Zone(g, original.Id) == CardLocation.Equipment(0) &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1 &&
                g.CardMovements.Count(m => m.CardId == original.Id && m.From.Zone == CardZoneKind.PublicPersistentPile &&
                    m.To == CardLocation.Processing) == 1 &&
                g.CardMovements.Count(m => m.CardId == equipped && m.From == CardLocation.Equipment(0)) == 1,
                "The exact original equipment enters natively after all recovery/spill children and cannot pay or return twice.");
            _ = Cold(g, r);
        }
    }

    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static IReadOnlyList<CardSnapshot> Pile(GameEngine g, int owner = 0)
    {
        var p = V(g, owner);
        return p.PublicPersistentPiles?.Where(x => x.SourceSkillId == Jiyun).SelectMany(x => x.Cards).ToArray()
            ?? (p.PublicPersistentPileSkillId == Jiyun ? p.PublicPersistentPileCards ?? [] : []);
    }
    private static CardLocation Zone(GameEngine g, int id) => g.CreateCardZoneDiagnostics().Single(z => z.CardId == id).Location;
    private static bool Marked(CardSnapshot card) => card.SkillMarkLabel?.Contains("集运", StringComparison.Ordinal) == true;
    private static string? B(PromptChoice c) => c.Parameters.GetValueOrDefault("branch");
    private static bool Branch(GameEngine g, string branch) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "li-feng" && B(c) == branch) == true;
    private static bool Option(GameEngine g, string option) => P(g)?.SkillPrompt?.SkillId == Watch &&
        P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == option);
    private static bool Optional(GameEngine g, string skill) => P(g)?.SkillPrompt?.SkillId == skill &&
        P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool Action(GameEngine g, string action) => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action) == true;
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.ContainsKey("option-id"));
    private static void Prepare(GameEngine g, int[] recipients)
    {
        Need(Optional(g, Jiyun), "The genuine Preparation boundary must publish Jiyun before recipients are selected.");
        Activate(g); Reach(g, () => Branch(g, "recipient"));
        foreach (var recipient in recipients)
            Answer(g, c => B(c) == "recipient" && c.Targets.SequenceEqual([recipient]));
        var frame = g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.LiFeng is not null);
        Need(frame.LiFeng is { Paid: false, Stage: LiFengStage.PreparationTargets } draft &&
            draft.Recipients.SequenceEqual(recipients) && draft.MaximumRecipients == Math.Max(1, Pile(g).Count) &&
            draft.Recipients is System.Collections.IList { IsReadOnly: true },
            "The unissued private recipient draft freezes the exact distinct selection and current real grain capacity.");
        Answer(g, c => B(c) == "start");
    }
    private static void GiveGrain(GameEngine g, int id, int recipient) =>
        Answer(g, c => B(c) == "give" && c.Cards.SequenceEqual([id]) && c.Targets.SequenceEqual([recipient]));
    private static void Use(GameEngine g, string id, int[]? cards = null, int[]? targets = null) =>
        Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Incoming(GameEngine g)
    {
        Use(g, "incoming", targets: [1]);
        Reach(g, () => P(g) is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("offer-option") == "target-0"));
        Answer(g, c => c.Targets.SequenceEqual([0]));
    }
    private static PromptChoice OriginalChoice(GameEngine g, int id) =>
        P(g)?.Choices.FirstOrDefault(c => c.Cards.SequenceEqual([id]) &&
            c.Parameters.GetValueOrDefault("native-original-public-pile") == "true")
        ?? throw new InvalidOperationException("Missing published original-pile response " + id + " " + Diagnostic(g));
    private static void PlayOriginalGrain(GameEngine g, int id, int[] targets)
    {
        var a = g.GetHumanLegalActions().FirstOrDefault(a => a.CardId == id && a.NativePublicPileSource is not null &&
            a.TargetSeats.SequenceEqual(targets)) ?? throw new InvalidOperationException("Missing original grain action " + id + " " + Diagnostic(g));
        Need(a.ConversionSource is null && (a.AdditionalConversionSources?.Count ?? 0) == 0,
            "Natural grain permission cannot borrow a ViewAs source or a synthetic physical material.");
        Accept(g, new PlayCardCommand(0, id, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId));
    }
    private static void PlayHand(GameEngine g, int id, int[] targets)
    {
        var a = g.GetHumanLegalActions().FirstOrDefault(a => a.CardId == id && a.NativePublicPileSource is null &&
            a.ConversionSource is null && a.TargetSeats.SequenceEqual(targets))
            ?? throw new InvalidOperationException("Missing genuine Hand action " + id + " " + Diagnostic(g));
        Accept(g, new PlayCardCommand(0, id, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId));
    }
    private static int[] NativeTargets(CardKind kind, int target) => kind switch
    {
        CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.Indulgence or CardKind.IronChain => [target],
        _ => []
    };
    private static void AssertOriginalUse(GameEngine g, CardSnapshot original, int provider, CardActionContext? captured = null)
    {
        var observed = captured is null ? E<CardActionAcceptedEvent>(g).Select(e => e.Action).ToArray() : new[] { captured };
        var actions = observed.Where(a => a.Type == CardActionType.Use &&
            a.ProviderSeat == provider && a.PhysicalCards is [var cost] && cost.CardId == original.Id &&
            cost.From.Zone == CardZoneKind.PublicPersistentPile).ToArray();
        Need(actions.Length == 1, "Expected exactly one observed typed whole Use with this original public-pile cost: " +
            JsonSerializer.Serialize(new { Original = original, Provider = provider, MatchingCount = actions.Length,
                ObservedActions = E<CardActionAcceptedEvent>(g).Select(e => e.Action).Where(a =>
                    a.PhysicalCards.Any(c => c.CardId == original.Id) || a.EffectiveKind == original.Kind),
                Appearances = E<CardUseAppearanceCapturedEvent>(g).Select(e => e.Action).Where(a =>
                    a.PhysicalCards.Any(c => c.CardId == original.Id) || a.EffectiveKind == original.Kind),
                Movements = g.CardMovements.Where(m => m.CardId == original.Id) }));
        var action = actions.Single();
        var declarations = E<CardUseDeclaredEvent>(g).Where(e => e.CardId == original.Id).ToArray();
        Need(declarations.Length == 1, "Expected exactly one declaration of the same original grain entity: " +
            JsonSerializer.Serialize(new { Original = original, Provider = provider, MatchingCount = declarations.Length,
                Declarations = E<CardUseDeclaredEvent>(g) }));
        var declaration = declarations.Single();
        Need(action.EffectiveKind == original.Kind && action.EffectiveSuit == original.Suit && action.EffectiveRank == original.Rank &&
            action.PhysicalCards.Single().CardKind == original.Kind && declaration.CardKind == original.Kind &&
            declaration.PublicCard is { } publicFace && publicFace.Id == original.Id && publicFace.Kind == original.Kind &&
            publicFace.Suit == original.Suit && publicFace.Rank == original.Rank &&
            E<NativePublicPileOriginalPaidEvent>(g).Count(e => e.CardId == original.Id && e.CardKind == original.Kind &&
                e.From == action.PhysicalCards.Single().From && e.ActorSeat == action.ActorSeat && e.ProviderSeat == provider &&
                e.Direction == NativePublicPileDirection.Use && E<NativePublicPileOriginalBoundEvent>(g).Count(b =>
                    b.ParentFrameId == declaration.ResolutionId && b.CardId == e.CardId && b.EntrySequence == e.EntrySequence && b.ActionId == action.ActionId) == 1) == 1 &&
            E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == declaration.ResolutionId) == 1 &&
            g.CardMovements.Count(m => m.CardId == original.Id && m.From.Zone == CardZoneKind.PublicPersistentPile) == 1 &&
            E<ShuliangGrainPaidEvent>(g).Count(e => e.CardId == original.Id && e.ProviderSeat == provider && !e.Played) == 1,
            "The whole natural Use keeps the original printed kind/suit/rank and exact pile cost, one quota invoice and one completion.");
    }
    private static void AssertPublicPile(GameEngine g, IEnumerable<int> ids)
    {
        var expected = ids.Order().ToArray();
        Need(Enumerable.Range(0, 4).All(viewer =>
        {
            var p = g.CreateSnapshot(viewer).Players[0];
            var cards = p.PublicPersistentPiles?.Where(x => x.SourceSkillId == Jiyun).SelectMany(x => x.Cards)
                ?? (p.PublicPersistentPileSkillId == Jiyun ? p.PublicPersistentPileCards ?? [] : []);
            return cards.Select(c => c.Id).Order().SequenceEqual(expected);
        }), "Every viewer sees the same public original grain entities, without acquiring their owner's private choices.");
    }
    private static void AssertPrivate(GameEngine g, int seat) => Need(g.CreateSnapshot(seat).PendingDecision is { IsPrivate: true } p &&
        p.PlayerSeat == seat && Enumerable.Range(0, 4).Where(s => s != seat).All(s => g.CreateSnapshot(s).PendingDecision is null),
        "Only the actual chooser can inspect and answer this private native or Li Feng decision.");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> test)
    {
        var p = P(g) ?? throw new InvalidOperationException("Missing published prompt " + Diagnostic(g));
        var choice = p.Choices.FirstOrDefault(test);
        Need(p.PlayerSeat == 0 && choice is not null, "Only the real human chooser may submit a currently published choice.");
        Accept(g, new AnswerPromptCommand(0, p.PromptId, choice!.Id, g.Revision));
    }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void NextOwnTurn(GameEngine g)
    {
        var turn = g.State.TurnNumber; End(g);
        Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } && g.State.TurnNumber > turn);
    }
    private static void Reach(GameEngine g, Func<bool> done, [CallerMemberName] string caller = "", [CallerLineNumber] int line = 0)
    {
        for (var step = 0; step < 220; step++)
        {
            if (done()) return;
            Need(g.State.Winner == Winner.None, "Unexpected game end before " + caller + ":" + line);
            var p = P(g);
            if (p is null || p.PlayerSeat != 0) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (p.Kind == DecisionKind.PlayCard) End(g);
            else if (p.SkillPrompt?.SkillId == Watch) Continue(g);
            else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification)
                Answer(g, c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("response") is not ("faction-slash-request" or "faction-defense-request"));
            else if (p.Kind == DecisionKind.RescueDying)
                Answer(g, c => c.Parameters.GetValueOrDefault("response") is "let-die" or "pass");
            else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip"))
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (Branch(g, "finish")) Answer(g, c => B(c) == "finish");
            else if (Branch(g, "cancel")) Answer(g, c => B(c) == "cancel");
            else throw new InvalidOperationException("Unexpected native decision before " + caller + ":" + line + " " + Diagnostic(g));
        }
        throw new InvalidOperationException("Missing bounded native boundary " + caller + ":" + line + " " + Diagnostic(g));
    }
    private static void Accept(GameEngine g, GameCommand command)
    {
        try
        {
            var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
            Need(result.Accepted, command.GetType().Name + " rejected: " + JsonSerializer.Serialize(result));
        }
        catch (Exception error) { throw new InvalidOperationException("scene=" + Scene + " command=" + command.GetType().Name + " cause=" + error + " " + Diagnostic(g), error); }
    }
    private static void Need(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("scene=" + Scene + " " + message + (Context is { } g ? " " + Diagnostic(g) : ""));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new
    {
        Scene, g.State.TurnNumber, g.State.CurrentSeat, g.State.Phase,
        Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).GeneralId, V(g, s).Role, V(g, s).Hp, V(g, s).IsAlive, V(g, s).HandCount, Pile = Pile(g, s) }),
        Prompt = P(g), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
        GrainPayments = E<ShuliangGrainPaidEvent>(g), Leases = E<JiyunLeaseIssuedEvent>(g), Claims = E<JiyunClaimedEntityEvent>(g)
    });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), g.CardMovements, Zones = g.CreateCardZoneDiagnostics(),
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(g.AcceptedCommands)
    });
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
        Need(State(g) == State(restored), "Cold replay preserves public original identities, private native choices, Hand leases and already-paid return cursors.");
        Context = restored; return restored;
    }
    private static (GameEngine, ContentRegistry) Start(string scene, bool beforePrep = false)
    {
        Scene = scene; Context = null; ContentRegistry r;
        try { r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene)); }
        catch (Exception error) { throw new InvalidOperationException("scene=" + scene + " fixture=" + error, error); }
        var g = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scene == "paid-death" ? Role.Renegade : Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12
        }, r);
        Context = g; Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId));
        if (beforePrep)
        {
            Reach(g, () => Optional(g, Jiyun));
            Need(Optional(g, Jiyun), "The actual owner's Preparation must precede normal flow.");
        }
        else Play(g);
        return (g, r);
    }
    private static CardKind PrimaryKind(string scene) => scene switch
    {
        "play-slash" => CardKind.FireSlash, "play-trick" or "paid-loss" or "paid-death" => CardKind.DrawTwo,
        "play-chain" => CardKind.IronChain, "play-equipment" => CardKind.Crossbow, "play-delayed" => CardKind.Indulgence,
        "dodge-quota" or "arrows" or "converted-lease" or "lease-dodge" => CardKind.Dodge,
        "null" or "lease-null" or "ai-null" => CardKind.Nullification, "rescue" => CardKind.Peach,
        "forced" => CardKind.FireSlash,
        "lion" => CardKind.SilverLion, "ox" => CardKind.WoodenOx, _ => CardKind.Slash
    };

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-li-feng", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            RegisterProductionSkills(b, "ol-li-feng", Jiyun, Shuliang);
            var faction = scene.StartsWith("faction-", StringComparison.Ordinal);
            var aiScene = scene is "ai-play" or "ai-null";
            if (faction) CharacterFixtureContent.Register(b, "classic:liu-bei");
            if (scene == "converted-lease") CharacterFixtureContent.Register(b, "classic:pang-tong");
            if (scene == "prep-replacement") RegisterProductionSkills(b, "ol-sun-hong", "ol:zenrun");
            static JsonNode N(string json) => JsonNode.Parse(json)!;
            static JsonObject A(string id, string effects, int cards = 0, int targets = 0, int? maximum = null)
            {
                var a = new JsonObject { ["id"] = id, ["minCards"] = cards, ["maxCards"] = maximum ?? cards,
                    ["minTargets"] = targets, ["maxTargets"] = targets, ["targetKind"] = "anyLiving", ["usesPerTurn"] = null,
                    ["effects"] = N("[" + effects + "]") };
                if ((maximum ?? cards) > 0) a["sourceZones"] = N("[\"hand\"]");
                return a;
            }
            var definitions = new JsonArray();
            var duel = A("duel", """{"op":"useSelectedActorDuel","target":"owner"}""", targets: 1);
            duel["targetKind"] = "otherLiving"; duel["usesPerPhase"] = 2;
            definitions.Add(new JsonObject
            {
                ["id"] = Driver, ["revision"] = 1,
                ["modifiers"] = N("""[{"id":"keep","query":"handLimit","operation":"add","value":40,"priority":0},{"id":"two-slashes","query":"slashLimit","operation":"add","value":5,"priority":0}]"""),
                ["activations"] = new JsonArray(
                    A("stock-two", """{"op":"draw","target":"owner","amount":2}"""),
                    A("stock-all", """{"op":"draw","target":"owner","amount":20},{"op":"draw","target":"owner","amount":4}"""),
                    A("give", """{"op":"giveSelected","target":"selectedTarget"}""", 1, 1),
                    A("wound", """{"op":"loseHp","target":"owner","amount":2}"""),
                    A("wound-peer", """{"op":"loseHp","target":"selectedTarget","amount":12}""", targets: 1),
                    A("poke", """{"op":"damage","target":"selectedTarget","amount":1}""", targets: 1),
                    A("incoming", """{"op":"offerVirtualSlashOrDraw","target":"selectedTarget"}""", targets: 1),
                    A("take", """{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"ownerHand","awaitMovementTriggers":true}""", targets: 1), duel)
            });
            definitions.Add(N("{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"));
            if (scene == "ai-play") definitions.Add(N("""{"id":"fixture:lf-ai-empty","revision":1,"triggers":[{"id":"actual-hand-discard","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]}]}"""));
            if (scene == "ai-null") definitions[0]!["viewAs"] = N("""[{"id":"actual-hand-duel","inputKinds":["nullification"],"inputSuits":[],"sourceZones":["hand"],"outputKind":"duel","forPlay":true,"forResponse":false,"useOnly":true,"singleCardTrickUse":true}]""");
            if (scene is "paid-loss" or "prep-loss")
                definitions.Add(N("{\"id\":\"" + Conditional + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"live-access\",\"skillId\":\"" + (scene == "prep-loss" ? Jiyun : Shuliang) + "\",\"minimumHp\":13}]}"));
            var watches = new JsonArray();
            if (scene.StartsWith("play-", StringComparison.Ordinal) || scene == "converted-lease")
            {
                var kind = scene switch { "play-slash" => "fireSlash", "play-chain" => "ironChain", "play-equipment" => "crossbow", "play-delayed" => "indulgence", _ => "drawTwo" };
                watches.Add(N($$"""{"id":"original-live-action","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["{{kind}}"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"original-live","options":[{"id":"original-live"}]}]}"""));
            }
            if (scene == "converted-lease") watches.Add(N("""{"id":"converted-live-action","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["ironChain"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"converted-live","options":[{"id":"converted-live"}]}]}"""));
            if (scene == "prep") watches.Add(N($$"""{"id":"prep-actual-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{PrepDraw}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"prep-gained","options":[{"id":"prep-gained"}]}]}"""));
            if (scene == "prep-loss") watches.Add(N($$"""{"id":"paid-prep-source","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{PrepDraw}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"prep-paid","options":[{"id":"prep-paid"}]},{"op":"loseHp","target":"owner","amount":1}]}"""));
            if (scene.StartsWith("paid-", StringComparison.Ordinal))
                watches.Add(N($$"""{"id":"paid-native-use","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["drawTwo"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"paid"}]},{"op":"loseHp","target":"owner","amount":{{(scene == "paid-death" ? 20 : 1)}}}]}"""));
            if (scene == "lion") watches.Add(N("""{"id":"native-lion-recovery","window":"afterHealthChanged","subject":"owner","condition":{"kind":"healthChangeKindIs","hpChangeKind":"recovery"},"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"lion","options":[{"id":"lion"}]}]}"""));
            if (scene == "ox") watches.Add(N("""{"id":"native-ox-spill","window":"cardsMoved","subject":"owner","sourceZones":["woodenOxGrain"],"movementOccurrence":"perBatch","movementReasons":["equipment.wooden-ox.grain-discard"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"ox","options":[{"id":"ox"}]}]}"""));
            if (scene == "faction-use") watches.Add(N("""{"id":"true-supplied-use","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"supplied","options":[{"id":"supplied"}]}]}"""));
            if (scene == "faction-use") watches.Add(N("""{"id":"actual-provider-completion","window":"cardSupplyCompleted","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"supply-paid","options":[{"id":"supply-paid"}]}]}"""));
            if (scene == "forced") watches.Add(N("""{"id":"true-forced-grain","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["fireSlash"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"forced","options":[{"id":"forced"}]}]}"""));
            if (watches.Count > 0) definitions.Add(new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = watches });
            if (scene == "forced") definitions.Add(N("""{"id":"fixture:lf-request","revision":1,"triggers":[{"id":"actual-request","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"eventSource"},{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"requested"}]}]}"""));
            if (scene is "barbarian" or "arrows") definitions.Add(N($$"""{"id":"fixture:lf-assault","revision":1,"triggers":[{"id":"actual-foreign-use","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"owner"},"targetRef":{"kind":"owner"},"outputKind":"{{(scene == "arrows" ? "arrowBarrage" : "barbarianAssault")}}"}]}]}"""));
            var labels = definitions.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new
            {
                name = n!["id"]!.GetValue<string>(), description = "原粮原牌与集运连续原实体租约",
                optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray())
                    .SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray())
                    .Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(x => x, x => x)
            });
            var catalog = SkillProgramCatalog.Load(
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs)
                b.AddSkill(new(id, id, "李丰真实原生边界") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:lf-own-pick", "本人", "固定公开选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => -100000000d) });
            var own = new List<string> { Driver };
            if (faction) own.Add("classic:jijiang");
            else if (aiScene) { }
            else if (scene == "prep-loss") own.AddRange([Conditional, Shuliang]);
            else { own.Add(Jiyun); own.Add(scene == "paid-loss" ? Conditional : Shuliang); }
            if (scene == "converted-lease") own.Add("classic:lianhuan");
            if (scene == "prep-replacement") own.Add("ol:zenrun");
            if (watches.Count > 0) own.Add(Watch);
            b.AddGeneral(new(Owner, "真实李丰原牌机制", "ol-li-feng", "fixture:lf-own-pick", faction ? "shu" : "qun", 12, own, GeneralGender.Male));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:lf-peer-" + i).ToArray();
            for (var i = 1; i <= 3; i++)
            {
                var pick = "fixture:lf-pick-" + i;
                b.AddSkill(new(pick, "其他角色", "稳定真实阵营") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role,
                    role => faction && i == 1 ? role == Role.Loyalist ? 100000000d : -100000000d : 1000000d * (4 - i)) });
                var skills = new List<string> { Quiet };
                if ((faction || aiScene) && i == 1) skills.AddRange([Jiyun, Shuliang]);
                if (scene == "faction-use" && i == 1) skills.Add(Watch);
                if (scene == "ai-play" && i == 1) { skills.Remove(Quiet); skills.Add("fixture:lf-ai-empty"); }
                if (scene is "barbarian" or "arrows") skills.Add("fixture:lf-assault");
                if (scene == "forced") skills.Add("fixture:lf-request");
                b.AddGeneral(new(peers[i - 1], "实际其他角色", "supporter", pick, faction && i == 1 ? "shu" : "wei", 12, skills));
            }
            var primary = PrimaryKind(scene);
            var basic = CardCatalog.Get(primary).CategoryName == "基本牌" ? primary : CardKind.Dodge;
            var trick = CardCatalog.Get(primary).CategoryName == "锦囊牌" ? primary :
                scene is "dodge-quota" or "duel-quota" ? CardKind.Nullification : CardKind.DrawTwo;
            var equipment = CardCatalog.Get(primary).CategoryName == "装备牌" ? primary : CardKind.Crossbow;
            foreach (var (id, kind) in new[] { ("primary", primary), ("basic", basic), ("trick", trick), ("equipment", equipment) })
                b.AddCard(new("fixture:lf-" + id, "真实" + kind, CardCatalog.Get(kind).CategoryName, "原生印刷实体", kind));
            var deck = Enumerable.Range(0, 24).Select(_ => new ContentDeckPhysicalCard("fixture:lf-primary", scene == "converted-lease" ? Suit.Club : Suit.Heart, 9)).ToList();
            foreach (var (id, kind) in new[] { ("basic", basic), ("trick", trick), ("equipment", equipment) })
                if (CardCatalog.Get(kind).CategoryName != CardCatalog.Get(primary).CategoryName)
                    deck.Add(new("fixture:lf-" + id, id == "trick" ? Suit.Spade : Suit.Club, id == "trick" ? 11 : 5));
            if (scene is "dodge-quota" or "duel-quota") deck.Add(new("fixture:lf-trick", Suit.Spade, 11));
            if (scene == "initial-short") deck = [new("fixture:lf-basic", Suit.Heart, 9), new("fixture:lf-equipment", Suit.Club, 5)];
            if (scene == "prep-replacement") deck = Enumerable.Range(0, 36).Select(i =>
                new ContentDeckPhysicalCard("fixture:lf-" + (i < 12 ? "basic" : i < 24 ? "trick" : "equipment"), Suit.Heart, 9)).ToList();
            b.AddDeck(new("fixture:lf-deck", "真实开局三类后同质的小牌堆", scene == "prep-replacement" ? 1 : 0, 0, []) { PhysicalCards = deck.ToArray() });
            var roles = faction ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Loyalist)] = 1 }
                : scene == "paid-death" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 }
                : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            b.AddMode(new(Mode, "李丰真实四人原牌边界", 4, 4, roles, "fixture:lf-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
        private static void RegisterProductionSkills(IContentRegistryBuilder b, string bundle, params string[] skills)
        {
            string Read(string suffix)
            {
                using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream(
                    "CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json")
                    ?? throw new InvalidOperationException("Missing exact production resource " + bundle + "." + suffix);
                using var reader = new StreamReader(stream); return reader.ReadToEnd();
            }
            var catalog = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
            foreach (var skill in skills)
            {
                var text = catalog.Presentations[skill];
                b.AddSkill(new(skill, text.Name, text.Description) { Program = catalog.Programs[skill], ProgramPresentation = text });
            }
        }
    }
}
