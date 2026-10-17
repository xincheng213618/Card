using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static partial class TaoQianChecks
{
    public static void ZhaohuoMarkedUsePlayedAndHandLimitExpiryCold()
    {
        var s = RestrictionScenario.Start("hand");
        var marked = s.MarkSelf();
        s.AssertForbiddenPlay(marked);
        s.Use("duel", targets: [1]);
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
        var duel = s.G.ResolutionStack.OfType<CardUseFrame>().Last();
        s.Need(duel.CardKind == CardKind.Duel && duel.SourceSeat == 1 &&
            s.P!.Choices.All(c => !c.Cards.Contains(marked)) &&
            s.P.Choices.Any(c => c.Cards.Count == 1),
            "The actual foreign Duel requests a played Slash, excludes the marked original entity and retains other real responses.");
        var response = s.P!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
        s.Cold(); s.Answer(c => c.Cards.SequenceEqual([response])); s.Play();
        s.Need(s.E<DuelResponseEvent>().Count(e => e.ResolutionId == duel.Id && e.ResponderSeat == 0 && e.UsedSlash && e.SlashCardId == response) == 1 &&
            !s.E<CardRespondedEvent>().Any(e => e.CardId == marked) && s.V().Hand.Any(c => c.Id == marked),
            "An unmarked actual played-card response completes its native Duel while the marked card stays unspent.");
        var beforeDiscard = s.G.CardMovements.Count;
        s.Use("discard", cards: [marked]); s.Play();
        s.Need(s.G.CardMovements.Skip(beforeDiscard).Count(m => m.CardId == marked && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) == 1 &&
            !s.V().Hand.Any(c => c.Id == marked),
            "Cannot Use or Play does not forbid an explicit native discard of that exact marked card.");
        s.Cold();

        var timing = RestrictionScenario.Start("timing");
        marked = timing.MarkSelf(); timing.Use("draw"); timing.Play();
        timing.AssertForbiddenPlay(marked);
        var turn = timing.G.State.TurnNumber;
        var handBefore = timing.V().HandCount;
        var limit = timing.V().HandLimit ?? throw new InvalidOperationException("The actual hand limit must be projected.");
        timing.End(); timing.Reach(() => timing.P is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards });
        timing.Need(timing.P!.RequiredCardCount == handBefore - 1 - limit &&
            !timing.P.ValidCardIds.Contains(marked) && timing.P.RequiredCardCount > 0 &&
            !timing.E<TurnEndedEvent>().Any(e => e.TurnNumber == turn && e.ActorSeat == 0),
            "The still-open actual turn excludes exactly the marked Hand entity when computing and publishing mandatory hand-limit discard.");
        timing.Cold();
        var discard = timing.P!.ValidCardIds.Take(timing.P.RequiredCardCount).ToArray();
        timing.Accept(new DiscardCardsCommand(0, discard, timing.P.PromptId, timing.G.Revision));
        timing.Reach(() => timing.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        var endingUse = timing.G.ResolutionStack.OfType<CardUseFrame>().Last();
        var ending = timing.G.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Single();
        var endingIssuer = timing.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == RestrictionEnding);
        timing.Need(ending.OwnerSeat == 0 && ending.TurnNumber == turn &&
            endingIssuer.WindowContext is { Window: SkillProgramTriggerWindow.TurnEnding, TargetSeat: 0 } endingContext &&
            endingContext.ParentFrameId == ending.Id && endingIssuer.OwnerSeat == endingUse.SourceSeat &&
            ending.Items[ending.ItemIndex].Candidate is { } endingCandidate && endingCandidate.OwnerSeat == endingIssuer.OwnerSeat &&
            endingCandidate.SkillId == RestrictionEnding && endingCandidate.SkillInstanceId == endingIssuer.SkillInstanceId &&
            endingUse.CardKind == CardKind.Slash && endingUse.TargetSeats.SequenceEqual([0]) && endingUse.SourceSeat != 0 &&
            timing.V().Hand.Any(c => c.Id == marked) && timing.P!.Choices.All(c => !c.Cards.Contains(marked)) &&
            timing.P.Choices.Any(c => c.Cards.Count == 1 && c.Parameters.GetValueOrDefault("conversion-skill-id") == RestrictionDriver) &&
            !timing.E<TurnEndedEvent>().Any(e => e.TurnNumber == turn && e.ActorSeat == 0),
            "A genuine foreign fixed Slash at Ending still cannot use the marked card as Dodge; expiry is not the start of Ending.");
        var dodge = timing.P!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
        timing.Cold(); timing.Answer(c => c.Cards.SequenceEqual([dodge])); timing.Play();
        timing.Need(timing.G.State.TurnNumber == turn && timing.E<TurnEndedEvent>().Count(e => e.TurnNumber == turn && e.ActorSeat == 0) == 1 &&
            timing.E<ZhaohuoStartedEvent>().Length == 1 && timing.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.CardId == marked && a.ConversionSource is null),
            "Production Dianjun creates a real post-TurnEnded Play in the same actual-turn number: the old mark expires and its end-tail self damage cannot open another Zhaohuo.");
        timing.Cold();
        var released = timing.G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.CardId == marked && a.ConversionSource is null);
        timing.Accept(new PlayCardCommand(0, marked, released.TargetSeats, timing.G.Revision, timing.P!.PromptId, released.PlayedCardKind, released.TargetCardId));
        timing.Play();
        var releasedUse = timing.E<CardUseDeclaredEvent>().Single(e => e.CardId == marked && e.SourceSeat == 0);
        timing.Need(timing.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == releasedUse.ResolutionId) == 1 &&
            timing.G.CardMovements.Count(m => m.CardId == marked && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1 &&
            timing.E<ZhaohuoStartedEvent>().Length == 1,
            "The released original entity is actually used and retired once through its native parent, rather than merely reappearing in a query.");
        timing.Cold();

        var pile = RestrictionScenario.Start("pile");
        marked = pile.MarkSelf();
        pile.Reach(() => pile.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("domain-action") == "store-card") == true);
        var store = pile.G.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.SkillId == RestrictionPile);
        var pileSlash = new CardConversionSource(RestrictionPile, "pile-slash", 0, store.SkillInstanceId);
        pile.Cold(); pile.Answer(c => c.Parameters.GetValueOrDefault("domain-action") == "store-card" && c.Cards.SequenceEqual([marked]));
        pile.Answer(c => c.Parameters.GetValueOrDefault("domain-action") == "store-finish"); pile.Play();
        pile.Need(!pile.V().Hand.Any(c => c.Id == marked) && pile.G.CardMovements.Count(m => m.CardId == marked && m.From == CardLocation.Hand(0) &&
            m.To is { Zone: CardZoneKind.PublicPersistentPile, OwnerSeat: 0 } && m.Reason.Value == "skill-program.public-pile.store-owned") == 1,
            "A real native preparation choice moves the same marked Hand entity into its own live public pile without forging a zone or a replacement card.");
        pile.Need(pile.G.GetHumanLegalActions().All(a => a.CardId != marked), "The ordinary own-pile Slash conversion query must retain the exact original entity restriction.");
        pile.Reject(new PlayCardCommand(0, marked, [1], pile.G.Revision, pile.P!.PromptId, CardKind.Slash) { ConversionSource = pileSlash });
        pile.Use("duel", targets: [1]); pile.Reach(() => pile.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
        var pileDuel = pile.G.ResolutionStack.OfType<CardUseFrame>().Last();
        pile.Need(pileDuel.CardKind == CardKind.Duel && pile.P!.Choices.All(c => !c.Cards.Contains(marked)) && pile.P.Choices.Any(c => c.Cards.Count == 1),
            "The same marked non-Hand entity is also excluded from a real pure-Played Duel conversion request, with ordinary Hand responses still available.");
        pile.Cold(); pile.Answer(c => c.Cards.Count == 1); pile.Play();
        pile.Need(pile.E<DuelResponseEvent>().Any(e => e.ResolutionId == pileDuel.Id && e.ResponderSeat == 0 && e.UsedSlash) &&
            !pile.E<CardRespondedEvent>().Any(e => e.CardId == marked) &&
            !pile.G.CardMovements.Any(m => m.CardId == marked && m.From.Zone == CardZoneKind.PublicPersistentPile && m.To == CardLocation.Processing),
            "The real Duel returns without spending the original restricted pile entity or leaking it through a forged conversion command.");
        pile.Cold();
    }

    private const string RestrictionDriver = "fixture:tqr-driver", RestrictionQuiet = "fixture:tqr-quiet",
        RestrictionPile = "fixture:tqr-pile", RestrictionEnding = "fixture:tqr-ending", RestrictionWatch = "fixture:tqr-watch",
        RestrictionOwner = "fixture:tqr-owner", RestrictionMode = "identity:classic-tao-qian-restriction";

    private sealed class RestrictionScenario(string scene, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() where T : IGameEvent => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Need(bool valid, string message) { if (!valid) throw new InvalidOperationException(message + " " + Diagnostic()); }
        public void Accept(GameCommand command)
        {
            try { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, "Rejected " + command.GetType().Name + ": " + JsonSerializer.Serialize(r)); }
            catch (Exception e) { throw new InvalidOperationException("TaoQian restriction " + scene + " cause=" + e.Message + " " + Diagnostic(), e); }
        }
        public void Reject(GameCommand command)
        { var before = State(); var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(!result.Accepted && before == State(), "Rejected marked input must not consume a card, quota, native cursor or private prompt."); }
        public void Answer(Func<PromptChoice, bool> match)
        { var c = P?.Choices.FirstOrDefault(match); Need(P is { PlayerSeat: 0 } && c is not null, "Expected one real human-owned published choice."); Accept(new AnswerPromptCommand(0, P!.PromptId, c!.Id, G.Revision)); }
        public void Use(string activation, int[]? cards = null, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, RestrictionDriver, activation, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public int MarkSelf()
        {
            if (scene != "pile") Use("self-hit");
            Reach(() => P?.SkillPrompt?.SkillId == "ol:zhaohuo" && P.PlayerSeat == 0);
            Need(P!.IsPrivate && P.Choices.All(c => c.Cards.Count == 1 && V().Hand.Any(h => h.Id == c.Cards.Single())) &&
                Enumerable.Range(1, 3).All(i => G.CreateSnapshot(i).PendingDecision is null), "Only the genuine damaged owner sees the self-gift Hand selection.");
            var id = P.Choices.First().Cards.Single(); var old = P; var movements = G.CardMovements.Count;
            var gains = E<ProgramBindingStartedEvent>().Count(e => e.SkillId == RestrictionWatch);
            Reject(new AnswerPromptCommand(1, P.PromptId, P.Choices.First().Id, G.Revision)); Cold(); Answer(c => c.Cards.SequenceEqual([id]));
            var self = E<ZhaohuoSelfGiftCommittedEvent>().Single();
            Need(self.CardId == id && self.Source.OwnerSeat == 0 && self.SequenceBefore == self.SequenceAfter &&
                G.CardMovements.Count == movements && E<ProgramBindingStartedEvent>().Count(e => e.SkillId == RestrictionWatch) == gains &&
                !E<ZhaohuoGiftPaidEvent>().Any() && V().Hand.Any(c => c.Id == id),
                "Self-gift selects one original Hand entity and signs its own typed ticket without any fake movement, CardsMoved loss or CardsGained gain.");
            Reject(new AnswerPromptCommand(0, old.PromptId, old.Choices.First().Id, G.Revision));
            Cold(); if (scene != "pile") Play(); return id;
        }
        public void AssertForbiddenPlay(int id)
        {
            Need(G.GetHumanLegalActions().All(a => a.CardId != id), "The original marked Hand entity cannot publish native or converted Use.");
            var ordinary = G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.ConversionSource is null);
            var converted = G.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.PlayedCardKind == CardKind.FireSlash && a.ConversionSource?.SkillId == RestrictionDriver);
            Reject(new PlayCardCommand(0, id, ordinary.TargetSeats, G.Revision, P!.PromptId, ordinary.PlayedCardKind));
            Reject(new PlayCardCommand(0, id, converted.TargetSeats, G.Revision, P!.PromptId, converted.PlayedCardKind) { ConversionSource = converted.ConversionSource });
        }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 180; i++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "Fixture ended before boundary " + line); var p = P;
                if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification)
                    Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("response") is not ("faction-slash-request" or "faction-defense-request"));
                else if (p.Kind == DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die");
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else throw new InvalidOperationException("Unexpected restriction boundary " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing restriction boundary " + line + " " + Diagnostic());
        }
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold replay preserves private choices, original marked entities, actual-turn expiry and native typed parents."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Diagnostic() => JsonSerializer.Serialize(new { Scene = scene, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner, Prompt = P, Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).HandCount, V(i).HandLimit }), Frames = G.ResolutionStack.Select(f => new { f.Id, Type = f.GetType().Name, Skill = (f as ProgramSkillFrame)?.SkillId, Index = (f as ProgramSkillFrame)?.InstructionIndex }), Facts = G.Events.TakeLast(10).Select(e => e.Payload), Moves = G.CardMovements.TakeLast(6) });
        public static RestrictionScenario Start(string scene)
        {
            ContentRegistry r; try { r = ContentRegistry.Build(new StandardContentPackage(), new RestrictionFixture(scene)); }
            catch (Exception e) { throw new InvalidOperationException("TaoQian restriction fixture " + scene + " cause=" + e.Message, e); }
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = RestrictionMode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, r);
            var s = new RestrictionScenario(scene, g, r); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
            s.Accept(new SelectGeneralCommand(0, RestrictionOwner, g.Revision, s.P!.PromptId));
            if (scene == "pile") s.Reach(() => s.P?.SkillPrompt?.SkillId == "ol:zhaohuo"); else s.Play(); return s;
        }
    }

    private sealed class RestrictionFixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-tao-qian-restriction", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            string Read(string part) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-tao-qian." + part + ".json") ?? throw new InvalidOperationException("Missing embedded current Tao Qian skill bundle."); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var actual = SkillProgramCatalog.Load(Read("rules"), Read("presentation")); const string skill = "ol:zhaohuo";
            b.AddSkill(new(skill, actual.Presentations[skill].Name, actual.Presentations[skill].Description) { Program = actual.Programs[skill], ProgramPresentation = actual.Presentations[skill] });
            if (scene == "timing") CharacterFixtureContent.Register(b, "ol:zhang-yi");
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static JsonObject A(string id, string effects, int cards = 0, int targets = 0) => new() { ["id"] = id, ["minCards"] = cards, ["maxCards"] = cards, ["sourceZones"] = new JsonArray("hand"), ["minTargets"] = targets, ["maxTargets"] = targets, ["targetKind"] = "anyLiving", ["usesPerTurn"] = null, ["effects"] = N("[" + effects + "]") };
            var duel = A("duel", """{"op":"useSelectedActorDuel","target":"owner"}""", targets: 1); duel["targetKind"] = "otherLiving"; duel["usesPerPhase"] = 2;
            var driver = new JsonObject { ["id"] = RestrictionDriver, ["revision"] = 1,
                ["activations"] = new JsonArray(A("self-hit", """{"op":"damage","target":"owner","amount":1}"""), A("discard", """{"op":"discardSelected","target":"owner","amount":1}""", cards: 1), A("draw", """{"op":"draw","target":"owner","amount":3}"""), duel),
                ["modifiers"] = N("""[{"id":"range","query":"slashDistanceLimit","operation":"unlimited","value":0,"priority":0},{"id":"quota","query":"slashLimit","operation":"add","value":5,"priority":0}]"""),
                ["viewAs"] = N("""[{"id":"real-fire-slash","inputKinds":["slash"],"inputSuits":[],"sourceZones":["hand"],"outputKind":"fireSlash","forPlay":true,"forResponse":false},{"id":"real-dodge","inputKinds":["slash"],"inputSuits":[],"sourceZones":["hand"],"outputKind":"dodge","forPlay":false,"forResponse":true}]""") };
            if (scene == "pile") driver["triggers"] = N("""[{"id":"before-storage-damage","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"priority":100,"effects":[{"op":"damage","target":"owner","amount":1}]}]""");
            var defs = new JsonArray(driver,
                new JsonObject { ["id"] = RestrictionQuiet, ["revision"] = 1, ["triggers"] = N("""[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]""") },
                new JsonObject { ["id"] = RestrictionWatch, ["revision"] = 1, ["triggers"] = N("""[{"id":"real-loss","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"lost","options":[{"id":"continue"}]}]},{"id":"real-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gained","options":[{"id":"continue"}]}]}]""") });
            if (scene == "timing") defs.Add(new JsonObject { ["id"] = RestrictionEnding, ["revision"] = 1, ["triggers"] = N("""[{"id":"actual-ending-slash","window":"turnEnding","subject":"owner","turnOwnerScope":"otherLiving","optional":false,"effects":[{"op":"useOwnerSlashAgainstTurnOwner","target":"owner","ignoreDistance":true}]}]""") });
            if (scene == "pile") defs.Add(new JsonObject { ["id"] = RestrictionPile, ["revision"] = 1,
                ["triggers"] = N("""[{"id":"native-owned-storage","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"priority":-100,"effects":[{"op":"storeArbitraryOwnedPublicPile","target":"owner"}]}]"""),
                ["viewAs"] = N("""[{"id":"pile-slash","inputKinds":[],"inputSuits":[],"sourceZones":["publicPersistentPile"],"sourcePileSkillId":"fixture:tqr-pile","outputKind":"slash","forPlay":true,"forResponse":true,"allowSameKind":true},{"id":"pile-dodge","inputKinds":[],"inputSuits":[],"sourceZones":["publicPersistentPile"],"sourcePileSkillId":"fixture:tqr-pile","outputKind":"dodge","forPlay":false,"forResponse":true,"allowSameKind":true}]""") });
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "Small genuine native command boundary", optionLabels = n["id"]!.GetValue<string>() == RestrictionWatch ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "Actual native restriction fixture") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            const string pick = "fixture:tqr-pick"; b.AddSkill(new(pick, "Human", "Public selection fixture") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) });
            var ownerSkills = new List<string> { RestrictionDriver, RestrictionWatch, skill }; if (scene == "timing") ownerSkills.Add("ol:dianjun"); if (scene == "pile") ownerSkills.Add(RestrictionPile);
            b.AddGeneral(new(RestrictionOwner, "招祸真实实体限制", "supporter", pick, "qun", 4, ownerSkills, GeneralGender.Male));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:tqr-peer-" + i).ToArray();
            foreach (var i in Enumerable.Range(1, 3))
            { var p = "fixture:tqr-pick-" + i; b.AddSkill(new(p, "Peer", "Public selection fixture") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (4 - i)) }); b.AddGeneral(new(peers[i - 1], "真实其他角色", "supporter", p, "qun", 12, scene == "timing" && i == 1 ? [RestrictionQuiet, RestrictionEnding] : [RestrictionQuiet], GeneralGender.Male)); }
            const string printed = "fixture:tqr-physical-slash"; b.AddCard(new(printed, "原生杀", CardCatalog.Get(CardKind.Slash).CategoryName, "Real entity", CardKind.Slash));
            b.AddDeck(new("fixture:tqr-deck", "Fixed actual Slash entities", 6, 0, []) { PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard(printed, new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond }[i % 4], i % 13 + 1)).ToArray() });
            b.AddMode(new(RestrictionMode, "招祸实际原生边界", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:tqr-deck", GeneralCandidateCount: 4, GeneralPoolIds: [RestrictionOwner, .. peers]));
        }
    }
}
