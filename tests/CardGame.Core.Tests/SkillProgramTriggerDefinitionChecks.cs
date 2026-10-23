using CardGame.Core;
using System.Text.Json.Nodes;

internal static class SkillProgramTriggerDefinitionChecks
{
    internal static void Run()
    {
        RejectsRetiredSchemas();
        LoadsCurrentTriggerAndKeepsCollectionsImmutable();
        RejectsInvalidSourcesWindowsEffectsAndUnknownFields();
    }

    private static void RejectsRetiredSchemas()
    {
        foreach (var version in Enumerable.Range(1, SkillProgramCatalog.RulesSchemaVersion - 1))
            AssertReject(CurrentRules.Replace("\"schemaVersion\":62", $"\"schemaVersion\":{version}", StringComparison.Ordinal),
                Presentation, "schemaVersion");
    }

    private static void LoadsCurrentTriggerAndKeepsCollectionsImmutable()
    {
        var program = SkillProgramCatalog.Load(CurrentRules, Presentation).Programs["test:trigger"];
        var trigger = program.Triggers.Single();
        Require(program.RuntimeVersion == "skill-program-v62" && program.MinimumRulesVersion == 172 &&
                program.Modifiers.Count == 0 && program.ViewAs.Count == 0 && program.Activations.Count == 0,
            "Current schema must support a trigger-only program.");
        Require(trigger.Window == SkillProgramTriggerWindow.CardResponseAccepted &&
                trigger.OwnerRelation == SkillProgramCardActionOwnerRelation.ConversionSource &&
                trigger.SourceSkillId == "test:source" && trigger.SourceViewAsId == "respond" && trigger.Optional &&
                trigger.Effects.Select(effect => effect.Op).SequenceEqual(
                    [SkillProgramEffectOp.Draw, SkillProgramEffectOp.SelectAndMoveOwnedCard]),
            "The common trigger model must retain conversion provenance and typed instructions.");
        RequireThrows<NotSupportedException>(() => ((ICollection<SkillProgramTrigger>)program.Triggers).Clear());
        RequireThrows<NotSupportedException>(() => ((ICollection<SkillProgramEffect>)trigger.Effects).Add(trigger.Effects[0]));
        var completed = JsonNode.Parse(CurrentRules)!;
        completed["skills"]![1]!["triggers"]![0]!["window"] = "cardResponseCompleted";
        Require(SkillProgramCatalog.Load(completed.ToJsonString(), Presentation).Programs["test:trigger"].Triggers.Single()
                is { Window: SkillProgramTriggerWindow.CardResponseCompleted, SourceViewAsId: "respond" },
            "A completed native response retains its actual response conversion source.");
        var playOnly = completed["skills"]![0]!["viewAs"]![0]!;
        playOnly["forResponse"] = false;
        playOnly["forPlay"] = true;
        playOnly["outputKind"] = "slash";
        AssertReject(completed.ToJsonString(), Presentation, "source viewAs does not support window");
    }

    private static void RejectsInvalidSourcesWindowsEffectsAndUnknownFields()
    {
        AssertReject(CurrentRules.Replace("\"sourceSkillId\":\"test:source\"", "\"sourceSkillId\":\"test:missing\"", StringComparison.Ordinal), Presentation, "unknown skill");
        AssertReject(CurrentRules.Replace("\"sourceViewAsId\":\"respond\"", "\"sourceViewAsId\":\"missing\"", StringComparison.Ordinal), Presentation, "unknown viewAs");
        AssertReject(CurrentRules.Replace("\"cardResponseAccepted\"", "\"retiredWindow\"", StringComparison.Ordinal), Presentation, "retiredWindow");
        AssertReject(CurrentRules.Replace("\"amount\":2,\"condition\"", "\"amount\":21,\"condition\"", StringComparison.Ordinal), Presentation, "between 1 and 20");
        AssertReject(CurrentRules.Replace("\"effects\":[", "\"mystery\":true,\"effects\":[", StringComparison.Ordinal), Presentation, "mystery");
        AssertReject(CurrentRules.Replace("\"draw\"", "\"stealDeck\"", StringComparison.Ordinal), Presentation, "stealDeck");
        var ending = JsonNode.Parse(CurrentRules)!;
        ending["skills"]![1]!["triggers"]![0] = JsonNode.Parse("""
            {"id":"ending-kill","window":"turnEnding","subject":"owner","turnOwnerScope":"own","optional":false,
             "condition":{"kind":"ownerKilledThisTurn"},"effects":[{"op":"draw","target":"owner","amount":1}]}
            """);
        Require(SkillProgramCatalog.Load(ending.ToJsonString(), Presentation).Programs["test:trigger"].Triggers.Single()
            .Window == SkillProgramTriggerWindow.TurnEnding, "Existing ending kill-history producers remain valid.");
        ending["skills"]![1]!["triggers"]![0]!["window"] = "afterTurnEnded";
        AssertReject(ending.ToJsonString(), Presentation, "after-turn kill history");
    }

    private static void AssertReject(string rules, string presentation, string expected)
    {
        try { _ = SkillProgramCatalog.Load(rules, presentation); }
        catch (InvalidOperationException exception) when (exception.Message.Contains(expected, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new InvalidOperationException($"Expected rejection containing '{expected}'.");
    }

    private static void RequireThrows<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private const string Presentation = """
        {"schemaVersion":3,"skills":{"test:source":{"name":"Source","description":"test"},"test:trigger":{"name":"Trigger","description":"test"}}}
        """;

    private const string CurrentRules = """
        {"schemaVersion":62,"skills":[
          {"id":"test:source","revision":1,"minimumRulesVersion": 172,"viewAs":[{"id":"respond","inputKinds":[],"inputSuits":[],"sourceZones":["hand"],"outputKind":"dodge","forPlay":false,"forResponse":true}]},
          {"id":"test:trigger","revision":1,"minimumRulesVersion": 172,"triggers":[{"id":"after-response","window":"cardResponseAccepted","ownerRelation":"conversionSource","sourceSkillId":"test:source","sourceViewAsId":"respond","optional":true,"effects":[
            {"op":"draw","target":"owner","amount":2,"condition":{"kind":"wounded"}},
            {"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"eventTarget"},"zones":["hand"],"count":1,"destination":"ownerHand"}
          ]}]}
        ]}
        """;
}
