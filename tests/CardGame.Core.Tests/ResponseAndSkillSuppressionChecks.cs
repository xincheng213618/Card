using CardGame.Content.Standard;
using CardGame.Core;

internal static class ResponseAndSkillSuppressionChecks
{

    public static void XiongluanBlocksHandButDoesNotIgnoreArmor()
    {
        var registry = Registry();
        var game = Start(registry, "classic:zhang-xiu", 1);
        ReachPlay(game);
        var action = game.GetHumanLegalActions().Single(item =>
            item.Kind == LegalActionKind.UseProgramSkill && item.ProgramSkillId == "classic:xiongluan");
        var result = game.Submit(new UseProgramSkillCommand(0, "classic:xiongluan",
            action.ProgramActivationId!, [], [1], game.Revision, game.PendingDecision!.PromptId));
        Require(result.Accepted, result.Error?.Message ?? "Xiongluan activation failed.");
        Drain(game);
        var owner = game.CreateSnapshot(0, true).Players[0];
        var events = game.Events.Select(item => item.Payload).ToArray();
        Require(owner.IsEquipmentAreaAbolished && owner.IsJudgmentAreaAbolished &&
                events.OfType<PlayerAreasAbolishedEvent>().Any(item =>
                item.Seat == 0 && item.Equipment && item.Judgment) &&
                events.OfType<HandCardColorRestrictionGrantedEvent>().Count(item =>
                    item.Restriction.AffectedSeat == 1) == 2,
            "Xiongluan must abolish both areas and block both hand-card colors for its chosen target.");
        var policy = events.OfType<DirectedTurnCardPolicyGrantedEvent>().Single().Policy;
        Require(policy.ActorSeat == 0 && policy.TargetSeat == 1 &&
                policy.Effects.HasFlag(DirectedTurnCardPolicyEffect.IgnoreDistance) &&
                policy.Effects.HasFlag(DirectedTurnCardPolicyEffect.BypassSlashLimit) &&
                !policy.Effects.HasFlag(DirectedTurnCardPolicyEffect.IgnoreArmor),
            "Xiongluan must keep the target's armor effective.");
        Require(game.GetHumanLegalActions().All(item =>
                item.Kind != LegalActionKind.UseProgramSkill || item.ProgramSkillId != "classic:xiongluan"),
            "Xiongluan must remain limited after use.");
    }

    public static void DuanchangPermanentlyRemovesKillersSkills()
    {
        var registry = Registry();
        for (var seed = 1; seed <= 60; seed++)
        {
            var game = Start(registry, "fixture:skill-loss-killer", seed,
                "fixture:duanchang-death");
            ReachPlay(game);
            var victim = game.CreateSnapshot(0, true).Players.FirstOrDefault(player =>
                player.GeneralId == "fixture:duanchang-victim" && player.IsAlive);
            if (victim is null) continue;
            var action = game.GetHumanLegalActions().FirstOrDefault(item =>
                item.Kind == LegalActionKind.UseProgramSkill &&
                item.ProgramSkillId == "fixture:skill-loss-damage");
            if (action is null) continue;
            var result = game.Submit(new UseProgramSkillCommand(0, "fixture:skill-loss-damage",
                "hit", [], [victim.Seat], game.Revision, game.PendingDecision!.PromptId));
            Require(result.Accepted, result.Error?.Message ?? "Death fixture activation failed.");
            Drain(game);
            var loss = game.Events.Select(item => item.Payload).OfType<CharacterSkillsLostEvent>()
                .SingleOrDefault(item => item.Seat == 0 && item.SourceSeat == victim.Seat);
            Require(loss is not null && loss.SkillIds.Contains("classic:fuqi") &&
                    loss.SkillIds.Contains("fixture:skill-loss-damage"),
                "Duanchang must permanently remove all of its killer's character skills.");
            Require(game.CreateSnapshot(0, true).Players[0].Skills is { Count: 0 },
                "The killer's public skill list must reflect the permanent loss.");
            return;
        }
        throw new InvalidOperationException("No surviving Duanchang victim fixture was found.");
    }

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Scenario());

    private static GameEngine Start(ContentRegistry registry, string generalId, int seed,
        string modeId = "fixture:response-capabilities")
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed, PlayerCount = 5, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = modeId, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 3
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Fixture did not start.");
        var selected = game.Submit(new SelectGeneralCommand(0, generalId,
            game.Revision, game.PendingDecision!.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "General selection failed.");
        return game;
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 60 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Advance(game);
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard, "Fixture did not reach Play.");
    }

    private static void Drain(GameEngine game)
    {
        for (var step = 0; step < 100 && game.ResolutionStack.Count > 0; step++) Advance(game);
        Require(game.ResolutionStack.Count == 0, "Skill resolution remained pending.");
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Fixture could not advance.");
    }

    private static void Play(GameEngine game, LegalAction action)
    {
        var result = game.Submit(new PlayCardCommand(0, action.CardId!.Value,
            action.TargetSeats, game.Revision, game.PendingDecision!.PromptId,
            action.PlayedCardKind, action.TargetCardId)
        {
            ConversionSource = action.ConversionSource,
            AdditionalConversionSources = action.AdditionalConversionSources
        });
        Require(result.Accepted, result.Error?.Message ?? "Card use failed.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Scenario : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("response-capabilities-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 149, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var driver = SkillProgramCatalog.Load("""
                {"schemaVersion":62,"skills":[{"id":"fixture:skill-loss-damage","revision":1,
                  "minimumRulesVersion":180,"activations":[{"id":"hit","usesPerTurn":1,
                    "minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,
                    "targetKind":"otherLiving","effects":[{"op":"damage","target":"selectedTarget","amount":5}]}]}]}
                """, """
                {"schemaVersion":3,"skills":{"fixture:skill-loss-damage":{"name":"伤害","description":"测试"}}}
                """);
            builder.AddSkill(new ContentSkillDefinition("fixture:skill-loss-damage", "伤害", "测试")
            { Program = driver.Programs["fixture:skill-loss-damage"] });
            builder.AddGeneral(new ContentGeneralDefinition("fixture:skill-loss-killer", "测试凶手",
                "supporter", "classic:fuqi", "qun", BaseHp: 4,
                AdditionalSkillIds: ["fixture:skill-loss-damage"]));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:duanchang-victim", "测试蔡文姬",
                "supporter", "classic:duanchang", "qun", BaseHp: 1,
                Gender: GeneralGender.Female));
            var targets = Enumerable.Range(1, 4).Select(index => $"fixture:response-target-{index}").ToArray();
            foreach (var id in targets)
                builder.AddGeneral(new ContentGeneralDefinition(id, "测试目标", "supporter",
                    "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:weapon-user", "装备测试",
                "supporter", "standard:none", "qun", BaseHp: 4));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:ol-wushen-user", "武神测试",
                "shen_guan_yu", "ol:wushen", "qun", BaseHp: 5));
            builder.AddDeck(new ContentDeckRecipe("fixture:response-capabilities-deck",
                "响应能力测试牌堆", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 160).Select(index =>
                    new ContentDeckPhysicalCard(index % 2 == 0 ? "standard:slash" : "standard:dodge",
                        (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            builder.AddMode(new ContentModeDefinition("fixture:response-capabilities", "响应能力测试",
                5, 5, new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, "fixture:response-capabilities-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: ["classic:zhang-xiu", .. targets]));
            builder.AddDeck(new ContentDeckRecipe("fixture:xingtian-weapon-deck",
                "刑天测试牌堆", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 160).Select(index =>
                    new ContentDeckPhysicalCard((index % 4) switch
                    {
                        0 => "special:xingtian-axe",
                        1 => "standard:slash",
                        2 => "standard:dodge",
                        _ => "standard:peach"
                    }, (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            builder.AddMode(new ContentModeDefinition("fixture:xingtian-weapon", "刑天武器测试",
                5, 5, new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, "fixture:xingtian-weapon-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: ["fixture:weapon-user", .. targets]));
            builder.AddMode(new ContentModeDefinition("fixture:duanchang-death", "断肠测试",
                5, 5, new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, "fixture:response-capabilities-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: ["fixture:skill-loss-killer", "fixture:duanchang-victim", .. targets.Take(3)]));

            AddSlashMode("fixture:ghost-blade", "fixture:weapon-user",
                ["special:ghost-dragon-crescent-blade", "standard:slash", "standard:dodge", "standard:peach"],
                [Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond]);
            AddSlashMode("fixture:scarlet-sword", "fixture:weapon-user",
                ["special:scarlet-blood-sword", "standard:slash", "standard:dodge", "standard:peach"],
                [Suit.Spade, Suit.Club, Suit.Heart, Suit.Diamond]);
            AddSlashMode("fixture:ol-wushen", "fixture:ol-wushen-user",
                ["standard:peach", "standard:dodge", "standard:slash", "standard:draw_two"],
                [Suit.Heart, Suit.Spade, Suit.Club, Suit.Diamond]);

            void AddSlashMode(string id, string actorId, string[] kinds, Suit[] suits)
            {
                builder.AddDeck(new ContentDeckRecipe(id + "-deck", id, 4, 2, [])
                {
                    PhysicalCards = Enumerable.Range(0, 160).Select(index =>
                        new ContentDeckPhysicalCard(kinds[index % kinds.Length],
                            suits[index % suits.Length], index % 13 + 1)).ToArray()
                });
                builder.AddMode(new ContentModeDefinition(id, id, 5, 5,
                    new Dictionary<string, int>
                    {
                        [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                        [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                    }, id + "-deck", GeneralCandidateCount: 5,
                    GeneralPoolIds: [actorId, .. targets]));
            }
        }
    }
}
