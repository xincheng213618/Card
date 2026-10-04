using CardGame.Core;
using CardGame.Content.Standard;
using static BoundaryLiDianChecks;

// Current ordinary OL boundary Zhou Tai: docs/content/sources/boundary-zhou-tai-2026-10-05.json.
internal static class BoundaryZhouTaiChecks
{
    private const string Buqu = "boundary:buqu-current";
    private const string Fenji = "boundary:fenji-current";

    public static void Definitions()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage());
        var general = registry.Generals["boundary:zhou-tai"];
        Require(general.Name == "界周泰" && general.FactionId == "wu" && general.BaseHp == 4 &&
            general.SkillIds.SequenceEqual([Buqu, Fenji]) &&
            general.VariantId == "boundary" && general.RulesetId == "sanguosha-ol",
            "The boundary Zhou Tai general registers the current OL wu 4HP pair.");
        var buqu = registry.GetSkill(Buqu).Program!;
        var rescue = buqu.Triggers.Single();
        Require(rescue.Window == SkillProgramTriggerWindow.SelfDyingResponse && !rescue.Optional &&
            rescue.Effects.Single().Op == SkillProgramEffectOp.RevealUniqueRankForDying &&
            rescue.Effects.Single().DestinationZone == CardZoneKind.BuquWound,
            "Buqu keeps the mandatory unique-rank dying rescue into the public wound pile.");
        var fenji = registry.GetSkill(Fenji).Program!;
        var discardTriggers = fenji.Triggers.Where(t => t.Window == SkillProgramTriggerWindow.DiscardPileReceived).ToArray();
        var gainTrigger = fenji.Triggers.Single(t => t.Window == SkillProgramTriggerWindow.CardsGained);
        var otherDiscard = discardTriggers.Single(t => t.DiscardOwnerScope == SkillProgramDiscardOwnerScope.Other);
        var ownDiscard = discardTriggers.Single(t => t.DiscardOwnerScope == SkillProgramDiscardOwnerScope.Own);
        Require(discardTriggers.Length == 2 &&
            otherDiscard.Optional && otherDiscard.MovementDiscardOnly &&
            otherDiscard.SourceZones.SequenceEqual([CardZoneKind.Hand]) &&
            otherDiscard.Effects.Select(e => e.Op).SequenceEqual(
                [SkillProgramEffectOp.SelectTarget, SkillProgramEffectOp.LoseHp, SkillProgramEffectOp.Draw]) &&
            ownDiscard.Optional && ownDiscard.MovementDiscardOnly &&
            ownDiscard.SourceZones.SequenceEqual([CardZoneKind.Hand]) &&
            ownDiscard.Effects.Select(e => e.Op).SequenceEqual([SkillProgramEffectOp.LoseHp, SkillProgramEffectOp.Draw]) &&
            ownDiscard.Effects[1].Amount == 2,
            "The discard branch covers every character's hand discard with one owner draw each. other=" +
            string.Join(",", otherDiscard.Effects.Select(e => e.Op)) + "/" + otherDiscard.MovementDiscardOnly + "/" +
            string.Join(",", otherDiscard.SourceZones) + " own=" +
            string.Join(",", ownDiscard.Effects.Select(e => e.Op)) + "/" + ownDiscard.MovementDiscardOnly + "/" +
            string.Join(",", ownDiscard.SourceZones));
        Require(gainTrigger.MovementOccurrence == SkillProgramMovementOccurrence.PerThirdPartyHandGain &&
            gainTrigger.Optional && gainTrigger.DestinationZones.SequenceEqual([CardZoneKind.Hand]) &&
            gainTrigger.Effects.Select(e => e.Op).SequenceEqual(
                [SkillProgramEffectOp.SelectTarget, SkillProgramEffectOp.LoseHp, SkillProgramEffectOp.Draw]),
            "The gain branch observes third-party hand transfers and pays one HP for two owner draws.");
        var presentation = registry.GetSkill(Fenji).ProgramPresentation!;
        Require(presentation.Name == "奋激" && presentation.Description.Contains("失去1点体力", StringComparison.Ordinal),
            "The presentation carries the current OL Fenji wording.");
    }

    public static void BuquRescueWoundHandLimitAndDuplicateRank()
    {
        var (g, r) = Start();
        Driver(g, "hurt-self");
        Driver(g, "hurt-self");
        Settle(g);
        Require(WoundCount(g) == 1 && g.CreateSnapshot(0).Players[0].Hp == 1,
            "The first dying reveals a unique-rank wound and rescues Zhou Tai to one HP.");
        Driver(g, "heal");
        Settle(g);
        var handCount = HandCount(g, 0);
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Reach(g, p => p.Kind == DecisionKind.DiscardCards && p.PlayerSeat == 0);
        Require(P(g)!.RequiredCardCount == handCount - 1,
            "One wound caps the hand limit at one even while HP sits at two.");
        Replay(g, r);
        var (g2, r2) = Start();
        Driver(g2, "hurt-self");
        Driver(g2, "hurt-self");
        Settle(g2);
        Require(WoundCount(g2) == 1 && g2.CreateSnapshot(0).Players[0].Hp == 1,
            "The scenario reset rescues once into a single spade-five wound.");
        Driver(g2, "hurt-self");
        for (var i = 0; i < 24 && g2.CreateSnapshot(0).Players[0].IsAlive; i++)
        {
            var pending = P(g2);
            if (pending is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 })
                Accept(g2, new AnswerPromptCommand(0, pending.PromptId,
                    pending.Choices.First(c => c.Parameters.GetValueOrDefault("response") == "let-die").Id, g2.Revision));
            else if (!g2.Submit(new AdvanceOneStepCommand(g2.Revision)).Accepted) break;
        }
        Require(WoundCount(g2) == 0 && g2.CreateSnapshot(0).Players[0].Hp == 0 &&
            !g2.CreateSnapshot(0).Players[0].IsAlive,
            "A repeated wound rank is removed, so the declined second dying stays fatal.");
    }

    public static void FenjiAnswersOtherHandDiscardsOnly()
    {
        var (g, r) = Start();
        var targetHand = HandCount(g, 1);
        Driver(g, "strip", [1]);
        RunUntilPlay(g, activateFenji: true);
        Require(g.CreateSnapshot(0).Players[0].Hp == 4 && HandCount(g, 1) == targetHand - 1 + 2,
            "An accepted Fenji costs Zhou Tai one HP and refills the discarder by two.");
        Replay(g, r);
        var (g2, r2) = Start();
        Driver(g2, "equip", [1]);
        RunUntilPlay(g2);
        Driver(g2, "strip-equipment", [1]);
        RunUntilPlay(g2);
        Require(g2.CreateSnapshot(0).Players[0].Hp == 5,
            "An equipment discard never offers the hand-only Fenji.");
        Replay(g2, r2);
    }

    public static void FenjiCoversOwnDiscardsAndThirdPartyGains()
    {
        var (g, r) = Start();
        var hand = HandCount(g, 0);
        Driver(g, "discard");
        RunUntilPlay(g, activateFenji: true);
        Require(g.CreateSnapshot(0).Players[0].Hp == 4 && HandCount(g, 0) == hand - 1 + 2,
            "An accepted own-discard Fenji draws Zhou Tai two cards.");
        Replay(g, r);
        var (g2, r2) = Start();
        var targetHand2 = HandCount(g2, 1);
        Driver(g2, "snatch", [1]);
        RunUntilPlay(g2, activateFenji: true);
        Require(g2.CreateSnapshot(0).Players[0].Hp == 4 && HandCount(g2, 1) == targetHand2 - 1 + 2,
            "An accepted gain Fenji costs one HP and refills the original owner by two.");
        Replay(g2, r2);
    }

    private static int HandCount(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat].HandCount;

    private static int WoundCount(GameEngine g) =>
        g.CreateCardZoneDiagnostics().Count(c => c.Location.Zone == CardZoneKind.BuquWound);

    // Drives the fixture back to the human play prompt, answering Fenji offers,
    // single-target confirmations and owned-card selections along the way.
    private static void RunUntilPlay(GameEngine g, bool activateFenji = false)
    {
        for (var i = 0; i < 200; i++)
        {
            var p = P(g);
            if (p is null) { Accept(g, new AdvanceOneStepCommand(g.Revision)); continue; }
            if (p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0) return;
            var choices = p.Choices;
            if (p.SkillPrompt?.SkillId == Fenji && choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"))
            {
                var pick = activateFenji ? "activate" : "skip";
                Accept(g, new AnswerPromptCommand(0, p.PromptId,
                    choices.First(c => c.Parameters.GetValueOrDefault("program-action") == pick).Id, g.Revision));
                continue;
            }
            if (choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"))
            {
                Accept(g, new AnswerPromptCommand(0, p.PromptId,
                    choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "select-target").Id, g.Revision));
                continue;
            }
            if (choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card"))
            {
                Accept(g, new AnswerPromptCommand(0, p.PromptId,
                    choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card").Id,
                    g.Revision));
                continue;
            }
            if (p.PlayerSeat == 0 && choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip"))
            {
                Accept(g, new AnswerPromptCommand(0, p.PromptId,
                    choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "skip").Id, g.Revision));
                continue;
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixture never returned to the play prompt: " +
            System.Text.Json.JsonSerializer.Serialize(P(g)));
    }

    internal static (GameEngine, ContentRegistry) Start()
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 37, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = "identity:zt",
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, r);
        Accept(g, new StartGameCommand());
        Reach(g, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, "fixture:zt", g.Revision, P(g)!.PromptId));
        Settle(g);
        return (g, r);
    }

    private static void Accept(GameEngine g, GameCommand command)
    {
        var result = g.Submit(command);
        Require(result.Accepted, result.Error?.Message ?? "Command rejected.");
    }

    private static void Driver(GameEngine g, string id, int[]? targets = null)
    {
        if (P(g) is null) Settle(g);
        var p = P(g) ?? throw new InvalidOperationException("No pending decision for the fixture driver.");
        Accept(g, new UseProgramSkillCommand(0, "fixture:zt-driver", id, [], targets ?? [], g.Revision, p.PromptId));
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
        public PackageManifest Manifest { get; } = new("fixture-zt", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            try
            {
                typeof(StandardContentPackage).Assembly.GetType("CardGame.Content.Standard.BoundaryZhouTaiContent")!
                    .GetMethod("Register", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(null, [b]);
            }
            catch (Exception diag)
            {
                Console.WriteLine("DIAG-INNER: " + (diag.InnerException?.ToString() ?? diag.ToString()));
                throw;
            }
            b.AddSkill(new("fixture:zt-driver", "fixture", "fixture")
            {
                Program = Load("fixture:zt-driver", "\"activations\":[" +
                    Activation("hurt-self", "[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":4}]") + "," +
                    Activation("heal", "[{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}]") + "," +
                    Activation("discard", "[{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"owner\"},\"zones\":[\"hand\"],\"count\":1,\"destination\":\"discardPile\"}]") + "," +
                    Activation("strip", "[{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"selectedTarget\"},\"zones\":[\"hand\"],\"count\":1,\"destination\":\"discardPile\",\"skipIfNoCards\":true}]", 1) + "," +
                    Activation("strip-equipment", "[{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"selectedTarget\"},\"zones\":[\"equipment\"],\"count\":1,\"destination\":\"discardPile\",\"skipIfNoCards\":true}]", 1) + "," +
                    Activation("equip", "[{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"resultBind\":\"equipped\"}]", 1) + "," +
                    Activation("snatch", "[{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"selectedTarget\"},\"zones\":[\"hand\"],\"count\":1,\"destination\":\"ownerHand\",\"skipIfNoCards\":true}]", 1) + "]")
            });
            b.AddGeneral(new("fixture:zt", "界周泰", "boundary_zhou_tai", Buqu, "wu", 4,
                new[] { Fenji, "fixture:zt-driver" }));
            for (var i = 1; i < 4; i++)
                b.AddGeneral(new($"fixture:zt-{i}", "其他" + i, "supporter", "standard:none", "wei", 5, null));
            b.AddDeck(new("fixture:zt-deck", "固定", 4, 1, [])
            {
                PhysicalCards = Enumerable.Range(0, 64).Select(i =>
                    new ContentDeckPhysicalCard("standard:slash", Suit.Spade, 5)).ToArray()
            });
            b.AddMode(new("identity:zt", "固定", 4, 4,
                new Dictionary<string, int> { { nameof(Role.Lord), 1 }, { nameof(Role.Loyalist), 1 }, { nameof(Role.Rebel), 2 } },
                "fixture:zt-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:zt", "fixture:zt-1", "fixture:zt-2", "fixture:zt-3"]));
        }
    }
}
