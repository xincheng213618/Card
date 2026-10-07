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
        // Play ending grants phase insertion (OL Pan Jun guanwei); turn ending
        // still rejects it.
        _ = Load(Rules("fixture:play-phase", [], [Trigger("run", "playEnding",
            """[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]""")]));
        Reject(Rules("fixture:turn-phase", [], [Trigger("run", "turnEnding",
            """[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]""")]),
            "PhaseInsertion");
        foreach (var window in new[] { "selfDyingResponse" })
            Reject(Rules("fixture:nested-judgment-" + window, [], [Trigger("run", window,
                JudgmentEffects)]), "Judgment");
        _ = Load(Rules("fixture:cards-moved-judgment", [], [Trigger("run", "cardsMoved",
            JudgmentEffects)]));
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
    {"schemaVersion":62,"skills":[{"id":"{{id}}","revision":1,"minimumRulesVersion": 171,
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
        schemaVersion = 3,
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
