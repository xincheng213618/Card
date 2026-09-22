using System.Text.Json;
using CardGame.Core;

internal static class ProgramCompositionContextChecks
{
    private const string Common = """
    [{"op":"draw","target":"owner","amount":1},
     {"op":"setChainedState","target":"owner","chained":true},
     {"op":"recover","target":"owner","amount":1}]
    """;

    public static void SharedWindowsAcceptCommonNodesAndRejectMissingContexts()
    {
        var windows = new[]
        {
            "turnStartBeforeNormalFlow", "drawPhaseStarting", "selfDyingResponse",
            "afterDamageApplied", "playEnding", "turnEnding", "cardsMoved"
        };
        foreach (var window in windows)
            _ = Load(Rules("fixture:common-" + window, [], [Trigger("run", window, Common)]));

        Reject(Rules("fixture:active-draw-plan", [Activation("active",
            """[{"op":"adjustNormalDraw","target":"owner","amount":1}]""")], []), "DrawPlan");
        Reject(Rules("fixture:play-draw-plan", [], [Trigger("run", "playEnding",
            """[{"op":"adjustNormalDraw","target":"owner","amount":1}]""")]), "DrawPlan");
        Reject(Rules("fixture:turn-damage", [], [Trigger("run", "turnEnding",
            """[{"op":"claimDamageCards","target":"owner"}]""")]), "Damage");
        Reject(Rules("fixture:play-phase", [], [Trigger("run", "playEnding",
            """[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]""")]),
            "PhaseInsertion");
        foreach (var window in new[] { "selfDyingResponse", "afterDamageApplied", "cardsMoved" })
            Reject(Rules("fixture:nested-judgment-" + window, [], [Trigger("run", window,
                JudgmentEffects)]), "Judgment");
    }

    public static void TargetSetIsConsumedOnce()
    {
        const string once = """
        [{"op":"selectTargets","target":"owner","targetKind":"otherLivingWithHand","minimumTargets":1,"maximumTargets":2,"targetAiOrder":"hostileThenHandCount"},
         {"op":"takeRandomHandCardFromSelectedTargets","target":"owner","amount":1}]
        """;
        _ = Load(Rules("fixture:targets-once", [], [Trigger("run", "drawPhaseStarting", once)]));
        var twice = once[..^1] + ",{" +
            "\"op\":\"takeRandomHandCardFromSelectedTargets\",\"target\":\"owner\",\"amount\":1}]";
        Reject(Rules("fixture:targets-twice", [], [Trigger("run", "drawPhaseStarting", twice)]),
            "can be consumed only once");
    }

    public static void ActiveStateAndJudgmentReplay()
    {
        const string stateId = "fixture:active-state-context";
        var stateRules = Rules(stateId, [Activation("active", Common)], []);
        var stateRegistry = ProgramCompositionEntryChecks.Registry(stateId, stateRules, Presentation(stateId));
        var state = ProgramCompositionEntryChecks.Start(stateRegistry, stateId);
        ProgramCompositionEntryChecks.ReachPlay(state);
        ProgramCompositionEntryChecks.UseActivation(state, stateId);
        ProgramCompositionEntryChecks.Require(state.CreateSnapshot(0, true).Players.Single(player => player.Seat == 0).IsChained,
            "The active common composition did not set the owner's chained state.");
        var stateReplay = GameReplay.Restore(
            ProgramCompositionEntryChecks.RoundTrip(state.CreateCheckpoint()), stateRegistry);
        RequireParity(state, stateReplay, "active state composition");

        const string judgmentId = "fixture:active-judgment-context";
        var judgmentRules = Rules(judgmentId, [Activation("active", JudgmentEffects)], []);
        var judgmentRegistry = ProgramCompositionEntryChecks.Registry(
            judgmentId, judgmentRules, Presentation(judgmentId));
        var judgment = ProgramCompositionEntryChecks.Start(judgmentRegistry, judgmentId);
        ProgramCompositionEntryChecks.ReachPlay(judgment);
        var beforeHand = judgment.CreateSnapshot(0, true).Players.Single(player => player.Seat == 0).HandCount;
        ProgramCompositionEntryChecks.UseActivation(judgment, judgmentId);
        var replay = GameReplay.Restore(
            ProgramCompositionEntryChecks.RoundTrip(judgment.CreateCheckpoint()), judgmentRegistry);
        ReachPlay(judgment);
        ReachPlay(replay);
        var resolved = judgment.Events.Select(item => item.Payload).OfType<JudgmentResolvedEvent>()
            .Any(item => item.Reason == "fixture.composition-judgment");
        var afterHand = judgment.CreateSnapshot(0, true).Players.Single(player => player.Seat == 0).HandCount;
        ProgramCompositionEntryChecks.Require(resolved && afterHand == beforeHand + 1,
            "The active judgment did not resume and move its final card to the owner's hand.");
        RequireParity(judgment, replay, "active judgment composition");
    }

    public static void PublicAiContextAccountsForReplacementAndExpressions()
    {
        ProgramAiEstimate Estimate(string id, string effects, PlayerSkillContext player,
            ProgramAiPublicContext context)
        {
            var program = Load(Rules(id, [Activation("active", effects)], []));
            return ProgramCompositionAi.Estimate(program.Activations.Single().Effects, player,
                publicContext: context);
        }

        var full = new PlayerSkillContext(0, 4, 4, 0, TurnPhase.Draw, IsOwnTurn: true);
        var replacement = Estimate("fixture:ai-replacement",
            """[{"op":"draw","target":"owner","amount":1}]""", full,
            new ProgramAiPublicContext(3, NormalDrawCount: 2, ReplacesNormalDraw: true));
        ProgramCompositionEntryChecks.Require(replacement.Score == -8 && replacement.Hint.OwnerDraw == 0,
            "Replacement AI did not charge the displaced normal draw before valuing its explicit draw.");

        var wounded = new PlayerSkillContext(0, 1, 4, 0, TurnPhase.Draw, IsOwnTurn: true);
        var faction = Estimate("fixture:ai-factions", """
            [{"op":"draw","target":"owner","numberExpression":"livingFactionCount"},
             {"op":"recoverTo","target":"owner","numberExpression":"livingFactionCount","minimumValue":1,"clampToMaxHp":true}]
            """, wounded, new ProgramAiPublicContext(3));
        ProgramCompositionEntryChecks.Require(
            faction.Hint.OwnerDraw == 3 && faction.Hint.OwnerRecovery == 2 && faction.Score == 60,
            "Public living-faction count did not drive draw and recover-to estimates.");

        var reveal = Estimate("fixture:ai-lost-hp", """
            [{"op":"revealTopCards","target":"owner","numberExpression":"ownerLostHp","resultBind":"lost","visibility":"public"},
             {"op":"moveBoundCards","target":"owner","sourceBind":"lost","destination":"ownerHand"}]
            """, new PlayerSkillContext(0, 2, 4, 0, TurnPhase.Draw, IsOwnTurn: true),
            new ProgramAiPublicContext(3));
        ProgramCompositionEntryChecks.Require(reveal.Hint.OwnerDraw == 2 && reveal.Score == 16,
            "Owner-lost-HP reveal did not estimate its public card count before movement.");
    }

    public static void ActiveTurnRuleModifierGrantsReplaysAndExpires()
    {
        const string id = "fixture:active-turn-rule";
        const string effects = """
        [{"op":"draw","target":"owner","amount":1},
         {"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":1}]
        """;
        var registry = ProgramCompositionEntryChecks.Registry(
            id, Rules(id, [Activation("active", effects)], []), Presentation(id));
        var game = ProgramCompositionEntryChecks.Start(registry, id);
        ProgramCompositionEntryChecks.ReachPlay(game);
        ProgramCompositionEntryChecks.Require(SlashLimit(game) == 1,
            "The fixture did not begin with the normal one-Slash limit.");
        ProgramCompositionEntryChecks.UseActivation(game, id);
        var grant = game.Events.Select(item => item.Payload).OfType<TurnRuleModifierGrantedEvent>().Single();
        ProgramCompositionEntryChecks.Require(
            SlashLimit(game) == 2 && grant.Modifier.Source.BindingId == "active" &&
            grant.Modifier.Query == SkillRuleQuery.SlashLimit && grant.Modifier.Amount == 1,
            "The active composition did not expose its extra Slash allowance through the real rule query.");
        var replay = GameReplay.Restore(
            ProgramCompositionEntryChecks.RoundTrip(game.CreateCheckpoint()), registry);
        RequireParity(game, replay, "active turn-rule modifier");

        ReachPlay(game);
        var play = game.PendingDecision ?? throw new InvalidOperationException("Play prompt missing after grant.");
        var ended = game.Submit(new EndPlayPhaseCommand(0, game.Revision, play.PromptId));
        ProgramCompositionEntryChecks.Require(ended.Accepted, ended.Error?.Message ?? "Could not end granting turn.");
        for (var step = 0; step < 64 && game.Events.Select(item => item.Payload)
                 .OfType<TurnCardUseEffectsExpiredEvent>().All(item =>
                     !item.GrantSequences.Contains(grant.Modifier.GrantSequence)); step++)
        {
            var advanced = game.PendingDecision is { Kind: DecisionKind.DiscardCards, PlayerSeat: 0 } discard
                ? game.Submit(new DiscardCardsCommand(0,
                    discard.ValidCardIds.Take(discard.RequiredCardCount).ToArray(), discard.PromptId, game.Revision))
                : game.Submit(new AdvanceOneStepCommand(game.Revision));
            ProgramCompositionEntryChecks.Require(advanced.Accepted,
                advanced.Error?.Message ?? "Could not reach turn-rule expiry.");
        }
        ProgramCompositionEntryChecks.Require(
            game.Events.Select(item => item.Payload).OfType<TurnCardUseEffectsExpiredEvent>()
                .Any(item => item.GrantSequences.Contains(grant.Modifier.GrantSequence)) && SlashLimit(game) == 1,
            "The active Slash-limit grant did not expire with its granting turn.");
    }

    private static int SlashLimit(GameEngine game)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var players = ((System.Collections.IEnumerable)typeof(GameEngine).GetField("_players", flags)!
            .GetValue(game)!).Cast<CharacterState>().ToArray();
        var evaluation = (RuleQueryEvaluation)typeof(GameEngine)
            .GetMethod("EvaluateSlashUseLimit", flags)!.Invoke(game, [players[0]])!;
        return (evaluation.Value as FiniteRuleQueryValue)?.Value ?? int.MaxValue;
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 256; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return;
            ProgramCompositionEntryChecks.Require(game.PendingDecision?.PlayerSeat != 0,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while resolving judgment.");
            var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
            ProgramCompositionEntryChecks.Require(result.Accepted, result.Error?.Message ?? "Advance failed.");
        }
        throw new InvalidOperationException("Program judgment did not return to Play.");
    }

    private static void RequireParity(GameEngine left, GameEngine right, string scenario) =>
        ProgramCompositionEntryChecks.Require(
            ProgramCompositionEntryChecks.State(left) == ProgramCompositionEntryChecks.State(right) &&
            ProgramCompositionEntryChecks.Events(left).SequenceEqual(ProgramCompositionEntryChecks.Events(right)),
            $"Checkpoint replay diverged for {scenario}.");

    private static SkillProgram Load(string rules)
    {
        using var document = JsonDocument.Parse(rules);
        var id = document.RootElement.GetProperty("skills")[0].GetProperty("id").GetString()!;
        return SkillProgramCatalog.Load(rules, Presentation(id)).Programs[id];
    }

    private static void Reject(string rules, string expected)
    {
        try { _ = Load(rules); }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains(expected, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new InvalidOperationException($"Expected rejection containing '{expected}'.");
    }

    private static string Rules(string id, IReadOnlyList<string> activations, IReadOnlyList<string> triggers) => $$"""
    {"schemaVersion":23,"skills":[{"id":"{{id}}","revision":1,"minimumRulesVersion":128,
    "modifiers":[],"viewAs":[],"activations":[{{string.Join(',', activations)}}],
    "triggers":[{{string.Join(',', triggers)}}],"contributions":[],"cardIdentities":[]}]}
    """;

    private static string Activation(string id, string effects) => $$"""
    {"id":"{{id}}","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,
    "targetKind":"anyLiving","usesPerTurn":1,"effects":{{effects}}}
    """;

    private static string Trigger(string id, string window, string effects)
    {
        var context = window switch
        {
            "cardsMoved" => ",\"sourceZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\"",
            "afterDamageApplied" => ",\"damageOccurrence\":\"perDamage\"",
            _ => string.Empty
        };
        return $$"""
        {"id":"{{id}}","window":"{{window}}","subject":"owner","optional":true,"priority":0{{context}},"effects":{{effects}}}
        """;
    }

    private static string Presentation(string id) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        skills = new Dictionary<string, object>
        {
            [id] = new { name = "上下文组合", description = "测试" }
        }
    });

    private const string JudgmentEffects = """
    [{"op":"startJudgment","target":"owner","judgmentReason":"fixture.composition-judgment","resultBind":"judgment","visibility":"public"},
     {"op":"moveBoundCards","target":"owner","sourceBind":"judgment","destination":"ownerHand"}]
    """;
}
