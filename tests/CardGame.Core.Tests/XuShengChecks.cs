using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class XuShengChecks
{
    private const string ClassicGeneral = "classic:xu-sheng";
    private const string BoundaryGeneral = "boundary:xu-sheng";
    private const string ClassicPojun = "classic:pojun";
    private const string BoundaryPojun = "boundary:pojun";

    public static void ClassicPojunHoldsAndReturnsAtTurnEnd()
    {
        var registry = Registry();
        var completed = 0;
        var equipmentHolds = 0;
        for (var seed = 1; seed <= 96 && (completed < 4 || equipmentHolds < 1); seed++)
        {
            var game = Start(registry, seed, ClassicGeneral);
            for (var attempt = 0; attempt < 5 && (completed < 4 || equipmentHolds < 1); attempt++)
            {
                if (!AdvanceToOwnPlay(game)) break;
                var slash = game.GetHumanLegalActions().FirstOrDefault(item =>
                    item.Kind == LegalActionKind.Slash && item.TargetSeat is not null);
                if (slash is null || game.PendingDecision is not { Kind: DecisionKind.PlayCard })
                {
                    if (!RunToEndOfTurn(game)) break;
                    continue;
                }
                var target = slash.TargetSeat!.Value;
                var eventsBefore = game.Events.Count;
                var beforeHold = game.CreateSnapshot(0, true).Players[target];
                var candidateIds = beforeHold.Hand.Select(card => card.Id)
                    .Concat(beforeHold.Equipment.Select(card => card.Id)).ToArray();

                var checkpoint = RoundTrip(game.CreateCheckpoint());
                var restored = GameReplay.Restore(checkpoint, registry);
                if (!Play(game, slash) || !Play(restored, slash)) break;

                if (!TryActivatePojun(game, out var holdPrompt)) break;
                Require(TryActivatePojun(restored, out var restoredHoldPrompt) &&
                        restoredHoldPrompt!.Value.target == holdPrompt!.Value.target,
                    "The restored Pojun activation must reach the same private hold draft.");

                // Prefer an equipment-zone candidate when present so the return-to-hand
                // rule is exercised against former equipment cards too.
                var candidates = holdPrompt!.Value.choices.Where(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards").ToArray();
                var pick = candidates.FirstOrDefault(choice => choice.Cards.Count > 0) ?? candidates.First();
                if (pick.Cards.Count > 0) equipmentHolds++;

                var answer = Answer(game, pick.Id);
                var restoredAnswer = Answer(restored, pick.Id);
                Require(answer.Accepted && restoredAnswer.Accepted,
                    answer.Error?.Message ?? restoredAnswer.Error?.Message ?? "A Pojun hold pick failed.");

                if (!TryFinishPojunHold(game)) break;
                Require(TryFinishPojunHold(restored), "The restored hold must finish the same way.");

                var placed = game.Events.Skip(eventsBefore).Select(item => item.Payload)
                    .OfType<ProgramHoldCardsPlacedEvent>()
                    .Single(item => item.SkillId == ClassicPojun && item.HolderSeat == target);
                var heldIds = placed.CardIds.ToArray();
                Require(heldIds.Length == 1 &&
                        (pick.Cards.Count == 0 || heldIds[0] == pick.Cards[0]) &&
                        candidateIds.Contains(heldIds[0]) &&
                        game.CreateSnapshot(0, true).Players[target].PojunHoldCount == 1 &&
                        game.CreateCardZoneDiagnostics().Single(zone => zone.CardId == heldIds[0]).Location ==
                            CardLocation.PojunHold(target),
                    "Exactly one chosen card must sit in the target's Pojun hold zone.");
                var heldCardId = heldIds[0];

                Require(State(game) == State(restored) && Events(game).SequenceEqual(Events(restored)),
                    $"The Pojun hold replay diverged at seed {seed}.");

                // A second Slash in the same play phase must re-offer Pojun.
                var secondSlash = game.PendingDecision is { Kind: DecisionKind.PlayCard }
                    ? game.GetHumanLegalActions().FirstOrDefault(item =>
                        item.Kind == LegalActionKind.Slash && item.TargetSeat is not null)
                    : null;
                if (secondSlash is not null)
                {
                    var playedSecond = Play(game, secondSlash);
                    var playedRestoredSecond = Play(restored, secondSlash);
                    if (playedSecond && playedRestoredSecond)
                    {
                        var prompt = game.PendingDecision;
                        Require(prompt is { Kind: DecisionKind.ProgramTrigger } &&
                                prompt.SkillPrompt?.SkillId == ClassicPojun,
                            "Pojun must re-trigger for a later Slash in the same play phase.");
                        Skip(game);
                        Skip(restored);
                        Require(State(game) == State(restored) &&
                                Events(game).SequenceEqual(Events(restored)),
                            $"The re-trigger replay diverged at seed {seed}.");
                    }
                    else break;
                }

                if (!RunToEndOfTurn(game)) break;
                var returned = game.Events.Skip(eventsBefore).Select(item => item.Payload)
                    .OfType<PojunHoldReturnedEvent>()
                    .Single(item => item.HolderSeat == target);
                Require(returned.CardIds.SequenceEqual(heldIds) &&
                        game.CardMovements.Any(move => move.CardId == heldCardId &&
                            move.From == CardLocation.PojunHold(target) &&
                            move.To == CardLocation.Hand(target) &&
                            move.Reason == CardMoveReasons.PojunHoldReturn),
                    "The held card must return to the holder's hand at the end of the turn.");
                Require(game.CreateSnapshot(0, true).Players[target].PojunHoldCount == 0,
                    "The Pojun hold zone must be empty after the return.");
                completed++;
            }
        }
        Require(completed >= 2 && equipmentHolds >= 1,
            $"The hold/return fixtures must cover hand and equipment holds (completed={completed}, equipment={equipmentHolds}).");
    }

    private static bool TryActivatePojun(GameEngine game,
        out (int target, IReadOnlyList<PromptChoice> choices)? holdPrompt)
    {
        holdPrompt = null;
        var prompt = game.PendingDecision;
        if (prompt is not { Kind: DecisionKind.ProgramTrigger } ||
            prompt.SkillPrompt?.SkillId is not (ClassicPojun or BoundaryPojun))
            return false;
        var activate = prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate");
        if (!Answer(game, activate.Id).Accepted) return false;
        var hold = game.PendingDecision;
        if (hold is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } ||
            hold.Choices.All(choice =>
                choice.Parameters.GetValueOrDefault("program-action") != "select-owned-cards"))
            return false; // target had no eligible cards, so the draft cancelled
        holdPrompt = (hold.TargetSeat ?? -1, hold.Choices);
        return true;
    }

    private static bool TryFinishPojunHold(GameEngine game)
    {
        // A pick that meets the required count completes the draft on its own and the
        // engine moves on; otherwise finish the still-suspended private draft explicitly.
        var prompt = game.PendingDecision;
        if (prompt is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } hold &&
            hold.Choices.FirstOrDefault(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards") is { } finish)
            return Answer(game, finish.Id).Accepted;
        return true;
    }

    /// <summary>Advances through intervening turns (answering human prompts) until
    /// Xu Sheng's next play decision or the game blocks.</summary>
    private static bool AdvanceToOwnPlay(GameEngine game)
    {
        for (var step = 0; step < 200; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return true;
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                if (!game.Submit(new AdvanceCommand(game.Revision)).Accepted) return false;
                continue;
            }
            GameCommand command = prompt.Kind switch
            {
                DecisionKind.DiscardCards => new DiscardCardsCommand(0,
                    prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                    prompt.PromptId, game.Revision),
                _ => new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.First().Id, game.Revision)
            };
            if (!game.Submit(command).Accepted) return false;
        }
        return false;
    }

    private static bool Play(GameEngine game, LegalAction action)
    {
        if (game.PendingDecision is not { Kind: DecisionKind.PlayCard } prompt) return false;
        return game.Submit(new PlayCardCommand(0, action.CardId!.Value,
            action.TargetSeats, game.Revision, prompt.PromptId, action.PlayedCardKind)).Accepted;
    }

    private static CommandResult Answer(GameEngine game, ChoiceId choiceId)
    {
        var prompt = game.PendingDecision!;
        return game.Submit(new AnswerPromptCommand(0, prompt.PromptId, choiceId, game.Revision));
    }

    private static void Skip(GameEngine game)
    {
        var prompt = game.PendingDecision!;
        var skip = prompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "skip" ||
            choice.Parameters.GetValueOrDefault("action") == "skip");
        Require(Answer(game, skip.Id).Accepted, "Skipping the Pojun prompt failed.");
    }

    private static bool RunToEndOfTurn(GameEngine game)
    {
        var startTurn = game.CreateSnapshot(0, true).TurnNumber;
        for (var step = 0; step < 80; step++)
        {
            if (game.CreateSnapshot(0, true).TurnNumber > startTurn)
                return true;
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                if (!game.Submit(new AdvanceCommand(game.Revision)).Accepted) return false;
                continue;
            }
            GameCommand command = prompt.Kind switch
            {
                DecisionKind.PlayCard when prompt.PlayerSeat == 0 =>
                    new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId),
                DecisionKind.DiscardCards => new DiscardCardsCommand(0,
                    prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                    prompt.PromptId, game.Revision),
                _ => new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.First().Id, game.Revision)
            };
            if (!game.Submit(command).Accepted) return false;
        }
        return false;
    }

    private static bool RunUntil(GameEngine game, Func<GameSnapshot, bool> predicate,
        Func<bool>? abort = null)
    {
        for (var step = 0; step < 120; step++)
        {
            if (abort is not null && abort()) return false;
            if (predicate(game.CreateSnapshot(0, true))) return true;
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                if (!game.Submit(new AdvanceCommand(game.Revision)).Accepted) return false;
                continue;
            }
            GameCommand command = prompt.Kind switch
            {
                DecisionKind.PlayCard when prompt.PlayerSeat == 0 =>
                    new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId),
                DecisionKind.DiscardCards => new DiscardCardsCommand(0,
                    prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                    prompt.PromptId, game.Revision),
                DecisionKind.RespondSlash when prompt.PlayerSeat == 0 => prompt.Choices
                    .FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("response") == "dodge") is { } dodge
                    ? new AnswerPromptCommand(0, prompt.PromptId, dodge.Id, game.Revision)
                    : new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.First().Id, game.Revision),
                _ => new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.First().Id, game.Revision)
            };
            if (!game.Submit(command).Accepted) return false;
        }
        return false;
    }

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Scenario());

    private static GameEngine Start(ContentRegistry registry, int seed, string generalId,
        string? modeId = null)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed, PlayerCount = 5, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = modeId ?? Scenario.ModeId, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 12
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Xu Sheng fixture did not start.");
        var setup = game.PendingDecision!;
        Require(setup.ValidContentIds.Contains(generalId) &&
                game.Submit(new SelectGeneralCommand(0, generalId, game.Revision, setup.PromptId)).Accepted,
            $"{generalId} was not selectable in the fixture.");
        return game;
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

    private sealed class Scenario : IGameContentPackage
    {
        internal const string ModeId = "identity:xu-sheng-check-5";
        internal const string DeathModeId = "identity:classic-xu-sheng-death";
        public PackageManifest Manifest { get; } = new("xu-sheng-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 144, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            var ids = Enumerable.Range(1, 4).Select(i => $"fixture:xu-sheng-target-{i}").ToArray();
            foreach (var id in ids)
                builder.AddGeneral(new ContentGeneralDefinition(id, "测试目标", "supporter",
                    "standard:none", "qun", BaseHp: 2));
            var frailIds = Enumerable.Range(1, 4).Select(i => $"fixture:xu-sheng-frail-{i}").ToArray();
            foreach (var id in frailIds)
                builder.AddGeneral(new ContentGeneralDefinition(id, "濒死目标", "supporter",
                    "standard:none", "qun", BaseHp: 1));
            var cardIds = new[]
            {
                "standard:slash", "standard:slash", "standard:slash", "standard:slash",
                "standard:slash", "standard:dodge", "standard:dodge", "standard:alcohol",
                "standard:crossbow", "standard:offensive_horse"
            };
            builder.AddDeck(new ContentDeckRecipe("fixture:xu-sheng-deck", "破军测试牌堆", 5, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 160).Select(index =>
                    new ContentDeckPhysicalCard(cardIds[index % cardIds.Length],
                        (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            // No alcohol in this deck so a dying target cannot be rescued.
            var deathCardIds = new[]
            {
                "standard:slash", "standard:slash", "standard:slash", "standard:slash",
                "standard:slash", "standard:dodge", "standard:dodge", "standard:dodge",
                "standard:crossbow", "standard:offensive_horse"
            };
            builder.AddDeck(new ContentDeckRecipe("fixture:xu-sheng-death-deck", "破军死亡测试牌堆", 5, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 160).Select(index =>
                    new ContentDeckPhysicalCard(deathCardIds[index % deathCardIds.Length],
                        (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            var roles = new Dictionary<string, int>
            {
                [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
            };
            builder.AddMode(new ContentModeDefinition(ModeId, "徐盛测试", 5, 5, roles,
                "fixture:xu-sheng-deck", GeneralCandidateCount: 6,
                GeneralPoolIds: [ClassicGeneral, BoundaryGeneral, .. ids]));
            builder.AddMode(new ContentModeDefinition(DeathModeId, "徐盛死亡测试", 5, 5, roles,
                "fixture:xu-sheng-death-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [BoundaryGeneral, .. frailIds]));
        }
    }
}
