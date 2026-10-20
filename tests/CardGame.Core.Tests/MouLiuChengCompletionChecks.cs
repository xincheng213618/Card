using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class MouLiuChengCompletionChecks
{
    private const string Lueying = "mou-mobile:lueying", Yingwu = "mou-mobile:yingwu",
        Driver = "fixture:mlcc-driver", Watch = "fixture:mlcc-watch", Quiet = "fixture:mlcc-quiet",
        Conditional = "fixture:mlcc-conditional", Gear = "fixture:mlcc-gear",
        Owner = "fixture:mlcc-owner", Ally = "fixture:mlcc-ally", EnemyA = "fixture:mlcc-enemy-a",
        EnemyB = "fixture:mlcc-enemy-b", Deck = "fixture:mlcc-deck",
        DrawReason = "skill-program.mou-mobile:liu-cheng.draw";
    private static ShanheStartingCard C(CardKind kind) => new(kind, Suit.Spade, 7);

    public static void CompletionMandatoryDebitDrawShortfallResponsesAndPaidChildrenCold()
    {
        var ordinary = Scene.Start(new("mandatory-once") { StockKind = CardKind.IronChain });
        var original = ordinary.PrepareFour(); ordinary.ReachChoice(CardKind.Dismantlement);
        ordinary.FirstPaid(); ordinary.Private(); ordinary.RejectPrompt(); ordinary.Cold();
        ordinary.Need(ordinary.E<MouLiuChengDrawReturnedEvent>().Single() is { RequestedCount: 1, ActualCount: 1, ReplacementCount: 0 } &&
            ordinary.E<MouLiuChengNativeIssuedEvent>().Length == 0,
            "The mandatory debit and requested Draw1 precede the only optional use; balance4 cannot loop into a second debit.");
        ordinary.Decline(); ordinary.Play(); ordinary.MaterialOnce(original);
        ordinary.Need(ordinary.Marks == 2 && ordinary.E<MouLiuChengCompletedEvent>().Single().UsedFollowup == false,
            "Declining the follow-up does not refund the mandatory cost or request another draw.");
        var recast = ordinary.V().Hand.Single(c => c.Kind == CardKind.IronChain).Id;
        ordinary.Accept(new RecastCardCommand(0, recast, ordinary.G.Revision, ordinary.P!.PromptId)); ordinary.Play();
        ordinary.Need(ordinary.Marks == 2 && ordinary.E<MouLiuChengCompletionStartedEvent>().Length == 1 &&
            ordinary.G.CardMovements.Any(m => m.CardId == recast && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile),
            "A real native recast moves and replaces its material without becoming a matching true Use completion."); ordinary.CheckAll(); ordinary.Cold();

        var empty = Scene.Start(new("requested-one-empty-stock") { EmptyAtDraw = true });
        var emptyOriginal = empty.PrepareFour(); empty.Reach(() => empty.Hold("pre-draw-child"));
        empty.Need(empty.Marks == 2 && empty.E<MouLiuChengCompletionStartedEvent>().Length == 1 &&
            empty.E<MouLiuChengDrawReturnedEvent>().Length == 0, "The paid parent owns its one request while a real before-draw child drains the remaining entities.");
        empty.Cold(); empty.Continue(); empty.ReachChoice(CardKind.Dismantlement);
        empty.Need(empty.E<MouLiuChengDrawReturnedEvent>().Single() is { RequestedCount: 1, ActualCount: 0, ReplacementCount: 0 } &&
            empty.G.CardMovements.All(m => m.Reason.Value != DrawReason),
            "Genuine Draw20+20+10 children exhaust the real stock and reshuffle supply; the original request remains1 with actual0.");
        empty.Decline(); empty.Play(); empty.MaterialOnce(emptyOriginal); empty.CheckAll(); empty.Cold();

        var replaced = Scene.Start(new("real-zenrun-replacement") { Zenrun = true });
        var replacedOriginal = replaced.PrepareFour(); replaced.Reach(() => replaced.ZenrunOffer());
        replaced.Need(replaced.Marks == 2, "The whole-draw replacement cannot precede the completion's actual debit2."); replaced.Cold();
        replaced.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        replaced.Reach(() => replaced.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "target") == true);
        replaced.Answer(c => c.Parameters.GetValueOrDefault("branch") == "target" && c.Targets.SequenceEqual([2]));
        replaced.Reach(() => replaced.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "pick") == true);
        replaced.Private(); replaced.Need(replaced.P!.Choices.Where(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)).All(c => c.Cards.Count == 0),
            "The actual Zenrun victim's Hand is represented by opaque slots.");
        replaced.Answer(c => c.Parameters.GetValueOrDefault("branch") == "pick" && c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
        replaced.Answer(c => c.Parameters.GetValueOrDefault("branch") == "take"); replaced.ReachChoice(CardKind.Dismantlement);
        replaced.Need(replaced.E<MouLiuChengDrawReturnedEvent>().Single() is { RequestedCount: 1, ActualCount: 0, ReplacementCount: 1 } &&
            replaced.E<ZenrunPaidEvent>().Length == 1 && replaced.G.CardMovements.All(m => m.Reason.Value != DrawReason),
            "One real gained foreign entity replaces the actual request without masquerading as a DrawPile draw.");
        replaced.Decline(); replaced.Play(); replaced.MaterialOnce(replacedOriginal); replaced.CheckAll(); replaced.Cold();

        foreach (var spec in new[]
        {
            new Spec("paid-source-loss") { LossAtDraw = 1 },
            new Spec("paid-self-rescue") { LossAtDraw = 4, StockKind = CardKind.Peach },
            new Spec("paid-owner-death") { LossAtDraw = 4 }
        })
        {
            var paid = Scene.Start(spec); var paidOriginal = paid.PrepareFour(); paid.Reach(() => paid.Hold("gain-child"));
            paid.FirstPaid(); paid.Private(); paid.RejectPrompt(); paid.Cold(); paid.Continue();
            if (spec.LossAtDraw == 4)
            {
                paid.Reach(() => paid.P is { Kind: DecisionKind.RescueDying } || !paid.V().IsAlive);
                if (spec.StockKind == CardKind.Peach)
                {
                    paid.Need(paid.P is { Kind: DecisionKind.RescueDying } && paid.V().Hp <= 0, "The paid draw child enters the actual native dying response.");
                    paid.Cold(); paid.Respond(paid.V().Hand.Single(c => c.Kind == CardKind.Peach).Id);
                    paid.ReachChoice(CardKind.Dismantlement);
                    paid.Need(paid.V().Hp == 1 && !paid.Has(Lueying) && !paid.Has(Yingwu),
                        "The genuinely drawn Peach rescues its owner and returns to the paid completion after both sources are gone.");
                    paid.Decline(); paid.Play(); paid.MaterialOnce(paidOriginal);
                }
                else
                {
                    if (paid.P is { Kind: DecisionKind.RescueDying }) paid.Answer(c => c.Parameters.GetValueOrDefault("response") is "let-die" or "pass");
                    paid.Reach(() => !paid.V().IsAlive);
                    paid.Need(paid.E<MouLiuChengNativeIssuedEvent>().Length == 0,
                        "A genuinely dead owner cannot issue a new follow-up; the native winner cutoff need not manufacture a trailing card Finish.");
                }
            }
            else
            {
                paid.ReachChoice(CardKind.Dismantlement);
                paid.Need(paid.V().Hp == 3 && !paid.Has(Lueying) && !paid.Has(Yingwu), "Real HP source loss happens after the paid draw, without rolling its cost back.");
                paid.UseFollowup(CardKind.Dismantlement, 2); paid.Play(); paid.MaterialOnce(paidOriginal);
                paid.Need(paid.E<MouLiuChengNativeIssuedEvent>().Length == 1 && paid.Marks == 2,
                    "An already-paid living owner may still use its real follow-up; lost Yingwu does not charge that child's completion.");
            }
            paid.Need(paid.E<MouLiuChengCompletionStartedEvent>().Length == 1 && paid.E<MouLiuChengDrawReturnedEvent>().Single().RequestedCount == 1 &&
                paid.G.CardMovements.Count(m => m.Reason.Value == DrawReason && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)) == 1,
                "Resuming source loss, rescue or death never repeats the debit, actual draw or original instruction."); paid.CheckAll(); paid.Cold();
        }

        var played = Scene.Start(new("pure-played-does-not-complete")); var playedOriginal = played.PrepareFour();
        played.ReachChoice(CardKind.Dismantlement); played.Decline(); played.Play();
        var playedId = played.V().Hand.Single(c => c.Kind == CardKind.Slash).Id;
        played.Use("duel", [1]); played.Reach(() => played.P is { Kind: DecisionKind.RespondSlash }); played.Respond(playedId); played.Play();
        played.Need(played.Marks == 2 && played.E<MouLiuChengCompletionStartedEvent>().Length == 1 &&
            played.E<CardUseFinishedEvent>().Count(e => e.CardKind == CardKind.Duel) == 1,
            "A real pure Played Slash in native Duel pays its entity without consuming an existing two-mark completion entitlement.");
        played.MaterialOnce(playedId); played.MaterialOnce(playedOriginal); played.CheckAll(); played.Cold();

        var foreign = Scene.Start(new("foreign-turn-true-nullification") { StockKind = CardKind.Nullification, EnemyTurnTrick = true });
        var foreignOriginal = foreign.PrepareFour(); foreign.ReachChoice(CardKind.Dismantlement); foreign.Decline(); foreign.Play();
        var nullId = foreign.V().Hand.Single(c => c.Kind == CardKind.Nullification).Id; foreign.End();
        foreign.Reach(() => foreign.P is { Kind: DecisionKind.Nullification } && foreign.G.State.CurrentSeat == 2);
        foreign.Respond(nullId); foreign.ReachChoice(CardKind.Slash); foreign.Private(); foreign.Cold();
        var claim = foreign.E<MouLiuChengCompletionStartedEvent>().Last().Claim;
        foreign.Need(claim.EffectiveKind == CardKind.Nullification && claim.ActorSeat == 0 && claim.TurnOwner == 2 && foreign.Marks == 0,
            "A genuine off-turn Nullification Use completes Yingwu and spends the carried balance without a new Play-phase accrual.");
        foreign.Decline(); foreign.Reach(() => foreign.E<CardUseFinishedEvent>().Any(e => e.CardKind == CardKind.Dismantlement));
        foreign.Need(foreign.E<MouLiuChengCompletionStartedEvent>().Length == 2 && foreign.E<MouLiuChengDrawReturnedEvent>().Length == 2 &&
            foreign.E<CardActionAcceptedEvent>().Count(e => e.Action.ActorSeat == 0 && e.Action.EffectiveKind == CardKind.Nullification) == 1,
            "The true response pays once and returns once to the original enemy trick.");
        foreign.MaterialOnce(nullId); foreign.MaterialOnce(foreignOriginal); foreign.CheckAll(); foreign.Cold();
    }

    public static void CompletionNativeAlternatingDismantlementSlashParentsPrivacyAndLegalityCold()
    {
        foreach (var zone in new[] { CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment })
        {
            var chain = Scene.Start(new("alternating-" + zone) { VictimZone = zone });
            var original = chain.PrepareFour(); var victim = chain.VictimEntity(); chain.ReachChoice(CardKind.Dismantlement);
            chain.Private(); chain.RejectPrompt(); chain.Cold(); chain.UseFollowup(CardKind.Dismantlement, 2);
            if (zone == CardZoneKind.Hand)
            {
                chain.Reach(() => chain.P is { Kind: DecisionKind.SelectTargetCard }); chain.Private();
                chain.Need(chain.P!.Choices.All(c => c.Cards.Count == 0 && !c.Parameters.ContainsKey("card-id")),
                    "Zero-material Dismantlement selects an actual foreign Hand slot without disclosing its physical identity.");
                chain.RejectPrompt(); chain.Cold(); chain.Answer(c => c.Parameters.ContainsKey("slot-index"));
            }
            chain.ReachChoice(CardKind.Slash);
            chain.Need(chain.Marks == 0 && chain.E<MouLiuChengCompletionStartedEvent>().Select(e => e.MarkBefore).SequenceEqual([4, 2]) &&
                chain.E<MouLiuChengCompletionStartedEvent>().Select(e => e.MarkAfter).SequenceEqual([2, 0]) &&
                chain.E<MouLiuChengCompletionStartedEvent>().Select(e => e.Claim.ActionId).Distinct().Count() == 2 &&
                chain.E<MouLiuChengDrawReturnedEvent>().All(e => e.RequestedCount == 1),
                "Only the distinct native Dismantlement completion pays the second pair; one completion never drains all four marks.");
            chain.Private(); chain.Cold(); var hp = chain.V(3).Hp; chain.UseFollowup(CardKind.Slash, 3); chain.Play();
            chain.Need(chain.V(3).Hp == hp - 1 && chain.E<MouLiuChengNativeIssuedEvent>().Select(e => e.Kind).SequenceEqual([CardKind.Dismantlement, CardKind.Slash]) &&
                chain.E<MouLiuChengNativeReturnedEvent>().Length == 2 && chain.AuditedNativeUses == 2 &&
                chain.G.CardMovements.Count(m => m.CardId == victim && m.From == new CardLocation(zone, 2) && m.To == CardLocation.Processing) == 1 &&
                chain.G.CardMovements.Count(m => m.CardId == victim && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
                "Both alternating outputs are genuine native Uses, with one actual victim material and two independent zero-material completions.");
            if (zone == CardZoneKind.Equipment)
                chain.Need(chain.E<RecoveryAppliedEvent>().Count(e => e.TargetSeat == 2 && e.Amount == 1) == 1 && chain.V(2).Hp == 12,
                    "The actual equipped SilverLion's departure runs its native removal recovery once before the original completed Slash returns.");
            chain.MaterialOnce(original); chain.CheckAll(); chain.Cold();
        }

        var counter = Scene.Start(new("native-zero-trick-counterspell") { StockKind = CardKind.Nullification });
        var originalCounter = counter.PrepareFour(); var untouched = counter.V(2).Hand.Single().Id;
        counter.ReachChoice(CardKind.Dismantlement); counter.UseFollowup(CardKind.Dismantlement, 2);
        counter.Reach(() => counter.P is { Kind: DecisionKind.Nullification });
        var counterId = counter.V().Hand.Single(c => c.Kind == CardKind.Nullification).Id;
        counter.Cold(); counter.Respond(counterId); counter.ReachChoice(CardKind.Slash);
        counter.Need(counter.E<MouLiuChengCompletionStartedEvent>().Last().Claim.EffectiveKind == CardKind.Nullification && counter.Marks == 0,
            "The true counterspell's completion, rather than the later nullified parent, owns the remaining two-mark payment.");
        counter.UseFollowup(CardKind.Slash, 3); counter.Play();
        counter.Need(counter.V(2).Hand.Any(c => c.Id == untouched) && counter.E<MouLiuChengNativeReturnedEvent>().Length == 2 && counter.AuditedNativeUses == 2,
            "Nested native Slash returns through the exact Nullification, zero-material trick and original completed Slash without taking the nullified target's card.");
        counter.MaterialOnce(counterId); counter.MaterialOnce(originalCounter); counter.CheckAll(); counter.Cold();

        var legal = Scene.Start(new("slash-quota-only-range-and-kongcheng") { StockKind = CardKind.IronChain, KongchengPeer = true });
        var legalOriginal = legal.PrepareFour(); legal.ReachChoice(CardKind.Dismantlement); legal.Decline(); legal.Play();
        var weapon = legal.V().Equipment.Single().Id; legal.Use("discard-weapon", cards: [weapon]); legal.Play();
        legal.Need(legal.V().Equipment.Count == 0, "The source really discards Fangtian before issuing the later ordinary Slash.");
        legal.Native(CardKind.IronChain, [0]); legal.ReachChoice(CardKind.Slash); legal.Private(); legal.Cold();
        var plans = legal.P!.Choices.Where(c => c.Parameters.GetValueOrDefault("branch") == "use").ToArray();
        legal.Need(plans.Length > 0 && plans.All(c => c.Targets.SequenceEqual([1])) && legal.V(3).HandCount == 0 &&
            legal.E<CardUseDeclaredEvent>().Count(e => e.SourceSeat == 0 && e.CardKind == CardKind.Slash && e.CardId > 0) == 1,
            "After the ordinary Slash quota is spent, the follow-up exempts only that quota: unarmed distance2, self and genuine empty-Hand Kongcheng remain forbidden.");
        var targetHp = legal.V(1).Hp; legal.UseFollowup(CardKind.Slash, 1); legal.Play();
        legal.Need(legal.V(1).Hp == targetHp - 1 && legal.E<MouLiuChengNativeIssuedEvent>().Single().Kind == CardKind.Slash,
            "The single remaining ordinary-distance target receives the real no-count Slash.");
        legal.MaterialOnce(legalOriginal); legal.CheckAll(); legal.Cold();

        var selfOnly = Scene.Start(new("self-only-paid-native-followups") { StockKind = CardKind.IronChain, SelfOnlyAtDraw = true });
        var selfOnlyOriginal = selfOnly.PrepareFour(); selfOnly.Reach(() => selfOnly.Hold("gain-child"));
        selfOnly.FirstPaid(); selfOnly.Private(); selfOnly.RejectPrompt(); selfOnly.Cold(); selfOnly.Continue(); selfOnly.Play();
        var restriction = selfOnly.E<CardTargetRestrictionGrantedEvent>().Single().Restriction;
        selfOnly.Need(restriction.Restriction == SkillProgramCardTargetRestriction.SelfOnly && restriction.Source.OwnerSeat == 0 &&
            restriction.TurnNumber == selfOnly.G.State.TurnNumber && restriction.TurnSeat == selfOnly.G.State.CurrentSeat &&
            Enumerable.Range(0, 4).All(viewer => selfOnly.G.CreateSnapshot(viewer).Players[0].ActiveEffects?.Any(e =>
                e.SkillId == Watch && e.Expiry == ActivePlayerEffectExpiry.CurrentTurnEnd && e.Description == "使用牌只能指定自己为目标。") == true) &&
            selfOnly.Marks == 2 && selfOnly.Has(Lueying) && selfOnly.Has(Yingwu) &&
            selfOnly.E<MouLiuChengDrawReturnedEvent>().Single() is { RequestedCount: 1, ActualCount: 1 } &&
            selfOnly.E<MouLiuChengCompletedEvent>().Single().UsedFollowup == false && selfOnly.E<MouLiuChengNativeIssuedEvent>().Length == 0,
            "A real paid draw-gain child grants the native SelfOnly turn restriction: Lueying still pays and draws once but cannot publish or issue a foreign-target Dismantlement.");
        selfOnly.Cold(); var selfIron = selfOnly.V().Hand.Single(c => c.Kind == CardKind.IronChain).Id;
        selfOnly.Native(CardKind.IronChain, [0]); selfOnly.Play();
        selfOnly.Need(selfOnly.Marks == 0 && selfOnly.E<MouLiuChengCompletionStartedEvent>().Select(e => e.Claim.EffectiveKind).SequenceEqual([CardKind.Slash, CardKind.IronChain]) &&
            selfOnly.E<MouLiuChengCompletionStartedEvent>().Select(e => e.MarkBefore).SequenceEqual([4, 2]) &&
            selfOnly.E<MouLiuChengCompletionStartedEvent>().Select(e => e.MarkAfter).SequenceEqual([2, 0]) &&
            selfOnly.E<MouLiuChengDrawReturnedEvent>().Length == 2 && selfOnly.E<MouLiuChengDrawReturnedEvent>().All(e => e.RequestedCount == 1 && e.ActualCount == 1) &&
            selfOnly.E<MouLiuChengCompletedEvent>().Length == 2 && selfOnly.E<MouLiuChengCompletedEvent>().All(e => !e.UsedFollowup) &&
            selfOnly.E<MouLiuChengNativeIssuedEvent>().Length == 0 && selfOnly.E<CardTargetRestrictionGrantedEvent>().Length == 1,
            "A genuinely legal self IronChain consumes the two carried marks and draws once under SelfOnly; Yingwu cannot publish or issue a foreign-target Slash even though its Slash quota is exempt.");
        selfOnly.MaterialOnce(selfIron); selfOnly.MaterialOnce(selfOnlyOriginal); selfOnly.CheckAll(); selfOnly.Cold();
    }

    private sealed record Spec(string Name)
    {
        public CardKind StockKind { get; init; } = CardKind.Slash;
        public CardZoneKind VictimZone { get; init; } = CardZoneKind.Hand;
        public bool EmptyAtDraw { get; init; }
        public bool Zenrun { get; init; }
        public int LossAtDraw { get; init; }
        public bool EnemyTurnTrick { get; init; }
        public bool KongchengPeer { get; init; }
        public bool SelfOnlyAtDraw { get; init; }
        public ShanheStartingCard[] Hand => VictimZone == CardZoneKind.Judgment ? [C(CardKind.Indulgence), C(CardKind.IronChain), C(CardKind.Slash)] : [C(CardKind.IronChain), C(CardKind.Slash)];
        public ShanheStartingCard[] Equipment => [C(CardKind.FangtianHalberd)];
        public CardKind[] Stock => VictimZone == CardZoneKind.Equipment ? [CardKind.SilverLion, .. Enumerable.Repeat(StockKind, 47)] : Enumerable.Repeat(StockKind, 48).ToArray();
        public ShanheStartingCard[] PeerHand => EnemyTurnTrick ? [C(CardKind.Dismantlement)] : VictimZone == CardZoneKind.Hand ? [C(CardKind.Slash)] : [];
    }

    private sealed class Fixture(Spec spec) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-liu-cheng-completion", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            static SkillProgramCatalog Bundle(string bundle)
            {
                string Read(string suffix)
                {
                    using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json") ?? throw new InvalidOperationException("Missing complete bundle " + bundle);
                    using var reader = new StreamReader(stream); return reader.ReadToEnd();
                }
                return SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
            }
            var real = Bundle("mou-liu-cheng-mobile");
            foreach (var id in new[] { Lueying, Yingwu }) b.AddSkill(new(id, real.Presentations[id].Name, real.Presentations[id].Description) { Program = real.Programs[id], ProgramPresentation = real.Presentations[id] });
            if (spec.KongchengPeer)
            {
                var passive = Bundle("passive-card-rules"); const string id = "classic:kongcheng";
                b.AddSkill(new(id, passive.Presentations[id].Name, passive.Presentations[id].Description) { Program = passive.Programs[id], ProgramPresentation = passive.Presentations[id] });
            }
            if (spec.Zenrun)
            {
                var zenrun = Bundle("ol-sun-hong"); const string id = "ol:zenrun";
                b.AddSkill(new(id, zenrun.Presentations[id].Name, zenrun.Presentations[id].Description) { Program = zenrun.Programs[id], ProgramPresentation = zenrun.Presentations[id] });
            }
            foreach (var kind in spec.Hand.Concat(spec.Equipment).Concat(spec.PeerHand).Select(c => c.Kind).Concat(spec.Stock).Distinct())
            {
                var card = CardCatalog.Get(kind); b.AddCard(new("fixture:mlcc-card-" + kind, card.DisplayName, card.CategoryName, card.Description, kind));
            }
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static string Pause(string id) => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"held\",\"options\":[{\"id\":\"" + id + "\"}]}";
            static JsonNode A(string id, string effects, int targets = 0, int cards = 0) => N("{\"id\":\"" + id + "\",\"minCards\":" + cards + ",\"maxCards\":" + cards + ",\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"" + (targets == 0 ? "anyLiving" : "otherLiving") + "\",\"usesPerTurn\":null,\"effects\":[" + effects + "]}");
            var duel = A("duel", "{\"op\":\"useSelectedActorDuel\",\"target\":\"owner\"}", targets: 1); duel["usesPerPhase"] = 2;
            var discard = A("discard-weapon", "{\"op\":\"discardSelected\",\"target\":\"owner\",\"amount\":1}", cards: 1); discard["sourceZones"] = N("[\"equipment\"]");
            var defs = new JsonArray(new JsonObject
            {
                ["id"] = Driver, ["revision"] = 1,
                ["activations"] = new JsonArray(A("recover-one", "{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}"),
                    duel, discard)
            }, N("{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"));
            var grants = new JsonArray(N("{\"id\":\"healthy-yingwu\",\"skillId\":\"" + Yingwu + "\",\"minimumHp\":4}"));
            if (spec.LossAtDraw > 0) grants.Add(N("{\"id\":\"healthy-lueying\",\"skillId\":\"" + Lueying + "\",\"minimumHp\":4}"));
            defs.Add(new JsonObject { ["id"] = Conditional, ["revision"] = 1, ["hpSkillGrants"] = grants });
            var watches = new JsonArray(
                N("{\"id\":\"lose-yingwu-before-complete\",\"window\":\"cardUseTargetsFinalized\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"ironChain\"],\"priority\":-1000,\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]}"),
                N("{\"id\":\"audit-native-completed\",\"window\":\"cardUseCompleted\",\"ownerRelation\":\"actor\",\"includeResponseUses\":true,\"singleActionInstance\":true,\"priority\":1000,\"optional\":false,\"effects\":[" + Pause("completed-use") + "]}"));
            if (spec.LossAtDraw > 0) watches.Add(N("{\"id\":\"paid-gain-child\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementReasons\":[\"" + DrawReason + "\"],\"movementOccurrence\":\"perBatch\",\"priority\":1000,\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Pause("gain-child") + ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + spec.LossAtDraw + "}]}"));
            if (spec.SelfOnlyAtDraw) watches.Add(N("{\"id\":\"paid-native-self-only\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementReasons\":[\"" + DrawReason + "\"],\"movementOccurrence\":\"perBatch\",\"priority\":1000,\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Pause("gain-child") + ",{\"op\":\"grantTurnCardTargetRestriction\",\"target\":\"owner\",\"targetRestriction\":\"selfOnly\"}]}"));
            if (spec.EmptyAtDraw) watches.Add(N("{\"id\":\"drain-before-draw\",\"window\":\"nativeDrawStarting\",\"subject\":\"owner\",\"condition\":{\"kind\":\"phaseIsPlay\"},\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Pause("pre-draw-child") + ",{\"op\":\"draw\",\"target\":\"owner\",\"amount\":20},{\"op\":\"draw\",\"target\":\"owner\",\"amount\":20},{\"op\":\"draw\",\"target\":\"owner\",\"amount\":10}]}"));
            defs.Add(new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = watches });
            if (spec.VictimZone == CardZoneKind.Equipment) defs.Add(N("{\"id\":\"" + Gear + "\",\"revision\":1,\"triggers\":[{\"id\":\"real-opening-equipment\",\"window\":\"gameStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"targetRef\":{\"kind\":\"owner\"},\"resultBind\":\"actual-equipment\"}]}]}"));
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new
            {
                name = n!["id"]!.GetValue<string>(), description = "谋刘赪真实完成段小夹具",
                optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(binding => binding!["effects"]!.AsArray())
                    .SelectMany(effect => effect!["options"]?.AsArray() ?? new JsonArray()).Select(option => option!["id"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).ToDictionary(id => id, id => id)
            });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, labels[id].name, labels[id].description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var own = new List<string> { Driver, Watch, Conditional }; if (spec.LossAtDraw == 0) own.Add(Lueying); if (spec.Zenrun) own.Add("ol:zenrun");
            b.AddGeneral(new(Owner, "完成段真实本人", "", "standard:none", "qun", 4, own));
            b.AddGeneral(new(Ally, "静默盟友", "", "standard:none", "qun", 12, [Quiet]));
            var enemySkills = new List<string>(); if (!spec.EnemyTurnTrick) enemySkills.Add(Quiet); if (spec.VictimZone == CardZoneKind.Equipment) enemySkills.Add(Gear);
            b.AddGeneral(new(EnemyA, "真实实体对手", "", "standard:none", "qun", 12, enemySkills));
            b.AddGeneral(new(EnemyB, "空手对手", "", "standard:none", "qun", 12, spec.KongchengPeer ? [Quiet, "classic:kongcheng"] : [Quiet]));
            b.AddDeck(new(Deck, "固定四十八张真实实体", 0, 0, []) { PhysicalCards = spec.Stock.Select(kind => new ContentDeckPhysicalCard("fixture:mlcc-card-" + kind, Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋刘赪真实完成段", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }

    private sealed class Scene(Spec spec, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        private readonly HashSet<long> _audited = [];
        public int AuditedNativeUses => _audited.Count;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool Has(string skill) => V().Skills?.Any(s => s.ContentId == skill) == true;
        public int Marks => V().Markers?.SingleOrDefault(m => m.Kind == PlayerMarkerKind.MouLiuChengChui)?.Count ?? 0;
        public bool Hold(string id) => P?.SkillPrompt?.SkillId == Watch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id);
        public bool Choice(CardKind? kind = null) => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mou-liu-cheng-use" &&
            (kind is null || c.Parameters.GetValueOrDefault("output-kind") == kind.ToString())) == true;
        public bool ZenrunOffer() => P?.SkillPrompt?.SkillId == "ol:zenrun" && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public int PrepareFour()
        {
            if (spec.VictimZone == CardZoneKind.Judgment) { Native(CardKind.Indulgence, [2]); Play(); }
            Native(CardKind.IronChain, [2, 3]); Play();
            Need(Marks == 2 && V().Hp == 3 && !Has(Yingwu) && E<MouLiuChengCompletionStartedEvent>().Length == 0,
                "A genuine two-target IronChain earns two shared marks before a lower-priority real HP loss removes only its completion source.");
            Use("recover-one"); Play(); Need(V().Hp == 4 && Has(Lueying) && Has(Yingwu) && Marks == 2,
                "Native recovery regrants the complete source without erasing its actual previously-earned balance.");
            var material = V().Hand.Single(c => c.Kind == CardKind.Slash).Id;
            Need(V().Hand.Count == 1, "The actual Slash is the genuine last Hand entity for Fangtian.");
            var action = G.GetHumanLegalActions().Where(a => a.CardId == material && a.ConversionSource is null && a.TargetSeats.Count >= 2).OrderByDescending(a => a.TargetSeats.Count).FirstOrDefault();
            Need(action is not null, "The actual equipped Fangtian publishes a native last-Hand multi-target Slash."); Native(action!); return material;
        }
        public int VictimEntity() => spec.VictimZone switch
        {
            CardZoneKind.Hand => V(2).Hand.Single().Id,
            CardZoneKind.Equipment => V(2).Equipment.Single().Id,
            CardZoneKind.Judgment => V(2).Judgment.Single().Id,
            _ => throw new InvalidOperationException("Unsupported exact native victim zone.")
        };
        public void FirstPaid() => Need(Marks == 2 && E<MouLiuChengCompletionStartedEvent>() is [{ MarkBefore: 4, MarkAfter: 2, Claim: { EffectiveKind: CardKind.Slash } }],
            "At least two marks force exactly one debit2 on this real completed Slash; its balance4 is not a while loop.");
        public void ReachChoice(CardKind kind) => Reach(() => Choice(kind));
        public void Decline() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "mou-liu-cheng-use" && c.Parameters.GetValueOrDefault("branch") == "decline");
        public void UseFollowup(CardKind kind, int target) => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "mou-liu-cheng-use" &&
            c.Parameters.GetValueOrDefault("branch") == "use" && c.Parameters.GetValueOrDefault("output-kind") == kind.ToString() && c.Targets.SequenceEqual([target]));
        public void Use(string id, int[]? targets = null, int[]? cards = null) => Accept(new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void Native(CardKind kind, int[] targets)
        {
            var action = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId is { } id && V().Hand.Any(c => c.Id == id && c.Kind == kind) && a.ConversionSource is null && a.Kind != LegalActionKind.Recast && a.TargetSeats.SequenceEqual(targets));
            Need(action is not null, "A genuinely published native " + kind + " plan is required."); Native(action!);
        }
        private void Native(LegalAction a) => Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId)
        { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        public void Respond(int id) => Answer(c => c.Cards.Contains(id) && c.Parameters.GetValueOrDefault("response") is not ("pass" or "take-damage" or "let-die"));
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") is "completed-use" or "gain-child" or "pre-draw-child");
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var p = P!; var c = p.Choices.SingleOrDefault(predicate); Need(p.PlayerSeat == 0 && c is not null, "The exact published human choice is required.");
            Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision));
        }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var step = 0; step < 320; step++)
            {
                Need(!spec.SelfOnlyAtDraw || !E<CardTargetRestrictionGrantedEvent>().Any(e => e.Restriction.Restriction == SkillProgramCardTargetRestriction.SelfOnly) || !Choice(),
                    "The issued native SelfOnly restriction prevents publishing either forbidden follow-up prompt; auto-declining an illegal offer cannot hide a regression.");
                if (done()) return;
                Need(G.State.Winner == Winner.None, "Native terminal cutoff precedes boundary " + line);
                if (P is null) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (P.Kind == DecisionKind.PlayCard) End();
                else if (Hold("completed-use")) { AuditNative(); Continue(); }
                else if (Hold("gain-child") || Hold("pre-draw-child")) Continue();
                else if (Choice()) Decline();
                else if (ZenrunOffer()) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (P.Kind == DecisionKind.SelectTargetCard)
                {
                    Private(); Cold(); Answer(c => c.Parameters.ContainsKey("slot-index") || c.Cards.Count == 1);
                }
                else if (P.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else throw new InvalidOperationException("Unexpected real boundary " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded native boundary " + line + " " + Diagnostic());
        }
        private void AuditNative()
        {
            foreach (var use in G.ResolutionStack.OfType<CardUseFrame>().Where(f => f.MouLiuChengOrigin is not null && !_audited.Contains(f.Id)))
            {
                var origin = use.MouLiuChengOrigin!; var a = use.Action;
                Need(a is { Type: CardActionType.Use, ActorSeat: 0, ProviderSeat: 0, EffectiveSuit: Suit.None, EffectiveRank: 0, EffectiveIsRed: null } &&
                    a.PhysicalCards.Count == 0 && use.PhysicalCardIds.Count == 0 && a.ActionId == origin.ActionId && a.EffectiveKind == origin.Kind &&
                    E<MouLiuChengNativeIssuedEvent>().Count(e => e.UseId == use.Id && e.ActionId == a.ActionId && e.Kind == origin.Kind) == 1,
                    "The live completed native child has its exact typed origin and canonical zero-material Use representation, never a displayed or generated physical card.");
                _audited.Add(use.Id); Cold();
            }
        }
        public void MaterialOnce(int id) => Need(G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
            "The original actual Hand material pays and leaves Processing exactly once.");
        public void CheckAll()
        {
            var starts = E<MouLiuChengCompletionStartedEvent>();
            Need(G.CardMovements.All(m => m.CardId > 0) && starts.All(e => e.MarkBefore - e.MarkAfter == 2) &&
                starts.Select(e => (e.Claim.Source.OwnerSeat, e.Claim.Source.SkillId, e.Claim.ActionId)).Distinct().Count() == starts.Length &&
                E<MouLiuChengDrawReturnedEvent>().All(e => e.RequestedCount == 1) &&
                E<MouLiuChengNativeIssuedEvent>().Select(e => e.UseId).Distinct().Count() == E<MouLiuChengNativeIssuedEvent>().Length,
                "No logical Card0 enters a zone; each genuine completion and native child owns one independent payment and one requested Draw1.");
            if (G.State.Winner == Winner.None)
                foreach (var issued in E<MouLiuChengNativeIssuedEvent>()) Need(E<MouLiuChengNativeReturnedEvent>().Count(e => e.UseId == issued.UseId && e.ActionId == issued.ActionId) == 1 &&
                    E<CardUseFinishedEvent>().Count(e => e.ResolutionId == issued.UseId && e.CardKind == issued.Kind) == 1,
                    "Each real follow-up retires once through its original native Finish and exact typed parent return.");
        }
        public void Private()
        {
            Need(P is { PlayerSeat: 0, IsPrivate: true } && Enumerable.Range(1, 3).All(i => G.CreateSnapshot(i).PendingDecision is null) &&
                Enumerable.Range(0, 4).All(viewer => G.CreateSnapshot(viewer).Players.Where(p => p.Seat != viewer).All(p => p.Hand.Count == 0)),
                "Only the actual chooser sees its private prompt; every other Hand remains opaque in player snapshots.");
            Immutable(P!.Choices); foreach (var c in P.Choices) { Immutable(c.Cards); Immutable(c.Targets); }
        }
        private void Immutable<T>(IReadOnlyList<T> values)
        {
            if (values is IList list && list.Count > 0)
            {
                var before = JsonSerializer.Serialize(values); var rejected = false;
                try { list[0] = list[0]; } catch (NotSupportedException) { rejected = true; }
                Need(rejected && before == JsonSerializer.Serialize(values), "Committed nested prompt collections are immutable.");
            }
        }
        public void RejectPrompt()
        {
            var p = P!; Reject(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision));
            Reject(new AnswerPromptCommand(0, p.PromptId, new("forged-mou-liu-cheng-plan"), G.Revision));
            Reject(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, G.Revision - 1));
        }
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements,
            Zones = G.CreateCardZoneDiagnostics(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands)
        });
        public void Cold()
        {
            var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before == State(), "Cold replay preserves every viewer, original completed Use, paid debit, requested draw and exact native child cursor.");
        }
        private void Reject(GameCommand command) { var before = State(); Need(!G.Submit(command).Accepted && before == State(), "Wrong-seat, forged and stale choices cannot mutate the paid native operation."); }
        public void Accept(GameCommand command)
        {
            try { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, "Rejected " + command.GetType().Name + ": " + result.Error?.Code + "/" + result.Error?.Message); }
            catch (Exception e) { throw new InvalidOperationException(spec.Name + ": " + e.Message + " " + Diagnostic(), e); }
        }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new
        {
            spec.Name, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner, Marks, Prompt = P,
            Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).IsAlive, V(i).HandCount, Equipment = V(i).Equipment.Select(c => c.Kind) }),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Facts = G.Events.Where(e => e.Payload.GetType().Name.StartsWith("MouLiuCheng", StringComparison.Ordinal)).TakeLast(12).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            Moves = G.CardMovements.TakeLast(10)
        });
        public static Scene Start(Spec spec)
        {
            ContentRegistry registry;
            try { registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(spec)); }
            catch (Exception e) { throw new InvalidOperationException(spec.Name + " fixture: " + e.Message, e); }
            var setup = new ShanheBattleSetup(83, 1, "mlcc-" + spec.Name, Owner, 4, 0, 0, 0, 0, [], spec.Hand, spec.Equipment,
                [new(EnemyA) { StartingHand = spec.PeerHand }, new(EnemyB) { StartingHand = [] }], [new(Ally) { StartingHand = [] }], new string('c', 64));
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false, MaxTurns = 12, AiPolicyVersion = AiDecisionPolicy.CurrentVersion
            }, registry);
            var scene = new Scene(spec, game, registry); scene.Accept(new StartGameCommand()); scene.Play(); return scene;
        }
    }
}
