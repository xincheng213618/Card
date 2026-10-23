using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
namespace CardGame.Core.Tests;

internal static class OlShenCaoCaoChecks
{
    private const string Hero = "ol:shen-cao-cao", Guixin = "ol:guixin";
    private const string Driver = "fixture:ol-caocao-driver", Watch = "fixture:ol-caocao-watch", Quiet = "fixture:ol-caocao-quiet";
    private const string Mode = "identity:classic-ol-caocao-fixture", Reason = "skill-program.ol:guixin.obtain";

    public static void PriorityRegionsFallbackRandomAndDamagePointCold()
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, Hero, g.Revision, P(g)!.PromptId)); Play(g);
        Use(g, "draw"); Play(g);
        var weapon = V(g).Hand.First(c => c.Kind == CardKind.Crossbow).Id;
        var horse = V(g).Hand.First(c => c.Kind == CardKind.DefensiveHorse).Id;
        foreach (var equipment in new[] { weapon, horse })
        {
            Use(g, "equip-other", [1]); Reach(g, p => p.Choices.Any(c => c.Cards.SequenceEqual([equipment])));
            Answer(g, c => c.Cards.SequenceEqual([equipment])); Play(g);
        }
        var indulgence = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Indulgence && a.TargetSeats.SequenceEqual([2]));
        Accept(g, new PlayCardCommand(0, indulgence.CardId!.Value, indulgence.TargetSeats, g.Revision, P(g)!.PromptId, indulgence.PlayedCardKind, indulgence.TargetCardId)); Play(g);
        Need(Zone(g, weapon) == CardLocation.Equipment(1) && Zone(g, horse) == CardLocation.Equipment(1) &&
            Zone(g, indulgence.CardId.Value) == CardLocation.Judgment(2), "The fixture creates two real distinct equipment slots and one real delayed trick before damage.");
        var hand = V(g).HandCount;
        Use(g, "damage"); Offer(g); Activate(g); Priority(g, CardZoneKind.Equipment); Paid(g);
        var first = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.OlGuixin is not null);
        var paid = first.OlGuixin!; var firstId = first.Id;
        Need(paid is { Stage: OlGuixinStage.MovementChildren, Cursor: 0, PreferredZone: CardZoneKind.Equipment, Payments.Count: 1 } &&
            paid.OtherSeats.SequenceEqual([1, 2, 3]) && paid.Payments[0].Movement.From == CardLocation.Equipment(1) &&
            paid.Payments[0].Candidates.Count(c => c.From == CardLocation.Equipment(1)) == 2 &&
            new[] { weapon, horse }.Contains(paid.Payments[0].Movement.CardId) && !V(g).IsFaceDown && V(g).HandCount == hand + 1 &&
            Zone(g, indulgence.CardId.Value) == CardLocation.Judgment(2),
            "One unified equipment preference chooses randomly within the two equipped entities, then pauses its paid gain before the next seat and turnover.");
        g = Cold(g, r); Continue(g);
        Paid(g); Continue(g); Paid(g); Continue(g); Offer(g);
        Need(V(g).IsFaceDown && V(g).HandCount == hand + 3 &&
            E<OlGuixinMovementPaidEvent>(g).Where(e => e.FrameId == firstId).Select(e => e.ChosenZone)
                .SequenceEqual([CardZoneKind.Equipment, CardZoneKind.Hand, CardZoneKind.Hand]) &&
            Zone(g, indulgence.CardId.Value) == CardLocation.Judgment(2),
            "The same equipment preference falls back to hand before judgment for every other seat without another area/card menu, and flips only at sweep completion.");
        Activate(g); Priority(g, CardZoneKind.Judgment);
        for (var i = 0; i < 3; i++) { Paid(g); Continue(g); }
        Play(g);
        Need(!V(g).IsFaceDown && V(g).HandCount == hand + 6 &&
            E<OlGuixinPriorityChosenEvent>(g).Select(e => e.PreferredZone).SequenceEqual([CardZoneKind.Equipment, CardZoneKind.Judgment]) &&
            E<OlGuixinStartedEvent>(g).Select(e => e.OccurrenceIndex).SequenceEqual([0, 1]) &&
            E<OlGuixinMovementPaidEvent>(g).Skip(3).Select(e => e.ChosenZone).SequenceEqual([CardZoneKind.Hand, CardZoneKind.Judgment, CardZoneKind.Hand]) &&
            Zone(g, indulgence.CardId.Value) == CardLocation.Hand(0) &&
            E<OlGuixinTurnoverIssuedEvent>(g).Select(e => (e.Before, e.After)).SequenceEqual([(false, true), (true, false)]) &&
            E<OlGuixinCompletedEvent>(g) is [{ PaidCharacters: 3, TurnoverIssued: true, Cancelled: false }, { PaidCharacters: 3, TurnoverIssued: true, Cancelled: false }],
            "Two actual damage points issue two independent unified preferences; judgment priority falls back to hand before remaining equipment, and all six gains and both turnovers settle once.");
        Need(g.CardMovements.Count(m => m.Reason.Value == Reason) == 6 && g.ResolutionStack.All(f => f is not ProgramSkillFrame { OlGuixin: not null }),
            "Cold return does not repeat a random draw or an already-paid native acquisition.");
    }
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static CardLocation Zone(GameEngine g, int card) => g.CreateCardZoneDiagnostics().Single(c => c.CardId == card).Location;
    private static string? Action(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private static void Need(bool condition, string why) { if (!condition) throw new InvalidOperationException(why); }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected real OL priority Guixin command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    {
        var p = P(g)!; var choice = p.Choices.FirstOrDefault(predicate);
        Need(choice is not null, "Missing published OL priority Guixin choice: " + JsonSerializer.Serialize(p));
        Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, g.Revision));
    }
    private static void Use(GameEngine g, string id, int[]? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Offer(GameEngine g) => Reach(g, p => p.SkillPrompt?.SkillId == Guixin && p.Choices.Any(c => Action(c) == "activate"));
    private static void Activate(GameEngine g) => Answer(g, c => Action(c) == "activate");
    private static void Paid(GameEngine g) => Reach(g, p => p.SkillPrompt?.SkillId == Watch);
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Priority(GameEngine g, CardZoneKind zone)
    {
        Reach(g, p => p.SkillPrompt?.SkillId == Guixin && p.Choices.Any(c => Action(c) == "ol-guixin-priority"));
        Need(P(g)!.Choices.Count == 3 && P(g)!.Choices.All(c => c.Cards.Count == 0 && c.Targets.Count == 0),
            "OL Guixin publishes exactly one three-region menu for the whole damage-point sweep.");
        Answer(g, c => c.Parameters.GetValueOrDefault("priority-zone") == zone.ToString());
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
                throw new InvalidOperationException("Unexpected OL priority Guixin boundary: " + JsonSerializer.Serialize(p));
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("OL priority Guixin missed its bounded native boundary.");
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
        public PackageManifest Manifest { get; } = new("fixture-ol-caocao", new(1, 0, 0), []);
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
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "Native OL priority-region Guixin fixture") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            for (var n = 1; n < 4; n++) b.AddGeneral(new($"fixture:ol-caocao-peer-{n}", "真实目标", "supporter", Quiet, "wei", 8));
            var kinds = new[] { "standard:crossbow", "standard:indulgence", "standard:defensive_horse", "standard:slash" };
            b.AddDeck(new("fixture:ol-caocao-deck", "固定实体牌堆", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard(kinds[i % kinds.Length], (Suit)(i % 4), i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "OL优先区域归心实结算", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:ol-caocao-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [Hero, "fixture:ol-caocao-peer-1", "fixture:ol-caocao-peer-2", "fixture:ol-caocao-peer-3"]));
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
