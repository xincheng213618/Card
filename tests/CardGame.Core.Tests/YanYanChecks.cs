using CardGame.Content.Standard;
using CardGame.Core;

internal static class YanYanChecks
{
    private const string GeneralId = "classic:yan-yan";
    private const string SkillId = "classic:juzhan";

    public static void ContentPolarityAndRulesBoundary()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        var previous = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 69, 0));
        Require(current.Generals[GeneralId] is
        {
            FactionId: "shu",
            BaseHp: 4,
            SkillIds: var skillIds
        } && skillIds.SequenceEqual([SkillId]) &&
                current.Skills[SkillId] is
                {
                    LegacyKind: null,
                    Tags: SkillTag.Conversion,
                    ExecutionForms: SkillExecutionForm.Trigger
                } &&
                !previous.Generals.ContainsKey(GeneralId) &&
                !previous.Skills.ContainsKey(SkillId),
            "Package 1.70.0 must add formal classic Yan Yan and tagged Juzhan without changing 1.69.0.");

        var registry = CreateRegistry();
        var game = CreateGame(registry, seed: 1);
        ReachFirstHumanPlay(game);
        var owner = game.CreateSnapshot(0, revealAll: true).Players[0];
        Require(owner.GeneralId == GeneralId &&
                owner.SkillRuntimeStates!.Single(state => state.SkillId == SkillId) is
                { IsAcquired: false, Usages.Count: 0 } && !IsYin(game),
            "A tagged formal conversion skill must register its public initial Yang state at setup.");

        var checkpoint = RoundTrip(game.CreateCheckpoint());
        var restored = GameReplay.Restore(checkpoint, registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
            "The initial Juzhan polarity must reconstruct exactly from the accepted setup commands.");

    }

    public static void YangYinLedgerAndReplay()
    {
        var registry = CreateRegistry();
        for (var seed = 1; seed <= 1_024; seed++)
        {
            var game = CreateGame(registry, seed);
            ReachFirstHumanPlay(game);
            if (!game.Submit(new EndPlayPhaseCommand(
                    0,
                    game.Revision,
                    game.PendingDecision!.PromptId)).Accepted)
            {
                continue;
            }

            if (!TryReachHumanJuzhan(game, SkillPolarity.Yang)) continue;
            var yangPrompt = game.PendingDecision!;
            var attackerSeat = yangPrompt.SourceSeat ??
                throw new InvalidOperationException("Juzhan Yang lost its Slash source.");
            var pendingCheckpoint = RoundTrip(game.CreateCheckpoint());
            var pendingReplay = GameReplay.Restore(pendingCheckpoint, registry);
            Require(SnapshotJson.Serialize(pendingReplay.CreateSnapshot(0, revealAll: true)) ==
                    SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                    pendingReplay.PendingDecision?.SkillPrompt?.SkillId == SkillId,
                "A paused Juzhan Yang choice must restore with the same private prompt and polarity.");

            var beforeYang = game.CreateSnapshot(0, revealAll: true);
            var yangChoice = yangPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "activate");
            Require(Answer(game, yangPrompt, yangChoice), "The formal Juzhan Yang choice was rejected.");
            var afterYang = game.CreateSnapshot(0, revealAll: true);
            var yangOwner = afterYang.Players[0];
            var yangState = yangOwner.SkillRuntimeStates!.Single(state => state.SkillId == SkillId);
            Require(IsYin(game) &&
                    yangOwner.HandCount == beforeYang.Players[0].HandCount + 1 &&
                    afterYang.Players[attackerSeat].HandCount == beforeYang.Players[attackerSeat].HandCount + 1 &&
                    yangState.DirectedPolicies is [var yangPolicy] &&
                    yangPolicy.ActorSeat == attackerSeat && yangPolicy.TargetSeat == 0 &&
                    yangPolicy.Effects == DirectedTurnCardPolicyEffect.ForbidTarget,
                "Yang must draw for both players, toggle its declared state and grant the exact directed prohibition.");
            Require(game.Events.Select(item => item.Payload).OfType<ProgramBooleanStateChangedEvent>().Last() is
                    { OwnerSeat: 0, SkillId: SkillId, StateId: "yin", Value: true } &&
                    game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                        .Count(item => item.SkillId == SkillId && item.OwnerSeat == 0 && item.Activated && item.Completed) == 1,
                "One card-use boundary must complete the binding and toggle its state exactly once.");

            if (!TryReachHumanPlay(game)) continue;
            var afterCardUseState = game.CreateSnapshot(0, revealAll: true).Players[0]
                .SkillRuntimeStates!.Single(state => state.SkillId == SkillId);
            Require(afterCardUseState.Usages.All(usage => usage.Scope != SkillUsageScope.Event),
                "Finishing the Slash must close its exact event ledger before the next human play boundary.");
            var legal = game.GetHumanLegalActions();
            var revealed = game.CreateSnapshot(0, revealAll: true);
            var duelActions = legal.Where(action => action.Kind == LegalActionKind.Duel).ToArray();
            var attack = legal.FirstOrDefault(action =>
                action.Kind == LegalActionKind.Slash &&
                action.TargetSeat is { } targetSeat &&
                revealed.Players[targetSeat].HandCount +
                (revealed.Players[targetSeat].Equipment?.Count ?? 0) +
                (revealed.Players[targetSeat].Judgment?.Count ?? 0) > 0 &&
                duelActions.Any(duel => duel.TargetSeat == targetSeat));
            if (attack is null) continue;
            var targetSeat = attack.TargetSeat!.Value;
            var duelCardId = duelActions.First(action => action.TargetSeat == targetSeat).CardId!.Value;
            var play = game.Submit(new PlayCardCommand(
                0,
                attack.CardId!.Value,
                attack.TargetSeats,
                game.Revision,
                game.PendingDecision!.PromptId,
                attack.PlayedCardKind)
            {
                ConversionSource = attack.ConversionSource
            });
            if (!play.Accepted || game.PendingDecision is not { SkillPrompt.SkillId: SkillId } yinActivation)
                continue;

            Require(Answer(game, yinActivation, yinActivation.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "activate")), "Yin activation failed.");
            var selectTarget = game.PendingDecision!;
            Require(Answer(game, selectTarget, selectTarget.Choices.Single(choice =>
                choice.Targets.SequenceEqual([targetSeat]))), "Yin event-target selection failed.");
            var yinPrompt = game.PendingDecision!;
            var hiddenTargetIds = game.CreateSnapshot(0, revealAll: true).Players[targetSeat].Hand
                .Select(card => card.Id).ToHashSet();
            Require(yinPrompt.SkillPrompt?.SkillId == SkillId && yinPrompt.IsPrivate &&
                    yinPrompt.Choices.Count > 0 &&
                    yinPrompt.ValidCardIds.All(id => !hiddenTargetIds.Contains(id)) &&
                    yinPrompt.Choices.Where(choice => choice.Parameters.GetValueOrDefault("source-zone") == "Hand")
                        .All(choice => choice.Cards.Count == 0) && game.CreateSnapshot(targetSeat).PendingDecision is null,
                "Yin must select one actual event target then expose opaque foreign hand slots through the common payment UI.");
            var yinCheckpoint = RoundTrip(game.CreateCheckpoint());
            var yinReplay = GameReplay.Restore(yinCheckpoint, registry);
            Require(SnapshotJson.Serialize(yinReplay.CreateSnapshot(0, revealAll: true)) ==
                    SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
                "A paused Juzhan Yin card choice must restore exactly.");

            var yinChoice = yinPrompt.Choices.First();
            Require(Answer(game, yinPrompt, yinChoice), "The formal Juzhan Yin choice was rejected.");
            if (!TryReachHumanPlay(game)) continue;

            var afterYin = game.CreateSnapshot(0, revealAll: true);
            var yinState = afterYin.Players[0].SkillRuntimeStates!.Single(state => state.SkillId == SkillId);
            var legalAfterYin = game.GetHumanLegalActions();
            Require(!IsYin(game) && yinState.DirectedPolicies is [var yinPolicy] &&
                    yinPolicy.ActorSeat == 0 && yinPolicy.TargetSeat == targetSeat &&
                    yinPolicy.Effects == DirectedTurnCardPolicyEffect.ForbidTarget &&
                    afterYin.Players[0].Hand.Any(card => card.Id == duelCardId) &&
                    !legalAfterYin.Any(action => action.CardId == duelCardId && action.TargetSeats.Contains(targetSeat)) &&
                    legalAfterYin.Any(action => action.CardId == duelCardId && action.TargetSeats.Any(seat => seat != targetSeat)),
                "Yin must obtain a card, toggle to Yang and prohibit every use against only the chosen target.");
            Require(game.Events.Select(item => item.Payload).OfType<ProgramBooleanStateChangedEvent>().Last() is
                    { OwnerSeat: 0, SkillId: SkillId, StateId: "yin", Value: false },
                "The common persistent-state event must publish the new face.");

            var completedCheckpoint = RoundTrip(game.CreateCheckpoint());
            var completedReplay = GameReplay.Restore(completedCheckpoint, registry);
            Require(SnapshotJson.Serialize(completedReplay.CreateSnapshot(0, revealAll: true)) ==
                    SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                    completedReplay.Events.Select(item => item.Payload.GetType().Name)
                        .SequenceEqual(game.Events.Select(item => item.Payload.GetType().Name)),
                "Both Juzhan sides and their target ledger must replay from accepted commands.");

            Require(game.Submit(new EndPlayPhaseCommand(
                    0,
                    game.Revision,
                    game.PendingDecision!.PromptId)).Accepted,
                "The Juzhan fixture could not end its restricted play phase.");
            if (!TryReachHumanPlay(game, skipJuzhan: true)) continue;
            var nextTurnState = game.CreateSnapshot(0, revealAll: true).Players[0]
                .SkillRuntimeStates!.Single(state => state.SkillId == SkillId);
            Require(nextTurnState.DirectedPolicies?.Count == 0 && !IsYin(game),
                "Juzhan target prohibitions must expire with the turn-scope ledger while polarity remains unchanged.");
            return;
        }

        throw new InvalidOperationException("Could not find a bounded formal Juzhan Yang/Yin fixture.");
    }

    private static bool IsYin(GameEngine game) => game.CreateSnapshot(0).Players[0].SkillRuntimeStates!
        .Single(state => state.SkillId == SkillId).BooleanStates!.Single(state => state.StateId == "yin").Value;

    private static bool TryReachHumanJuzhan(GameEngine game, SkillPolarity expectedState)
    {
        for (var step = 0; step < 2_048 && game.State.Winner == Winner.None; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0, SkillPrompt.SkillId: SkillId } prompt)
            {
                var state = IsYin(game) ? SkillPolarity.Yin : SkillPolarity.Yang;
                return state == expectedState && prompt.Choices.Any();
            }
            if (game.PendingDecision is { PlayerSeat: 0 } human)
            {
                if (human.Kind == DecisionKind.PlayCard) return false;
                if (human.Choices.Count == 0) return false;
                if (!Answer(game, human, human.Choices[^1])) return false;
                continue;
            }
            if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted) return false;
        }
        return false;
    }

    private static bool TryReachHumanPlay(GameEngine game, bool skipJuzhan = false)
    {
        for (var step = 0; step < 4_096 && game.State.Winner == Winner.None; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return true;
            if (game.PendingDecision is { PlayerSeat: 0 } human)
            {
                if (human.SkillPrompt?.SkillId == SkillId && !skipJuzhan) return false;
                if (human.Choices.Count == 0) return false;
                var choice = human.SkillPrompt?.SkillId == SkillId
                    ? human.Choices.Single(item => item.Parameters.GetValueOrDefault("program-action") == "skip")
                    : human.Choices[^1];
                if (!Answer(game, human, choice)) return false;
                continue;
            }
            if (!game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted) return false;
        }
        return false;
    }

    private static void ReachFirstHumanPlay(GameEngine game)
    {
        Require(game.Submit(new StartGameCommand()).Accepted, "The Yan Yan fixture failed to start.");
        for (var step = 0; step < 2_048; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return;
            if (game.PendingDecision is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } selection)
            {
                Require(selection.ValidContentIds.Contains(GeneralId, StringComparer.Ordinal),
                    $"The Yan Yan candidate list omitted {GeneralId}.");
                Require(game.Submit(new SelectGeneralCommand(
                        0,
                        GeneralId,
                        game.Revision,
                        selection.PromptId)).Accepted,
                    "The Yan Yan fixture could not select its formal general.");
                continue;
            }
            Require(game.PendingDecision?.PlayerSeat != 0,
                $"Unexpected human prompt {game.PendingDecision?.Kind} before Yan Yan's first play phase.");
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "The Yan Yan fixture could not reach its first play phase.");
        }
        throw new InvalidOperationException("The Yan Yan fixture did not reach play in bounded steps.");
    }

    private static bool Answer(GameEngine game, PendingDecision prompt, PromptChoice choice) =>
        game.Submit(new AnswerPromptCommand(
            prompt.PlayerSeat,
            prompt.PromptId,
            choice.Id,
            game.Revision)).Accepted;

    private static GameEngine CreateGame(ContentRegistry registry, int seed) =>
        GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 4,
            ModeId = ScenarioPackage.ModeId,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 60
        }, registry);

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
        public const string ModeId = "identity:classic-yan-yan-test-4";
        private const string DeckId = "fixture:yan-yan-deck";
        private static readonly string[] BlankGeneralIds =
            ["fixture:yan-yan-blank-1", "fixture:yan-yan-blank-2", "fixture:yan-yan-blank-3"];

        public PackageManifest Manifest { get; } = new(
            "yan-yan-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 70, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var (id, index) in BlankGeneralIds.Select((id, index) => (id, index)))
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    $"拒战目标{index + 1}",
                    "supporter",
                    "standard:none",
                    "qun",
                    BaseHp: 4));
            }

            var cards = new[]
            {
                (Id: "standard:slash", Suit: Suit.Spade),
                (Id: "standard:slash", Suit: Suit.Club),
                (Id: "standard:slash", Suit: Suit.Heart),
                (Id: "standard:duel", Suit: Suit.Diamond),
                (Id: "standard:dismantlement", Suit: Suit.Spade),
                (Id: "standard:arrow_barrage", Suit: Suit.Heart),
                (Id: "standard:peach", Suit: Suit.Diamond)
            };
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "严颜拒战转换与目标账本测试牌堆",
                InitialHandSize: 12,
                DrawPerTurn: 1,
                Cards: [])
            {
                PhysicalCards = Enumerable.Range(0, 192)
                    .Select(index => cards[index % cards.Length])
                    .Select((card, index) => new ContentDeckPhysicalCard(
                        card.Id,
                        card.Suit,
                        index % 13 + 1))
                    .ToArray()
            });
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "严颜拒战转换与目标账本测试",
                4,
                4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId,
                GeneralCandidateCount: 4,
                GeneralPoolIds: [GeneralId, .. BlankGeneralIds]));
        }
    }
}
