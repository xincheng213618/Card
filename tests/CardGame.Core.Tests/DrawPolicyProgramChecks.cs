using CardGame.Content.Standard;
using CardGame.Core;

internal static class DrawPolicyProgramChecks
{

    public static void TurnPoliciesAreTypedIdempotentAndExpireTogether()
    {
        var store = new TurnCardUseEffectStore();
        var source = new CardUseEffectSource(
            "fixture:policy", "branch", 0, "seat-0:fixture:policy");
        var prohibition = store.GrantActionProhibition(
            7, 0, 101, 0, source,
            [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash],
            [CardActionType.Use, CardActionType.Response]);
        var duplicate = store.GrantActionProhibition(
            7, 0, 101, 0, source,
            [CardKind.ThunderSlash, CardKind.Slash, CardKind.FireSlash],
            [CardActionType.Response, CardActionType.Use]);
        var slashLimit = store.GrantRuleModifier(
            7, 0, 101, 1, source,
            SkillRuleQuery.SlashLimit, SkillRuleOperation.Add, 1);
        var distance = store.GrantRuleModifier(
            7, 0, 101, 2, source,
            SkillRuleQuery.SlashDistanceLimit, SkillRuleOperation.Unlimited, 0);
        var target = store.GrantTargetRestriction(
            7, 0, 102, 0, source, SkillProgramCardTargetRestriction.SelfOnly);
        var conversion = store.GrantConversion(
            7, 0, 103, 0, source, "judgment",
            SkillProgramCardColorRelation.OppositeBoundCard,
            boundCardIsRed: true,
            CardKind.Duel);
        var duplicateConversion = store.GrantConversion(
            7, 0, 103, 0, source, "judgment",
            SkillProgramCardColorRelation.OppositeBoundCard,
            boundCardIsRed: true,
            CardKind.Duel);

        Require(prohibition == duplicate && store.ActionProhibitions.Count == 1 &&
                store.IsCardUseForbidden(7, 0, 0, CardKind.Slash, CardActionType.Use) &&
                store.IsCardUseForbidden(7, 0, 0, CardKind.Slash, CardActionType.Response) &&
                !store.IsCardUseForbidden(7, 0, 1, CardKind.Slash, CardActionType.Use) &&
                store.GetRuleModifiers(7, 0, 0, SkillRuleQuery.SlashLimit).SequenceEqual([slashLimit]) &&
                store.GetRuleModifiers(7, 0, 0, SkillRuleQuery.SlashDistanceLimit).SequenceEqual([distance]) &&
                store.HasTargetRestriction(7, 0, 0, SkillProgramCardTargetRestriction.SelfOnly) &&
                conversion == duplicateConversion &&
                store.GetConversions(7, 0, 0, CardKind.Duel, inputIsRed: false)
                    .SequenceEqual([conversion]) &&
                store.GetConversions(7, 0, 0, CardKind.Duel, inputIsRed: true).Count == 0,
            "Turn policies must remain typed, actor-scoped and idempotent.");
        store.AssertInvariants();

        var expired = store.ExpireTurn(7, 0);
        Require(expired.SequenceEqual(new[]
                {
                    prohibition.GrantSequence,
                    slashLimit.GrantSequence,
                    distance.GrantSequence,
                    target.GrantSequence,
                    conversion.GrantSequence
                }.Order()) &&
                !store.IsCardUseForbidden(7, 0, 0, CardKind.Slash, CardActionType.Use) &&
                store.GetRuleModifiers(7, 0, 0, SkillRuleQuery.SlashLimit).Count == 0 &&
                !store.HasTargetRestriction(7, 0, 0, SkillProgramCardTargetRestriction.SelfOnly) &&
                store.GetConversions(7, 0, 0, CardKind.Duel, inputIsRed: false).Count == 0,
            "Every policy granted for one turn must expire in the same deterministic boundary.");
        store.AssertInvariants();
    }

    private static void Reject(string rules, string expected)
    {
        try
        {
            _ = SkillProgramCatalog.Load(rules, Presentation);
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains(expected, StringComparison.Ordinal))
        {
            return;
        }
        throw new InvalidOperationException($"Expected rejection containing '{expected}'.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private const string ActiveRules = """
        {
          "schemaVersion": 62,
          "skills": [{
            "id": "fixture:active-op",
            "revision": 1,
            "minimumRulesVersion": 171,
            "modifiers": [],
            "viewAs": [],
            "activations": [{
              "id": "active",
              "minCards": 0,
              "maxCards": 0,
              "minTargets": 0,
              "maxTargets": 0,
              "targetKind": "anyLiving",
              "usesPerTurn": 1,
              "effects": [{"op":"__OP__","target":"owner","amount":1}]
            }],
            "triggers": [],
            "contributions": [],
            "cardIdentities": []
          }]
        }
        """;

    private const string Presentation = """
        {
          "schemaVersion": 3,
          "skills": {
            "fixture:active-op": {"name":"主动技","description":"验证主动入口操作边界。"}
          }
        }
        """;
}
