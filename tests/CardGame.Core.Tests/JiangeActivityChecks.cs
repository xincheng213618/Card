using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class JiangeActivityChecks
{
    public static void SetupTiersTeamsPrivateRosterAndReplay()
    {
        var registry = ComposedSkillContentRegistry.CreateShowcase(includeActivities: true);
        foreach (var team in new[] { "team:shu", "team:wei" })
        foreach (var difficulty in new[] { "normal", "advanced", "difficult" })
        {
            var mode = registry.Modes[$"activity:jiange-{difficulty}"];
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = 17, PlayerCount = 8, HumanSeat = 3, HumanRole = null, HumanTeamId = team, ModeId = mode.Id,
                AdvanceAfterHumanCommands = false, UseInteractiveSetup = false, MaxTurns = 12
            }, registry);
            var players = game.CreateSnapshot(3).Players;
            Require(players.Count(p => p.TeamId == team) == 4 && players.Count(p => p.IsHuman) == 1,
                "PvE has two public four-seat teams and exactly one human.");
            foreach (var player in players)
            {
                var offset = (player.Seat - 3 + 8) % 8;
                var regular = offset is 0 or 5;
                var spirit = offset is 2 or 3;
                var pools = regular ? mode.TeamActivity!.PlayerPools : spirit ? mode.TeamActivity!.SpiritPools : mode.TeamActivity!.MachinePools;
                Require(pools[player.TeamId!].Contains(player.GeneralId), "Every seat must use its assigned faction and character type.");
                var extraHp = player.MaxHp - registry.Generals[player.GeneralId].BaseHp;
                Require(regular ? extraHp == 2 : difficulty switch
                {
                    "normal" => extraHp == 0,
                    "advanced" => spirit ? extraHp is >= 1 and <= 3 : extraHp == 1,
                    _ => spirit ? extraHp is >= 2 and <= 3 : extraHp == 2
                }, "Player and NPC tiers must use their separate cumulative health tables.");
                Require(regular ? player.HandCount == 5 : difficulty == "normal" ? player.HandCount == 4 : spirit
                    ? difficulty == "advanced" ? player.HandCount is >= 5 and <= 6 : player.HandCount is >= 5 and <= 7
                    : player.HandCount == 5,
                    "Initial hands apply the cumulative tier table without the 2v2 initiative penalty.");
                Require(player.Skills!.Any(s => s.ContentId == "jiange:niepan") == regular &&
                    player.Skills!.Any(s => s.ContentId == (team == "team:shu" ? "jiange:fuyou" : "jiange:kuanggu")) == regular,
                    "Tier rescue and the event belong only to the two regular player slots, including the AI teammate.");
                if (!player.IsHuman) Require(player.Hand.Count == 0, "Friendly AI and NPC hands remain private.");
            }
            Accept(game, new StartGameCommand());
            Cold(game, registry);
        }

        var draft = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, PlayerCount = 8, HumanSeat = 3, HumanRole = null, HumanTeamId = "team:shu", ModeId = "activity:jiange-normal",
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false
        }, registry);
        Accept(draft, new StartGameCommand());
        for (var i = 0; i < 20 && draft.PendingDecision?.Kind != DecisionKind.SelectGeneral; i++) Accept(draft, new AdvanceOneStepCommand(draft.Revision));
        var prompt = draft.PendingDecision!;
        Require(prompt.PlayerSeat == 3 && prompt.ValidContentIds.All(id => registry.Generals[id].FactionId == "shu" && !id.StartsWith("jiange:")),
            "The human receives only ordinary same-faction candidates, before AI selections.");
        Require(draft.CreateSnapshot(2).PendingDecision is null, "Enemy viewers cannot inspect the human draft.");
        var before = JsonSerializer.Serialize(draft.CreateSnapshot(3));
        Require(!draft.Submit(new SelectGeneralCommand(3, "jiange:jia-ren-zi-dan", draft.Revision, prompt.PromptId)).Accepted &&
            before == JsonSerializer.Serialize(draft.CreateSnapshot(3)), "Forged NPC selections must be rejected atomically.");
        Accept(draft, new RequestGeneralRosterCommand(3, true, draft.Revision, prompt.PromptId));
        Require(draft.PendingDecision!.ValidContentIds.All(id => registry.Generals[id].FactionId == "shu" && !id.StartsWith("jiange:")),
            "The expanded roster must retain faction and seat-type restrictions.");
        Cold(draft, registry);
    }

    public static void RedTrickRepeatsOnePhysicalUseAndReplays()
    {
        var source = ComposedSkillContentRegistry.CreateShowcase(includeActivities: true).Modes["activity:jiange-normal"].TeamActivity!;
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new StandardJiangeActivityPackage(),
            new QuietActivity(source, false), new RedDeck());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, PlayerCount = 8, HumanSeat = 0, HumanRole = null, HumanTeamId = "team:shu", ModeId = "fixture:jiange-quiet-battle",
            DeckId = "fixture:jiange-red-deck", AdvanceAfterHumanCommands = false, UseInteractiveSetup = false, MaxTurns = 4
        }, registry);
        Accept(game, new StartGameCommand());
        for (var i = 0; i < 50 && game.PendingDecision?.Kind != DecisionKind.PlayCard; i++) Accept(game, new AdvanceOneStepCommand(game.Revision));
        Require(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }, "The real activity must reach the human play phase.");
        var hand = game.CreateSnapshot(0).Players[0].Hand;
        var card = hand.First(c => c.Kind == CardKind.DrawTwo);
        Accept(game, new PlayCardCommand(0, card.Id, [], game.Revision, game.PendingDecision!.PromptId));
        for (var i = 0; i < 30 && game.PendingDecision?.Kind != DecisionKind.PlayCard; i++) Accept(game, new AdvanceOneStepCommand(game.Revision));
        Require(game.CreateSnapshot(0).Players[0].HandCount == hand.Count - 1 + 4,
            "Fuyou resolves one red DrawTwo twice without paying or counting another use.");
        Require(game.Events.Count(e => e.Payload is AutomaticExtraCardEffectIssuedEvent) == 1 &&
                game.Events.Count(e => e.Payload is ExtraCardEffectStartedEvent) == 1,
            "A single original use must own exactly one extra effect round.");
        Cold(game, registry);
    }

    public static void NpcEffectsAndLimitedRescueUseRealCommands()
    {
        var source = ComposedSkillContentRegistry.CreateShowcase(includeActivities: true).Modes["activity:jiange-normal"].TeamActivity!;
        ContentRegistry Build(bool targetLoss) => ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new StandardJiangeActivityPackage(), new QuietActivity(source, targetLoss));
        var registry = Build(false);
        GameEngine Create() => GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, PlayerCount = 8, HumanSeat = 0, HumanRole = null, HumanTeamId = "team:shu", ModeId = "fixture:jiange-quiet-battle",
            AdvanceAfterHumanCommands = false, UseInteractiveSetup = false, UseInteractiveDiscard = false, MaxTurns = 12
        }, registry);
        var rescue = Create();
        Accept(rescue, new StartGameCommand());
        ReachPlay(rescue);
        var before = rescue.CreateSnapshot(0).Players[0].HandCount;
        Accept(rescue, new UseProgramSkillCommand(0, "fixture:jiange-quiet", "hurt", [], [], rescue.Revision, rescue.PendingDecision!.PromptId));
        var prompt = rescue.PendingDecision!;
        var choice = prompt.Choices.Single(c => c.Parameters.GetValueOrDefault("skill-id") == "jiange:niepan");
        Cold(rescue, registry);
        Accept(rescue, new AnswerPromptCommand(0, prompt.PromptId, choice.Id, rescue.Revision));
        ReachPlay(rescue);
        Require(rescue.CreateSnapshot(0).Players[0] is { Hp: 3 } survivor && survivor.HandCount == before + 3,
            "Screenshot Niepan heals three and adds three cards without discarding the original hand.");
        Cold(rescue, registry);
        Accept(rescue, new UseProgramSkillCommand(0, "fixture:jiange-quiet", "hurt", [], [], rescue.Revision, rescue.PendingDecision!.PromptId));
        for (var i = 0; i < 50 && rescue.CreateSnapshot(0).Players[0].IsAlive; i++)
        {
            Require(rescue.PendingDecision?.Choices.All(c => c.Parameters.GetValueOrDefault("skill-id") != "jiange:niepan") != false,
                "A spent tier rescue must not be offered twice.");
            Accept(rescue, new AdvanceOneStepCommand(rescue.Revision));
        }
        Require(!rescue.CreateSnapshot(0).Players[0].IsAlive, "The second lethal HP loss must exhaust the once-per-game rescue.");
        Cold(rescue, registry);

        var game = Create();
        Accept(game, new StartGameCommand());
        for (var step = 0; step < 400 && game.State.TurnNumber < 9; step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } play)
                Accept(game, new EndPlayPhaseCommand(0, game.Revision, play.PromptId));
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        Require(game.State.TurnNumber >= 9, "One bounded orbit must finish with the real NPC turn and damage cursors.");
        var players = game.CreateSnapshot(0).Players;
        var fireMachine = players.Single(p => p.GeneralId == "jiange:shi-huo-suan-ni").Seat;
        var fireImmune = players.Single(p => p.GeneralId == "jiange:chi-yu-zhu-que").Seat;
        var burns = game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Where(e => e.SourceSeat == fireMachine).ToArray();
        Require(burns.Length == 3 && burns.All(e => e.Nature == DamageNature.Fire && e.Amount == 1 &&
                players[e.TargetSeat].TeamId == "team:shu" && e.TargetSeat != fireImmune),
            "Lianyu hits each enemy once, never allies, and Yuhuo prevents its fire damage.");
        Require(game.Events.Any(e => e.Payload is PublicTeamTargetsBoundEvent), "NPC effects must actually execute the public-team target binding.");
        Cold(game, registry);

        registry = Build(true);
        var victory = Create();
        Accept(victory, new StartGameCommand());
        ReachPlay(victory);
        var spirits = victory.CreateSnapshot(0).Players.Where(p => p.GeneralId is "jiange:jia-ren-zi-dan" or "jiange:qiao-kui-jun-yi").ToArray();
        foreach (var spirit in spirits)
        {
            Accept(victory, new UseProgramSkillCommand(0, "fixture:jiange-quiet", "hurt", [], [spirit.Seat], victory.Revision, victory.PendingDecision!.PromptId));
            for (var i = 0; i < 50 && victory.State.Winner == Winner.None && victory.PendingDecision?.Kind != DecisionKind.PlayCard; i++)
                Accept(victory, new AdvanceOneStepCommand(victory.Revision));
        }
        Require(victory.State.WinnerTeamId == "team:shu" && victory.CreateSnapshot(0).Players.Count(p => p.TeamId == "team:wei" && p.IsAlive) == 2,
            "Defeating both enemy spirits wins while their two machines are still alive.");
        Cold(victory, registry);
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 50; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The fixture did not return to its real human play boundary.");
    }

    private sealed class QuietActivity(TeamActivityDefinition source, bool targetLoss) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("jiange-quiet-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$$$"""
                {"schemaVersion":{{{{SkillProgramCatalog.RulesSchemaVersion}}}},"skills":[{"id":"fixture:jiange-quiet","revision":1,
                "activations":[{"id":"hurt","minCards":0,"maxCards":0,"minTargets":{{{{(targetLoss ? 1 : 0)}}}},"maxTargets":{{{{(targetLoss ? 1 : 0)}}}},
                "targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"{{{{(targetLoss ? "loseHp" : "loseHpUnclamped")}}}}","target":"{{{{(targetLoss ? "selectedTarget" : "owner")}}}}","amount":{{{{(targetLoss ? 20 : 6)}}}}}]}]}]}
                """, $$$$"""
                {"schemaVersion":{{{{SkillProgramCatalog.PresentationSchemaVersion}}}},"skills":{"fixture:jiange-quiet":{"name":"test HP loss","description":"quiet fixture"}}}
                """);
            builder.AddSkill(new("fixture:jiange-quiet", "quiet", "fixture") { Program = catalog.Programs["fixture:jiange-quiet"] });
            foreach (var faction in new[] { "wei", "shu" })
            for (var i = 0; i < 2; i++) builder.AddGeneral(new($"fixture:quiet-{faction}-{i}", $"quiet {faction} {i}", "", "fixture:jiange-quiet", faction));
            builder.AddDeck(new("fixture:jiange-dodge", "quiet deck", 4, 2, [new("standard:dodge", 100)]));
            var players = new Dictionary<string, IReadOnlyList<string>>
            {
                ["team:wei"] = ["fixture:quiet-wei-0", "fixture:quiet-wei-1"],
                ["team:shu"] = ["fixture:quiet-shu-0", "fixture:quiet-shu-1"]
            };
            var activity = source with { PlayerPools = players };
            builder.AddMode(new("fixture:jiange-quiet-battle", "quiet activity", 8, 8, new Dictionary<string, int>(),
                "fixture:jiange-dodge", GeneralPoolIds: players.Values.Concat(source.SpiritPools.Values).Concat(source.MachinePools.Values)
                    .SelectMany(ids => ids).ToArray(), ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { ["team:wei"] = 4, ["team:shu"] = 4 }) { TeamActivity = activity });
        }
    }

    private sealed class RedDeck : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("jiange-red-deck-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder) => builder.AddDeck(new("fixture:jiange-red-deck", "red DrawTwo", 4, 2, [])
        { PhysicalCards = Enumerable.Range(0, 100).Select(i => new ContentDeckPhysicalCard("standard:draw_two", Suit.Heart, i % 13 + 1)).ToArray() });
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Require(result.Accepted, result.Error?.ToString() ?? "Command rejected.");
    }
    private static void Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(JsonSerializer.Serialize(game.CreateSnapshot(3)) == JsonSerializer.Serialize(restored.CreateSnapshot(3)) &&
            JsonSerializer.Serialize(game.CardMovements) == JsonSerializer.Serialize(restored.CardMovements),
            "Activity setup, private state and exact card movement replay from the accepted prefix.");
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
