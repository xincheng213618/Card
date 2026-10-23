using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static class AnniversaryHuZhaoChecks
{
    private const string Hero = "anniversary:hu-zhao", Midu = "anniversary:midu", Driver = "fixture:hu-zhao-driver", Watch = "fixture:hu-zhao-watch", Quiet = "fixture:hu-zhao-quiet";
    private const string Mode = "identity:classic-hu-zhao-fixture";
    public static void NativeAreaCostsJudgmentRestorationHuomoAndDistanceCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, Hero, g.Revision, P(g)!.PromptId)); Play(g);
        var startingMaximum = V(g).MaxHp;
        DriverUse(g, "stock"); Play(g); DriverUse(g, "lose"); Play(g);
        var weapon = Native(g, CardKind.Crossbow, []); Play(g);
        var armor = Native(g, CardKind.SilverLion, []); Play(g);
        var delayed = Native(g, CardKind.Lightning, [0]); Play(g);
        var before = V(g).HandCount;
        Need(V(g).Hp == startingMaximum - 1 && Zone(g, weapon) == CardLocation.Equipment(0) && Zone(g, armor) == CardLocation.Equipment(0) &&
            Zone(g, delayed) == CardLocation.Judgment(0) && g.GetCombatDistance(2, 0) == 2 && g.GetCombatDistance(0, 2) == 2,
            "The real initial field contains two equipment slots and a delayed trick, with one native lost HP before Silver Lion is removed.");
        MiduUse(g); Menu(g, "abolish");
        g = Cold(g, registry); Answer(g, c => Branch(c) == "abolish" && Value(c) == "35"); Menu(g, "recipient");
        Answer(g, c => c.Targets.SequenceEqual([0])); Reach(g, p => p.SkillPrompt?.SkillId == Watch);
        var paid = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.HuZhaoMidu is not null); var id = paid.Id;
        Need(paid.HuZhaoMidu is { Paid: true, Stage: HuZhaoMiduStage.CostChildren, SelectedMask: 35, Recipient: 0, Draw: null } &&
            new[] { weapon, armor, delayed }.All(card => Zone(g, card) == CardLocation.DiscardPile) &&
            V(g).IsJudgmentAreaAbolished && V(g).EquipmentSlotCapacities![EquipmentSlot.Weapon] == 0 && V(g).EquipmentSlotCapacities![EquipmentSlot.Armor] == 0 &&
            V(g).Hp == startingMaximum && V(g).HandCount == before,
            "The original frame pays all three real areas once, removes their entities and resolves the native Silver Lion recovery before drawing.");
        g = Cold(g, registry); Continue(g); Play(g);
        Need(V(g).HandCount == before + 3 && E<HuZhaoMiduPaidEvent>(g).Count(e => e.FrameId == id) == 1 &&
            E<HuZhaoMiduCompletedEvent>(g).Single(e => e.FrameId == id).DrawCount == 3 &&
            g.GetCombatDistance(2, 0) == 3 && g.GetCombatDistance(0, 2) == 1 && !g.GetHumanLegalActions().Any(a => a.SkillId == Midu),
            "Paid cold return draws exactly three once; only the two equipment slots count towards the one-point directional distance bonus, and the shared Play quota is spent.");

        NextPlay(g); MiduUse(g); Menu(g, "restore"); Answer(g, c => Branch(c) == "restore" && Value(c) == "32"); Play(g);
        var restored = E<HuZhaoMiduCompletedEvent>(g).Last();
        Need(!V(g).IsJudgmentAreaAbolished && V(g).Skills!.Any(s => s.Id == "classic:huomo") &&
            E<ProgramTurnSkillsGrantedEvent>(g).Single(e => e.FrameId == restored.FrameId).GrantedSkillIds.SequenceEqual(["classic:huomo"]) &&
            g.GetCombatDistance(2, 0) == 3 && g.GetCombatDistance(0, 2) == 1,
            "Restoring the judgment area really enables it and grants the existing Huomo without changing the equipment-only distance count.");
        var conversion = g.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == "classic:huomo" && a.PlayedCardKind == CardKind.Slash && a.TargetSeats.SequenceEqual([1]));
        var material = conversion.CardId!.Value; var peerHp = V(g, 1).Hp;
        PlayAction(g, conversion); Play(g);
        Need(Zone(g, material) == CardLocation.DrawPile && V(g, 1).Hp == peerHp - 1 &&
            g.GetHumanLegalActions().All(a => a.ConversionSource?.SkillId != "classic:huomo" || a.PlayedCardKind != CardKind.Slash),
            "Granted Huomo pays its real black nonbasic entity onto the deck and performs the native Slash with the existing per-name restriction.");

        NextPlay(g);
        Need(V(g).Skills!.All(s => s.Id != "classic:huomo") && E<ProgramNextActualTurnStartSkillGrantExpiredEvent>(g).Count(e => e.RecipientSeat == 0 && e.Grant.SkillId == "classic:huomo") == 1,
            "Only the independent borrowed Huomo grant expires at the recipient's next real turn start.");
        before = V(g).HandCount; MiduUse(g); Menu(g, "abolish"); Answer(g, c => Branch(c) == "abolish" && Value(c) == "28"); Menu(g, "recipient"); Answer(g, c => c.Targets.SequenceEqual([0])); Play(g);
        Need(V(g).HandCount == before + 3 && !V(g).IsJudgmentAreaAbolished && V(g).EquipmentSlotCapacities!.Values.Count(cap => cap == 0) == 5 &&
            g.GetCombatDistance(2, 0) == 4 && g.GetCombatDistance(0, 2) == 1,
            "The remaining three real equipment slots can all be abolished together; at least three disabled equipment slots raises both distance adjustments to two.");
        NextPlay(g); before = V(g).HandCount; var movements = g.CardMovements.Count;
        MiduUse(g); Menu(g, "abolish"); Answer(g, c => Branch(c) == "abolish" && Value(c) == "0"); Menu(g, "recipient"); Answer(g, c => c.Targets.SequenceEqual([0])); Play(g);
        Need(V(g).HandCount == before && g.CardMovements.Count == movements && E<HuZhaoMiduCompletedEvent>(g).Last().DrawCount == 0,
            "Zero selected areas is a genuine no-effect activation with no invented draw or physical movement.");
        var replay = GameReplay.Replay(g.CreateCheckpoint().Options, g.AcceptedCommands, registry);
        Need(State(g) == State(replay), "The aggregate actual area payments, native children, grants and actions replay identically after the representative cold resumes.");
    }
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static CardLocation Zone(GameEngine g, int id) => g.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
    private static string? Branch(PromptChoice c) => c.Parameters.GetValueOrDefault("mode");
    private static string? Value(PromptChoice c) => c.Parameters.GetValueOrDefault("value");
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected real Hu Zhao command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    {
        var p = P(g)!; var choice = p.Choices.FirstOrDefault(predicate);
        Need(choice is not null, "Missing exact Hu Zhao menu choice: " + JsonSerializer.Serialize(p));
        Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, g.Revision));
    }
    private static void MiduUse(GameEngine g) => Accept(g, new UseProgramSkillCommand(0, Midu, "abolish-or-restore", [], [], g.Revision, P(g)!.PromptId));
    private static void DriverUse(GameEngine g, string activation) => Accept(g, new UseProgramSkillCommand(0, Driver, activation, [], [], g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void NextPlay(GameEngine g) { Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Play(g); }
    private static void Menu(GameEngine g, string branch) => Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "anniversary-hu-zhao" && Branch(c) == branch));
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void PlayAction(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId)
        { ConversionSource = a.ConversionSource });
    private static int Native(GameEngine g, CardKind kind, int[] targets)
    {
        var ids = V(g).Hand.Where(c => c.Kind == kind).Select(c => c.Id).ToHashSet();
        var action = g.GetHumanLegalActions().First(a => a.CardId is { } id && ids.Contains(id) && a.ConversionSource is null && a.TargetSeats.SequenceEqual(targets));
        PlayAction(g, action); return action.CardId!.Value;
    }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> ready)
    {
        for (var i = 0; i < 240; i++)
        {
            if (P(g) is { } p)
            {
                if (ready(p)) return;
                if (p.Kind == DecisionKind.SelectFaction) { Answer(g, _ => true); continue; }
                if (p.SkillPrompt?.SkillId == Watch) { Continue(g); continue; }
                if (p.Kind is DecisionKind.Nullification or DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.RescueDying) { Answer(g, c => c.Cards.Count == 0); continue; }
                throw new InvalidOperationException("Unexpected Hu Zhao boundary: " + JsonSerializer.Serialize(p));
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Hu Zhao did not reach the expected real boundary.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Zones = g.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(restored), "Hu Zhao's exact paid receipt and native child cold-restore identically."); return restored; }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-hu-zhao", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(new OwnerBuilder(b), Hero);
            CharacterFixtureContent.Register(b, "classic:zhong-yao");
            b.AddCard(new("fixture:hu-zhao-silver-lion", "白银狮子", "装备牌", CardCatalog.Get(CardKind.SilverLion).Description, CardKind.SilverLion));
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"stock","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-area","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementOccurrence":"perBatch",
                "movementReasons":["equipment.slot-abolished","skill-program.anniversary:midu.abolish-judgment"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var labels = new Dictionary<string, object> { [Driver] = new { name = "原生驱动", description = "真实摸牌与失去体力。" }, [Quiet] = new { name = "安静同桌", description = "跳过出牌。" },
                [Watch] = new { name = "原生废栏付款子窗", description = "真实移动后的继续。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } };
            var c = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in c.Programs) b.AddSkill(new(id, id, "Native Hu Zhao fixture") { Program = p, ProgramPresentation = c.Presentations[id] });
            for (var n = 1; n < 4; n++) b.AddGeneral(new($"fixture:hu-zhao-peer-{n}", "真实安静同桌", "supporter", Quiet, "wei", 8));
            var kinds = new[] { "standard:crossbow", "fixture:hu-zhao-silver-lion", "standard:lightning", "standard:dismantlement" };
            b.AddDeck(new("fixture:hu-zhao-deck", "真实非基本牌组", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard(kinds[i % 4], Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "胡昭原生区域主流", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:hu-zhao-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [Hero, "fixture:hu-zhao-peer-1", "fixture:hu-zhao-peer-2", "fixture:hu-zhao-peer-3"]));
        }
    }
    private sealed class OwnerBuilder(IContentRegistryBuilder b) : IContentRegistryBuilder
    {
        public void AddGeneral(ContentGeneralDefinition d) => b.AddGeneral(d.Id == Hero ? d with { AdditionalSkillIds = [.. d.AdditionalSkillIds!, Driver, Watch] } : d);
        public void AddSkill(ContentSkillDefinition d) => b.AddSkill(d); public void AddCard(ContentCardDefinition d) => b.AddCard(d);
        public void AddDeck(ContentDeckRecipe d) => b.AddDeck(d); public void AddMode(ContentModeDefinition d) => b.AddMode(d);
    }
}
