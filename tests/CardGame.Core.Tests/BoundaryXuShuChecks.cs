using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using static BoundaryLiDianChecks;

internal static class BoundaryXuShuChecks
{
    private const string Zhuhai = "boundary:zhuhai-current";
    private const string Qianxin = "boundary:qianxin-current";
    private const string Jianyan = "boundary:jianyan-current";

    public static void RunCurrentRulesAndAwakening()
    {
        var (g, registry) = Start();
        Driver(g, "damage", [1]); Settle(g);
        Require(!g.CreateSnapshot(0).Players[0].Skills!.Any(skill => skill.ContentId == Jianyan),
            "An unwounded source does not awaken after actual damage.");
        Driver(g, "hurt"); Settle(g);
        var before = g.State.Players[0].MaxHp;
        Driver(g, "damage", [1]); Settle(g);
        Require(g.State.Players[0].MaxHp == before - 1 &&
            g.CreateSnapshot(0).Players[0].Skills!.Any(skill => skill.ContentId == Jianyan),
            "Wounded actual source damage reduces maximum HP once and grants the derived Jianyan skill.");
        Driver(g, "damage", [1]); Settle(g);
        Require(g.State.Players[0].MaxHp == before - 1, "The game-scoped awakening cannot repeat.");
        ReplayFourViews(g, registry);

        var (silent, sr) = Start(damagingTurns: false);
        var turn = silent.State.TurnNumber;
        EndPlay(silent);
        Reach(silent, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0 && silent.State.TurnNumber > turn);
        Require(!silent.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Any(e => e.SkillId == Zhuhai),
            "Other-turn HP loss alone does not count as dealt damage or offer Zhuhai.");
        ReplayFourViews(silent, sr);

        var old = new SkillProgramTriggerFacts(0, 3, true);
        Require(!JsonSerializer.Serialize(old).Contains("TurnOwnerDamageDealtThisTurn", StringComparison.Ordinal) &&
            !JsonSerializer.Serialize(new ProgramSkillFrame(1, 0, "old", "old", "hash", 1, [], []))
                .Contains("FixedTargetSlash", StringComparison.Ordinal),
            "Historical catalogs omit the new nullable facts and draft fields.");
        foreach (var members in new[]
        {
            "\"activations\":[" + Activation("bad", "[{\"op\":\"useOwnerSlashAgainstTurnOwner\",\"target\":\"owner\",\"ignoreDistance\":true}]") + "]",
            "\"triggers\":[{\"id\":\"bad\",\"window\":\"preparationStarting\",\"subject\":\"owner\",\"optional\":false,\"condition\":{\"kind\":\"compare\",\"left\":{\"kind\":\"turnOwnerDamageDealtThisTurn\"},\"operator\":\"greaterThan\",\"right\":{\"kind\":\"integerConstant\",\"value\":0}},\"effects\":[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}]}]",
            "\"activations\":[" + Activation("bad", "[{\"op\":\"declareDeckCriterionAndGiveMatchingCard\",\"target\":\"owner\"},{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}]") + "]"
        })
        {
            var rejected = false;
            try { Load("fixture:xu-bad", members); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "The new owning operations reject an unsupported entry point or composition.");
        }
    }
    private static void EndPlay(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Driver(GameEngine g, string id, int[]? targets = null) =>
        Accept(g, new UseProgramSkillCommand(0, "fixture:xu-driver", id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static CharacterState[] Players(GameEngine g) => ((IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(g)!).ToArray();
    private static SkillProgram Load(string id, string members) => SkillProgramCatalog.Load("{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"" + id + "\",\"revision\":1," + members + "}]}",
        "{\"schemaVersion\":3,\"skills\":{\"" + id + "\":{\"name\":\"fixture\",\"description\":\"fixture\"}}}").Programs[id];
    private static string Activation(string id, string effects, int targets = 0) => "{\"id\":\"" + id + "\",\"usesPerTurn\":null,\"minCards\":0,\"maxCards\":0,\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"effects\":" + effects + "}";
    private static (GameEngine, ContentRegistry) Start(bool damagingTurns = true, bool initialJianyan = false, bool mixedDeck = false, bool native = false,
        bool faction = false, bool gainChild = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(damagingTurns, initialJianyan, mixedDeck, native, faction, gainChild));
        var game = GameEngine.CreateStandard(new GameOptions
        { Seed = 31, PlayerCount = 4, HumanSeat = native ? -1 : 0, HumanRole = native ? null : Role.Lord,
            ModeId = "identity:xu", UseInteractiveSetup = !native, AdvanceAfterHumanCommands = false,
            UseInteractiveDiscard = false, MaxTurns = 8 }, registry);
        Accept(game, new StartGameCommand());
        if (!native)
        {
            Reach(game, p => p.Kind == DecisionKind.SelectGeneral);
            Accept(game, new SelectGeneralCommand(0, "fixture:xu", game.Revision, P(game)!.PromptId)); Settle(game);
        }
        return (game, registry);
    }

    private sealed class Fixture(bool damagingTurns, bool initialJianyan, bool mixedDeck, bool native, bool faction, bool gainChild) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-xu", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "boundary:xu-shu");
            builder.AddSkill(new("fixture:xu-driver", "fixture", "fixture")
            { Program = Load("fixture:xu-driver", "\"activations\":[" + Activation("hurt", "[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]") + "," +
                Activation("damage", "[{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":1}]", 1) + "]") });
            builder.AddSkill(new("fixture:xu-quiet", "quiet", "quiet")
            { Program = Load("fixture:xu-quiet", "\"triggers\":[{\"id\":\"quiet\",\"window\":\"playPhaseStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"grantTurnCardActionProhibition\",\"target\":\"owner\",\"cardKinds\":[\"slash\",\"fireSlash\",\"thunderSlash\"],\"actionTypes\":[\"use\"]}]}]") });
            builder.AddSkill(new("fixture:xu-source", "source", "source")
            { Program = Load("fixture:xu-source", "\"triggers\":[{\"id\":\"source\",\"window\":\"playPhaseStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"" + (damagingTurns ? "damage" : "loseHp") + "\",\"target\":\"owner\",\"amount\":1}]}]") });
            if (faction) builder.AddSkill(new("fixture:xu-faction", "激将", "fixture")
            { Tags = SkillTag.Lord, Program = Load("fixture:xu-faction", "\"cardPolicies\":[{\"id\":\"allied\",\"kind\":\"factionResponseRequest\",\"requiredCardKinds\":[\"slash\"],\"factionId\":\"shu\",\"ownerRole\":\"lord\"}]") });
            if (gainChild) builder.AddSkill(new("fixture:xu-gain", "实际获得子窗", "fixture")
            { Program = Load("fixture:xu-gain", "\"triggers\":[{\"id\":\"gift-child\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementReasons\":[\"program.declared-deck.give\"],\"movementOccurrence\":\"perBatch\",\"optional\":true,\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]}]") });
            builder.AddGeneral(new("fixture:xu", "徐庶", "boundary_xu_shu", Zhuhai, "shu", 4,
                new[] { Qianxin }.Concat(initialJianyan ? [Jianyan] : Array.Empty<string>()).Concat(native ? ["fixture:xu-source", "fixture:xu-quiet"] : ["fixture:xu-driver"])
                    .Concat(faction ? ["fixture:xu-faction"] : Array.Empty<string>()).Concat(gainChild ? ["fixture:xu-gain"] : Array.Empty<string>()).ToArray(), GeneralGender.Male));
            for (var i = 1; i < 4; i++)
                builder.AddGeneral(new($"fixture:xu-{i}", "其他" + i, "supporter", native ? Zhuhai : "standard:none", "shu", 8,
                    ["fixture:xu-quiet", "fixture:xu-source"], i == 1 ? GeneralGender.Female : GeneralGender.Male));
            builder.AddDeck(new("fixture:xu-deck", "固定", 4, 1, [])
            { PhysicalCards = Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard("standard:slash", mixedDeck && i % 2 == 0 ? Suit.Heart : Suit.Spade, i % 13 + 1)).ToArray() });
            builder.AddMode(new("identity:xu", "固定", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:xu-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:xu", "fixture:xu-1", "fixture:xu-2", "fixture:xu-3"]));
        }
    }
}
