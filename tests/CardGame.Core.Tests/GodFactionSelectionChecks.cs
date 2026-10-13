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
        foreach (var role in new[] { Role.Lord, Role.Loyalist })
            VerifyFactionVisibilityAndReplay(registry, role);
        VerifyPrivateAiFactionLogBeforeOpeningConfirmation(registry);

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

    private static void VerifyPrivateAiFactionLogBeforeOpeningConfirmation(ContentRegistry registry)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = ModeId, UseInteractiveSetup = true, ConfirmOpeningHand = true,
            AdvanceAfterHumanCommands = false, AiPolicyVersion = 2
        }, registry);
        var publishedLogs = new List<GameLogEntry>();
        game.LogAdded += publishedLogs.Add;
        Require(game.Submit(new StartGameCommand()).Accepted, "The private AI faction fixture must start.");
        var generalPrompt = game.PendingDecision!;
        Require(game.Submit(new SelectGeneralCommand(0, LordGeneralId, game.Revision, generalPrompt.PromptId)).Accepted,
            "The human Lord must select the non-god general, leaving the fixture's god general to an AI.");
        for (var step = 0; step < 16 && game.PendingDecision?.Kind != DecisionKind.OpeningHand; step++)
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "The bounded private AI faction fixture must advance to its opening confirmation.");
        Require(game.PendingDecision is { Kind: DecisionKind.OpeningHand } &&
                !game.Events.Any(item => item.Payload is SetupCompletedEvent or GameStartedEvent),
            "The private AI faction must be inspected before opening confirmation reveals the other generals.");
        var faction = game.Events.Select(item => item.Payload).OfType<GodFactionSelectedEvent>().Single();
        var hidden = game.CreateSnapshot(0).Players[faction.ActorSeat];
        var owner = game.CreateSnapshot(faction.ActorSeat).Players[faction.ActorSeat];
        Require(faction.ActorSeat != 0 && !hidden.IsGeneralPublic && hidden.GeneralId == string.Empty &&
                hidden.FactionId is null && owner.GeneralId == GodGeneralId && owner.FactionId == faction.FactionId,
            "The AI's actual god general and effective faction remain hidden from the local human at this boundary.");
        var restored = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        RequireSameViews(game, restored, "The private AI faction and opening confirmation must cold replay exactly.");
        Require(game.Log.SequenceEqual(restored.Log), "Cold replay must reproduce the current public log.");
        Require(game.Log.Concat(restored.Log).Concat(publishedLogs).All(entry =>
                !(entry.ActorSeat == faction.ActorSeat && entry.Type == "GodFactionSelected") &&
                !entry.Message.Contains(registry.Generals[GodGeneralId].Name, StringComparison.Ordinal)),
            "A hidden AI god general and its faction must not enter the public log or LogAdded observers before setup reveal.");

        foreach (var current in new[] { game, restored })
        {
            var opening = current.PendingDecision!;
            Require(current.Submit(new AnswerPromptCommand(0, opening.PromptId,
                    opening.Choices.Single(choice => choice.Parameters["action"] == "confirm").Id,
                    current.Revision)).Accepted, "The actual opening confirmation must succeed once.");
            Require(current.CreateSnapshot(0).Players[faction.ActorSeat] is
                    { IsGeneralPublic: true, IsFactionRevealed: true } visible &&
                    visible.GeneralId == GodGeneralId && visible.FactionId == faction.FactionId &&
                    current.Events.Select(item => item.Payload).OfType<GodFactionSelectedEvent>().Count() == 1,
                "Setup reveal must publish the committed AI general and faction without selecting the faction again.");
        }
        RequireSameViews(game, restored, "Opening confirmation after cold replay must preserve every public view.");
    }

    private static void VerifyFactionVisibilityAndReplay(ContentRegistry registry, Role role)
    {
        var (game, prompt) = CreateFixture(registry, role);
        var isLord = role == Role.Lord;
        Require(prompt is
            {
                Kind: DecisionKind.SelectFaction,
                PlayerSeat: 0,
                IsPrivate: true
            } &&
            prompt.Choices.Select(choice => choice.Parameters.GetValueOrDefault("faction-id"))
                .SequenceEqual(["wei", "shu", "wu", "qun"]),
            "A selected god general must receive one private ordered Wei/Shu/Wu/Qun setup prompt.");
        Require(Enumerable.Range(1, game.PlayerCount - 1).All(seat =>
                game.CreateSnapshot(seat).PendingDecision is null &&
                game.CreateSnapshot(seat).Players[0].IsGeneralPublic == isLord &&
                game.CreateSnapshot(seat).Players[0].GeneralId == (isLord ? GodGeneralId : string.Empty)),
            "The god Lord is already public, while a non-Lord god general and every faction choice stay private to their owner.");

        var beforeRejected = GameCheckpointJson.Serialize(game.CreateCheckpoint());
        Require(!game.Submit(new AnswerPromptCommand(0, prompt.PromptId, new ChoiceId("setup.god-faction.forged"), game.Revision)).Accepted &&
                !game.Submit(new AnswerPromptCommand(1, prompt.PromptId, prompt.Choices[0].Id, game.Revision)).Accepted &&
                beforeRejected == GameCheckpointJson.Serialize(game.CreateCheckpoint()),
            "Forged factions and another seat's answers must be rejected without changing the private setup boundary.");

        var promptCheckpoint = RoundTrip(game.CreateCheckpoint());
        var restoredAtPrompt = GameReplay.Restore(promptCheckpoint, registry);
        RequireSameViews(game, restoredAtPrompt, "The paused god-faction prompt and every viewer must restore exactly.");

        var qunChoice = prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("faction-id") == "qun");
        ChooseFaction(game, qunChoice);
        ChooseFaction(restoredAtPrompt, restoredAtPrompt.PendingDecision!.Choices.Single(choice =>
            choice.Id == qunChoice.Id));

        var ownerView = game.CreateSnapshot(0);
        var opponentView = game.CreateSnapshot(1);
        Require(ownerView.Players[0].FactionId == "qun" &&
                ownerView.Players[0].IsFactionRevealed == isLord &&
                opponentView.Players[0].IsFactionRevealed == isLord &&
                opponentView.Players[0].FactionId == (isLord ? "qun" : null),
            "A revealed god Lord publishes the chosen faction immediately; a non-Lord retains it privately until setup reveal.");
        Require(registry.Generals[GodGeneralId].FactionId == "god",
            "A per-match god-faction choice must not mutate the general's printed faction.");
        if (isLord)
            Require(game.Log.Any(entry => entry.ActorSeat == 0 && entry.Type == "GodFactionSelected" &&
                    entry.Message.Contains(registry.Generals[GodGeneralId].Name, StringComparison.Ordinal) &&
                    entry.Message.Contains("群", StringComparison.Ordinal)),
                "The already public god Lord's chosen faction remains visible in the public setup log.");
        beforeRejected = GameCheckpointJson.Serialize(game.CreateCheckpoint());
        Require(!game.Submit(new AnswerPromptCommand(0, prompt.PromptId, qunChoice.Id, game.Revision)).Accepted &&
                beforeRejected == GameCheckpointJson.Serialize(game.CreateCheckpoint()),
            "A retired faction prompt cannot choose again after the public or private faction has committed.");
        RequireSameViews(game, GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry),
            "The committed faction must cold replay with its exact Lord or non-Lord visibility before setup completes.");

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
        RequireSameViews(game, restoredAtPrompt, "The completed setup must preserve every viewer across cold replay.");
        Require(game.Events.Select(item => item.Payload.GetType().Name)
                    .SequenceEqual(restoredAtPrompt.Events.Select(item => item.Payload.GetType().Name)),
            "The selected faction, effective Huangtian relation and event stream did not replay exactly.");
        var huangtianOwner = game.CreateSnapshot(0).Players.Single(player => player.GeneralId == LordGeneralId);
        Require(huangtianOwner.Skills!.Any(skill => skill.ContentId == "classic:huangtian") == !isLord,
            "The printed Huangtian skill belongs only to its actual Lord, never to a non-Lord who selected that general.");
        if (!isLord)
        {
            Require(game.GetHumanLegalActions().Any(action => action.ProgramSkillId == "classic:huangtian" &&
                    action.ProgramSkillOwnerSeat == huangtianOwner.Seat && action.SelectableTargetSeats.SequenceEqual([huangtianOwner.Seat])) &&
                    restoredAtPrompt.GetHumanLegalActions().Any(action => action.ProgramSkillId == "classic:huangtian"),
                "The god general's chosen Qun faction must enable the real other-owner Huangtian contribution, including after replay.");
            var wei = GameReplay.Restore(promptCheckpoint, registry);
            ChooseFaction(wei, wei.PendingDecision!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("faction-id") == "wei"));
            AdvanceToHumanPlay(wei);
            Require(wei.GetHumanLegalActions().All(action => action.ProgramSkillId != "classic:huangtian"),
                "The same god general choosing Wei must not receive the Qun-only Huangtian contribution.");
        }
    }

    private static (GameEngine Game, PendingDecision Prompt) CreateFixture(ContentRegistry registry, Role role)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31,
            PlayerCount = 4,
            ModeId = ModeId,
            HumanSeat = 0,
            HumanRole = role,
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
                Require(selection.ValidContentIds.Contains(GodGeneralId, StringComparer.Ordinal),
                    "The fixed private pool must keep the god general available to its human chooser.");
                var selected = game.Submit(new SelectGeneralCommand(
                    0, GodGeneralId, game.Revision, selection.PromptId));
                Require(selected.Accepted, selected.Error?.Message ?? "The god general could not be selected.");
                continue;
            }
            if (game.PendingDecision is { Kind: DecisionKind.SelectFaction, PlayerSeat: 0 } faction)
                return (game, faction);
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "God-faction setup failed to advance.");
        }

        throw new InvalidOperationException("The fixed god-faction setup fixture did not reach its choice.");
    }

    private static void RequireSameViews(GameEngine game, GameEngine restored, string message) =>
        Require(Enumerable.Range(0, game.PlayerCount).Append(-1).All(seat =>
            SnapshotJson.Serialize(game.CreateSnapshot(seat)) == SnapshotJson.Serialize(restored.CreateSnapshot(seat))) &&
            SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)), message);

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
            builder.AddSkill(new ContentSkillDefinition("god-faction-test:lord-selection", "固定主公候选", "只用于真实选将评分的无运行技能")
            { SelectionWeights = new Dictionary<Role, double> { [Role.Lord] = 10_000 } });
            builder.AddGeneral(new ContentGeneralDefinition(
                GodGeneralId, "神势力测试", "guan_yu", "standard:none", "god", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition(
                LordGeneralId, "黄天测试主公", "zhang_jiao", "classic:huangtian", "qun", BaseHp: 8,
                AdditionalSkillIds: ["god-faction-test:lord-selection"]));
            builder.AddGeneral(new ContentGeneralDefinition(
                "god-faction-test:other-1", "势力陪测甲", "cao_cao", "standard:none", "wei", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition(
                "god-faction-test:other-2", "势力陪测乙", "liu_bei", "standard:none", "shu", BaseHp: 8));
            builder.AddDeck(new ContentDeckRecipe("god-faction-test:deck", "固定黄天贡献手牌", 4, 2,
                [new ContentDeckCardCount("standard:dodge", 64)]));
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
                DeckId: "god-faction-test:deck",
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
