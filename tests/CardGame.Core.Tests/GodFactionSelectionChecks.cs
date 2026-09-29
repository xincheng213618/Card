using CardGame.Content.Standard;
using CardGame.Core;

internal static class GodFactionSelectionChecks
{
    private const string ModeId = "identity:classic-god-faction-4";
    private const string GodGeneralId = "god-faction-test:god";
    private const string LordGeneralId = "god-faction-test:zhang-jiao";

    internal static void PromptPrivacyEffectiveFactionAndReplay()
    {
        var registry = CreateRegistry();
        var (game, prompt) = FindFixture(registry);
        Require(prompt is
            {
                Kind: DecisionKind.SelectFaction,
                PlayerSeat: 0,
                IsPrivate: true
            } &&
            prompt.Choices.Select(choice => choice.Parameters.GetValueOrDefault("faction-id"))
                .SequenceEqual(["wei", "shu", "wu", "qun"]),
            "A selected god general must receive one private ordered Wei/Shu/Wu/Qun setup prompt.");

        var promptCheckpoint = RoundTrip(game.CreateCheckpoint());
        var restoredAtPrompt = GameReplay.Restore(promptCheckpoint, registry);
        Require(SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(restoredAtPrompt.CreateSnapshot(0, revealAll: true)),
            "The paused god-faction prompt did not restore exactly.");

        var qunChoice = prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("faction-id") == "qun");
        ChooseFaction(game, qunChoice);
        ChooseFaction(restoredAtPrompt, restoredAtPrompt.PendingDecision!.Choices.Single(choice =>
            choice.Id == qunChoice.Id));

        var ownerView = game.CreateSnapshot(0);
        var opponentView = game.CreateSnapshot(1);
        Require(ownerView.Players[0].FactionId == "qun" &&
                !ownerView.Players[0].IsFactionRevealed &&
                opponentView.Players[0].FactionId is null,
            "The chosen god faction must remain private until the general is publicly revealed.");
        Require(registry.Generals[GodGeneralId].FactionId == "god",
            "A per-match god-faction choice must not mutate the general's printed faction.");

        AdvanceToHumanPlay(game);
        AdvanceToHumanPlay(restoredAtPrompt);
        Require(game.CreateSnapshot(1).Players[0] is
            {
                FactionId: "qun",
                IsFactionRevealed: true,
                IsGeneralPublic: true
            },
            "Completing setup must reveal the chosen effective faction together with the god general.");
        Require(game.Events.Select(item => item.Payload).OfType<GodFactionSelectedEvent>()
                .Single(item => item.ActorSeat == 0).FactionId == "qun",
            "The trusted event stream must audit the exact effective faction choice.");
        Require(game.Events.Select(item => item.Payload).OfType<GodFactionSelectionRequestedEvent>()
                .Single(item => item.ActorSeat == 0).FactionIds.SequenceEqual(GodFactionChoices),
            "The trusted event stream must audit the exact ordered faction candidates.");
        Require(SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(restoredAtPrompt.CreateSnapshot(0, revealAll: true)) &&
                game.Events.Select(item => item.Payload.GetType().Name)
                    .SequenceEqual(restoredAtPrompt.Events.Select(item => item.Payload.GetType().Name)),
            "The selected faction, effective Huangtian relation and event stream did not replay exactly.");

        var unattended = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 27,
            PlayerCount = 4,
            ModeId = ModeId,
            HumanSeat = -1,
            HumanRole = null,
            UseInteractiveSetup = false,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 10
        }, registry);
        Require(unattended.Submit(new StartGameCommand()).Accepted,
            "The unattended god-faction fixture could not start.");
        var unattendedGod = unattended.CreateSnapshot(0, revealAll: true).Players.Single(player =>
            player.GeneralId == GodGeneralId);
        Require(GodFactionChoices.Contains(unattendedGod.FactionId) &&
                unattended.Events.Select(item => item.Payload).OfType<GodFactionSelectedEvent>()
                    .Any(item => item.ActorSeat == unattendedGod.Seat && item.FactionId == unattendedGod.FactionId),
            "Unattended setup must choose and audit one deterministic legal faction without opening a prompt.");
        var unattendedReplay = GameReplay.Restore(RoundTrip(unattended.CreateCheckpoint()), registry);
        Require(SnapshotJson.Serialize(unattended.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(unattendedReplay.CreateSnapshot(0, revealAll: true)),
            "The automatic god-faction choice did not replay exactly.");
    }

    private static bool HasClassicHuangtianContribution(PendingDecision prompt) =>
        prompt.Choices.Any(choice =>
            choice.Parameters.GetValueOrDefault("action") == "use-program-skill" &&
            choice.Parameters.GetValueOrDefault("skill-id") == "classic:huangtian");

    private static (GameEngine Game, PendingDecision Prompt) FindFixture(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 4,
                ModeId = ModeId,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2,
                MaxTurns = 30
            }, registry);
            Require(game.Submit(new StartGameCommand()).Accepted,
                "The god-faction fixture could not start.");

            for (var step = 0; step < 32; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } selection)
                {
                    if (!selection.ValidContentIds.Contains(GodGeneralId, StringComparer.Ordinal)) break;
                    var selected = game.Submit(new SelectGeneralCommand(
                        0, GodGeneralId, game.Revision, selection.PromptId));
                    Require(selected.Accepted, selected.Error?.Message ?? "The god general could not be selected.");
                    continue;
                }
                if (game.PendingDecision is { Kind: DecisionKind.SelectFaction, PlayerSeat: 0 } faction)
                {
                    return (game, faction);
                }
                var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
                if (!advanced.Accepted) break;
            }
        }

        throw new InvalidOperationException("No bounded god-faction setup fixture was found.");
    }

    private static void ChooseFaction(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("Faction prompt was lost.");
        var selected = game.Submit(new AnswerPromptCommand(
            prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision));
        Require(selected.Accepted, selected.Error?.Message ?? "The faction choice was rejected.");
    }

    private static void AdvanceToHumanPlay(GameEngine game)
    {
        if (!TryAdvanceToHumanPlay(game))
            throw new InvalidOperationException("The god-faction fixture did not reach human play.");
    }

    private static bool TryAdvanceToHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 2_048 && game.State.Status != EngineStatus.Completed; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return true;
            if (game.PendingDecision is { PlayerSeat: 0 } prompt)
            {
                var choice = prompt.Choices.FirstOrDefault(candidate =>
                        candidate.Parameters.GetValueOrDefault("response") is "dodge" or "slash" or
                            "rescue-peach" or "rescue-alcohol") ??
                    prompt.Choices.FirstOrDefault();
                if (choice is null) return false;
                var answered = game.Submit(new AnswerPromptCommand(
                    0, prompt.PromptId, choice.Id, game.Revision));
                if (!answered.Accepted) return false;
                continue;
            }
            var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
            if (!result.Accepted) return false;
        }
        return false;
    }

    private static ContentRegistry CreateRegistry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new FixturePackage());

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static readonly string[] GodFactionChoices = ["wei", "shu", "wu", "qun"];

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FixturePackage : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new(
            "god-faction-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 66, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                GodGeneralId, "神势力测试", "guan_yu", "standard:none", "god", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition(
                LordGeneralId, "黄天测试主公", "zhang_jiao", "classic:huangtian", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition(
                "god-faction-test:other-1", "势力陪测甲", "cao_cao", "standard:none", "wei", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition(
                "god-faction-test:other-2", "势力陪测乙", "liu_bei", "standard:none", "shu", BaseHp: 8));
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "神势力选择场景",
                4,
                4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId: "standard:basic-demo",
                GeneralCandidateCount: 4,
                GeneralPoolIds:
                [
                    GodGeneralId,
                    LordGeneralId,
                    "god-faction-test:other-1",
                    "god-faction-test:other-2"
                ]));
        }
    }
}
