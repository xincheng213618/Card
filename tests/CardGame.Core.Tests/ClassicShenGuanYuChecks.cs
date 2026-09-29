using CardGame.Content.Standard;
using CardGame.Core;

internal static class ClassicShenGuanYuChecks
{
    internal static void WushenRealDeckIdentityAndReplay()
    {
        var registry = CreateRegistry();
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
                MaxTurns = 40
            }, registry);
            Require(game.Submit(new StartGameCommand()).Accepted,
                "The formal Shen Guan Yu fixture could not start.");
            var selection = game.PendingDecision;
            Require(selection is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } &&
                    selection.ValidContentIds.Contains(GeneralId, StringComparer.Ordinal),
                "The formal Shen Guan Yu fixture did not expose its fixed general pool.");
            Require(game.Submit(new SelectGeneralCommand(
                    0, GeneralId, game.Revision, selection!.PromptId)).Accepted,
                "Formal Shen Guan Yu could not be selected.");
            if (!AdvanceToFactionChoice(game))
                continue;
            var faction = game.PendingDecision!;
            var wei = faction.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("faction-id") == "wei");
            Require(game.Submit(new AnswerPromptCommand(
                    0, faction.PromptId, wei.Id, game.Revision)).Accepted,
                "Formal Shen Guan Yu could not choose an effective faction.");
            if (!AdvanceToHumanPlay(game))
                continue;

            var owner = game.CreateSnapshot(0, revealAll: true).Players[0];
            var physicalPeach = owner.Hand.FirstOrDefault(card =>
                card.Kind == CardKind.Peach && card.Suit == Suit.Heart);
            if (physicalPeach is null)
                continue;
            var actions = game.GetHumanLegalActions();
            var wushenSlash = actions.FirstOrDefault(action =>
                action.CardId == physicalPeach.Id &&
                action.Kind == LegalActionKind.Slash &&
                action.TargetSeats.SequenceEqual([2]) &&
                action.PlayedCardKind == CardKind.Slash &&
                action.ConversionSource is
                {
                    SkillId: "classic:wushen",
                    BindingId: "heart-hand-as-slash",
                    OwnerSeat: 0
                });
            if (wushenSlash is null)
                continue;
            Require(!actions.Any(action =>
                    action.CardId == physicalPeach.Id && action.Kind == LegalActionKind.Peach),
                "A heart Peach in Shen Guan Yu's hand must not retain a physical Peach play entry.");

            var paused = RoundTrip(game.CreateCheckpoint());
            var restored = GameReplay.Restore(paused, registry);
            Play(game, wushenSlash);
            Play(restored, restored.GetHumanLegalActions().Single(action =>
                action.CardId == physicalPeach.Id &&
                action.Kind == LegalActionKind.Slash &&
                action.TargetSeats.SequenceEqual([2])));
            AdvanceUntilSettled(game);
            AdvanceUntilSettled(restored);

            var accepted = game.Events.Select(item => item.Payload)
                .OfType<CardActionAcceptedEvent>()
                .Single(item => item.Action.PhysicalCards.Any(card => card.CardId == physicalPeach.Id));
            Require(accepted.Action.EffectiveKind == CardKind.Slash &&
                    accepted.Action.PhysicalCards.Single().CardKind == CardKind.Peach &&
                    accepted.Action.ConversionChain.Single() is
                    {
                        SkillId: "classic:wushen",
                        BindingId: "heart-hand-as-slash",
                        OwnerSeat: 0
                    },
                "Formal Wushen must freeze a real heart Peach as an ordinary Slash with its physical identity intact.");
            Require(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } next &&
                    !next.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("action") == "slash"),
                "Formal Wushen must ignore distance without bypassing the normal Slash count.");
            Require(SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) ==
                    SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) &&
                    game.Events.Select(item => item.Payload.GetType().Name)
                        .SequenceEqual(restored.Events.Select(item => item.Payload.GetType().Name)),
                "Formal Wushen did not replay exactly from its real-deck play prompt.");
            return;
        }

        throw new InvalidOperationException(
            "No bounded real classic deck fixture dealt Shen Guan Yu a heart Peach with a distance-two target.");
    }


    private const string GeneralId = "classic:shen-guan-yu";
    private const string ModeId = "identity:classic-shen-guan-yu-4";
    private const string DyingGeneralId = "classic-shen-guan-yu-test:wushen-owner";
    private const string DyingModeId = "identity:classic-wushen-dying-4";

    internal static void WuhunRealDeckDeathChainAndReplay()
    {
        var registry = CreateRegistry();
        for (var seed = 1; seed <= 2_048; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 4,
                ModeId = ModeId,
                HumanSeat = -1,
                HumanRole = null,
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2,
                MaxTurns = 100
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted)
                continue;
            for (var step = 0; step < 8_192 && game.State.Status != EngineStatus.Completed; step++)
            {
                var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
                if (!advanced.Accepted)
                    break;
            }

            var ownerSeat = game.CreateSnapshot(0, revealAll: true).Players
                .Single(player => player.GeneralId == GeneralId).Seat;
            var events = game.Events.Select(item => item.Payload).ToArray();
            var marker = events.OfType<PlayerMarkerChangedEvent>().FirstOrDefault(item =>
                item.SkillOwnerSeat == ownerSeat && item.Delta == 1);
            var started = events.OfType<ProgramBindingStartedEvent>().FirstOrDefault(item =>
                item.OwnerSeat == ownerSeat && item.SkillId == "classic:wuhun" &&
                item.Window == SkillProgramTriggerWindow.OwnerDied);
            var judgment = events.OfType<JudgmentResolvedEvent>().FirstOrDefault(item =>
                item.Reason == JudgmentReasons.Wuhun);
            var directDeath = events.OfType<ProgramSkillCauseDeathDeclaredEvent>().FirstOrDefault(item =>
                item.SourceSeat == ownerSeat && item.SkillId == "classic:wuhun");
            var cleared = events.OfType<PlayerMarkerChangedEvent>().FirstOrDefault(item =>
                item.SkillOwnerSeat == ownerSeat && item.Delta < 0 &&
                item.Reason == "program.attributed-marker.death-clear");
            if (marker is null || started is null || judgment is null ||
                directDeath is null || cleared is null)
                continue;

            Require(directDeath.TargetSeat == judgment.TargetSeat &&
                    !events.OfType<PlayerDyingEvent>().Any(item =>
                        item.VictimSeat == directDeath.TargetSeat &&
                        events.ToList().IndexOf(item) > Array.IndexOf(events, directDeath)),
                "Formal Wuhun must select a maximum Nightmare holder and cause direct death without a rescue window.");
            var restored = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Require(SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) ==
                    SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) &&
                    game.Events.Select(item => item.Payload.GetType().Name)
                        .SequenceEqual(restored.Events.Select(item => item.Payload.GetType().Name)),
                "Formal Wuhun's real-deck death chain did not replay exactly.");
            return;
        }

        throw new InvalidOperationException(
            "No bounded all-AI classic-deck match completed formal Wuhun's direct-death chain.");
    }

    private static bool AdvanceToFactionChoice(GameEngine game)
    {
        for (var step = 0; step < 64; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.SelectFaction, PlayerSeat: 0 })
                return true;
            if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted)
                return false;
        }
        return false;
    }

    private static bool AdvanceToHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 256; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 })
                return true;
            if (game.PendingDecision is { PlayerSeat: 0 })
                return false;
            if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted)
                return false;
        }
        return false;
    }

    private static void Play(GameEngine game, LegalAction action)
    {
        var played = game.Submit(new PlayCardCommand(
            0,
            action.CardId!.Value,
            action.TargetSeats,
            game.Revision,
            game.PendingDecision!.PromptId,
            action.PlayedCardKind)
        {
            ConversionSource = action.ConversionSource
        });
        Require(played.Accepted, played.Error?.Message ?? "The formal Wushen Slash was rejected.");
    }

    private static void AdvanceUntilSettled(GameEngine game)
    {
        for (var step = 0; step < 256; step++)
        {
            if (game.ResolutionStack.Count == 0 &&
                game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 })
                return;
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted,
                advanced.Error?.Message ?? "The formal Wushen Slash could not settle.");
        }
        throw new InvalidOperationException("The formal Wushen Slash did not settle in bounded steps.");
    }

    private static ContentRegistry CreateRegistry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new ScenarioPackage());

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new(
            "classic-shen-guan-yu-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 67, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                DyingGeneralId,
                "武神救援边界测试",
                "shen_guan_yu",
                "classic:wushen",
                "god",
                BaseHp: 1));
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "经典神关羽真实牌堆场景",
                4,
                4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId: "classic:standard-deck",
                GeneralCandidateCount: 4,
                GeneralPoolIds:
                [
                    GeneralId,
                    "classic:cao-cao",
                    "classic:liu-bei",
                    "classic:sun-quan"
                ]));
            builder.AddMode(new ContentModeDefinition(
                DyingModeId,
                "武神红桃桃救援边界",
                4,
                4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId: "classic:standard-deck",
                GeneralCandidateCount: 4,
                GeneralPoolIds:
                [
                    DyingGeneralId,
                    "classic:lu-bu",
                    "classic:zhang-fei",
                    "classic:ma-chao"
                ]));
        }
    }
}
