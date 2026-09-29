using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class LiDianChecks
{
    private const string Mode = "identity:li-dian-check-5";
    private const string FragileMode = "identity:classic-li-dian-lethal-check-5";

    public static void WangxiTakenDamageOffersPrivateChoice()
    {
        var registry = Registry();
        var game = Start(registry);
        for (var step = 0; step < 1200 && game.State.Winner == Winner.None; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt?.SkillPrompt?.SkillId == "classic:wangxi")
            {
                var damage = game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                    .LastOrDefault();
                Require(damage is not null && damage.TargetSeat == 0 && damage.SourceSeat != 0 &&
                        prompt.IsPrivate && game.CreateSnapshot(1).PendingDecision is null,
                    "Taking damage from another living player must offer Li Dian a private Wangxi choice.");
                var sourceSeat = damage!.SourceSeat;
                var before = game.CreateSnapshot(0, true).Players;
                var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
                AnswerAction(game, "activate");
                AnswerAction(paused, "activate");
                var after = game.CreateSnapshot(0, true).Players;
                Require(after[0].HandCount == before[0].HandCount + 1 &&
                        after[sourceSeat].HandCount == before[sourceSeat].HandCount + 1 &&
                        State(game) == State(paused) && Events(game).SequenceEqual(Events(paused)),
                    "Taking-damage Wangxi must grant exactly one card to each participant and replay.");
                return;
            }
            if (prompt is { PlayerSeat: 0 })
            {
                if (prompt.Kind == DecisionKind.PlayCard)
                {
                    Require(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)).Accepted,
                        "Could not end Li Dian Play while waiting for incoming damage.");
                    continue;
                }
                if (prompt.Kind == DecisionKind.ProgramTrigger &&
                    prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip"))
                {
                    AnswerAction(game, "skip");
                    continue;
                }
                if (prompt.Choices.FirstOrDefault(choice => choice.Cards.Count == 0) is { } decline)
                {
                    Answer(game, decline);
                    continue;
                }
                throw new InvalidOperationException($"Unexpected human prompt {prompt.Kind} while waiting for damage.");
            }
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "Could not advance AI turns toward incoming damage.");
        }
        throw new InvalidOperationException("No bounded AI turn dealt damage to Li Dian.");
    }

    private static void Play(GameEngine game, LegalAction action)
    {
        var result = game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            game.Revision, Prompt(game).PromptId, action.PlayedCardKind, action.TargetCardId));
        Require(result.Accepted, result.Error?.Message ?? "Li Dian card play failed.");
    }

    private static ContentRegistry Registry(int deckSize = 160, bool fragileTargets = false,
        bool lightningOnly = false, bool ganglieTarget = false) => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new Scenario(deckSize, fragileTargets, lightningOnly, ganglieTarget));

    private static GameEngine Start(ContentRegistry registry, int seed = 142)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed, PlayerCount = 5,
            ModeId = registry.Modes.ContainsKey(FragileMode) ? FragileMode : Mode,
            HumanSeat = 0,
            HumanRole = Role.Lord, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Li Dian fixture start failed.");
        var select = Prompt(game);
        Require(select.ValidContentIds.Contains("classic:li-dian"), "Li Dian was not offered.");
        Require(game.Submit(new SelectGeneralCommand(0, "classic:li-dian", game.Revision,
            select.PromptId)).Accepted, "Li Dian selection failed.");
        return game;
    }

    private static PendingDecision Prompt(GameEngine game) =>
        game.PendingDecision ?? throw new InvalidOperationException("Li Dian fixture lost its prompt.");

    private static void AnswerAction(GameEngine game, string action) => Answer(game,
        Prompt(game).Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == action));

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = Prompt(game);
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Li Dian answer failed.");
    }

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));
    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, true));
    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray();
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Scenario(int deckSize, bool fragileTargets, bool lightningOnly,
        bool ganglieTarget) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("li-dian-scenario", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddDeck(new ContentDeckRecipe("fixture:li-dian-deck", "Li Dian Deck", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, deckSize).Select(index =>
                    lightningOnly
                        ? new ContentDeckPhysicalCard("standard:lightning", Suit.Spade, 2)
                        : new ContentDeckPhysicalCard(index % 7 == 0 ? "standard:alcohol" : "standard:slash",
                            (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            if (fragileTargets)
                foreach (var index in Enumerable.Range(1, 4))
                    builder.AddGeneral(new ContentGeneralDefinition(
                        $"fixture:li-dian-target-{index}", "忘隙击杀目标", "supporter",
                        "standard:none", "wei", BaseHp: 1));
            builder.AddMode(new ContentModeDefinition(fragileTargets ? FragileMode : Mode, "Li Dian", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, "fixture:li-dian-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: fragileTargets
                    ? ["classic:li-dian", "fixture:li-dian-target-1", "fixture:li-dian-target-2",
                        "fixture:li-dian-target-3", "fixture:li-dian-target-4"]
                    : ganglieTarget
                        ? ["classic:li-dian", "classic:xiahou-dun", "classic:guan-yu",
                            "classic:zhang-fei", "classic:sun-quan"]
                        : ["classic:li-dian", "classic:liu-bei", "classic:guan-yu",
                            "classic:zhang-fei", "classic:sun-quan"]));
        }
    }
}
