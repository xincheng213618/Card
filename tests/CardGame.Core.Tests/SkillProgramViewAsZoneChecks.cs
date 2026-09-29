using CardGame.Core;

internal static class SkillProgramViewAsZoneChecks
{
    private const string Rules = """
        {"schemaVersion":62,"skills":[{"id":"test:owned-red-slash","revision":1,
        "minimumRulesVersion": 171,"viewAs":[{"id":"red-owned","sourceZones":["hand","equipment"],
        "inputKinds":[],"inputSuits":["heart","diamond"],"outputKind":"slash",
        "forPlay":true,"forResponse":true}]}]}
        """;
    private const string Presentation = """
        {"schemaVersion":3,"skills":{"test:owned-red-slash":{"name":"红牌当杀","description":"测试来源牌区"}}}
        """;

    public static void DefinitionAndZoneIsolation()
    {
        var program = SkillProgramCatalog.Load(Rules, Presentation).Programs["test:owned-red-slash"];
        Require(program is { RuntimeVersion: SkillProgramCatalog.RuntimeVersion, MinimumRulesVersion: 171 } &&
                program.ViewAs.Single().SourceZones.SequenceEqual(
                    [CardZoneKind.Hand, CardZoneKind.Equipment]),
            "Schema 46 must keep the source-zone contract in the compiled viewAs rule.");
        Reject(Rules.Replace("[\"hand\",\"equipment\"]", "[]", StringComparison.Ordinal),
            "sourceZones");
        Reject(Rules.Replace("[\"hand\",\"equipment\"]", "[\"judgment\"]", StringComparison.Ordinal),
            "sourceZones");
        Reject(Rules.Replace("\"inputKinds\":[]", "\"inputCount\":2,\"inputKinds\":[]",
                StringComparison.Ordinal),
            "sourceZones");
        var categoryRules = Rules.Replace("\"inputKinds\":[]",
            "\"inputKinds\":[],\"inputCategories\":[\"basic\",\"equipment\"]",
            StringComparison.Ordinal);
        Require(SkillProgramCatalog.Load(categoryRules, Presentation)
                .Programs["test:owned-red-slash"].ViewAs.Single().InputCategories
                .SequenceEqual([SkillProgramCardCategory.Basic, SkillProgramCardCategory.Equipment]),
            "A conversion may use a public card-category input filter.");
        Reject(categoryRules.Replace("[\"hand\",\"equipment\"]", "[\"hand\"]",
                StringComparison.Ordinal)
            .Replace("\"inputKinds\":[]", "\"inputCount\":2,\"inputKinds\":[]",
                StringComparison.Ordinal), "inputCategories");
        Reject(Rules.Replace("\"outputKind\":\"slash\"", "\"outputKind\":\"peach\"",
            StringComparison.Ordinal), "forPlay");
        Reject(Rules.Replace("\"outputKind\":\"slash\"", "\"outputKind\":\"fireAttack\"",
                StringComparison.Ordinal)
            .Replace("\"forResponse\":true", "\"forResponse\":false,\"allowChainedInput\":true",
                StringComparison.Ordinal), "allowChainedInput");

        Require(program.ViewAs.Single().InputSuits.SequenceEqual([Suit.Heart, Suit.Diamond]) &&
                program.ViewAs.Single().OutputKind == CardKind.Slash,
            "The compiled conversion must retain its source filter and output kind.");
    }

    private static void Reject(string rules, string expected)
    {
        try
        {
            SkillProgramCatalog.Load(rules, Presentation);
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains(expected, StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException($"Malformed schema-46 viewAs did not reject {expected}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
