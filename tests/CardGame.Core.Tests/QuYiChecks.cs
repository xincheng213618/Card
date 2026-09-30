using CardGame.Content.Standard;
using CardGame.Core;

internal static class QuYiChecks
{
    private const string General = "classic:qu-yi";
    private const string DamageGeneral = "fixture:qu-yi-damage";


    public static void NearbySlashCannotRespondBeforeDamageBonus()
    {
        var registry = Registry();
        for (var seed = 1; seed <= 120; seed++)
        {
            var game = Start(registry, seed, General, "fixture:qu-yi-mixed");
            ReachPlay(game);
            var target = game.CreateSnapshot(0, true).Players[1];
            var slash = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Slash && action.TargetSeat == 1);
            if (slash is null || !target.Hand.Any(card => card.Kind == CardKind.Dodge)) continue;
            Play(game, slash);
            FinishResolution(game);
            var events = game.Events.Select(item => item.Payload).ToArray();
            Require(!events.OfType<ResponseRequestedEvent>().Any(item =>
                    item.TargetSeat == 1 && item.IncomingCard is CardKind.Slash or
                        CardKind.FireSlash or CardKind.ThunderSlash),
                "Fuqi must remove the nearby target's Dodge response before damage.");
            Require(events.OfType<DamageRequestedEvent>().Any(item =>
                    item.SourceSeat == 0 && item.TargetSeat == 1 && item.Amount == 2) &&
                    events.OfType<ProgramCardDamageModifiedEvent>().Any(item =>
                        item.Source.SkillId == "classic:jiaozi" && item.ModifiedAmount == 2),
                "Jiaozi must increase damage once when Qu Yi alone has the most hand cards.");
            return;
        }
        throw new InvalidOperationException("No nearby Slash fixture with a Dodge was found.");
    }








    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Scenario());

    private static GameEngine Start(ContentRegistry registry, int seed, string generalId, string modeId)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed, PlayerCount = 5, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = modeId, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 3
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Qu Yi fixture did not start.");
        var prompt = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, generalId, game.Revision, prompt.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Qu Yi selection failed.");
        return game;
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 50 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Advance(game);
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard, "Qu Yi fixture did not reach Play.");
    }

    private static void FinishResolution(GameEngine game)
    {
        for (var step = 0; step < 100 && game.ResolutionStack.Count > 0; step++)
            Advance(game);
        Require(game.ResolutionStack.Count == 0, "Qu Yi card or damage resolution remained active.");
    }

    private static void Play(GameEngine game, LegalAction action)
    {
        var result = game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId));
        Require(result.Accepted, result.Error?.Message ?? "Qu Yi card use failed.");
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Qu Yi fixture could not advance.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Scenario : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("qu-yi-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 148, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            const string rules = """
                {"schemaVersion":62,"skills":[{"id":"fixture:damage-driver","revision":1,
                "minimumRulesVersion":178,"activations":[{"id":"hit","usesPerTurn":1,
                "minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,
                "targetKind":"otherLiving","effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"hit-after-discard","usesPerTurn":1,
                "minCards":2,"maxCards":2,"sourceZones":["hand"],
                "minTargets":1,"maxTargets":1,"targetKind":"otherLiving",
                "effects":[{"op":"discardSelected","target":"owner","amount":2},
                {"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"feed-and-hit","usesPerTurn":1,
                "minCards":2,"maxCards":2,"sourceZones":["hand"],
                "minTargets":1,"maxTargets":1,"targetKind":"otherLiving",
                "effects":[{"op":"giveSelected","target":"selectedTarget"},
                {"op":"damage","target":"selectedTarget","amount":1}]}]}]}
                """;
            const string presentation = """
                {"schemaVersion":3,"skills":{"fixture:damage-driver":{"name":"伤害驱动","description":"测试"}}}
                """;
            var driver = SkillProgramCatalog.Load(rules, presentation);
            builder.AddSkill(new ContentSkillDefinition("fixture:damage-driver", "伤害驱动", "测试")
            { Program = driver.Programs["fixture:damage-driver"] });
            builder.AddGeneral(new ContentGeneralDefinition(DamageGeneral, "麹义伤害测试", "qu_yi",
                "classic:fuqi", "qun", BaseHp: 4,
                AdditionalSkillIds: ["classic:jiaozi", "fixture:damage-driver"]));
            var targets = Enumerable.Range(1, 4).Select(index => $"fixture:qu-yi-target-{index}").ToArray();
            foreach (var target in targets)
                builder.AddGeneral(new ContentGeneralDefinition(target, "测试目标", "supporter",
                    "standard:none", "qun", BaseHp: 8));
            var mixed = new[] { "standard:slash", "standard:slash", "standard:dodge",
                "standard:barbarian_assault", "standard:slash", "standard:dodge",
                "standard:draw_two", "standard:nullification" };
            var assault = new[] { "standard:slash", "standard:slash", "standard:slash",
                "standard:barbarian_assault" };
            var barrage = new[] { "standard:slash", "standard:dodge", "standard:dodge",
                "standard:arrow_barrage" };
            var duel = new[] { "standard:slash", "standard:slash", "standard:slash",
                "standard:duel" };
            var nullification = new[] { "standard:dismantlement", "standard:nullification",
                "standard:slash", "standard:nullification" };
            AddMode("fixture:qu-yi-mixed", mixed);
            AddMode("fixture:qu-yi-assault", assault);
            AddMode("fixture:qu-yi-barrage", barrage);
            AddMode("fixture:qu-yi-duel", duel);
            AddMode("fixture:qu-yi-nullification", nullification);
            AddMode("fixture:qu-yi-incoming", mixed);

            void AddMode(string id, string[] kinds)
            {
                var deckId = id + "-deck";
                builder.AddDeck(new ContentDeckRecipe(deckId, "麹义测试牌堆", 4, 2, [])
                {
                    PhysicalCards = Enumerable.Range(0, 160).Select(index =>
                        new ContentDeckPhysicalCard(kinds[index % kinds.Length],
                            (Suit)(index % 4), index % 13 + 1)).ToArray()
                });
                builder.AddMode(new ContentModeDefinition(id, "麹义测试", 5, 5,
                    new Dictionary<string, int>
                    {
                        [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                        [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                    }, deckId, GeneralCandidateCount: id == "fixture:qu-yi-incoming" ? 5 : 6,
                    GeneralPoolIds: id == "fixture:qu-yi-incoming"
                        ? [DamageGeneral, General, .. targets.Skip(1)]
                        : [General, DamageGeneral, .. targets]));
            }
        }
    }
}
