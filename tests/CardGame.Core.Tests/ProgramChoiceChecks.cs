using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ProgramChoiceChecks
{
    private const string SkillId = "fixture:conditional-benefit";

    public static void DefinitionsValidateChoicesPaymentsAndPresentationHash()
    {
        var current = Load(CurrentRules, Presentation("摸两张", "回血", "复原"));
        var effects = current.Triggers.Single().Effects;
        var payment = effects.Single(effect => effect.Op == SkillProgramEffectOp.SelectAndMoveOwnedCard);
        var choice = effects.Single(effect => effect.Op == SkillProgramEffectOp.ChooseOption);
        Require(current.RuntimeVersion == "skill-program-v62" && current.MinimumRulesVersion == 171 &&
                payment.CardCategories.SequenceEqual([
                    SkillProgramCardCategory.Trick,
                    SkillProgramCardCategory.Equipment
                ]) && choice.Options.Select(option => option.Id).SequenceEqual(["draw", "recover", "restore"]),
            "Schema 30 must retain the generic payment categories and named conditional options.");

        Reject(BuildRules(Effects.Replace("\"sourceBind\":\"benefit\",\"optionId\":\"draw\"",
                "\"sourceBind\":\"missing\",\"optionId\":\"draw\"", StringComparison.Ordinal)),
            Presentation("摸两张", "回血", "复原"), "unknown choice result or option");
        Reject(BuildRules(Effects.Replace("\"sourceBind\":\"benefit\",\"optionId\":\"draw\"",
                "\"sourceBind\":\"benefit\",\"optionId\":\"missing\"", StringComparison.Ordinal)),
            Presentation("摸两张", "回血", "复原"), "unknown choice result or option");
        Reject(BuildRules(Effects.Replace(ChooseNode, ChooseNode + "," + ChooseNode, StringComparison.Ordinal)),
            Presentation("摸两张", "回血", "复原"), "duplicate result binding");

        var withoutFilter = Load(
            CurrentRules.Replace(",\"cardCategories\":[\"trick\",\"equipment\"]", "", StringComparison.Ordinal),
            Presentation("摸两张", "回血", "复原"));
        Require(withoutFilter.Triggers.Single().Effects
                    .Single(effect => effect.Op == SkillProgramEffectOp.SelectAndMoveOwnedCard)
                    .CardCategories.Count == 0,
            "Omitting cardCategories must retain the unfiltered payment contract.");
        Reject(CurrentRules.Replace("[\"trick\",\"equipment\"]", "[]", StringComparison.Ordinal),
            Presentation("摸两张", "回血", "复原"), "must not be empty");
        Reject(CurrentRules.Replace("\"cardOwnerRef\":{\"kind\":\"owner\"}",
                "\"cardOwnerRef\":{\"kind\":\"actor\"}", StringComparison.Ordinal),
            Presentation("摸两张", "回血", "复原"), "same participant");

        Reject(CurrentRules, Presentation("摸两张", "回血", null), "missing label");
        var relabeled = Load(CurrentRules, Presentation("抽二", "回复一", "解除异常"));
        Require(current.GameplayHash == relabeled.GameplayHash &&
                current.Triggers.Single().Effects.Single(effect => effect.Op == SkillProgramEffectOp.ChooseOption)
                    .Options.Select(option => option.Label).SequenceEqual(["摸两张", "回血", "复原"]) &&
                relabeled.Triggers.Single().Effects.Single(effect => effect.Op == SkillProgramEffectOp.ChooseOption)
                    .Options.Select(option => option.Label).SequenceEqual(["抽二", "回复一", "解除异常"]),
            "Changing presentation-only option labels must not change the gameplay hash.");

        const string topEffects = """
            [{"op":"chooseOption","target":"owner","resultBind":"benefit","options":[{"id":"draw"},{"id":"recover"}]},
             {"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"actor"},
              "zones":["hand"],"count":1,"destination":"drawPileTop","resultBind":"topped","awaitMovementTriggers":true,
              "condition":{"kind":"choiceIs","sourceBind":"benefit","optionId":"draw"}}]
            """;
        var topRules = BuildRules(topEffects).Replace("\"window\":\"turnEnding\",\"subject\":\"owner\"",
            "\"window\":\"cardUseBeforeTargetEffects\",\"ownerRelation\":\"target\",\"cardKinds\":[\"slash\"]", StringComparison.Ordinal);
        var top = Load(topRules, Presentation("置顶", "摸牌", null)).Triggers.Single().Effects.Last();
        Require(top.CardOwnerRef?.Kind == ProgramParticipantRef.Actor &&
            top.ChooserRef?.Kind == ProgramParticipantRef.Owner && top.AwaitMovementTriggers &&
            top.Destination == SkillProgramCardDestination.DrawPileTop,
            "A choice-gated target defender may put one opaque card-user hand card on top through awaited native movement.");
        foreach (var invalid in new[]
        {
            topRules.Replace("\"zones\":[\"hand\"]", "\"zones\":[\"hand\",\"equipment\"]", StringComparison.Ordinal),
            topRules.Replace("\"chooserRef\":{\"kind\":\"owner\"}", "\"chooserRef\":{\"kind\":\"eventSource\"}", StringComparison.Ordinal),
            topRules.Replace("\"cardOwnerRef\":{\"kind\":\"actor\"}", "\"cardOwnerRef\":{\"kind\":\"selectedTarget\"}", StringComparison.Ordinal),
            topRules.Replace(",\"awaitMovementTriggers\":true", "", StringComparison.Ordinal),
            topRules.Replace("\"condition\":{\"kind\":\"choiceIs\",\"sourceBind\":\"benefit\",\"optionId\":\"draw\"}",
                "\"condition\":{\"kind\":\"always\"}", StringComparison.Ordinal)
        }) Reject(invalid, Presentation("置顶", "摸牌", null), "top-deck");
    }

    private static SkillProgram Load(string rules, string presentation) =>
        SkillProgramCatalog.Load(rules, presentation).Programs[SkillId];

    private static void Reject(string rules, string presentation, string expected)
    {
        try { _ = Load(rules, presentation); }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains(expected, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new InvalidOperationException($"Expected invalid generic-choice content to mention '{expected}'.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string BuildRules(string effects) => $$"""
        {"schemaVersion":62,"skills":[{"id":"{{SkillId}}","revision":1,
        "minimumRulesVersion": 171,"modifiers":[],"viewAs":[],"activations":[],
        "triggers":[{"id":"support","window":"turnEnding","subject":"owner","optional":false,
        "priority":0,"usageScope":"turn","usageLimit":1,"effects":{{effects}}}],
        "contributions":[],"cardIdentities":[]}]}
        """;

    private static string Presentation(string draw, string recover, string? restore) =>
        "{\"schemaVersion\":3,\"skills\":{\"" + SkillId +
        "\":{\"name\":\"条件收益\",\"description\":\"公共选择测试\",\"optionLabels\":{" +
        "\"draw\":\"" + draw + "\",\"recover\":\"" + recover + "\"" +
        (restore is null ? "" : ",\"restore\":\"" + restore + "\"") + "}}}}";

    private const string ChooseNode =
        "{\"op\":\"chooseOption\",\"target\":\"selectedTarget\",\"resultBind\":\"benefit\",\"options\":[" +
        "{\"id\":\"draw\",\"condition\":{\"kind\":\"always\"}}," +
        "{\"id\":\"recover\",\"condition\":{\"kind\":\"wounded\"}}," +
        "{\"id\":\"restore\",\"condition\":{\"kind\":\"any\",\"children\":[" +
        "{\"kind\":\"faceDown\"},{\"kind\":\"chained\"}]}}]}";

    private const string Effects =
        "[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLiving\"}," +
        "{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"}," +
        "\"cardOwnerRef\":{\"kind\":\"owner\"},\"zones\":[\"hand\",\"equipment\"],\"count\":1," +
        "\"destination\":\"discardPile\",\"cardCategories\":[\"trick\",\"equipment\"]}," + ChooseNode + "," +
        "{\"op\":\"draw\",\"target\":\"selectedTarget\",\"amount\":2," +
        "\"condition\":{\"kind\":\"choiceIs\",\"sourceBind\":\"benefit\",\"optionId\":\"draw\"}}," +
        "{\"op\":\"recover\",\"target\":\"selectedTarget\",\"amount\":1," +
        "\"condition\":{\"kind\":\"choiceIs\",\"sourceBind\":\"benefit\",\"optionId\":\"recover\"}}," +
        "{\"op\":\"setFaceState\",\"target\":\"selectedTarget\",\"faceDown\":false," +
        "\"condition\":{\"kind\":\"choiceIs\",\"sourceBind\":\"benefit\",\"optionId\":\"restore\"}}," +
        "{\"op\":\"setChainedState\",\"target\":\"selectedTarget\",\"chained\":false," +
        "\"condition\":{\"kind\":\"choiceIs\",\"sourceBind\":\"benefit\",\"optionId\":\"restore\"}}]";

    private static readonly string CurrentRules = BuildRules(Effects);
}
