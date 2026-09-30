using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhangSongChecks
{
    private const string ClassicGeneral = "classic:zhang-song";
    private const string BoundaryGeneral = "boundary:zhang-song";
    private const string ClassicQiangzhi = "classic:qiangzhi";
    private const string BoundaryQiangzhi = "boundary:qiangzhi";
    private const string ClassicXiantu = "classic:xiantu";
    private const string BoundaryXiantu = "boundary:xiantu";






    public static void EquipmentUsesReplaceAndResumeExactlyOnce()
    {
        foreach (var general in new[] { ClassicGeneral, BoundaryGeneral })
        {
            var skill = general == ClassicGeneral ? ClassicQiangzhi : BoundaryQiangzhi;
            var (game, registry) = FindEquipmentReveal(general, requirePair: true);
            var pair = game.CreateSnapshot(0, true).Players[0].Hand.Where(card => EquipmentCatalog.IsEquipment(card.Kind))
                .GroupBy(card => EquipmentCatalog.Get(card.Kind).Slot).First(group => group.Count() >= 2).Take(2).ToArray();
            for (var i = 0; i < pair.Length; i++)
            {
                Require(AdvanceToPlayDecision(game), "The equipment use must return to the play decision.");
                var action = game.GetHumanLegalActions().First(item => item.Kind == LegalActionKind.Equip && item.CardId == pair[i].Id);
                var before = game.CardMovements.Count;
                Require(Play(game, action), "The equipment use must be legal.");
                Require(game.PendingDecision?.SkillPrompt?.SkillId == skill &&
                        game.CreateSnapshot(0, true).Players[0].Equipment.Any(card => card.Id == pair[i].Id),
                    "A matching equipment use must offer Qiangzhi after the equipment enters its slot.");
                Require(game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>()
                        .Any(frame => frame.Continuation == ProgramCardContinuation.CommittedSimpleCard),
                    "Both current Qiangzhi texts trigger at use commitment, before the use completes.");
                var restored = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
                Require(State(game) == State(restored) && Events(game).SequenceEqual(Events(restored)),
                    "An equipment Qiangzhi prompt must restore exactly.");
                foreach (var branch in new[] { game, restored })
                {
                    var choice = branch.PendingDecision!.Choices.Single(item =>
                        item.Parameters.GetValueOrDefault("program-action") == (i == 0 ? "activate" : "skip"));
                    Require(Answer(branch, choice.Id).Accepted, "An equipment draw or decline must resume.");
                    Require(AdvanceToPlayDecision(branch), "The resumed equipment use must finish.");
                }
                Require(State(game) == State(restored) && Events(game).SequenceEqual(Events(restored)) &&
                        game.CardMovements.Skip(before).Count(move => move.Reason.Value == $"skill-program.{skill}.Draw") == (i == 0 ? 1 : 0),
                    "Equipment Qiangzhi must draw once, or zero after declining, including after restore.");
            }
            Require(game.CreateCardZoneDiagnostics().Single(card => card.CardId == pair[0].Id).Location == CardLocation.DiscardPile &&
                    game.CreateSnapshot(0, true).Players[0].Equipment.Any(card => card.Id == pair[1].Id),
                "Replacing equipment must discard the old physical card and retain the new one.");
        }
    }



    private static (GameEngine Game, ContentRegistry Registry) FindEquipmentReveal(string general, bool requirePair)
    {
        var registry = Registry();
        var skill = general == ClassicGeneral ? ClassicQiangzhi : BoundaryQiangzhi;
        for (var seed = 1; seed <= 512; seed++)
        {
            var game = Start(registry, seed, general);
            if (!AdvanceToOwnPrompt(game, skill, out var prompt)) continue;
            var hand = game.CreateSnapshot(0, true).Players[0].Hand;
            if (requirePair && !hand.Where(card => EquipmentCatalog.IsEquipment(card.Kind))
                    .GroupBy(card => EquipmentCatalog.Get(card.Kind).Slot).Any(group => group.Count() >= 2)) continue;
            Require(Answer(game, prompt.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate").Id).Accepted,
                "The equipment reveal must activate.");
            var target = game.PendingDecision!.Choices.FirstOrDefault(choice => choice.Targets.Count == 1 &&
                game.CreateSnapshot(0, true).Players[choice.Targets[0]].Hand.Any(card => EquipmentCatalog.IsEquipment(card.Kind)));
            if (target is null) continue;
            Require(Answer(game, target.Id).Accepted, "The equipment holder must be selectable.");
            if (general == BoundaryGeneral)
            {
                var equipmentIds = game.CreateSnapshot(0, true).Players[target.Targets[0]].Hand
                    .Where(card => EquipmentCatalog.IsEquipment(card.Kind)).Select(card => card.Id).ToHashSet();
                Require(Answer(game, game.PendingDecision!.Choices.First(choice => choice.Cards.Any(equipmentIds.Contains)).Id).Accepted,
                    "Boundary Qiangzhi must select the exact equipment card.");
            }
            if (!AdvanceToPlayDecision(game) || !BooleanState(game, 0, skill, "shown-equipment")) continue;
            if (!requirePair && !game.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.Slash)) continue;
            return (game, registry);
        }
        throw new InvalidOperationException("No bounded equipment-reveal fixture was found.");
    }








    private static bool BooleanState(GameEngine game, int seat, string skillId, string stateId)
    {
        var states = game.CreateSnapshot(0, true).Players[seat].SkillRuntimeStates;
        return states is not null && states.Where(state => state.SkillId == skillId)
            .SelectMany(state => state.BooleanStates ?? [])
            .Any(state => state.StateId == stateId && state.Value);
    }


    /// <summary>Advances until one of Zhang Song's own play-phase prompts for
    /// <paramref name="skillId"/> is pending, skipping the reveal offer when a
    /// later skill is requested; returns false when the play decision arrives.</summary>
    private static bool AdvanceToOwnPrompt(GameEngine game, string skillId, [NotNullWhen(true)] out PendingDecision? prompt)
    {
        prompt = null;
        for (var step = 0; step < 400; step++)
        {
            var pending = game.PendingDecision;
            if (pending is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } trigger &&
                trigger.SkillPrompt?.SkillId is not null &&
                IsZhangSongSkill(trigger.SkillPrompt.SkillId) &&
                trigger.SkillPrompt.SkillId == skillId)
            {
                prompt = pending;
                return true;
            }
            if (pending is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return false;
            if (pending is null)
            {
                if (game.State.Status == EngineStatus.Completed ||
                    !game.Submit(new AdvanceCommand(game.Revision)).Accepted) return false;
                continue;
            }
            if (!AutoAnswer(game, pending)) return false;
        }
        return false;
    }

    /// <summary>Advances through Zhang Song's own turn (declining his own triggers)
    /// until a Xiantu prompt is pending during another player's play phase.</summary>

    /// <summary>Drives the engine until a program prompt for <paramref name="skillId"/>
    /// is pending, answering intervening follow-up choices; null when it never appears.</summary>

    private static bool IsZhangSongSkill(string skillId) =>
        skillId is ClassicQiangzhi or BoundaryQiangzhi or ClassicXiantu or BoundaryXiantu;

    /// <summary>Drives the engine (AdvanceCommand while idle) until Zhang Song's
    /// play decision is pending; every human answer can leave the engine idle.</summary>
    private static bool AdvanceToPlayDecision(GameEngine game)
    {
        for (var step = 0; step < 200; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return true;
            var pending = game.PendingDecision;
            if (pending is null)
            {
                if (game.State.Status == EngineStatus.Completed ||
                    !game.Submit(new AdvanceCommand(game.Revision)).Accepted) return false;
                continue;
            }
            if (!AutoAnswer(game, pending)) return false;
        }
        return false;
    }

    /// <summary>Answers any prompt that is not the one the caller waits for:
    /// Zhang Song's own program triggers are skipped, his play phase is ended,
    /// discards are paid and other prompts take their first choice.</summary>
    private static bool AutoAnswer(GameEngine game, PendingDecision pending)
    {
        if (pending is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } trigger &&
            trigger.SkillPrompt?.SkillId is not null && IsZhangSongSkill(trigger.SkillPrompt.SkillId))
        {
            var choice = trigger.Choices.FirstOrDefault(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "skip") ?? trigger.Choices.First();
            return Answer(game, choice.Id).Accepted;
        }
        GameCommand command = pending.Kind switch
        {
            DecisionKind.PlayCard when pending.PlayerSeat == 0 =>
                new EndPlayPhaseCommand(0, game.Revision, pending.PromptId),
            DecisionKind.DiscardCards => new DiscardCardsCommand(pending.PlayerSeat,
                pending.ValidCardIds.Take(pending.RequiredCardCount).ToArray(),
                pending.PromptId, game.Revision),
            _ => new AnswerPromptCommand(pending.PlayerSeat, pending.PromptId,
                pending.Choices.First().Id, game.Revision)
        };
        return game.Submit(command).Accepted;
    }

    private static bool RunToEndOfTurn(GameEngine game)
    {
        var startTurn = game.CreateSnapshot(0, true).TurnNumber;
        for (var step = 0; step < 200; step++)
        {
            if (game.CreateSnapshot(0, true).TurnNumber > startTurn) return true;
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                if (game.State.Status == EngineStatus.Completed ||
                    !game.Submit(new AdvanceCommand(game.Revision)).Accepted) return false;
                continue;
            }
            if (prompt is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } trigger &&
                trigger.SkillPrompt?.SkillId is not null && IsZhangSongSkill(trigger.SkillPrompt.SkillId))
            {
                var skip = trigger.Choices.FirstOrDefault(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "skip");
                if (skip is null || !Answer(game, skip.Id).Accepted) return false;
                continue;
            }
            GameCommand command = prompt.Kind switch
            {
                DecisionKind.PlayCard when prompt.PlayerSeat == 0 =>
                    new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId),
                DecisionKind.DiscardCards => new DiscardCardsCommand(prompt.PlayerSeat,
                    prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                    prompt.PromptId, game.Revision),
                _ => new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
                    prompt.Choices.First().Id, game.Revision)
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

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Scenario());

    private static GameEngine Start(ContentRegistry registry, int seed, string generalId)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed, PlayerCount = 5, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Scenario.ModeId, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 12
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Zhang Song fixture did not start.");
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
        internal const string ModeId = "identity:zhang-song-check-5";
        public PackageManifest Manifest { get; } = new("zhang-song-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 145, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            var ids = Enumerable.Range(1, 4).Select(i => $"fixture:zhang-song-target-{i}").ToArray();
            foreach (var id in ids)
                builder.AddGeneral(new ContentGeneralDefinition(id, "测试目标", "supporter",
                    "standard:none", "qun", BaseHp: 5));
            // Every category appears often, no alcohol exists, and the 5-HP
            // targets keep every fixture alive through the Xiantu penalties.
            var cardIds = new[]
            {
                "standard:slash", "standard:dodge", "standard:draw_two", "standard:slash",
                "standard:dodge", "standard:crossbow", "standard:draw_two", "standard:snatch",
                "standard:dismantlement", "standard:peach", "standard:offensive_horse", "standard:dodge"
            };
            builder.AddDeck(new ContentDeckRecipe("fixture:zhang-song-deck", "张松测试牌堆", 5, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 160).Select(index =>
                    new ContentDeckPhysicalCard(cardIds[index % cardIds.Length],
                        (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            var roles = new Dictionary<string, int>
            {
                [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
            };
            builder.AddMode(new ContentModeDefinition(ModeId, "张松测试", 5, 5, roles,
                "fixture:zhang-song-deck", GeneralCandidateCount: 6,
                GeneralPoolIds: [ClassicGeneral, BoundaryGeneral, .. ids]));
        }
    }
}
