using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class MobileShenCaoCaoChecks
{
    private const string Hero = "mobile:shen-cao-cao", Guixin = "mobile:guixin";
    private const string Driver = "fixture:mobile-caocao-driver", Watch = "fixture:mobile-caocao-watch", Quiet = "fixture:mobile-caocao-quiet";
    private const string Mode = "identity:classic-mobile-caocao-fixture", Reason = "skill-program.mobile:guixin.obtain";

    public static void GuixinSelectsAreasPerDamagePointAndColdReturnsOnce()
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, Hero, g.Revision, P(g)!.PromptId)); Play(g);
        Use(g, "draw"); Play(g);
        var equipment = V(g).Hand.First(c => c.Kind == CardKind.Crossbow).Id;
        Use(g, "equip-other", [1]);
        Reach(g, p => p.Choices.Any(c => c.Cards.SequenceEqual([equipment])));
        Answer(g, c => c.Cards.SequenceEqual([equipment])); Play(g);
        var indulgence = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Indulgence && a.TargetSeats.SequenceEqual([2]));
        Accept(g, new PlayCardCommand(0, indulgence.CardId!.Value, indulgence.TargetSeats, g.Revision, P(g)!.PromptId, indulgence.PlayedCardKind, indulgence.TargetCardId));
        Play(g);
        Need(Zone(g, equipment) == CardLocation.Equipment(1) && Zone(g, indulgence.CardId.Value) == CardLocation.Judgment(2),
            "The fixture creates a real chosen public weapon and a real delayed trick through native commands.");
        var hand = V(g).HandCount; var thirdHand = V(g, 3).HandCount;
        Use(g, "damage"); Offer(g); Activate(g);
        Claim(g, 1, c => c.Cards.SequenceEqual([equipment])); Paid(g);
        var first = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Guixin);
        var firstId = first.Id;
        Need(first.MobileGuixin is { Stage: MobileGuixinStage.MovementChildren, Cursor: 0, Payments.Count: 1 } paid &&
             paid.Payments[0].CardId == equipment && paid.OtherSeats.SequenceEqual([1, 2, 3]) && first.PendingMovementContinuation is not null &&
             Zone(g, equipment) == CardLocation.Hand(0) && Zone(g, indulgence.CardId.Value) == CardLocation.Judgment(2) &&
             V(g).HandCount == hand + 1 && V(g, 3).HandCount == thirdHand && !V(g).IsFaceDown,
            "The first actual damage point obtains the selected public equipment, then pauses its real gain child before visiting the next character or flipping.");
        g = Cold(g, r); Continue(g);
        Claim(g, 2, c => c.Cards.SequenceEqual([indulgence.CardId.Value])); Paid(g); Continue(g);
        Claim(g, 3, c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand) && c.Cards.Count == 0);
        Paid(g); Continue(g); Offer(g);
        Need(V(g).IsFaceDown && V(g).HandCount == hand + 3 && V(g, 3).HandCount == thirdHand - 1 &&
             E<MobileGuixinCompletedEvent>(g).Single(e => e.FrameId == firstId) is { PaidCharacters: 3, TurnoverIssued: true, Cancelled: false } &&
             E<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == firstId && e.Activated && e.Completed) == 1,
            "The chosen Judgment card and opaque foreign hand card move once each; the first sweep flips only after all three gain children return.");
        Activate(g);
        foreach (var seat in new[] { 1, 2, 3 })
        {
            Claim(g, seat, c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand) && c.Cards.Count == 0);
            Paid(g); Continue(g);
        }
        Play(g);
        Need(!V(g).IsFaceDown && V(g).HandCount == hand + 6 &&
             E<MobileGuixinStartedEvent>(g).Select(e => e.OccurrenceIndex).SequenceEqual([0, 1]) &&
             E<MobileGuixinTurnoverIssuedEvent>(g).Select(e => (e.Before, e.After)).SequenceEqual([(false, true), (true, false)]) &&
             E<MobileGuixinCompletedEvent>(g).Length == 2 && g.CardMovements.Count(m => m.Reason.Value == Reason) == 6 &&
             g.ResolutionStack.All(f => f is not ProgramSkillFrame { MobileGuixin: not null }),
            "One real two-point damage gives two independent accepted Guixin sweeps, six actual acquisitions and exactly two opposite turnovers after cold replay.");
    }
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static CardLocation Zone(GameEngine g, int card) => g.CreateCardZoneDiagnostics().Single(c => c.CardId == card).Location;
    private static string? Action(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private static void Need(bool condition, string why) { if (!condition) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected real mobile Guixin command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    {
        var p = P(g)!; var choice = p.Choices.FirstOrDefault(predicate);
        Need(choice is not null, "Missing published mobile Guixin choice: " + JsonSerializer.Serialize(p));
        Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, g.Revision));
    }
    private static void Use(GameEngine g, string id, int[]? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Offer(GameEngine g) => Reach(g, p => p.SkillPrompt?.SkillId == Guixin && p.Choices.Any(c => Action(c) == "activate"));
    private static void Activate(GameEngine g) => Answer(g, c => Action(c) == "activate");
    private static void Paid(GameEngine g) => Reach(g, p => p.SkillPrompt?.SkillId == Watch);
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Claim(GameEngine g, int target, Func<PromptChoice, bool> predicate)
    {
        Reach(g, p => p.SkillPrompt?.SkillId == Guixin && p.TargetSeat == target && p.Choices.Any(c => Action(c) == "mobile-guixin"));
        Answer(g, predicate);
    }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> ready)
    {
        for (var n = 0; n < 200; n++)
        {
            if (P(g) is { } p)
            {
                if (ready(p)) return;
                if (p.Kind == DecisionKind.SelectFaction) { Answer(g, _ => true); continue; }
                if (p.Kind == DecisionKind.Nullification) { Answer(g, c => c.Cards.Count == 0); continue; }
                throw new InvalidOperationException("Unexpected mobile Guixin boundary: " + JsonSerializer.Serialize(p));
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Mobile Guixin missed its bounded native boundary.");
    }
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
        Need(State(g) == State(restored), "The same command prefix cold-restores all four views, original damage point, private paid card invoice, queued child and physical zones.");
        return restored;
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(),
        Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Zones = g.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(g.AcceptedCommands)
    });
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mobile-caocao", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(new OwnerBuilder(b), Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"draw","targetKind":"anyLiving","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                {"id":"damage","targetKind":"anyLiving","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":2}]},
                {"id":"equip-other","targetKind":"otherLiving","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment","awaitMovementTriggers":true}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{Reason}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var labels = new Dictionary<string, object>
            {
                [Driver] = new { name = "真实归心驱动", description = "真实摸牌、伤害与装备转交。" },
                [Watch] = new { name = "真实获得子窗", description = "暂停每次真实归心牌移动。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Quiet] = new { name = "安静角色", description = "跳过他人的出牌阶段。" }
            };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "Native mobile Guixin fixture") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            for (var n = 1; n < 4; n++) b.AddGeneral(new($"fixture:mobile-caocao-peer-{n}", "真实目标", "supporter", Quiet, "wei", 8));
            var kinds = new[] { "standard:crossbow", "standard:indulgence", "standard:dodge", "standard:slash" };
            b.AddDeck(new("fixture:mobile-caocao-deck", "固定实体牌堆", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard(kinds[i % kinds.Length], (Suit)(i % 4), i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "移动神曹操实结算", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:mobile-caocao-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [Hero, "fixture:mobile-caocao-peer-1", "fixture:mobile-caocao-peer-2", "fixture:mobile-caocao-peer-3"]));
        }
    }
    private sealed class OwnerBuilder(IContentRegistryBuilder b) : IContentRegistryBuilder
    {
        public void AddGeneral(ContentGeneralDefinition d) => b.AddGeneral(d.Id == Hero ? d with { AdditionalSkillIds = [..d.AdditionalSkillIds!, Driver, Watch] } : d);
        public void AddSkill(ContentSkillDefinition d) => b.AddSkill(d);
        public void AddCard(ContentCardDefinition d) => b.AddCard(d);
        public void AddDeck(ContentDeckRecipe d) => b.AddDeck(d);
        public void AddMode(ContentModeDefinition d) => b.AddMode(d);
    }
}
