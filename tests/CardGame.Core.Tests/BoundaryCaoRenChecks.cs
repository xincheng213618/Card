using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

// Current ordinary OL boundary Cao Ren: docs/content/sources/boundary-cao-ren-2026-10-05.json.
internal static class BoundaryCaoRenChecks
{
    private const string Jushou = "boundary:jushou-current";
    private const string Jiewei = "boundary:jiewei-current";
    private const string DriverId = "fixture:cr-driver";
    private static bool armorDeck;

    public static void Definitions()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage());
        var general = registry.Generals["boundary:cao-ren"];
        Require(general.Name == "界曹仁" && general.FactionId == "wei" && general.BaseHp == 4 &&
            general.SkillIds.SequenceEqual([Jushou, Jiewei]) &&
            general.VariantId == "boundary" && general.RulesetId == "sanguosha-ol",
            "The boundary Cao Ren general registers the current OL wei 4HP pair.");
        var jushou = registry.GetSkill(Jushou).Program!;
        var ending = jushou.Triggers.Single();
        Require(ending.Window == SkillProgramTriggerWindow.PlayEnding && ending.Optional &&
            ending.Effects.Select(e => e.Op).SequenceEqual(
                [SkillProgramEffectOp.SetFaceState, SkillProgramEffectOp.Draw, SkillProgramEffectOp.DiscardHandOrUseEquipment]) &&
            ending.Effects[1].Amount == 4,
            "Jushou is an optional play-ending flip that draws four and pays the discard-or-use cost.");
        var jiewei = registry.GetSkill(Jiewei).Program!;
        var viewAs = jiewei.ViewAs.Single();
        Require(viewAs.Id == "equipment-as-nullification" && viewAs.OutputKind == CardKind.Nullification &&
            !viewAs.ForPlay && viewAs.ForResponse && viewAs.SourceZones.SequenceEqual([CardZoneKind.Equipment]),
            "Jiewei converts one equipment-zone card into a response-only Nullification.");
        var flipUp = jiewei.Triggers.Single();
        Require(flipUp.Window == SkillProgramTriggerWindow.CharacterTurnedFaceUp && flipUp.Optional &&
            flipUp.Effects.Select(e => e.Op).SequenceEqual(
                [SkillProgramEffectOp.SelectAndMoveOwnedCard, SkillProgramEffectOp.SelectTarget,
                 SkillProgramEffectOp.MoveFieldEquipment]) &&
            flipUp.Effects[0].Destination == SkillProgramCardDestination.DiscardPile,
            "Jiewei pays one own card on turning face up and relocates one field equipment card.");
        var presentation = registry.GetSkill(Jiewei).ProgramPresentation!;
        Require(presentation.Name == "解围" && presentation.Description.Contains("装备区", StringComparison.Ordinal) &&
            registry.GetSkill(Jushou).ProgramPresentation!.Name == "据守",
            "The presentation carries the current OL Jushou/Jiewei wording.");
    }

    public static void JushouFlipsDrawsAndDiscardsHandCard()
    {
        var (g, r) = Start(armorDeck: false);
        Settle(g);
        var handBefore = HandCount(g, 0);
        var discardBefore = DiscardPileCount(g);
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Activate(g, Jushou);
        Require(g.CreateSnapshot(0).Players[0].IsFaceDown && HandCount(g, 0) == handBefore + 4,
            "An accepted Jushou turns the general face down and draws four before its cost.");
        AnswerStrategicCard(g);
        Require(g.CreateSnapshot(0).Players[0].IsFaceDown &&
            HandCount(g, 0) == handBefore + 3 && DiscardPileCount(g) == discardBefore + 1,
            "The mandatory cost discards exactly one hand card and stays face down.");
        Replay(g, r);
    }

    public static void JushouCostMayUseEquipmentInstead()
    {
        var (g, r) = Start(armorDeck: true);
        Settle(g);
        var handBefore = HandCount(g, 0);
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Activate(g, Jushou);
        var pick = Await(g, p => p.Choices.Any(c =>
            c.Parameters.GetValueOrDefault("program-action") == "strategic-choice" &&
            c.Parameters.GetValueOrDefault("choice")!.StartsWith("card-", StringComparison.Ordinal)));
        var used = pick.Choices.First(c => c.Parameters.GetValueOrDefault("choice")!.StartsWith("card-", StringComparison.Ordinal));
        Accept(g, new AnswerPromptCommand(0, pick.PromptId, used.Id, g.Revision));
        Require(g.CreateSnapshot(0).Players[0].IsFaceDown && HandCount(g, 0) == handBefore + 3 &&
            EquipmentCount(g, 0) == 1 && IsAt(g, used.Cards.Single(), CardLocation.Equipment(0)),
            "Paying with an equipment card uses it through the real equipment path instead of discarding.");
        Replay(g, r);
    }

    public static void JieweiMovesFieldEquipmentAfterTurningFaceUp()
    {
        var (g, r) = Start(armorDeck: true);
        Settle(g);
        Driver(g, "equip", [0]);
        Settle(g);
        Require(EquipmentCount(g, 0) == 1, "The fixture equips one real armor on Cao Ren.");
        Driver(g, "flip-down");
        Settle(g);
        Require(g.CreateSnapshot(0).Players[0].IsFaceDown, "The fixture turns Cao Ren face down.");
        Driver(g, "flip-up");
        var activate = Await(g, p => p.Choices.Any(c =>
            c.Parameters.GetValueOrDefault("skill-id") == Jiewei &&
            c.Parameters.GetValueOrDefault("program-action") == "activate"));
        Accept(g, new AnswerPromptCommand(0, activate.PromptId,
            activate.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "activate").Id, g.Revision));
        AnswerFirst(g, "select-and-move-owned-card");
        AnswerFirst(g, "select-target");
        var pick = Await(g, p => p.Choices.Any(c =>
            c.Parameters.GetValueOrDefault("program-action") == "strategic-choice" &&
            c.Parameters.GetValueOrDefault("choice")!.StartsWith("card-", StringComparison.Ordinal)));
        var move = pick.Choices.First(c => c.Parameters.GetValueOrDefault("choice")!.StartsWith("card-", StringComparison.Ordinal));
        Accept(g, new AnswerPromptCommand(0, pick.PromptId, move.Id, g.Revision));
        Require(!g.CreateSnapshot(0).Players[0].IsFaceDown && EquipmentCount(g, 0) == 0 &&
            EquipmentCount(g, 1) == 1 && IsAt(g, move.Cards.Single(), CardLocation.Equipment(1)),
            "Turning face up pays one card and moves Cao Ren's field equipment into another player's free slot.");
        Replay(g, r);
    }

    private static int HandCount(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat].HandCount;

    private static int DiscardPileCount(GameEngine g) =>
        g.CreateCardZoneDiagnostics().Count(c => c.Location.Zone == CardZoneKind.DiscardPile);

    private static int EquipmentCount(GameEngine g, int seat) => g.CreateCardZoneDiagnostics()
        .Count(c => c.Location.Zone == CardZoneKind.Equipment && c.Location.OwnerSeat == seat);

    private static bool IsAt(GameEngine g, int cardId, CardLocation location) =>
        g.CreateCardZoneDiagnostics().Any(c => c.CardId == cardId && c.Location.Zone == location.Zone &&
            c.Location.OwnerSeat == location.OwnerSeat);

    private static PendingDecision Await(GameEngine g, Func<PendingDecision, bool> goal)
    {
        for (var i = 0; i < 160; i++)
        {
            if (P(g) is { } p && goal(p)) return p;
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The Cao Ren fixture never reached its target prompt.");
    }

    private static void Activate(GameEngine g, string skillId)
    {
        var p = Await(g, pending => pending.Choices.Any(c =>
            c.Parameters.GetValueOrDefault("skill-id") == skillId &&
            c.Parameters.GetValueOrDefault("program-action") == "activate"));
        Accept(g, new AnswerPromptCommand(0, p.PromptId,
            p.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "activate").Id, g.Revision));
    }

    private static void AnswerStrategicCard(GameEngine g)
    {
        var p = Await(g, pending => pending.Choices.Any(c =>
            c.Parameters.GetValueOrDefault("program-action") == "strategic-choice"));
        Accept(g, new AnswerPromptCommand(0, p.PromptId, p.Choices.First(c =>
            c.Parameters.GetValueOrDefault("program-action") == "strategic-choice" &&
            c.Parameters.GetValueOrDefault("choice")!.StartsWith("card-", StringComparison.Ordinal)).Id, g.Revision));
    }

    private static void AnswerFirst(GameEngine g, string programAction)
    {
        var p = Await(g, pending => pending.Choices.Any(c =>
            c.Parameters.GetValueOrDefault("program-action") == programAction));
        Accept(g, new AnswerPromptCommand(0, p.PromptId,
            p.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == programAction).Id, g.Revision));
    }

    private static void Driver(GameEngine g, string id, int[]? targets = null)
    {
        if (P(g) is null) Settle(g);
        var p = P(g) ?? throw new InvalidOperationException("No pending decision for the fixture driver.");
        Accept(g, new UseProgramSkillCommand(0, DriverId, id, [], targets ?? [], g.Revision, p.PromptId));
    }

    private static SkillProgram Load(string id, string members) => SkillProgramCatalog.Load(
        "{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"" + id +
        "\",\"revision\":1," + members + "}]}",
        "{\"schemaVersion\":3,\"skills\":{\"" + id + "\":{\"name\":\"fixture\",\"description\":\"fixture\"}}}").Programs[id];

    private static string Activation(string id, string effects, int targets = 0) =>
        "{\"id\":\"" + id + "\",\"usesPerTurn\":null,\"minCards\":0,\"maxCards\":0,\"minTargets\":" + targets +
        ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"effects\":" + effects + "}";

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-cr", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            try
            {
                typeof(StandardContentPackage).Assembly.GetType("CardGame.Content.Standard.BoundaryCaoRenContent")!
                    .GetMethod("Register", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(null, [b]);
            }
            catch (Exception diag)
            {
                Console.WriteLine("DIAG-INNER: " + (diag.InnerException?.ToString() ?? diag.ToString()));
                throw;
            }
            b.AddSkill(new("fixture:cr-driver", "fixture", "fixture")
            {
                Program = Load("fixture:cr-driver", "\"activations\":[" +
                    Activation("equip", "[{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"resultBind\":\"equipped\"}]", 1) + "," +
                    Activation("flip-down", "[{\"op\":\"setFaceState\",\"target\":\"owner\",\"faceDown\":true}]") + "," +
                    Activation("flip-up", "[{\"op\":\"setFaceState\",\"target\":\"owner\",\"faceDown\":false}]") + "]")
            });
            b.AddGeneral(new("fixture:cr", "界曹仁", "boundary_cao_ren", Jushou, "wei", 4,
                new[] { Jiewei, DriverId }));
            for (var i = 1; i < 4; i++)
                b.AddGeneral(new($"fixture:cr-{i}", "其他" + i, "supporter", "standard:none", "wei", 5, null));
            b.AddDeck(new("fixture:cr-deck", "固定", 4, 1, [])
            {
                PhysicalCards = Enumerable.Range(0, 64).Select(i =>
                    new ContentDeckPhysicalCard(armorDeck ? "standard:bagua" : "standard:slash", Suit.Club, 7)).ToArray()
            });
            b.AddMode(new("identity:cr", "固定", 4, 4,
                new Dictionary<string, int> { { nameof(Role.Lord), 1 }, { nameof(Role.Loyalist), 1 }, { nameof(Role.Rebel), 2 } },
                "fixture:cr-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:cr", "fixture:cr-1", "fixture:cr-2", "fixture:cr-3"]));
        }
    }

    private static (GameEngine, ContentRegistry) Start(bool armorDeck)
    {
        BoundaryCaoRenChecks.armorDeck = armorDeck;
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 37, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = "identity:cr",
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, r);
        Accept(g, new StartGameCommand());
        Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, "fixture:cr", g.Revision, P(g)!.PromptId));
        Settle(g);
        return (g, r);
    }
}
