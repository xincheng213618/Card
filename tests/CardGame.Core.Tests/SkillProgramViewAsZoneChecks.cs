using CardGame.Core;

internal static class SkillProgramViewAsZoneChecks
{
    private const string Rules = """
        {"schemaVersion":58,"skills":[{"id":"test:owned-red-slash","revision":1,
        "minimumRulesVersion":168,"viewAs":[{"id":"red-owned","sourceZones":["hand","equipment"],
        "inputKinds":[],"inputSuits":["heart","diamond"],"outputKind":"slash",
        "forPlay":true,"forResponse":true}]}]}
        """;
    private const string Presentation = """
        {"schemaVersion":3,"skills":{"test:owned-red-slash":{"name":"红牌当杀","description":"测试来源牌区"}}}
        """;

    public static void DefinitionAndZoneIsolation()
    {
        var program = SkillProgramCatalog.Load(Rules, Presentation).Programs["test:owned-red-slash"];
        Require(program is { RuntimeVersion: "skill-program-v58", MinimumRulesVersion: 168 } &&
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

        var context = new PlayerSkillContext(0, 4, 5, 4, TurnPhase.Play);
        var hand = new Card(7101, CardKind.Peach, Suit.Heart, 1);
        var equipment = new Card(7102, CardKind.Crossbow, Suit.Diamond, 2);
        var black = new Card(7103, CardKind.Peach, Suit.Spade, 3);
        var rules = new SkillProgramRules([program], new HashSet<int> { hand.Id, black.Id },
            new HashSet<int> { equipment.Id });
        Require(rules.CanUseAsSlash(context, hand) &&
                rules.CanUseAsSlash(context, equipment) &&
                rules.CanUseAsResponse(context, equipment, CardKind.Slash) &&
                !rules.CanUseAsSlash(context, black),
            "Configured source zones must include owned red hand/equipment, not black cards.");
        var handOnly = new SkillProgramRules([program], new HashSet<int> { hand.Id });
        Require(!handOnly.CanUseAsSlash(context, equipment),
            "An equipment card not owned by the rule context must not acquire a conversion.");
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
