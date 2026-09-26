using System.Text.Json;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DamageAfterDyingChecks
{
    public static void KuangguUsesDistanceAtLethalDamage()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new KuangguScenario());
        Check(distance: 1, shouldRecover: true);
        Check(distance: 2, shouldRecover: false);

        void Check(int distance, bool shouldRecover)
        {
            for (var seed = 1; seed <= 512; seed++)
            {
                var game = GameEngine.CreateStandard(new GameOptions
                {
                    Seed = seed, PlayerCount = 5, ModeId = KuangguScenario.ModeId,
                    HumanSeat = 0, HumanRole = Role.Lord, UseInteractiveSetup = true,
                    UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10
                }, registry);
                Require(game.Submit(new StartGameCommand()).Accepted, "Kuanggu fixture start failed.");
                var select = game.PendingDecision!;
                Require(game.Submit(new SelectGeneralCommand(0, "classic:wei-yan", game.Revision,
                    select.PromptId)).Accepted, "Wei Yan selection failed.");
                for (var step = 0; step < 30 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
                    Advance(game);
                if (game.PendingDecision?.Kind != DecisionKind.PlayCard) continue;

                if (distance == 2)
                {
                    var equipment = game.GetHumanLegalActions().FirstOrDefault(action =>
                        action.Kind == LegalActionKind.Equip && action.CardId is { } id &&
                        game.CreateSnapshot(0, true).Players[0].Hand.Any(card =>
                            card.Id == id && card.Kind == CardKind.StoneAxe));
                    if (equipment is null) continue;
                    Play(game, equipment);
                    if (game.PendingDecision?.Kind != DecisionKind.PlayCard)
                        Advance(game);
                    if (game.PendingDecision?.Kind != DecisionKind.PlayCard) continue;
                }

                var slash = game.GetHumanLegalActions().FirstOrDefault(action =>
                    action.Kind == LegalActionKind.Slash && action.TargetSeats.Count == 1 &&
                    game.GetCombatDistance(0, action.TargetSeats.Single()) == distance);
                if (slash is null) continue;

                var targetSeat = slash.TargetSeats.Single();
                SetHp(game, 0, game.CreateSnapshot(0, true).Players[0].MaxHp - 1);
                SetHp(game, targetSeat, 1);
                var eventCount = game.Events.Count;
                Play(game, slash);
                for (var step = 0; step < 60 && !game.Events.Skip(eventCount)
                         .Any(item => item.Payload is AfterDamageEvent damage &&
                             damage.TargetSeat == targetSeat); step++)
                    Advance(game);
                var events = game.Events.Skip(eventCount).Select(item => item.Payload).ToArray();
                Require(events.OfType<PlayerDiedEvent>().Any(item => item.VictimSeat == targetSeat),
                    "Kuanggu fixture must kill its one-HP target.");
                Require(events.OfType<KuangguRecoveredEvent>().Any() == shouldRecover &&
                        game.CreateSnapshot(0, true).Players[0].Hp ==
                        game.CreateSnapshot(0, true).Players[0].MaxHp - (shouldRecover ? 0 : 1),
                    $"Kuanggu must use distance {distance} captured when lethal damage applied.");
                return;
            }
            throw new InvalidOperationException($"No bounded Kuanggu lethal hit at distance {distance}.");
        }
    }

    public static void RescuedLethalDamageOffersWangxiAfterRescue()
    {
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(),
            new RescueScenario());

        for (var seed = 1; seed <= 512; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed, PlayerCount = 5, ModeId = RescueScenario.ModeId,
                HumanSeat = 0, HumanRole = Role.Lord, UseInteractiveSetup = true,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10
            }, registry);
            Require(game.Submit(new StartGameCommand()).Accepted, "Rescue fixture start failed.");
            var select = game.PendingDecision!;
            Require(game.Submit(new SelectGeneralCommand(0, "classic:li-dian", game.Revision,
                select.PromptId)).Accepted, "Li Dian selection failed.");
            for (var step = 0; step < 30 && game.PendingDecision?.SkillPrompt?.SkillId != "classic:xunxun"; step++)
                Advance(game);
            Require(game.PendingDecision?.SkillPrompt?.SkillId == "classic:xunxun",
                "Li Dian draw replacement was not reached.");
            AnswerAction(game, "skip");
            for (var step = 0; step < 20 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
                Advance(game);
            if (game.PendingDecision?.Kind != DecisionKind.PlayCard) continue;

            var snapshot = game.CreateSnapshot(0, revealAll: true);
            var slash = game.GetHumanLegalActions()
                .Where(action => action.Kind == LegalActionKind.Slash && action.CardId is not null &&
                                 action.TargetSeats.Count == 1)
                .FirstOrDefault(action =>
                {
                    var target = snapshot.Players[action.TargetSeats.Single()];
                    return target.Hp == 1 && target.Hand.Any(card => card.Kind == CardKind.Peach);
                });
            if (slash is null) continue;

            var victim = slash.TargetSeats.Single();
            var played = game.Submit(new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats,
                game.Revision, game.PendingDecision.PromptId, slash.PlayedCardKind, slash.TargetCardId));
            Require(played.Accepted, played.Error?.Message ?? "Rescue fixture Slash failed.");
            for (var step = 0; step < 100 && game.PendingDecision?.SkillPrompt?.SkillId != "classic:wangxi"; step++)
            {
                Require(game.PendingDecision?.SkillPrompt?.SkillId != "classic:wangxi",
                    "Wangxi appeared before rescue completed.");
                if (game.PendingDecision is { PlayerSeat: 0 } prompt)
                {
                    var decline = prompt.Choices.FirstOrDefault(choice => choice.Cards.Count == 0);
                    Require(decline is not null, "Rescue fixture needs a legal decline.");
                    Answer(game, decline!);
                }
                else
                {
                    Advance(game);
                }
            }
            if (game.PendingDecision?.SkillPrompt?.SkillId != "classic:wangxi") continue;

            var events = game.Events.Select(item => item.Payload).ToArray();
            var damageIndex = Array.FindIndex(events, item => item is DamageAppliedEvent damage &&
                damage.SourceSeat == 0 && damage.TargetSeat == victim && damage.RemainingHp == 0);
            var dyingIndex = Array.FindIndex(events, item => item is PlayerDyingEvent dying &&
                dying.VictimSeat == victim);
            var recoveryIndex = Array.FindIndex(events, item => item is RecoveryAppliedEvent recovery &&
                recovery.TargetSeat == victim);
            Require(damageIndex >= 0 && dyingIndex > damageIndex && recoveryIndex > dyingIndex &&
                    game.CreateSnapshot(0, true).Players[victim].IsAlive,
                "Lethal damage must enter dying and be rescued before Wangxi is offered.");

            var replay = GameReplay.Restore(GameCheckpointJson.Deserialize(
                GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Require(replay.PendingDecision?.SkillPrompt?.SkillId == "classic:wangxi" &&
                    State(game) == State(replay) && Events(game).SequenceEqual(Events(replay)),
                "The post-rescue Wangxi choice must survive checkpoint replay.");
            AnswerAction(game, "activate");
            AnswerAction(replay, "activate");
            Require(State(game) == State(replay) && Events(game).SequenceEqual(Events(replay)),
                "The post-rescue Wangxi draw must replay exactly.");
            return;
        }
        throw new InvalidOperationException("No bounded lethal Slash was rescued before Wangxi.");
    }

    private static void Advance(GameEngine game) =>
        Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
            "Could not advance the rescue fixture.");

    private static void Play(GameEngine game, LegalAction action)
    {
        var result = game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId));
        Require(result.Accepted, result.Error?.Message ?? "Damage timing card play failed.");
    }

    private static void SetHp(GameEngine game, int seat, int hp)
    {
        var field = typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var players = (System.Collections.IList)field.GetValue(game)!;
        players[seat]!.GetType().GetProperty("Hp")!.SetValue(players[seat], hp);
    }

    private static void AnswerAction(GameEngine game, string action) => Answer(game,
        game.PendingDecision!.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == action));

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision!;
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Rescue fixture answer failed.");
    }

    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, true));
    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray();
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class RescueScenario : IGameContentPackage
    {
        public const string ModeId = "identity:classic-damage-after-dying-rescue-5";
        public PackageManifest Manifest { get; } = new("damage-after-dying-rescue", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddDeck(new ContentDeckRecipe("fixture:rescue-deck", "Rescue Deck", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 160).Select(index =>
                    new ContentDeckPhysicalCard(index % 3 == 0 ? "standard:peach" : "standard:slash",
                        (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            foreach (var index in Enumerable.Range(1, 4))
                builder.AddGeneral(new ContentGeneralDefinition(
                    $"fixture:rescue-target-{index}", "濒死救回目标", "supporter",
                    "standard:none", "wei", BaseHp: 1));
            builder.AddMode(new ContentModeDefinition(ModeId, "Rescue", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, "fixture:rescue-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: ["classic:li-dian", "fixture:rescue-target-1", "fixture:rescue-target-2",
                    "fixture:rescue-target-3", "fixture:rescue-target-4"]));
        }
    }

    private sealed class KuangguScenario : IGameContentPackage
    {
        public const string ModeId = "identity:classic-kuanggu-lethal-5";
        public PackageManifest Manifest { get; } = new("kuanggu-lethal", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddDeck(new ContentDeckRecipe("fixture:kuanggu-deck", "Kuanggu Deck", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 160).Select(index =>
                    new ContentDeckPhysicalCard(index % 5 == 0 ? "classic:stone-axe" : "standard:slash",
                        (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            foreach (var index in Enumerable.Range(1, 4))
                builder.AddGeneral(new ContentGeneralDefinition(
                    $"fixture:kuanggu-target-{index}", "狂骨目标", "supporter",
                    "standard:none", "wei", BaseHp: 1));
            builder.AddMode(new ContentModeDefinition(ModeId, "Kuanggu", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, "fixture:kuanggu-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: ["classic:wei-yan", "fixture:kuanggu-target-1", "fixture:kuanggu-target-2",
                    "fixture:kuanggu-target-3", "fixture:kuanggu-target-4"]));
        }
    }
}
