using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BianFuRenChecks
{
    private const string Mode = "identity:bian-fu-ren-fixture", Driver = "fixture:bfr-driver";
    private const string Quiet = "fixture:bfr-quiet", Red = "fixture:bfr-red";
    private const string Wanwei = "ol:wanwei", Yuejian = "ol:yuejian";

    public static void NativeForeignLossFreezesActorAndUsesOneActualTurnQuota()
    {
        foreach (var kind in new[] { CardKind.Snatch, CardKind.Dismantlement })
        {
            var (g, r) = Start();
            var original = V(g, 1).Hand.Select(card => card.Id).ToArray();
            Use(g, kind, 1);
            Reach(g, p => p.Kind == DecisionKind.SelectTargetCard && p.PlayerSeat == 0);
            Require(P(g)!.Choices.All(choice => choice.Cards.Count == 0),
                "A native foreign hand choice publishes opaque slots rather than private card identities.");
            Answer(g, _ => true);
            Reach(g, p => Activation(p, Wanwei));
            var parent = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Last();
            var invoice = parent.Batch.BianFuRenOwnedLosses!.Single(loss => loss.OwnerSeat == 1);
            Require(invoice.ActorSeat == 0 && invoice.OwnerSeat == 1 && original.Contains(invoice.CardId) &&
                invoice.Discarded == (kind == CardKind.Dismantlement) &&
                parent.Batch.Movements.Any(move => move.Sequence == invoice.CompletionMovementSequence && move.CardId == invoice.CardId),
                "Snatch and Dismantlement freeze their actual foreign actor, original owned entity and completed movement, including native Processing payment.");
            g = Cold(g, r); Activate(g); Play(g);
            Require(E<WanweiAcquisitionIssuedEvent>(g).Single() is { OwnerSeat: 1, OtherActorSeat: 0, MatchedName: true, ActualCount: 1 } &&
                g.CardMovements.Count(move => move.To == CardLocation.Hand(1) && move.Reason.Value == "skill-program.wanwei.named-acquisition") == 1,
                "Accepted Wanwei acquires exactly one actual matching draw-pile entity rather than redrawing or returning its lost card.");
            Use(g, kind, 1); Reach(g, p => p.Kind == DecisionKind.SelectTargetCard && p.PlayerSeat == 0); Answer(g, _ => true); Play(g);
            Require(E<WanweiAcquisitionIssuedEvent>(g).Count() == 1 && Started(g, Wanwei) == 1,
                "A second native foreign loss in the same actual turn cannot consume a second Wanwei use.");
            _ = Cold(g, r);
        }
    }

    public static void ForeignSkillSelfDiscardDoesNotQualifyAndMissingNameDraws()
    {
        var (g, r) = Start();
        DriverUse(g, "self-discard", 1);
        Reach(g, p => p.SkillPrompt?.SkillId == Driver && p.PlayerSeat == 1);
        Answer(g, choice => choice.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card");
        Play(g);
        Require(Started(g, Wanwei) == 0 && !E<BianFuRenForeignDiscardProducerEvent>(g).Any(),
            "A foreign skill's owner does not become the discarder when the actual published chooser discards their own HE.");
        _ = Cold(g, r);

        (g, r) = Start(smallDeck: true);
        var holder = Enumerable.Range(0, 4).Single(seat => V(g, seat).Hand.Any(card => card.Kind == CardKind.Slash));
        var unique = V(g, holder).Hand.Single(card => card.Kind == CardKind.Slash).Id;
        if (holder == 0)
        {
            DriverUse(g, "give", 1, [unique]); Play(g); holder = 1;
        }
        var slot = V(g, holder).Hand.ToList().FindIndex(card => card.Id == unique);
        Use(g, CardKind.Dismantlement, holder);
        Reach(g, p => p.Kind == DecisionKind.SelectTargetCard && p.PlayerSeat == 0);
        Answer(g, choice => choice.Parameters.GetValueOrDefault("slot-index") == slot.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Reach(g, p => Activation(p, Wanwei)); g = Cold(g, r); Activate(g); Play(g);
        Require(E<WanweiAcquisitionIssuedEvent>(g).Single() is { MatchedName: false, ActualCount: 1 } &&
            g.CardMovements.Count(move => move.To == CardLocation.Hand(holder) && move.Reason.Value == "skill-program.wanwei.fallback-draw") == 1,
            "With no matching draw-pile name, the real exhausted small deck follows normal reshuffle and draws exactly one card.");
        _ = Cold(g, r);
    }

    public static void WholeHandEffectiveSuitsExactMaterialsAndTwoUsesRestore()
    {
        var (g, r) = Start(red: true);
        for (var use = 1; use <= 3; use++)
        {
            var before = V(g, 1).Hand.Select(card => card.Id).ToArray();
            var entity = Use(g, CardKind.IronChain, 1);
            if (use <= 2)
            {
                Reach(g, p => Activation(p, Yuejian));
                var parent = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Last();
                var receipt = parent.Batch.BianFuRenUsedDiscards!.Single();
                var material = receipt.Materials.Single();
                Require(receipt.Action.ActorSeat == 0 && receipt.Action.EffectiveKind == CardKind.IronChain &&
                    receipt.Action.EffectiveDesignatedTargetSeats.SequenceEqual([1]) && material.CardId == entity &&
                    material.PrintedKind == CardKind.Crossbow && Location(g, entity) == CardLocation.DiscardPile,
                    "The accepted converted native use retains its original physical equipment material, actual actor and target, rather than guessing from the discard's current source.");
                AuditNoProvenanceDrift(g, material);
                g = Cold(g, r); Activate(g); Play(g);
                var reveal = E<ProgramCardsRevealedEvent>(g).Last(fact => fact.SkillId == Yuejian);
                Require(reveal.Cards.Select(card => card.Id).Order().SequenceEqual(before.Order()) &&
                    E<YuejianAcquisitionIssuedEvent>(g).Last() is { DifferentSuits: true, ActualCount: 1 } &&
                    Location(g, entity) == CardLocation.Hand(1),
                    "Yuejian reveals the complete original hand and uses its effective Heart suits against the incoming Club before obtaining that exact entity once.");
                g = Cold(g, r);
            }
            else
            {
                Play(g);
                Require(Started(g, Yuejian) == 2 && Location(g, entity) == CardLocation.DiscardPile,
                    "The third actual incoming use in this turn has no remaining Yuejian opportunity.");
            }
        }
        var (same, sr) = Start();
        var sameEntity = Use(same, CardKind.IronChain, 1); Reach(same, p => Activation(p, Yuejian));
        same = Cold(same, sr); Activate(same); Play(same);
        Require(E<YuejianAcquisitionIssuedEvent>(same).Single() is { DifferentSuits: false, ActualCount: 0 } &&
            E<ProgramCardsRevealedEvent>(same).Single(fact => fact.SkillId == Yuejian).Cards.Count == 4 &&
            Location(same, sameEntity) == CardLocation.DiscardPile,
            "A shared effective suit still reveals every hand card and consumes the accepted use, but obtains no incoming material.");
        _ = Cold(same, sr);
    }

    public static void DelayedPlacementKeepsOriginalUseUntilItsExactLaterDiscard()
    {
        var (g, r) = Start(red: true);
        var entity = Use(g, CardKind.Indulgence, 1); Play(g);
        var placement = E<BianFuRenDelayedUsePlacedEvent>(g).Single(fact => fact.CardId == entity);
        Require(Location(g, entity) == CardLocation.Judgment(1) && Started(g, Yuejian) == 0,
            "Placing a real foreign Indulgence freezes its native use invoice without prematurely claiming it from judgment.");
        g = Cold(g, r); Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Reach(g, p => Activation(p, Yuejian));
        var parent = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Last();
        var invoice = parent.Batch.BianFuRenUsedDiscards!.Single();
        Require(invoice.Action.ActionId == placement.ActionId && invoice.NativeUseId == placement.NativeUseId &&
            invoice.Materials.Single().DelayedPlacementMovementSequence == placement.PlacementMovementSequence &&
            g.CardMovements.Any(move => move.CardId == entity && move.Reason == CardMoveReasons.DelayedCardFinish),
            "The later judgment discard uses the original accepted actor/target and uninterrupted exact placement, after the original use frame has ended.");
        g = Cold(g, r); Activate(g);
        Until(g, () => E<YuejianAcquisitionIssuedEvent>(g).Any());
        Require(Location(g, entity) == CardLocation.Hand(1) && E<YuejianAcquisitionIssuedEvent>(g).Single() is { ActualCount: 1 },
            "The native delayed finish can return the same original physical Indulgence material through Yuejian exactly once.");
        _ = Cold(g, r);
    }

    public static void SuccessfulDeclarationAdoptsOneOriginalPhysicalPaymentAndRestores()
    {
        var (g, r) = Start(red: true, declaration: true);
        var action = g.GetHumanLegalActions().First(candidate => candidate.Kind == LegalActionKind.Slash &&
            candidate.PlayedCardKind == CardKind.Slash && candidate.TargetSeats.SequenceEqual([1]) &&
            candidate.ConversionSource?.SkillId == "classic:guhuo");
        var entity = action.CardId!.Value;
        Accept(g, new PlayCardCommand(0, entity, [1], g.Revision, P(g)!.PromptId, CardKind.Slash)
            { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
        Reach(g, prompt => prompt.Choices.Any(choice => choice.Parameters.ContainsKey("declaration-answer")));
        var declaration = g.ResolutionStack.OfType<CardDeclarationFrame>().Single();
        Require(declaration.Payment.BianFuRenPaymentSequence is { } paid && g.CardMovements.Any(move =>
            move.Sequence == paid && move.CardId == entity && move.Reason.Value == "conversion.declaration.pay"),
            "The actual declaration frame freezes its one original hidden payment before any challenge or native use exists.");
        g = Cold(g, r); Answer(g, choice => choice.Parameters.GetValueOrDefault("declaration-answer") == "challenge");
        Reach(g, prompt => Activation(prompt, Yuejian));
        var receipt = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Last().Batch.BianFuRenUsedDiscards!.Single();
        var material = receipt.Materials.Single();
        Require(E<CardDeclarationRevealedEvent>(g).Any(fact => fact.DeclarationId == declaration.Id && fact.Succeeded && fact.Card.Id == entity) &&
            material.PaymentMovementSequence == declaration.Payment.BianFuRenPaymentSequence &&
            material.OriginalPayment is { } original && original.NativeUseId == receipt.NativeUseId &&
            original.ActionId == receipt.Action.ActionId &&
            g.CardMovements.Count(move => move.CardId == entity && move.To == CardLocation.Processing) == 1,
            "The successful true Slash declaration adopts the same physical payment into its actual native use, without a fabricated second move or another action's invoice.");
        AuditNoProvenanceDrift(g, material); g = Cold(g, r); Activate(g); Play(g);
        Require(Location(g, entity) == CardLocation.Hand(1) && E<YuejianAcquisitionIssuedEvent>(g).Single() is { ActualCount: 1 } &&
            g.CardMovements.Count(move => move.CardId == entity && move.To == CardLocation.Processing) == 1,
            "Yuejian claims the one successful declared Slash entity after its genuine native finish and cold recovery never repays it.");
        _ = Cold(g, r);
    }

    private static void AuditNoProvenanceDrift(GameEngine g, BianFuRenUsedMaterial material)
    {
        // Read-only host audit complements the real commands and Replay above.
        // A forged older finish index must not become available just because the
        // same entity happens to occupy the discard pile again.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var available = typeof(GameEngine).GetMethod("BianFuRenMaterialAvailable", flags)!;
        var matches = typeof(GameEngine).GetMethod("BianFuRenOriginalPaymentMatches", flags)!;
        var proof = material.OriginalPayment!;
        Require(available.Invoke(g, [material]) is true &&
            available.Invoke(g, [material with { CompletionMovementSequence = material.PaymentMovementSequence }]) is false &&
            available.Invoke(g, [material with { PaymentMovementSequence = material.PaymentMovementSequence - 1 }]) is false &&
            matches.Invoke(g, [material, proof.NativeUseId + 1, proof.ActionId]) is false &&
            matches.Invoke(g, [material, proof.NativeUseId, proof.ActionId + 1]) is false,
            "Availability and the owning invoice reject old payment/finish ordinals and another use/action, even for the same entity at the same current zone.");
    }

    private static int Use(GameEngine g, CardKind kind, int target)
    {
        var action = g.GetHumanLegalActions().First(a => a.PlayedCardKind == kind && a.TargetSeats.SequenceEqual([target]) &&
            a.ConversionSource?.SkillId == Driver);
        Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId,
            action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource,
            AdditionalConversionSources = action.AdditionalConversionSources });
        return action.CardId.Value;
    }
    private static void DriverUse(GameEngine g, string id, int target, IReadOnlyList<int>? cards = null) =>
        Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], [target], g.Revision, P(g)!.PromptId));
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static int Started(GameEngine g, string skill) => E<ProgramBindingStartedEvent>(g).Count(fact => fact.SkillId == skill);
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static CardLocation Location(GameEngine g, int id) => g.CreateCardZoneDiagnostics().Single(card => card.CardId == id).Location;
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(seat => g.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Activation(PendingDecision p, string skill) => p.SkillPrompt?.SkillId == skill && p.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Activate(GameEngine g) => Answer(g, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Play(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate) => Until(g, () => P(g) is { } p && predicate(p));
    private static void Until(GameEngine g, Func<bool> predicate)
    {
        for (var i = 0; i < 160; i++)
        {
            if (predicate()) return;
            var p = P(g);
            if (p is not null && (Activation(p, Wanwei) || Activation(p, Yuejian)))
                Answer(g, choice => choice.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is not null && p.Choices.Any(choice => choice.Parameters.GetValueOrDefault("response") == "pass"))
                Answer(g, choice => choice.Parameters.GetValueOrDefault("response") == "pass");
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        var pending = P(g);
        var diagnostic = JsonSerializer.Serialize(new { pending?.Kind, pending?.PlayerSeat,
            Skill = pending?.SkillPrompt?.SkillId, ChoiceCount = pending?.Choices.Count,
            LastFacts = g.Events.TakeLast(6).Select(item => item.Payload.GetType().Name).ToArray() });
        throw new InvalidOperationException("Bian Fu Ren did not reach the real boundary: " + diagnostic[..Math.Min(500, diagnostic.Length)]);
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real Bian Fu Ren command."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new {
        Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(g.CreateSnapshot(seat))).ToArray(),
        Frames = g.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())).ToArray(),
        Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics()
    });
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
        Require(State(g) == State(restored), "All private views, frozen physical invoices, typed owning frames and real command prefix cold-restore exactly.");
        return restored;
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Start(bool red = false, bool smallDeck = false, bool declaration = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(red, smallDeck, declaration));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, "fixture:bfr-actor", g.Revision, P(g)!.PromptId)); Play(g); return (g, r);
    }

    private sealed class Fixture(bool red, bool smallDeck, bool declaration) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-bian-fu-ren", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var helper = new PeerBuilder(b, red); CharacterFixtureContent.Register(helper, "ol:bian-fu-ren");
            if (declaration) CharacterFixtureContent.Register(b, "classic:yu-ji");
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"viewAs":[
                {"id":"as-dismantlement","inputKinds":[],"inputSuits":[],"outputKind":"dismantlement","forPlay":true,"forResponse":false},
                {"id":"as-snatch","inputKinds":[],"inputSuits":[],"outputKind":"snatch","forPlay":true,"forResponse":false},
                {"id":"as-chain","inputKinds":[],"inputSuits":[],"outputKind":"ironChain","forPlay":true,"forResponse":false},
                {"id":"as-indulgence","inputKinds":[],"inputSuits":[],"outputKind":"indulgence","forPlay":true,"forResponse":false}],
               "activations":[
                {"id":"self-discard","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"selectedTarget"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"discardPile","awaitMovementTriggers":true}]},
                {"id":"give","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget","amount":1}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Red}}","revision":1,"cardPolicies":[{"id":"effective-heart","kind":"rewriteSuit","inputSuit":"club","outputSuit":"heart"}]}]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, Quiet, Red }.ToDictionary(id => id, id => new { name = id, description = "Small actual Bian Fu Ren behavior fixture." })
            }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "Small actual fixture") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:bfr-lord", "Fixed actor", "Fixed actual Lord") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == Role.Lord ? 10000d : -10000d) });
            b.AddSkill(new("fixture:bfr-peer", "Fixed peer", "Controlled other participants") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == Role.Lord ? -10000d : 10000d) });
            b.AddGeneral(new("fixture:bfr-actor", "Actual foreign card user", "supporter", Driver, "shu", 8,
                new[] { "fixture:bfr-lord" }.Concat(declaration ? new[] { "classic:guhuo" } : []).ToArray()));
            foreach (var id in new[] { "fixture:bfr-peer-2", "fixture:bfr-peer-3" }) b.AddGeneral(helper.Peer! with { Id = id });
            b.AddDeck(new("fixture:bfr-deck", "Uniform actual printed materials", 4, 0, []) {
                PhysicalCards = Enumerable.Range(0, smallDeck ? 17 : 160).Select(index => new ContentDeckPhysicalCard(
                    declaration || smallDeck && index == 0 ? "standard:slash" : "standard:crossbow", Suit.Club, 7)).ToArray()
            });
            b.AddMode(new(Mode, "Actual Bian Fu Ren invoices", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:bfr-deck",
                GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:bfr-actor", "ol:bian-fu-ren", "fixture:bfr-peer-2", "fixture:bfr-peer-3"]));
        }
    }
    private sealed class PeerBuilder(IContentRegistryBuilder b, bool red) : IContentRegistryBuilder
    {
        public ContentGeneralDefinition? Peer { get; private set; }
        public void AddGeneral(ContentGeneralDefinition definition)
        {
            Peer = definition with { AdditionalSkillIds = (definition.AdditionalSkillIds ?? [])
                .Concat(new[] { Quiet, "fixture:bfr-peer" }).Concat(red ? new[] { Red } : []).ToArray() };
            b.AddGeneral(Peer);
        }
        public void AddCard(ContentCardDefinition definition) => b.AddCard(definition);
        public void AddSkill(ContentSkillDefinition definition) => b.AddSkill(definition);
        public void AddDeck(ContentDeckRecipe definition) => b.AddDeck(definition);
        public void AddMode(ContentModeDefinition definition) => b.AddMode(definition);
    }
}
