using System.Collections;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class LeCaiYongChecks
{
    private const string Jiaowei = "le:jiaowei", Feibai = "le:feibai";
    private const string Driver = "fixture:lcy-driver", Quiet = "fixture:lcy-quiet", Watch = "fixture:lcy-watch", Conditional = "fixture:lcy-conditional";
    private const string Owner = "fixture:lcy-owner", Ally = "fixture:lcy-ally", EnemyA = "fixture:lcy-enemy-a", EnemyB = "fixture:lcy-enemy-b", Deck = "fixture:lcy-deck";
    private const string ObtainReason = "skill-program.le-cai-yong.feibai.obtain", DrawReason = "skill-program.le-cai-yong.feibai.draw";
    private static ShanheStartingCard C(CardKind kind, int rank = 1) => new(kind, Suit.Spade, rank);
    private static bool Xian(CardSnapshot c) => c.SkillMarkLabel?.Contains("弦", StringComparison.Ordinal) == true;

    public static void InitialXianActualLossBinaryProtectionArmorAndExpiryCold()
    {
        var limit = Scene.Start(new("initial-limit") { Hand = [C(CardKind.Slash), C(CardKind.Dodge), C(CardKind.Peach), C(CardKind.Alcohol)] });
        var initial = limit.V().Hand.Select(c => c.Id).ToArray();
        limit.Need(limit.V() is { Hp: 3, MaxHp: 3 } && initial.Length == 4 && limit.V().Hand.All(Xian) &&
            limit.E<JiaoweiInitializedEvent>().Single().EntityCount == 4 && limit.E<XianEntityMarkedEvent>().Count(e => e.Origin == XianMarkOrigin.OpeningHand) == 4,
            "The genuine three-HP owner marks exactly its four final initial Hand entities once."); limit.Cold(); limit.Use("draw-four"); limit.Play();
        limit.Need(limit.V().Hand.Count(Xian) == 4 && limit.V().Hand.Count(c => !Xian(c)) == 4, "A later genuine Draw4 does not become a second opening initialization.");
        var turn = limit.G.State.TurnNumber; limit.End(); limit.Reach(() => limit.G.State.TurnNumber > turn && limit.P is { Kind: DecisionKind.PlayCard });
        limit.Need(initial.All(id => limit.V().Hand.Any(c => c.Id == id && Xian(c))) && limit.V().Hand.Count(c => !Xian(c)) == 3 &&
            !limit.G.CardMovements.Any(m => m.TurnNumber == turn && initial.Contains(m.CardId) && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile),
            "The real discard phase excludes the four Xian originals and discards only the ordinary over-limit unmarked entity."); limit.Cold();

        var shield = Scene.Start(new("loss-protection") { Hand = [C(CardKind.Slash, 1), C(CardKind.Slash, 2), C(CardKind.Dodge, 3), C(CardKind.Dodge, 4)], ConditionalSources = true });
        var originals = shield.V().Hand.Select(c => c.Id).ToArray(); shield.Use("give", [1], originals.Take(2).ToArray()); shield.Play();
        shield.Use("give", [1], [originals[2]]); shield.Play();
        shield.Need(shield.E<JiaoweiProtectionArmedEvent>().Length == 2 && shield.E<JiaoweiProtectionArmedEvent>().Select(e => e.ActualLostCount).SequenceEqual([2, 1]) &&
            shield.E<JiaoweiProtectionArmedEvent>().Last().ReplacedArmId == shield.E<JiaoweiProtectionArmedEvent>().First().ArmId && shield.V(1).Hand.All(Xian),
            "Actual same-batch two-Xian loss and a subsequent loss replace one binary entitlement; the physical marks remain on the transferred originals.");
        shield.Use("armor-self"); shield.Play(); shield.Use("hp-loss-one"); shield.Play();
        shield.Need(shield.V() is { Hp: 2, Armor: 2 } && shield.E<JiaoweiDamagePreventedEvent>().Length == 0,
            "Direct HP Loss bypasses Armor and does not consume the already-earned next-damage prevention."); shield.Cold();
        shield.Use("self-damage-two"); shield.Play();
        shield.Need(shield.V() is { Hp: 2, Armor: 2 } && shield.E<DamageAppliedEvent>().Length == 0 && shield.E<JiaoweiDamagePreventedEvent>().Single().Amount == 2,
            "The next complete positive native damage is prevented before Armor payment, including its full two-point amount.");
        shield.Use("self-damage-one"); shield.Play();
        shield.Need(shield.V() is { Hp: 2, Armor: 1 } && shield.E<DamageAppliedEvent>().Single() is { Amount: 1, ArmorPayment: { ArmorAbsorbed: 1, HpLossAmount: 0 } } &&
            shield.E<JiaoweiDamagePreventedEvent>().Length == 1, "Coalesced losses do not reveal a second stacked prevention when the first entitlement is consumed.");
        shield.Use("discard", cards: [originals[3]]); shield.Play(); shield.Use("hp-loss-one"); shield.Play();
        shield.Need(!shield.Has(Jiaowei) && !shield.Has(Feibai) && shield.V().Hp == 1, "A genuine HP conditional source loss removes both skills after another actual marked loss has armed protection.");
        shield.Use("self-damage-two"); shield.Play();
        shield.Need(shield.V() is { Hp: 1, Armor: 1 } && shield.E<JiaoweiDamagePreventedEvent>().Length == 2,
            "Already-earned protection remains spendable after actual source loss, and still precedes Armor."); shield.Use("recover-two"); shield.Play();
        shield.Need(shield.Has(Jiaowei) && shield.E<JiaoweiInitializedEvent>().Length == 1 && shield.E<XianEntityMarkedEvent>().Count(e => e.Origin == XianMarkOrigin.OpeningHand) == 4,
            "Real regrant restores the capability without marking later cards or reinitializing its original entities."); shield.Cold();

        var expiry = Scene.Start(new("expiry") { Hand = [C(CardKind.Slash)] }); expiry.Use("give", [1], [expiry.V().Hand.Single().Id]); expiry.Play();
        var armedTurn = expiry.G.State.TurnNumber; expiry.End(); expiry.Reach(() => expiry.G.State.TurnNumber > armedTurn && expiry.P is { Kind: DecisionKind.PlayCard });
        expiry.Use("self-damage-one"); expiry.Play();
        expiry.Need(expiry.V().Hp == 2 && expiry.E<DamageAppliedEvent>().Single().Amount == 1 && expiry.E<JiaoweiDamagePreventedEvent>().Length == 0,
            "An unused prior-turn entitlement cannot prevent damage in the next real actual turn."); expiry.Cold();
    }

    public static void FeibaiEffectiveNamesPreviousUsePrivateSamplesAndCold()
    {
        var names = Scene.Start(new("names") { Hand = [C(CardKind.Slash), C(CardKind.Duel)],
            Stock = Enumerable.Repeat(CardKind.Slash, 16).Concat(Enumerable.Repeat(CardKind.Duel, 16)).Concat(Enumerable.Repeat(CardKind.BaguaFormation, 16)).ToArray() });
        var slash = names.V().Hand.Single(c => c.Kind == CardKind.Slash).Id; names.Native(CardKind.Slash, [1]); names.ReachSample();
        names.Need(names.Prefix(CardKind.Slash) is { PreviousKind: null, NameLength: 1, PreviousNameLength: 0, X: 1 },
            "The first actual Use uses its own one-character name with no invented previous card."); names.AssertSample(1, 2); names.SelectSample(); names.Play();
        var duel = names.V().Hand.Single(c => c.Kind == CardKind.Duel).Id; names.Native(CardKind.Duel, [1]); names.ReachSample();
        names.Need(names.Prefix(CardKind.Duel) is { PreviousKind: CardKind.Slash, NameLength: 2, PreviousNameLength: 1, X: 3 },
            "The next real Duel uses its effective two-character name and the same actor's prior declared Slash, not a gained card or another actor's response.");
        names.AssertSample(3, 2); names.SelectSample(); names.Play();
        names.Need(names.PaidOnce(slash) && names.PaidOnce(duel) && names.E<FeibaiObtainedEvent>().Length == 2 && names.E<FeibaiDisabledEvent>().Length == 0 &&
            names.E<FeibaiCompletedEvent>().All(e => !e.Fallback && e.ObtainedCardId is not null),
            "Each successful private sample obtains one actual deck entity without Draw2, new fallback Xian marks or same-turn disable."); names.Cold();

        var one = Scene.Start(new("one-candidate") { Hand = [C(CardKind.Slash)], Stock = [CardKind.Slash, .. Enumerable.Repeat(CardKind.Duel, 47)] });
        one.Native(CardKind.Slash, [1]); one.ReachSample(); one.AssertSample(1, 1); one.SelectSample(); one.Play();
        one.Need(one.E<FeibaiObtainedEvent>().Length == 1 && one.E<FeibaiFallbackDrawReturnedEvent>().Length == 0, "One real matching deck card is sufficient for successful acquisition; no second candidate is fabricated."); one.Cold();

        var converted = Scene.Start(new("effective-fire") { Hand = [C(CardKind.Slash)], Stock = Enumerable.Repeat(CardKind.Duel, 48).ToArray(), FireConversion = true });
        var material = converted.V().Hand.Single().Id;
        var action = converted.G.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == Driver && a.PlayedCardKind == CardKind.FireSlash && a.TargetSeats.SequenceEqual([1]));
        converted.Native(action); converted.ReachSample();
        converted.Need(converted.Prefix(CardKind.FireSlash) is { PreviousKind: null, NameLength: 2, X: 2 },
            "A real Slash material converted to FireSlash uses the effective 火杀 two-rune name, preserving the Sirui naming convention rather than normalizing it to 杀.");
        converted.AssertSample(2, 2); converted.SelectSample(); converted.Play(); converted.Need(converted.PaidOnce(material), "The actual converted Hand material pays once through its native Use."); converted.Cold();

        var opening = Scene.Start(new("opening-native") { Hand = [C(CardKind.Crossbow)], OpeningEquipment = true, Stock = [CardKind.Crossbow, .. Enumerable.Repeat(CardKind.Slash, 47)] });
        var initialHandEquipment = opening.V().Hand.Single(c => c.Kind == CardKind.Crossbow).Id;
        var initialEquipmentUse = opening.E<CardUseDeclaredEvent>().Single(e => e.SourceSeat == 0 && e.CardKind == CardKind.Crossbow);
        opening.Need(opening.G.State.TurnNumber == 0 && opening.Prefix(CardKind.Crossbow) is { ActualTurn: 0, PreviousKind: null, NameLength: 4, X: 4 } &&
            initialEquipmentUse.CardId > 0 && initialEquipmentUse.CardId != initialHandEquipment && opening.V().Equipment.Any(c => c.Id == initialEquipmentUse.CardId) &&
            opening.G.CardMovements.Count(m => m.CardId == initialEquipmentUse.CardId && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing) == 1,
            "A supported real GameStarting random-deck Equipment Use pays its only actual equipment candidate and captures a first-name prefix before any TurnStarted fact.");
        opening.Private(); opening.Cold(); opening.ActivateFeibai(); opening.Play();
        opening.Need(opening.E<FeibaiCompletedEvent>().Single().Fallback && opening.E<FeibaiDisabledEvent>().Single().ActualTurn == 0 &&
            opening.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == initialEquipmentUse.ResolutionId && e.CardKind == CardKind.Crossbow && e.CardId == initialEquipmentUse.CardId) == 1 && opening.FallbackDraws().Length == 2,
            "The setup native whole Use returns once after its real failed obtain Draw2; setup failure has its exact turn-zero entitlement.");
        opening.Native(CardKind.Crossbow, []); opening.ReachFeibai();
        opening.Need(opening.G.State.TurnNumber > 0 && opening.Prefix(CardKind.Crossbow) is { PreviousKind: null, NameLength: 4, X: 4 },
            "The first genuine TurnStarted clears the setup predecessor and setup failure disable, so its first true Use can activate Feibai again.");
        opening.ActivateFeibai(); opening.Play(); opening.Need(opening.E<FeibaiDisabledEvent>().Select(e => e.ActualTurn).Distinct().Count() == 2 && opening.FallbackDraws().Length == 4 &&
            opening.G.CardMovements.Count(m => m.CardId == initialHandEquipment && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            opening.V().Equipment.Any(c => c.Id == initialHandEquipment) && opening.E<CardUseFinishedEvent>().Count(e => e.CardId == initialHandEquipment && e.CardKind == CardKind.Crossbow) == 1,
            "Setup and the first real turn each own one separate paid fallback; the initial native card cannot replay its completion."); opening.Cold();
    }

    public static void FeibaiTrueDodgeNullParentsAndPlayedExclusionCold()
    {
        var dodge = Scene.Start(new("true-dodge") { Hand = [C(CardKind.Dodge)] }); var dodgeId = dodge.V().Hand.Single().Id;
        dodge.IncomingSlash(); dodge.Respond(dodgeId); dodge.ReachSample(); dodge.AssertSample(1, 2); dodge.SelectSample(); dodge.Play();
        var response = dodge.E<CardActionAcceptedEvent>().Single(e => e.Action.ActorSeat == 0 && e.Action.EffectiveKind == CardKind.Dodge).Action;
        var incoming = dodge.E<CardUseDeclaredEvent>().Single(e => e.SourceSeat == 1 && e.CardKind == CardKind.Slash);
        dodge.Need(dodge.Prefix(CardKind.Dodge) is { PreviousKind: null, NameLength: 1, X: 1 } && dodge.PaidOnce(dodgeId) &&
            response is { Type: CardActionType.Response, ProviderSeat: 0 } && response.ParentActionId == dodge.E<FeibaiUsePrefixCapturedEvent>().Single(e => e.ActorSeat == 1 && e.EffectiveKind == CardKind.Slash).ActionId &&
            dodge.E<FeibaiUsePrefixCapturedEvent>().Count(e => e.ActionId == response.ActionId) == 1 &&
            dodge.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == incoming.ResolutionId && e.CardKind == CardKind.Slash) == 1 && dodge.E<FeibaiCompletedEvent>().Count(e => e.ActionId == response.ActionId) == 1,
            "An actual defensive Dodge is one true Use with its own prior chain, and its private Feibai child returns to the original native Slash once."); dodge.Cold();

        var counter = Scene.Start(new("true-null") { Hand = [C(CardKind.Dismantlement), C(CardKind.Nullification)], PeerHand = [C(CardKind.Slash)] });
        var nullId = counter.V().Hand.Single(c => c.Kind == CardKind.Nullification).Id; var trickId = counter.V().Hand.Single(c => c.Kind == CardKind.Dismantlement).Id;
        counter.Native(CardKind.Dismantlement, [1]); counter.Reach(() => counter.P is { Kind: DecisionKind.Nullification }); counter.Respond(nullId);
        counter.ReachFeibai(); counter.ActivateFeibai(); counter.Play();
        var nullAction = counter.E<CardActionAcceptedEvent>().Single(e => e.Action.ActorSeat == 0 && e.Action.EffectiveKind == CardKind.Nullification).Action;
        var originalTrick = counter.E<CardUseDeclaredEvent>().Single(e => e.CardId == trickId && e.CardKind == CardKind.Dismantlement);
        counter.Need(counter.Prefix(CardKind.Nullification) is { PreviousKind: CardKind.Dismantlement, NameLength: 4, PreviousNameLength: 4, X: 8 } &&
            counter.PaidOnce(nullId) && counter.PaidOnce(trickId) && counter.V(1).HandCount == 1 &&
            nullAction is { Type: CardActionType.Response, ProviderSeat: 0 } && nullAction.ParentActionId == counter.Prefix(CardKind.Dismantlement).ActionId &&
            counter.E<FeibaiUsePrefixCapturedEvent>().Count(e => e.ActionId == nullAction.ActionId) == 1 &&
            counter.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == originalTrick.ResolutionId && e.CardKind == CardKind.Dismantlement) == 1 && counter.E<FeibaiFallbackDrawReturnedEvent>().Single().ActualCount == 2 &&
            counter.E<FeibaiCompletedEvent>().Count(e => e.ActionId == nullAction.ActionId) == 1,
            "The genuine counterspell reads the earlier declared parent name, performs its empty-eight-rune-pool fallback once, and returns to the nullified physical trick without taking the target card."); counter.Cold();

        var played = Scene.Start(new("pure-played") { Hand = [C(CardKind.Slash), C(CardKind.Crossbow)] }); var playedId = played.V().Hand.Single(c => c.Kind == CardKind.Slash).Id;
        played.Use("duel", [1]); played.Reach(() => played.P is { Kind: DecisionKind.RespondSlash }); played.Respond(playedId); played.Play();
        played.Need(played.PaidOnce(playedId) && played.E<FeibaiStartedEvent>().Length == 0 && !played.E<FeibaiUsePrefixCapturedEvent>().Any(e => e.ActorSeat == 0),
            "A real pure Played Slash during Duel pays normally without Feibai or a previous-Use ledger entry.");
        played.Native(CardKind.Crossbow, []); played.ReachFeibai();
        played.Need(played.Prefix(CardKind.Crossbow) is { PreviousKind: null, NameLength: 4, X: 4 }, "A later actual Equipment Use still has no previous own Use after the pure Played response.");
        played.SkipFeibai(); played.Play(); played.Cold();
    }

    public static void FeibaiActualFallbackShortDrawMarksDisableAndSourceLossCold()
    {
        var normal = Scene.Start(new("fallback-two") { Hand = [C(CardKind.SilverLion), C(CardKind.Crossbow), C(CardKind.BaguaFormation)] });
        normal.Native(CardKind.SilverLion, []); normal.ReachFeibai(); normal.ActivateFeibai(); normal.Play();
        var actual = normal.FallbackDraws(); normal.AssertFallback(2);
        normal.Need(actual.Select(m => m.CardId).All(id => normal.V().Hand.Any(c => c.Id == id && Xian(c))), "The real fallback marks exactly its actual Draw2 entities before any following gain child.");
        normal.Native(CardKind.Crossbow, []); normal.Play();
        normal.Need(normal.E<FeibaiStartedEvent>().Length == 1 && normal.E<FeibaiDisabledEvent>().Length == 1 && normal.FallbackDraws().Length == 2,
            "After one failed obtain, a second true Use in the same actual turn cannot issue another Feibai or Draw2.");
        var disabledTurn = normal.G.State.TurnNumber; normal.End(); normal.Reach(() => normal.G.State.TurnNumber > disabledTurn && normal.P is { Kind: DecisionKind.PlayCard });
        normal.Native(CardKind.BaguaFormation, []); normal.ReachFeibai(); normal.ActivateFeibai(); normal.Play();
        normal.Need(normal.E<FeibaiDisabledEvent>().Length == 2 && normal.E<FeibaiDisabledEvent>().Select(e => e.ActualTurn).Distinct().Count() == 2 && normal.FallbackDraws().Length == 4,
            "A true new actual turn renews Feibai; it does not inherit the last turn's failure disable."); normal.Cold();

        foreach (var count in new[] { 0, 1 })
        {
            var shortDeck = Scene.Start(new("short-" + count) { Hand = [C(CardKind.Crossbow)] });
            shortDeck.Use(count == 0 ? "drain-zero" : "drain-one"); shortDeck.Play();
            shortDeck.Need(shortDeck.G.CreateSnapshot(0).DrawPileCount == count && shortDeck.V().Hand.Count(c => !Xian(c)) == 48 - count,
                "Three legal real Draw effects of at most twenty each leave exactly zero or one stock entity; later draws are not opening Xian.");
            shortDeck.Native(CardKind.Crossbow, []); shortDeck.ReachFeibai(); shortDeck.ActivateFeibai(); shortDeck.Play(); shortDeck.AssertFallback(count);
            shortDeck.Need(shortDeck.V().Hand.Count(Xian) == count && shortDeck.E<XianEntityMarkedEvent>().Count(e => e.Origin == XianMarkOrigin.FeibaiFallbackDraw) == count,
                "A native requested Draw2 shortfall creates only its real zero/one Xian material, without a nominal two-card mark or fake reshuffle material."); shortDeck.Cold();
        }

        var loss = Scene.Start(new("fallback-source-loss") { Hand = [C(CardKind.SilverLion), C(CardKind.Crossbow)], ConditionalSources = true, LoseSourceAtDraw = true });
        loss.Native(CardKind.SilverLion, []); loss.ReachFeibai(); loss.ActivateFeibai(); loss.Reach(() => loss.Hold());
        loss.Need(loss.Has(Feibai) && loss.FallbackDraws().Length == 2 && loss.FallbackDraws().All(m => loss.V().Hand.Any(c => c.Id == m.CardId && Xian(c))),
            "The paid fallback's actual entities are already Xian at their real CardsGained child, before that child changes HP and source."); loss.Private(); loss.Cold(); loss.Continue(); loss.Play();
        loss.Need(loss.V().Hp == 1 && !loss.Has(Feibai) && !loss.Has(Jiaowei) && loss.E<FeibaiCompletedEvent>().Single().Fallback && loss.FallbackDraws().Length == 2,
            "The original paid Draw returns once after actual source loss without paying or marking its entities twice.");
        loss.Use("recover-two"); loss.Play(); loss.Native(CardKind.Crossbow, []); loss.Play();
        loss.Need(loss.Has(Feibai) && loss.E<JiaoweiInitializedEvent>().Length == 1 && loss.E<FeibaiStartedEvent>().Length == 1 && loss.E<FeibaiDisabledEvent>().Length == 1 && loss.FallbackDraws().Length == 2,
            "Regrant does not reset same-turn disabled state or opening marks, and cannot restart the paid fallback."); loss.Cold();
    }

    private sealed record Spec(string Name)
    {
        public ShanheStartingCard[] Hand { get; init; } = [];
        public ShanheStartingCard[] PeerHand { get; init; } = [];
        public CardKind[] Stock { get; init; } = Enumerable.Repeat(CardKind.Slash, 48).ToArray();
        public bool ConditionalSources { get; init; }
        public bool FireConversion { get; init; }
        public bool LoseSourceAtDraw { get; init; }
        public bool OpeningEquipment { get; init; }
    }

    private sealed class Fixture(Spec spec) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-le-cai-yong", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.le-cai-yong." + suffix + ".json") ?? throw new InvalidOperationException("Missing complete real Le Cai Yong bundle."); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var production = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
            foreach (var (id, program) in production.Programs) b.AddSkill(new(id, production.Presentations[id].Name, production.Presentations[id].Description) { Program = program, ProgramPresentation = production.Presentations[id] });
            foreach (var kind in spec.Stock.Concat(spec.Hand.Select(c => c.Kind)).Concat(spec.PeerHand.Select(c => c.Kind)).Distinct())
            { var card = CardCatalog.Get(kind); b.AddCard(new("fixture:lcy-card-" + kind, card.DisplayName, card.CategoryName, card.Description, kind)); }
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static JsonObject A(string id, string effects, int targets = 0, int cards = 0, int? maximum = null) => new()
            { ["id"] = id, ["minCards"] = cards, ["maxCards"] = maximum ?? cards, ["minTargets"] = targets, ["maxTargets"] = targets, ["targetKind"] = "anyLiving", ["usesPerTurn"] = null, ["effects"] = N("[" + effects + "]") };
            static string Draw(int n) => "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":" + n + "}";
            const string capture = "{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"paid\"}";
            var acts = new JsonArray(A("draw-four", Draw(4)), A("drain-zero", Draw(20) + "," + Draw(20) + "," + Draw(8)), A("drain-one", Draw(20) + "," + Draw(20) + "," + Draw(7)),
                A("give", capture + ",{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"paid\",\"destination\":\"selectedTargetHand\"}", 1, 1, 4),
                A("discard", capture + ",{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"paid\",\"destination\":\"discardPile\",\"awaitMovementTriggers\":true}", cards: 1, maximum: 4),
                A("armor-self", "{\"op\":\"grantArmor\",\"target\":\"owner\",\"amount\":2}"),
                A("hp-loss-one", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}"),
                A("self-damage-one", "{\"op\":\"damage\",\"target\":\"owner\",\"amount\":1}"), A("self-damage-two", "{\"op\":\"damage\",\"target\":\"owner\",\"amount\":2}"),
                A("recover-two", "{\"op\":\"recover\",\"target\":\"owner\",\"amount\":2}"), A("incoming", "{\"op\":\"offerVirtualSlashOrDraw\",\"target\":\"selectedTarget\"}", 1));
            var duel = A("duel", "{\"op\":\"useSelectedActorDuel\",\"target\":\"owner\"}", 1); duel["usesPerPhase"] = 2; duel["targetKind"] = "otherLiving"; acts.Add(duel);
            var defs = new JsonArray(new JsonObject { ["id"] = Driver, ["revision"] = 1, ["activations"] = acts },
                N("{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"));
            if (spec.FireConversion) defs[0]!["viewAs"] = N("[{\"id\":\"real-fire-slash\",\"inputKinds\":[\"slash\"],\"inputSuits\":[],\"sourceZones\":[\"hand\"],\"outputKind\":\"fireSlash\",\"forPlay\":true,\"forResponse\":false}]");
            if (spec.OpeningEquipment) defs[0]!["triggers"] = N("[{\"id\":\"real-opening-native-equipment\",\"window\":\"gameStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"targetRef\":{\"kind\":\"owner\"},\"resultBind\":\"actual-equipment\"}]}]");
            if (spec.ConditionalSources) defs.Add(N("{\"id\":\"" + Conditional + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"healthy-jiaowei\",\"skillId\":\"" + Jiaowei + "\",\"minimumHp\":2},{\"id\":\"healthy-feibai\",\"skillId\":\"" + Feibai + "\",\"minimumHp\":2}]}"));
            if (spec.LoseSourceAtDraw) defs.Add(N("{\"id\":\"" + Watch + "\",\"revision\":1,\"triggers\":[{\"id\":\"paid-fallback-draw\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementReasons\":[\"" + DrawReason + "\"],\"movementOccurrence\":\"perBatch\",\"priority\":1000,\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"held\",\"options\":[{\"id\":\"continue\"}]},{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":2}]}]}"));
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "乐蔡邕固定起始牌真实行为夹具",
                optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(binding => binding!["effects"]!.AsArray())
                    .SelectMany(effect => effect!["options"]?.AsArray() ?? new JsonArray()).Select(option => option!["id"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).ToDictionary(id => id, id => id) });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, labels[id].name, labels[id].description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var own = new List<string> { Driver }; if (spec.ConditionalSources) own.Add(Conditional); else own.AddRange([Jiaowei, Feibai]); if (spec.LoseSourceAtDraw) own.Add(Watch);
            b.AddGeneral(new(Owner, "乐蔡邕真实本人", "", "standard:none", "qun", 3, own));
            foreach (var peer in new[] { Ally, EnemyA, EnemyB }) b.AddGeneral(new(peer, "实际其他角色", "", "standard:none", "qun", 12, [Quiet]));
            b.AddDeck(new(Deck, "四十八张固定实体", 0, 0, []) { PhysicalCards = spec.Stock.Select(kind => new ContentDeckPhysicalCard("fixture:lcy-card-" + kind, Suit.Spade, 1)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "乐蔡邕真实小局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }

    private sealed class Scene(Spec spec, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool Has(string skill) => V().Skills?.Any(s => s.ContentId == skill) == true;
        public FeibaiUsePrefixCapturedEvent Prefix(CardKind kind) => E<FeibaiUsePrefixCapturedEvent>().Last(e => e.ActorSeat == 0 && e.EffectiveKind == kind);
        public CardMovementRecord[] FallbackDraws() => G.CardMovements.Where(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == DrawReason).ToArray();
        public bool IsAction(PromptChoice c, string action) => c.Parameters.GetValueOrDefault("program-action") == action;
        public bool IsSample() => P?.Choices.Any(c => IsAction(c, "le-cai-yong-feibai-sample")) == true;
        public bool Offer() => P?.SkillPrompt?.SkillId == Feibai && P.Choices.Any(c => IsAction(c, "activate"));
        public bool Hold() => P?.SkillPrompt?.SkillId == Watch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public bool PaidOnce(int id) => G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1;
        public void Use(string id, int[]? targets = null, int[]? cards = null) => Accept(new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void Native(CardKind kind, int[] targets)
        { var action = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId is { } id && V().Hand.Any(c => c.Id == id && c.Kind == kind) && a.ConversionSource is null && a.Kind != LegalActionKind.Recast && a.TargetSeats.SequenceEqual(targets)); Need(action is not null, "The real published native " + kind + " plan must exist."); Native(action!); }
        public void Native(LegalAction action) => Accept(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
        public void ReachFeibai() => Reach(Offer);
        public void ActivateFeibai() => Answer(c => IsAction(c, "activate"));
        public void SkipFeibai() => Answer(c => IsAction(c, "skip"));
        public void ReachSample() { Reach(() => Offer() || IsSample()); if (!IsSample()) ActivateFeibai(); Reach(IsSample); }
        public void AssertSample(int nameLength, int count)
        {
            var choices = P!.Choices.Where(c => IsAction(c, "le-cai-yong-feibai-sample")).ToArray(); var cards = G.CreateSnapshot(0).PrivateRevealedCards ?? [];
            Need(choices.Length == count && choices.All(c => c.Cards.Count == 1 && c.Targets.Count == 0) && choices.Select(c => c.Cards.Single()).Distinct().Count() == count &&
                cards.Count == count && cards.Select(c => c.Id).ToHashSet().SetEquals(choices.Select(c => c.Cards.Single())) &&
                cards.All(c => CardCatalog.Get(c.Kind).DisplayName.EnumerateRunes().Count() == nameLength && G.CreateCardZoneDiagnostics().Single(z => z.CardId == c.Id).Location == CardLocation.DrawPile),
                "The private sample consists of exactly the actual eligible deck entities and their effective native name lengths.");
            Private(); Immutable(cards); foreach (var choice in choices) Immutable(choice.Cards); RejectPrompt(); Cold();
        }
        public void SelectSample()
        {
            var old = P!; var choice = old.Choices.First(c => IsAction(c, "le-cai-yong-feibai-sample")); var id = choice.Cards.Single();
            Answer(c => c.Id == choice.Id); Play(); Need(G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == ObtainReason) == 1,
                "The canonical selected sample moves the original deck entity into Hand once, not through a fabricated Draw."); RejectOld(old);
        }
        public void AssertFallback(int count)
        {
            Need(E<FeibaiFallbackDrawReturnedEvent>().Single() is { ActualCount: var actual, ReplacementCount: 0 } && actual == count && FallbackDraws().Length == count &&
                E<FeibaiDisabledEvent>().Single().ActualTurn == G.State.TurnNumber && E<XianEntityMarkedEvent>().Count(e => e.Origin == XianMarkOrigin.FeibaiFallbackDraw) == count &&
                E<FeibaiCompletedEvent>().Single() is { Fallback: true, ObtainedCardId: null } && E<FeibaiSampleEntityEvent>().Length == 0,
                "The failed actual obtain issues one true Draw2 outcome, marks only actual materials and disables this owner for the current actual turn.");
        }
        public void IncomingSlash() { Use("incoming", [1]); Reach(() => P?.Choices.Any(c => IsAction(c, "virtual-slash-offer") && c.Targets.SequenceEqual([0])) == true); Answer(c => IsAction(c, "virtual-slash-offer") && c.Targets.SequenceEqual([0])); Reach(() => P is { Kind: DecisionKind.RespondDodge }); }
        public void Respond(int id) { Private(); RejectPrompt(); Cold(); Answer(c => c.Cards.SequenceEqual([id]) && c.Parameters.GetValueOrDefault("response") is not ("pass" or "take-damage" or "let-die")); }
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(p.PlayerSeat == 0 && c is not null, "The owner chooses one actually published canonical option."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var step = 0; step < 200; step++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "The bounded fixture ended before boundary " + line); var p = P;
                if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (Hold()) Continue();
                else if (p.Choices.Any(c => IsAction(c, "skip"))) Answer(c => IsAction(c, "skip"));
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else if (p.Kind == DecisionKind.SelectTargetCard) Answer(c => c.Parameters.ContainsKey("slot-index") || c.Cards.Count == 1);
                else throw new InvalidOperationException("Unexpected boundary " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded boundary " + line + " " + Diagnostic());
        }
        public void Private() => Need(P is { PlayerSeat: 0, IsPrivate: true } && Enumerable.Range(1, 3).All(v => G.CreateSnapshot(v).PendingDecision is null) &&
            Enumerable.Range(0, 4).All(v => G.CreateSnapshot(v).Players.Where(p => p.Seat != v).All(p => p.Hand.Count == 0)), "Only the true chooser sees private canonical choices and every foreign Hand remains opaque.");
        public void Immutable<T>(IReadOnlyList<T> list) { if (list is IList mutable && mutable.Count > 0) { var before = JsonSerializer.Serialize(list); var rejected = false; try { mutable[0] = mutable[0]; } catch (NotSupportedException) { rejected = true; } Need(rejected && JsonSerializer.Serialize(list) == before, "Prepared nested card collections reject observer mutation."); } }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Cold replay preserves exact owning private samples, actual prefix and marks, shield/failure payment, native child returns and commands."); }
        public void Reject(GameCommand command) { var before = State(); Need(!G.Submit(command).Accepted && State() == before, "A stale, forged or foreign command cannot alter marks, private choices, random outcomes, paid cards or history."); }
        public void RejectPrompt() { var p = P!; Reject(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision)); Reject(new AnswerPromptCommand(0, p.PromptId, new("forged-le-cai-yong"), G.Revision)); }
        public void RejectOld(PendingDecision p) => Reject(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, G.Revision));
        public void Accept(GameCommand command) { try { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, "Rejected " + command.GetType().Name + ": " + result.Error?.Code + "/" + result.Error?.Message); } catch (Exception e) { throw new InvalidOperationException(spec.Name + ": " + e.Message + " " + Diagnostic(), e); } }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { spec.Name, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner, Prompt = P,
            Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).MaxHp, V(i).Armor, V(i).IsAlive, V(i).HandCount }),
            Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind, f.Step, Native = f is CardUseFrame u ? new { u.SourceSeat, u.CardId, u.CardKind, u.PhysicalCardIds, u.TargetSeats } : null }),
            Prefix = E<FeibaiUsePrefixCapturedEvent>().TakeLast(5), Protection = E<JiaoweiProtectionArmedEvent>().TakeLast(3), Prevention = E<JiaoweiDamagePreventedEvent>(), Draws = E<FeibaiFallbackDrawReturnedEvent>(),
            Accepted = E<CardActionAcceptedEvent>().TakeLast(3), Declared = E<CardUseDeclaredEvent>().TakeLast(3), Finished = E<CardUseFinishedEvent>().TakeLast(3), FeibaiCompleted = E<FeibaiCompletedEvent>().TakeLast(3),
            Bindings = E<ProgramBindingResolvedEvent>().Where(e => e.SkillId is Jiaowei or Feibai).TakeLast(5) });
        public static Scene Start(Spec spec)
        {
            ContentRegistry registry; try { registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(spec)); } catch (Exception e) { throw new InvalidOperationException(spec.Name + " fixture: " + e.Message, e); }
            var setup = new ShanheBattleSetup(83, 1, "lcy-" + spec.Name, Owner, 3, 0, 0, 0, 3, [], spec.Hand, [],
                [new(EnemyA) { StartingHand = [] }, new(EnemyB) { StartingHand = [] }], [new(Ally) { StartingHand = spec.PeerHand }], new string('f', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var scene = new Scene(spec, game, registry); scene.Accept(new StartGameCommand()); if (spec.OpeningEquipment) scene.ReachFeibai(); else scene.Play(); return scene;
        }
    }
}
