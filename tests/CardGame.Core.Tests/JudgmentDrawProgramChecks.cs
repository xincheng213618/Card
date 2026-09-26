using CardGame.Content.Standard;
using CardGame.Core;

internal static class JudgmentDrawProgramChecks
{
    private const string SkillId = "classic:shuangxiong";
    private const string GeneralId = "classic:yan-liang-wen-chou";

    public static void DefinitionsAndVersionBoundary()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        var historical = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 106, 0));
        var skill = current.Skills[SkillId];
        var trigger = skill.Program?.Triggers.Single();

        Require(GameCheckpoint.CurrentRulesVersion >= 126 &&
                StandardClassicGeneralPackage.CurrentVersion >= new Version(1, 107, 0) &&
                skill is
                {
                    LegacyKind: null,
                    Program.UsesCompositionKernel: true,
                    Program.MinimumRulesVersion: 128
                } &&
                skill.ExecutionForms == (SkillExecutionForm.State | SkillExecutionForm.Trigger) &&
                trigger is
                {
                    Window: SkillProgramTriggerWindow.DrawPhaseStarting,
                    Optional: true,
                    DrawPhaseMode: SkillProgramDrawPhaseMode.Replacement,
                    Effects:
                    [
                        {
                            Op: SkillProgramTriggerEffectOp.StartJudgment,
                            JudgmentReason: JudgmentReasons.Shuangxiong,
                            ResultBind: "judgment",
                            Visibility: SkillProgramCardSetVisibility.Public
                        },
                        {
                            Op: SkillProgramTriggerEffectOp.GrantTurnCardConversion,
                            SourceBind: "judgment",
                            ColorRelation: SkillProgramCardColorRelation.OppositeBoundCard,
                            OutputKind: CardKind.Duel
                        },
                        {
                            Op: SkillProgramTriggerEffectOp.MoveBoundCards,
                            SourceBind: "judgment",
                            Destination: SkillProgramCardDestination.OwnerHand
                        }
                    ]
                } &&
                historical.Skills[SkillId] is
                {
                    LegacyKind: SkillKind.Shuangxiong,
                    Program: null
                },
            "Schema 21 must publish program Shuangxiong at package 1.107 without mutating 1.106.");

        Reject(
            Rules.Replace("\"schemaVersion\":21", "\"schemaVersion\":20", StringComparison.Ordinal)
                .Replace("\"minimumRulesVersion\":126", "\"minimumRulesVersion\":125", StringComparison.Ordinal),
            "requires schema 21");
        Reject(
            Rules.Replace("\"sourceBind\":\"judgment\"", "\"sourceBind\":\"missing\"", StringComparison.Ordinal),
            "unknown conversion");
    }

    public static void JudgmentBindingConversionAndReplay()
    {
        var fixture = FindFixture();
        var game = fixture.Game;
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Program Shuangxiong lost its activation prompt.");
        var activate = prompt.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate" &&
            choice.Parameters.GetValueOrDefault("skill-id") == SkillId);
        var accepted = game.Submit(new AnswerPromptCommand(
            0, prompt.PromptId, activate.Id, game.Revision));
        Require(accepted.Accepted, accepted.Error?.Message ?? "Program Shuangxiong was rejected.");
        ReachHumanPlay(game);

        var judgment = game.Events.Select(item => item.Payload)
            .OfType<JudgmentResolvedEvent>()
            .Last(item => item.Reason == JudgmentReasons.Shuangxiong);
        var grant = game.Events.Select(item => item.Payload)
            .OfType<CardConversionGrantedEvent>()
            .Single(item => item.Conversion.Source.SkillId == SkillId);
        var binding = game.Events.Select(item => item.Payload)
            .OfType<ProgramBindingResolvedEvent>()
            .Single(item => item.SkillId == SkillId && item.Activated);
        var snapshot = game.CreateSnapshot(0, revealAll: true);
        var hand = snapshot.Players.Single(player => player.Seat == 0).Hand.ToDictionary(card => card.Id);
        var judgmentCardId = judgment.CardId ??
            throw new InvalidOperationException("Program Shuangxiong judgment did not reveal a card.");
        var judgmentIsRed = judgment.Suit is Suit.Heart or Suit.Diamond;
        var actions = game.GetHumanLegalActions()
            .Where(action => action.Kind == LegalActionKind.Duel &&
                             action.PlayedCardKind == CardKind.Duel &&
                             action.ConversionSource?.SkillId == SkillId)
            .ToArray();

        Require(binding.Completed &&
                grant.Conversion is
                {
                    ColorRelation: SkillProgramCardColorRelation.OppositeBoundCard,
                    OutputKind: CardKind.Duel
                } &&
                grant.Conversion.BoundCardIsRed == judgmentIsRed &&
                hand.ContainsKey(judgmentCardId) &&
                actions.Length > 0 &&
                actions.All(action => action.CardId is { } id && hand.TryGetValue(id, out var card) &&
                    (card.Suit is Suit.Heart or Suit.Diamond) != judgmentIsRed &&
                    action.ConversionSource?.BindingId == "judgment-replacement.turn-1"),
            $"Program Shuangxiong must bind and gain the final judgment card, then expose only opposite-color Duels. " +
            $"binding={binding.Completed}, boundRed={grant.Conversion.BoundCardIsRed}, judgmentRed={judgmentIsRed}, " +
            $"judgmentInHand={hand.ContainsKey(judgmentCardId)}, actions={actions.Length}, " +
            $"actionCards=[{string.Join(',', actions.Select(action => action.CardId))}], " +
            $"bindings=[{string.Join(',', actions.Select(action => action.ConversionSource?.BindingId))}]");

        var restored = GameReplay.Restore(game.CreateCheckpoint(), fixture.Registry);
        var restoredActions = restored.GetHumanLegalActions()
            .Where(action => action.Kind == LegalActionKind.Duel &&
                             action.PlayedCardKind == CardKind.Duel &&
                             action.ConversionSource?.SkillId == SkillId)
            .ToArray();
        Require(restoredActions.Length == actions.Length &&
                restoredActions.Select(ActionIdentity).SequenceEqual(actions.Select(ActionIdentity)),
            "Program Shuangxiong turn conversions must replay exactly.");

        var chosen = restoredActions[0];
        var played = restored.Submit(new PlayCardCommand(
            0,
            chosen.CardId!.Value,
            chosen.TargetSeats,
            restored.Revision,
            restored.PendingDecision!.PromptId,
            chosen.PlayedCardKind)
        {
            ConversionSource = chosen.ConversionSource
        });
        Require(played.Accepted && restored.Events.Select(item => item.Payload)
                .OfType<CardUseDeclaredEvent>()
                .Any(item => item.CardId == chosen.CardId &&
                             item.CardKind == CardKind.Duel),
            played.Error?.Message ?? "The exact Shuangxiong Duel conversion was not accepted.");

        var replayed = GameReplay.Restore(restored.CreateCheckpoint(), fixture.Registry);
        Require(SnapshotJson.Serialize(replayed.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)),
            "A used Shuangxiong conversion must preserve checkpoint replay parity.");
    }

    private static string ActionIdentity(LegalAction action) =>
        $"{action.CardId}:{string.Join(',', action.TargetSeats)}:{action.ConversionSource}";

    private static Fixture FindFixture()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        for (var seed = 1; seed <= 16_384; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                PlayerCount = 5,
                ModeId = "identity:classic-5",
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 80
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted) continue;
            var selection = game.PendingDecision;
            if (selection is not { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } ||
                !selection.ValidContentIds.Contains(GeneralId)) continue;
            if (!game.Submit(new SelectGeneralCommand(
                    0, GeneralId, game.Revision, selection.PromptId)).Accepted) continue;
            if (!ReachShuangxiongPrompt(game)) continue;

            // Preserve the pre-activation prompt for the actual assertion, but probe a
            // replay-restored copy so the bounded seed search guarantees an opposite-
            // color hand card after the otherwise random judgment.
            var probe = GameReplay.Restore(game.CreateCheckpoint(), registry);
            var prompt = probe.PendingDecision!;
            var activate = prompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "activate" &&
                choice.Parameters.GetValueOrDefault("skill-id") == SkillId);
            if (!probe.Submit(new AnswerPromptCommand(
                    0, prompt.PromptId, activate.Id, probe.Revision)).Accepted) continue;
            ReachHumanPlay(probe);
            if (probe.GetHumanLegalActions().Any(action =>
                    action.PlayedCardKind == CardKind.Duel &&
                    action.ConversionSource?.SkillId == SkillId))
            {
                return new Fixture(game, registry);
            }
        }
        throw new InvalidOperationException(
            "No bounded current Shuangxiong fixture exposed a convertible hand card.");
    }

    private static bool ReachShuangxiongPrompt(GameEngine game)
    {
        for (var step = 0; step < 256; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } prompt &&
                prompt.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("skill-id") == SkillId)) return true;
            if (game.PendingDecision?.PlayerSeat == 0) return false;
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            if (!advanced.Accepted) return false;
        }
        return false;
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 512; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return;
            Require(game.PendingDecision?.PlayerSeat != 0,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while resolving program Shuangxiong.");
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted,
                advanced.Error?.Message ?? "Program Shuangxiong could not advance to play.");
        }
        throw new InvalidOperationException("Program Shuangxiong did not reach Play in bounded steps.");
    }

    private static void Reject(string rules, string expected)
    {
        try
        {
            _ = SkillProgramCatalog.Load(rules, Presentation);
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains(expected, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        throw new InvalidOperationException($"Expected rejection containing '{expected}'.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record Fixture(GameEngine Game, ContentRegistry Registry);

    private const string Rules = """
        {"schemaVersion":21,"skills":[{
          "id":"fixture:judgment-draw","revision":1,"minimumRulesVersion":126,
          "modifiers":[],"viewAs":[],"activations":[],"triggers":[{
            "id":"replace","window":"drawPhaseStarting","subject":"owner","optional":true,
            "drawPhaseMode":"replacement","effects":[
              {"op":"startJudgment","target":"owner","judgmentReason":"fixture.judgment","resultBind":"judgment","visibility":"public"},
              {"op":"grantTurnCardConversion","target":"owner","sourceBind":"judgment","colorRelation":"oppositeBoundCard","outputKind":"duel"},
              {"op":"moveBoundCards","target":"owner","sourceBind":"judgment","destination":"ownerHand"}
            ]}],"contributions":[],"cardIdentities":[]
        }]}
        """;

    private const string Presentation = """
        {"schemaVersion":1,"skills":{
          "fixture:judgment-draw":{"name":"判定摸牌","description":"测试判定结果绑定。"}
        }}
        """;
}
