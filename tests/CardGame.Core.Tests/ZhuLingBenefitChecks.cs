using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhuLingBenefitChecks
{
    private const string Skill = "ol:zhanyi", Driver = "fixture:zhanyi-benefit-driver", Hp = "fixture:zhanyi-benefit-hp";

    public static void SharedRecoveryAndRuleExpirationKeepLegacyDefaults()
    {
        var store = new TurnCardUseEffectStore();
        var source = new CardUseEffectSource(Skill, "launch", 0, "owner-instance");
        var kinds = new List<CardKind> { CardKind.Peach, CardKind.Alcohol };
        var old = store.GrantRuleModifier(1, 0, 10, 0, source, SkillRuleQuery.CardUseDistanceLimit, SkillRuleOperation.Unlimited, 0, [CardKind.Slash]);
        var distance = store.GrantRuleModifier(1, 0, 11, 0, source, SkillRuleQuery.CardUseDistanceLimit, SkillRuleOperation.Unlimited, 0,
            [CardKind.Slash], expiration: SkillProgramDamageModifierExpiration.NextOwnerTurnStart);
        var current = store.GrantRecoveryModifier(1, 0, 10, 0, source, kinds, 1);
        var next = store.GrantRecoveryModifier(1, 0, 11, 0, source, kinds, 1, SkillProgramDamageModifierExpiration.NextOwnerTurnStart);
        var oldHand = store.GrantHandLimitKindExemption(1, 0, 10, 0, source, [CardKind.DrawTwo]);
        var hand = store.GrantHandLimitKindExemption(1, 0, 11, 0, source, [CardKind.DrawTwo], SkillProgramDamageModifierExpiration.NextOwnerTurnStart);
        kinds.Clear();
        Require(store.GrantRecoveryModifier(1, 0, 11, 0, source, [CardKind.Alcohol, CardKind.Peach], 1,
                SkillProgramDamageModifierExpiration.NextOwnerTurnStart) == next && next.CardKinds.Count == 2,
            "Recovery grants defensively freeze card kinds and have an idempotent frame/instruction key.");
        Require(!JsonSerializer.Serialize(old).Contains("Expiration", StringComparison.Ordinal) && !JsonSerializer.Serialize(oldHand).Contains("Expiration", StringComparison.Ordinal) &&
            JsonSerializer.Serialize(distance).Contains("Expiration", StringComparison.Ordinal),
            "Old unconfigured turn-rule payloads omit the additive default property.");
        Require(store.GetRecoveryModifiers(1, 0, 1, CardKind.Peach).Count == 0 &&
            store.GetRecoveryModifiers(1, 0, 0, CardKind.Slash).Count == 0,
            "The actual user, rather than card provider or damage source, owns recovery bonuses, and Slash cannot recover.");
        Require(store.ExpireRecoveryAndRuleModifiersAtOwnerTurnStart(0, 1).Count == 0,
            "The granting actual turn cannot expire its own next-turn benefits.");
        Require(store.ExpireTurn(1, 0).Order().SequenceEqual(new[] { old.GrantSequence, current.GrantSequence, oldHand.GrantSequence }.Order()) &&
            store.GetRuleModifiers(2, 1, 0, SkillRuleQuery.CardUseDistanceLimit, CardKind.Slash).SequenceEqual([distance]) &&
            store.GetRecoveryModifiers(2, 1, 0, CardKind.Peach).Single().GrantSequence == next.GrantSequence &&
            store.GetHandLimitExemptCardKinds(2, 1, 0).SetEquals([CardKind.DrawTwo]),
            "Default grants expire at the current turn end; explicit next-owner grants stay active during another player's turn.");
        Require(store.ExpireRecoveryAndRuleModifiersAtOwnerTurnStart(1, 2).Count == 0 &&
            store.ExpireRecoveryAndRuleModifiersAtOwnerTurnStart(0, 3).Count == 3 && store.HandLimitKindExemptions.Count == 0,
            "Only the source owner's next actual turn expires next-owner benefits.");
        store.GrantRecoveryModifier(3, 0, 12, 0, source, [CardKind.Peach], 1, SkillProgramDamageModifierExpiration.NextOwnerTurnStart);
        Require(store.ExpireRecoveryAndRuleModifiersOnDeath(0).Count == 1, "Dead owners cannot retain pending recovery benefits.");
        store.AssertInvariants();
        var root = JsonNode.Parse(Read("ol-zhu-ling", "rules"))!;
        var launch = root["skills"]![0]!["triggers"]![0]!["effects"]![0]!.AsObject();
        launch.Remove("recoveryBonus"); launch.Remove("expires"); launch.Remove("expireBooleanStatesAtOwnerTurnStart");
        Require(SkillProgramCatalog.Load(root.ToJsonString(), Read("ol-zhu-ling", "presentation")).Programs[Skill]
            .Triggers[0].Effects[0].CardUseBenefitPolicy is null, "Old launch configuration must retain its original behavior with no inferred opt-in.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static string Read(string name, string suffix)
    { var a = typeof(StandardContentPackage).Assembly; using var reader = new StreamReader(a.GetManifestResourceStream(a.GetManifestResourceNames().Single(n => n.EndsWith(name + "." + suffix + ".json", StringComparison.Ordinal)))!); return reader.ReadToEnd(); }
}
