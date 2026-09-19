using CardGame.Core;

internal static class SkillProgramTriggerDefinitionChecks
{
    internal static void Run()
    {
        LegacyV1HashAndContractRemainStable();
        LoadsTriggerOnlyV2AndKeepsCollectionsImmutable();
        RejectsInvalidSourcesWindowsEffectsAndUnknownFields();
    }

    private static void LegacyV1HashAndContractRemainStable()
    {
        const string rules = """
            {"schemaVersion":1,"skills":[{"id":"test:legacy","revision":1,"modifiers":[{"query":"drawCount","operation":"add","value":1}],"viewAs":[],"activations":[]}]}
            """;
        var program = SkillProgramCatalog.Load(rules, Presentation("test:legacy")).Programs["test:legacy"];
        Require(program.RuntimeVersion == "skill-program-v1" && program.MinimumRulesVersion == 79,
            "Schema 1 must retain its runtime and minimum rules versions.");
        Require(program.GameplayHash == "47c7b901455dd746a7ef25267778ce0805b53db5ba1a19ea70ff5eafc0ab5452",
            "Schema 1 gameplay hash changed.");
        Require(program.Triggers.Count == 0 && SkillProgramCatalog.RuntimeVersion == "skill-program-v1",
            "Schema 1 and the catalog compatibility constant must remain trigger-free v1 contracts.");
        AssertReject(rules.Replace("\"activations\":[]", "\"activations\":[],\"triggers\":[]", StringComparison.Ordinal),
            Presentation("test:legacy"), "triggers");
    }

    private static void LoadsTriggerOnlyV2AndKeepsCollectionsImmutable()
    {
        var catalog = SkillProgramCatalog.Load(ValidV2, Presentation("test:source", "test:trigger"));
        var program = catalog.Programs["test:trigger"];
        var trigger = program.Triggers.Single();
        Require(program.RuntimeVersion == "skill-program-v2" && program.MinimumRulesVersion == 80 &&
                program.Modifiers.Count == 0 && program.ViewAs.Count == 0 && program.Activations.Count == 0,
            "Schema 2 must permit a trigger-only program and default omitted legacy arrays to empty.");
        Require(trigger.Window == SkillProgramTriggerWindow.CardResponseAccepted &&
                trigger.SourceSkillId == "test:source" && trigger.SourceViewAsId is null && trigger.Optional &&
                trigger.Effects.Select(effect => effect.Op).SequenceEqual(
                    [SkillProgramTriggerEffectOp.Draw, SkillProgramTriggerEffectOp.ObtainOpponentHandCard]),
            "Schema 2 trigger fields were not retained as typed definitions.");
        RequireThrows<NotSupportedException>(() =>
            ((ICollection<SkillProgramTrigger>)program.Triggers).Clear());
        RequireThrows<NotSupportedException>(() =>
            ((ICollection<SkillProgramTriggerEffect>)trigger.Effects).Add(trigger.Effects[0]));
    }

    private static void RejectsInvalidSourcesWindowsEffectsAndUnknownFields()
    {
        var presentation = Presentation("test:source", "test:trigger");
        AssertReject(ValidV2.Replace("\"sourceSkillId\":\"test:source\"", "\"sourceSkillId\":\"test:missing\"", StringComparison.Ordinal),
            presentation, "unknown skill");
        AssertReject(ValidV2.Replace("\"sourceViewAsId\":null", "\"sourceViewAsId\":\"missing\"", StringComparison.Ordinal),
            presentation, "unknown viewAs");
        AssertReject(ValidV2.Replace("\"cardResponseAccepted\"", "\"cardUseTargetsFinalized\"", StringComparison.Ordinal),
            presentation, "does not support window");
        AssertReject(ValidV2.Replace("\"amount\":2,\"condition\"", "\"amount\":21,\"condition\"", StringComparison.Ordinal),
            presentation, "between 1 and 20");
        AssertReject(ValidV2.Replace("\"obtainOpponentHandCard\",\"target\":\"owner\",\"amount\":1",
                "\"obtainOpponentHandCard\",\"target\":\"opponent\",\"amount\":1", StringComparison.Ordinal),
            presentation, "requires amount 1 and target owner");
        AssertReject(ValidV2.Replace("\"effects\":[", "\"mystery\":true,\"effects\":[", StringComparison.Ordinal),
            presentation, "mystery");
        AssertReject(ValidV2.Replace("\"draw\"", "\"stealDeck\"", StringComparison.Ordinal),
            presentation, "stealDeck");
    }

    private static string Presentation(params string[] ids) =>
        "{\"schemaVersion\":1,\"skills\":{" + string.Join(',', ids.Select(id =>
            $"\"{id}\":{{\"name\":\"{id}\",\"description\":\"test\"}}")) + "}}";

    private static void AssertReject(string rules, string presentation, string expected)
    {
        try
        {
            _ = SkillProgramCatalog.Load(rules, presentation);
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains(expected, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
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

    private const string ValidV2 = """
        {"schemaVersion":2,"skills":[
          {"id":"test:source","revision":1,"viewAs":[{"id":"respond","inputKinds":[],"inputSuits":[],"outputKind":"dodge","forPlay":false,"forResponse":true}]},
          {"id":"test:trigger","revision":1,"triggers":[{"id":"after-response","window":"cardResponseAccepted","sourceSkillId":"test:source","sourceViewAsId":null,"optional":true,"effects":[
            {"op":"draw","target":"owner","amount":2,"condition":{"kind":"wounded"}},
            {"op":"obtainOpponentHandCard","target":"owner","amount":1}
          ]}]}
        ]}
        """;
}
