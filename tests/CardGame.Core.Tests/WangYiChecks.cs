using CardGame.Content.Standard;
using CardGame.Core;

internal static class WangYiChecks
{
    private const int HumanSeat = 1;
    private const string GeneralId = "classic:wang-yi";
    private const string ZhenlieSkillId = "classic:zhenlie";
    private const string MijiSkillId = "classic:miji";

    public static void ContentPromptAndRulesBoundary()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[GeneralId] is
                {
                    FactionId: "wei",
                    BaseHp: 3,
                    Gender: GeneralGender.Female,
                    PortraitKey: "wang_yi",
                    SkillIds: var skillIds
                } && skillIds.SequenceEqual([ZhenlieSkillId, MijiSkillId]),
            "Current Wang Yi must keep the Wei three-HP Zhenlie and Miji card.");

        Require(current.Packages.Any(package =>
                    package.Id == "standard-classic-generals" &&
                    package.Version == StandardClassicGeneralPackage.CurrentVersion) &&
                current.Skills[ZhenlieSkillId] is
                {
                    LegacyKind: null,
                    Program:
                    {
                        RuntimeVersion: "skill-program-v51",
                        MinimumRulesVersion: 161,
                        Triggers.Count: 1
                    } zhenlieProgram,
                    ExecutionForms: SkillExecutionForm.Trigger,
                    ActionForms: SkillActionForm.None
                } &&
                current.Skills[MijiSkillId] is
                {
                    LegacyKind: null,
                    Program:
                    {
                        RuntimeVersion: "skill-program-v49",
                        MinimumRulesVersion: 159,
                        Triggers.Count: 1
                    } program,
                    ExecutionForms: SkillExecutionForm.Trigger,
                    ActionForms: SkillActionForm.None
                } &&
                zhenlieProgram.Triggers.Single() is
                {
                    Id: "nullify-other-card-target",
                    Window: SkillProgramTriggerWindow.CardUseBeforeTargetEffects,
                    OwnerRelation: SkillProgramCardActionOwnerRelation.Target,
                    Optional: true,
                    UsesSharedExecutor: true,
                    Effects.Count: 3
                } zhenlieTrigger &&
                zhenlieTrigger.Condition is
                {
                    Kind: SkillProgramTriggerConditionKind.Not,
                    Children: [{ Kind: SkillProgramTriggerConditionKind.CardActionActorIsOwner }]
                } &&
                zhenlieTrigger.Effects[0].Op == SkillProgramTriggerEffectOp.NullifyCurrentCardEffect &&
                zhenlieTrigger.Effects[1] is
                {
                    Op: SkillProgramTriggerEffectOp.LoseHp,
                    Target: SkillProgramTriggerEffectTarget.Owner,
                    Amount: 1
                } &&
                zhenlieTrigger.Effects[2] is
                {
                    Op: SkillProgramTriggerEffectOp.SelectAndMoveOwnedCard,
                    Destination: SkillProgramCardDestination.DiscardPile,
                    SkipIfNoCards: true
                } &&
                program.Triggers.Single() is
                {
                    Id: "miji-at-turn-end",
                    Window: SkillProgramTriggerWindow.TurnEnding,
                    Optional: true,
                    UsesSharedExecutor: true,
                    Effects.Count: 2
                } trigger &&
                trigger.Effects[0] is
                {
                    Op: SkillProgramTriggerEffectOp.Draw,
                    NumberExpression: SkillProgramNumberExpression.OwnerLostHp,
                    ResultBind: "drawn"
                } &&
                trigger.Effects[1] is
                {
                    Op: SkillProgramTriggerEffectOp.DistributeOwnedCards,
                    NumberExpression: SkillProgramNumberExpression.BoundCardCount,
                    SourceBind: "drawn",
                    TargetKind: SkillProgramTargetKind.OtherLiving,
                    AllowDeclineBeforeFirst: true
                },
            "Current package must preserve schema-51 Zhenlie and schema-49 Miji while versioning the newer Cheng Pu program independently.");

        var fixture = FindFixture(ScenarioPackage.SlashModeId);
        var game = fixture.Game;
        var prompt = RequireProgramPrompt(game, ZhenlieSkillId);
        Require(prompt.IsPrivate &&
                prompt.PlayerSeat == HumanSeat &&
                prompt.SourceSeat is >= 0 &&
                prompt.Choices.Count == 2 &&
                prompt.Choices.Select(choice => choice.Parameters.GetValueOrDefault("program-action"))
                    .Order(StringComparer.Ordinal)
                    .SequenceEqual(["activate", "skip"]),
            "Zhenlie must publish one private public-program activation choice after another player's Slash targets Wang Yi.");

        var before = SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true));
        var restored = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), fixture.Registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(HumanSeat, revealAll: true)) == before &&
                restored.PendingDecision is
                {
                    Kind: DecisionKind.ProgramTrigger,
                    PlayerSeat: HumanSeat,
                    IsPrivate: true,
                    SkillPrompt.SkillId: ZhenlieSkillId
                },
            "A paused Zhenlie activation choice must replay exactly.");

        var revision = game.Revision;
        var forged = game.Submit(new AnswerPromptCommand(
            HumanSeat,
            prompt.PromptId,
            new ChoiceId("zhenlie.forged"),
            game.Revision));
        Require(!forged.Accepted && game.Revision == revision &&
                SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)) == before,
            "A forged Zhenlie answer must be rejected atomically.");

    }

    public static void ZhenlieSlashAndMijiDistributionReplay()
    {
        var fixture = FindFixture(ScenarioPackage.SlashModeId);
        var game = fixture.Game;
        var activation = RequireProgramPrompt(game, ZhenlieSkillId);
        var sourceSeat = activation.SourceSeat ??
            throw new InvalidOperationException("The Zhenlie fixture did not identify the Slash source.");
        var sourceHandBeforeDiscard = game.CreateSnapshot(HumanSeat, revealAll: true)
            .Players[sourceSeat].HandCount;

        AnswerProgram(game, ZhenlieSkillId, "activate");
        var discard = RequireProgramPrompt(game, ZhenlieSkillId);
        var pausedAfterCost = game.CreateSnapshot(HumanSeat, revealAll: true);
        Require(pausedAfterCost.Players[HumanSeat].Hp == 2 &&
                game.ResolutionStack.OfType<CardUseFrame>().Single().IneffectiveTargetSeats?.SequenceEqual([HumanSeat]) == true &&
                discard.IsPrivate &&
                discard.Choices.All(choice =>
                    choice.Parameters.GetValueOrDefault("card-owner-seat") == sourceSeat.ToString()) &&
                discard.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card" &&
                    choice.Parameters.GetValueOrDefault("source-zone") == CardZoneKind.Hand.ToString() &&
                    choice.Cards.Count == 0) &&
                game.Events.Select(item => item.Payload).OfType<ProgramCardEffectNullifiedEvent>().Any(item =>
                    item is { SkillId: ZhenlieSkillId, OwnerSeat: HumanSeat, CardKind: CardKind.Slash } &&
                    item.SourceSeat == sourceSeat) &&
                game.Events.Select(item => item.Payload).OfType<ProgramSkillHpLostEvent>().Any(item =>
                    item is { SkillId: ZhenlieSkillId, TargetSeat: HumanSeat, Amount: 1, RemainingHp: 2 }),
            "Using public-program Zhenlie must nullify only Wang Yi, pay HP, and keep source hand identities opaque.");

        var pausedReplay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), fixture.Registry);
        Require(SnapshotJson.Serialize(pausedReplay.CreateSnapshot(HumanSeat, revealAll: true)) ==
                SnapshotJson.Serialize(pausedAfterCost) &&
                pausedReplay.PendingDecision is
                {
                    Kind: DecisionKind.ProgramTrigger,
                    PlayerSeat: HumanSeat,
                    IsPrivate: true,
                    SkillPrompt.SkillId: ZhenlieSkillId
                } replayDiscard &&
                replayDiscard.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card" &&
                    choice.Parameters.GetValueOrDefault("source-zone") == CardZoneKind.Hand.ToString() &&
                    choice.Cards.Count == 0),
            "A paused post-cost Zhenlie discard must replay without exposing source hand card ids.");

        Answer(game, discard.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card" &&
            choice.Parameters.GetValueOrDefault("source-zone") == CardZoneKind.Hand.ToString()));
        var nullified = game.Events.Select(item => item.Payload).OfType<ProgramCardEffectNullifiedEvent>().Last();
        Require(nullified is
                {
                    OwnerSeat: HumanSeat,
                    SourceSeat: var resolvedSource,
                    SkillId: ZhenlieSkillId,
                    BindingId: "nullify-other-card-target",
                    CardKind: CardKind.Slash
                } && resolvedSource == sourceSeat &&
                game.CreateSnapshot(HumanSeat, revealAll: true).Players[sourceSeat].HandCount ==
                    sourceHandBeforeDiscard - 1 &&
                game.CardMovements.Any(item =>
                    item.From == CardLocation.Hand(sourceSeat) &&
                    item.To == CardLocation.DiscardPile &&
                    item.Reason.Value == "skill-program.classic:zhenlie.SelectAndMoveOwnedCard") &&
                game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Any(item =>
                    item is
                    {
                        SkillId: ZhenlieSkillId,
                        BindingId: "nullify-other-card-target",
                        OwnerSeat: HumanSeat,
                        Activated: true,
                        Completed: true
                    }) &&
                game.Events.Select(item => item.Payload).OfType<CardEffectSkippedEvent>().Any(item =>
                    item.TargetSeat == HumanSeat &&
                    item.CardKind == CardKind.Slash &&
                    item.Reason == CardEffectSkipReason.SkillNullified) &&
                game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                    .All(item => item.TargetSeat != HumanSeat),
            $"Zhenlie must discard one source card and finish the ineffective Slash without damage " +
            $"(nullified={nullified}, sourceHand={sourceHandBeforeDiscard}->" +
            $"{game.CreateSnapshot(HumanSeat, revealAll: true).Players[sourceSeat].HandCount}, " +
            $"skips={game.Events.Select(item => item.Payload).OfType<CardEffectSkippedEvent>().Count()}, " +
            $"damage=[{string.Join(',', game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().Select(item => item.TargetSeat))}]).");

        var resolvedReplay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), fixture.Registry);
        Require(SnapshotJson.Serialize(resolvedReplay.CreateSnapshot(HumanSeat, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)),
            "A completed Zhenlie Slash branch must replay exactly.");

        ReachHumanPlay(game, skipAdditionalZhenlie: true);
        var play = RequirePrompt(game, DecisionKind.PlayCard);
        Require(game.Submit(new EndPlayPhaseCommand(
                HumanSeat,
                game.Revision,
                play.PromptId)).Accepted,
            "The Wang Yi fixture could not end its play phase.");
        ReachMiji(game);
        var miji = RequireMijiPrompt(game);
        Require(miji.IsPrivate &&
                miji.Choices.Select(choice => choice.Parameters.GetValueOrDefault("program-action"))
                    .Order(StringComparer.Ordinal)
                    .SequenceEqual(["activate", "skip"]),
            "An injured Wang Yi must receive one private generic Miji activation choice at the end phase.");

        var mijiPausedReplay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), fixture.Registry);
        Require(mijiPausedReplay.PendingDecision is
                { Kind: DecisionKind.ProgramTrigger, PlayerSeat: HumanSeat, IsPrivate: true,
                    SkillPrompt.SkillId: MijiSkillId } &&
                SnapshotJson.Serialize(mijiPausedReplay.CreateSnapshot(HumanSeat, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)),
            "A paused Miji activation choice must replay exactly.");

        var handBeforeMiji = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].HandCount;
        AnswerProgram(game, MijiSkillId, "activate");
        var gift = RequireMijiPrompt(game);
        Require(gift.IsPrivate &&
                gift.ValidCardIds.Count > 0 &&
                gift.ValidTargetSeats.All(targetSeat => targetSeat != HumanSeat) &&
                gift.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "decline-owned-card-distribution") &&
                gift.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "distribute-owned-card" &&
                    choice.Cards.Count == 1 &&
                    choice.Targets.Count == 1),
            "After drawing one card, Miji must allow either no distribution or an exact hand-card recipient pair.");

        var optionalBranch = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), fixture.Registry);
        AnswerProgram(optionalBranch, MijiSkillId, "decline-owned-card-distribution");
        var optionalHandCount = optionalBranch.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].HandCount;
        var optionalBindings = optionalBranch.Events.Select(item => item.Payload)
            .OfType<ProgramBindingResolvedEvent>().Where(item => item.SkillId == MijiSkillId).ToArray();
        var optionalDistributions = optionalBranch.Events.Select(item => item.Payload)
            .OfType<ProgramOwnedCardDistributedEvent>().Where(item => item.SkillId == MijiSkillId).ToArray();
        Require(optionalHandCount ==
                    handBeforeMiji + 1 &&
                optionalBindings.Any(item =>
                    item is { SkillId: MijiSkillId, BindingId: "miji-at-turn-end", Activated: true, Completed: true }) &&
                optionalDistributions.Length == 0,
            $"Miji distribution must remain wholly optional after the draw " +
            $"(hand={handBeforeMiji}->{optionalHandCount}, " +
            $"bindings=[{string.Join(';', optionalBindings.Select(item => $"{item.BindingId}:{item.Activated}:{item.Completed}"))}], " +
            $"distributed={optionalDistributions.Length}).");

        var selectedGift = gift.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "distribute-owned-card");
        Answer(game, selectedGift);
        var distributed = game.Events.Select(item => item.Payload).OfType<ProgramOwnedCardDistributedEvent>().Last();
        Require(distributed is
                {
                    OwnerSeat: HumanSeat,
                    SkillId: MijiSkillId,
                    DistributionIndex: 1,
                    RequiredCount: 1
                } &&
                distributed.TargetSeat == selectedGift.Targets[0] &&
                distributed.CardId == selectedGift.Cards[0] &&
                game.CardMovements.Count(item =>
                    item.CardId == distributed.CardId &&
                    item.Reason.Value == "skill-program.classic:miji.DistributeOwnedCards") == 2 &&
                game.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>().Any(item =>
                    item is { SkillId: MijiSkillId, BindingId: "miji-at-turn-end", Activated: true, Completed: true }),
            "Once Miji distribution starts, it must give the exact drawn count to another living character.");

        var completedReplay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), fixture.Registry);
        Require(SnapshotJson.Serialize(completedReplay.CreateSnapshot(HumanSeat, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)),
            "The completed Zhenlie and Miji command prefix must replay exactly.");

    }

    public static void ZhenlieNullifiesOnlyItsGroupEffect()
    {
        var fixture = FindFixture(ScenarioPackage.ArrowModeId);
        var game = fixture.Game;
        var prompt = RequireProgramPrompt(game, ZhenlieSkillId);
        Require(prompt.SourceSeat is >= 0,
            "The group-effect fixture must retain the Arrow Barrage source.");
        var resolutionId = game.ResolutionStack.OfType<CardUseFrame>().Single().Id;

        AnswerProgram(game, ZhenlieSkillId, "activate");
        var discard = RequireProgramPrompt(game, ZhenlieSkillId);
        Answer(game, discard.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card" &&
            choice.Parameters.GetValueOrDefault("source-zone") == CardZoneKind.Hand.ToString()));
        AdvanceUntilCardUseFinished(game, resolutionId);

        var damage = game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().ToArray();
        Require(game.Events.Select(item => item.Payload).OfType<ProgramCardEffectNullifiedEvent>().Any(item =>
                    item.OwnerSeat == HumanSeat &&
                    item.SkillId == ZhenlieSkillId &&
                    item.CardKind == CardKind.ArrowBarrage) &&
                game.Events.Select(item => item.Payload).OfType<CardEffectSkippedEvent>().Any(item =>
                    item.TargetSeat == HumanSeat &&
                    item.CardKind == CardKind.ArrowBarrage &&
                    item.Reason == CardEffectSkipReason.SkillNullified) &&
                damage.All(item => item.TargetSeat != HumanSeat) &&
                damage.Any(item => item.TargetSeat is 2 or 3),
            $"Zhenlie must skip only Wang Yi while the same group trick continues for other targets " +
            $"(skips=[{string.Join(';', game.Events.Select(item => item.Payload).OfType<CardEffectSkippedEvent>().Select(item => $"{item.TargetSeat}:{item.CardKind}:{item.Reason}"))}], " +
            $"damage=[{string.Join(';', damage.Select(item => $"{item.SourceSeat}>{item.TargetSeat}:{item.Amount}"))}]).");
    }

    private static void AdvanceUntilCardUseFinished(GameEngine game, long resolutionId)
    {
        for (var step = 0; step < 256; step++)
        {
            if (game.Events.Select(item => item.Payload).OfType<CardUseFinishedEvent>()
                .Any(item => item.ResolutionId == resolutionId))
            {
                return;
            }
            Require(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while resolving the group trick.");
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "The Wang Yi fixture could not finish the active group trick.");
        }
        throw new InvalidOperationException("The active group trick did not finish in bounded steps.");
    }

    private static Fixture FindFixture(string modeId)
    {
        var registry = CreateRegistry();
        for (var seed = 1; seed <= 2_048; seed++)
        {
            var game = CreateGame(registry, seed, modeId);
            if (!TryStartAndSelect(game)) continue;
            if (game.PendingDecision is not
                {
                    Kind: DecisionKind.ProgramTrigger,
                    PlayerSeat: HumanSeat,
                    SourceSeat: >= 0,
                    SkillPrompt.SkillId: ZhenlieSkillId
                })
            {
                continue;
            }

            return new Fixture(game, registry, seed);
        }
        throw new InvalidOperationException($"No bounded Wang Yi fixture reached Zhenlie in mode {modeId}.");
    }

    private static bool TryStartAndSelect(GameEngine game)
    {
        Require(game.Submit(new StartGameCommand()).Accepted,
            "The Wang Yi fixture failed to start.");
        var selection = RequirePrompt(game, DecisionKind.SelectGeneral);
        if (!selection.ValidContentIds.Contains(GeneralId, StringComparer.Ordinal)) return false;
        Require(game.Submit(new SelectGeneralCommand(
                HumanSeat,
                GeneralId,
                game.Revision,
                selection.PromptId)).Accepted,
            "The Wang Yi fixture could not select its formal general.");

        for (var step = 0; step < 512; step++)
        {
            if (game.PendingDecision is
                { PlayerSeat: HumanSeat, Kind: DecisionKind.PlayCard } or
                { PlayerSeat: HumanSeat, Kind: DecisionKind.ProgramTrigger,
                    SkillPrompt.SkillId: ZhenlieSkillId })
            {
                return true;
            }
            Require(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} before Wang Yi was targeted.");
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "The Wang Yi fixture could not reach its first target or play boundary.");
        }
        throw new InvalidOperationException("The Wang Yi fixture did not reach Zhenlie or play in bounded steps.");
    }

    private static void ReachHumanPlay(GameEngine game, bool skipAdditionalZhenlie)
    {
        for (var step = 0; step < 1_024; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            if (game.PendingDecision is
                { Kind: DecisionKind.ProgramTrigger, PlayerSeat: HumanSeat,
                    SkillPrompt.SkillId: ZhenlieSkillId })
            {
                Require(skipAdditionalZhenlie,
                    "The fixture unexpectedly exposed an additional Zhenlie prompt.");
                AnswerProgram(game, ZhenlieSkillId, "skip");
                continue;
            }
            Require(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while returning to play.");
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "The Wang Yi fixture could not advance to human play.");
        }
        throw new InvalidOperationException("The Wang Yi fixture did not reach human play in bounded steps.");
    }

    private static void ReachMiji(GameEngine game)
    {
        for (var step = 0; step < 256; step++)
        {
            if (game.PendingDecision is
                { Kind: DecisionKind.ProgramTrigger, PlayerSeat: HumanSeat, SkillPrompt.SkillId: MijiSkillId }) return;
            Require(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} before Miji.");
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "The Wang Yi fixture could not advance to Miji.");
        }
        throw new InvalidOperationException("The Wang Yi fixture did not reach Miji in bounded steps.");
    }

    private static void AnswerProgram(GameEngine game, string skillId, string action)
    {
        var prompt = RequireProgramPrompt(game, skillId);
        Answer(game, prompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == action));
    }

    private static PendingDecision RequireMijiPrompt(GameEngine game) =>
        RequireProgramPrompt(game, MijiSkillId);

    private static PendingDecision RequireProgramPrompt(GameEngine game, string skillId) =>
        game.PendingDecision is
            { Kind: DecisionKind.ProgramTrigger, PlayerSeat: HumanSeat, SkillPrompt: { } skillPrompt } prompt &&
        skillPrompt.SkillId == skillId
            ? prompt
            : throw new InvalidOperationException(
                $"Expected human generic {skillId} prompt, found " +
                $"{game.PendingDecision?.Kind.ToString() ?? "no prompt"}/" +
                $"{game.PendingDecision?.SkillPrompt?.SkillId ?? "no skill"}.");

    private static void Answer(GameEngine game, DecisionKind kind, string action)
    {
        var prompt = RequirePrompt(game, kind);
        var choice = prompt.Choices.First(candidate =>
            candidate.Parameters.GetValueOrDefault("action") == action);
        Answer(game, choice);
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("There is no Wang Yi prompt to answer.");
        var result = game.Submit(new AnswerPromptCommand(
            HumanSeat,
            prompt.PromptId,
            choice.Id,
            game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "The Wang Yi prompt answer was rejected.");
    }

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { PlayerSeat: HumanSeat } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException(
                $"Expected human {kind}, found {game.PendingDecision?.Kind.ToString() ?? "no prompt"}.");

    private static GameEngine CreateGame(
        ContentRegistry registry,
        int seed,
        string modeId,
        int rulesVersion = GameCheckpoint.CurrentRulesVersion)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 4,
            ModeId = modeId,
            HumanSeat = HumanSeat,
            HumanRole = Role.Rebel,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = modeId == ScenarioPackage.ArrowModeId ? 1 : 2,
            MaxTurns = 40
        }, registry);
        return rulesVersion == GameCheckpoint.CurrentRulesVersion
            ? game
            : GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
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

    private sealed record Fixture(GameEngine Game, ContentRegistry Registry, int Seed);

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string SlashModeId = "identity:classic-wang-yi-slash-test-4";
        public const string ArrowModeId = "identity:classic-wang-yi-arrow-test-4";
        private const string SlashDeckId = "fixture:wang-yi-slash-deck";
        private const string ArrowDeckId = "fixture:wang-yi-arrow-deck";
        private static readonly string[] BlankGeneralIds =
        [
            "fixture:wang-yi-lord",
            "fixture:wang-yi-supporter-a",
            "fixture:wang-yi-supporter-b"
        ];

        public PackageManifest Manifest { get; } = new(
            "wang-yi-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 85, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in BlankGeneralIds)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "贞烈测试目标",
                    "supporter",
                    "standard:none",
                    "shu",
                    BaseHp: 8));
            }

            builder.AddDeck(new ContentDeckRecipe(
                SlashDeckId,
                "王异贞烈杀测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards = Enumerable.Range(0, 160)
                    .Select(index => Physical("standard:slash", index))
                    .ToArray()
            });

            builder.AddDeck(new ContentDeckRecipe(
                ArrowDeckId,
                "王异贞烈万箭测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards = Enumerable.Range(0, 160)
                    .Select(index => Physical("standard:arrow_barrage", index))
                    .ToArray()
            });

            AddMode(SlashModeId, SlashDeckId, "王异贞烈杀测试");
            AddMode(ArrowModeId, ArrowDeckId, "王异贞烈万箭测试");
            return;

            void AddMode(string modeId, string deckId, string displayName) =>
                builder.AddMode(new ContentModeDefinition(
                    modeId,
                    displayName,
                    4,
                    4,
                    new Dictionary<string, int>
                    {
                        [nameof(Role.Lord)] = 1,
                        [nameof(Role.Loyalist)] = 1,
                        [nameof(Role.Rebel)] = 1,
                        [nameof(Role.Renegade)] = 1
                    },
                    deckId,
                    GeneralCandidateCount: 4,
                    GeneralPoolIds: [GeneralId, .. BlankGeneralIds]));

            static ContentDeckPhysicalCard Physical(string cardId, int index) =>
                new(cardId, (Suit)(index % 4), index % 13 + 1);
        }
    }
}
