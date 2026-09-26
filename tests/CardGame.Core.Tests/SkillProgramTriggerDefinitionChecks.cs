using CardGame.Core;

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
        foreach (var version in Enumerable.Range(1, 57).Append(59))
            AssertReject(CurrentRules.Replace("\"schemaVersion\":58", $"\"schemaVersion\":{version}", StringComparison.Ordinal),
                Presentation, "schemaVersion");
    }

    private static void LoadsCurrentTriggerAndKeepsCollectionsImmutable()
    {
        var program = SkillProgramCatalog.Load(CurrentRules, Presentation).Programs["test:trigger"];
        var trigger = program.Triggers.Single();
        Require(program.RuntimeVersion == "skill-program-v58" && program.MinimumRulesVersion == 168 &&
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
    }

    private static void RejectsInvalidSourcesWindowsEffectsAndUnknownFields()
    {
        AssertReject(CurrentRules.Replace("\"sourceSkillId\":\"test:source\"", "\"sourceSkillId\":\"test:missing\"", StringComparison.Ordinal), Presentation, "unknown skill");
        AssertReject(CurrentRules.Replace("\"sourceViewAsId\":\"respond\"", "\"sourceViewAsId\":\"missing\"", StringComparison.Ordinal), Presentation, "unknown viewAs");
        AssertReject(CurrentRules.Replace("\"cardResponseAccepted\"", "\"retiredWindow\"", StringComparison.Ordinal), Presentation, "retiredWindow");
        AssertReject(CurrentRules.Replace("\"amount\":2,\"condition\"", "\"amount\":21,\"condition\"", StringComparison.Ordinal), Presentation, "between 1 and 20");
        AssertReject(CurrentRules.Replace("\"effects\":[", "\"mystery\":true,\"effects\":[", StringComparison.Ordinal), Presentation, "mystery");
        AssertReject(CurrentRules.Replace("\"draw\"", "\"stealDeck\"", StringComparison.Ordinal), Presentation, "stealDeck");
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
        {"schemaVersion":58,"skills":[
          {"id":"test:source","revision":1,"minimumRulesVersion":168,"viewAs":[{"id":"respond","inputKinds":[],"inputSuits":[],"sourceZones":["hand"],"outputKind":"dodge","forPlay":false,"forResponse":true}]},
          {"id":"test:trigger","revision":1,"minimumRulesVersion":168,"triggers":[{"id":"after-response","window":"cardResponseAccepted","ownerRelation":"conversionSource","sourceSkillId":"test:source","sourceViewAsId":"respond","optional":true,"effects":[
            {"op":"draw","target":"owner","amount":2,"condition":{"kind":"wounded"}},
            {"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"eventTarget"},"zones":["hand"],"count":1,"destination":"ownerHand"}
          ]}]}
        ]}
        """;
}
