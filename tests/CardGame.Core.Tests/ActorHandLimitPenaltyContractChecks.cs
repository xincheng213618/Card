using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;

internal static class ActorHandLimitPenaltyContractChecks
{
    public static void RejectsMalformedContracts()
    {
        const string skillId = "fixture:actor-hand-limit-contract";
        var valid = new JsonObject
        {
            ["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion,
            ["skills"] = new JsonArray(JsonNode.Parse("""
                {"id":"fixture:actor-hand-limit-contract","revision":1,"triggers":[
                  {"id":"penalize-designating-actor","window":"cardUseTargetsFinalized",
                   "ownerRelation":"target","singleActionInstance":true,"onlyDesignatedCardTargets":true,
                   "optional":false,"cardKinds":["slash"],"effects":[
                     {"op":"grantActorHandLimitPenalty","target":"owner","amount":1,"condition":{"kind":"always"}},
                     {"op":"draw","target":"owner","amount":1,"condition":{"kind":"always"}}
                   ]}
                ]}
                """))
        };
        var presentation = JsonSerializer.Serialize(new
        {
            schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
            skills = new Dictionary<string, object>
            {
                [skillId] = new { name = "用牌者手牌上限罚额", description = "真实指定目标后签发罚额并继续原生摸牌。" }
            }
        });

        JsonObject Skill(JsonObject rules) => rules["skills"]![0]!.AsObject();
        JsonObject Trigger(JsonObject rules) => Skill(rules)["triggers"]![0]!.AsObject();
        JsonArray Effects(JsonObject rules) => Trigger(rules)["effects"]!.AsArray();
        JsonObject Penalty(JsonObject rules) => Effects(rules)[0]!.AsObject();
        SkillProgramCatalog Load(JsonObject rules) => SkillProgramCatalog.Load(rules.ToJsonString(), presentation);
        void Reject(JsonObject rules, string name)
        {
            try { _ = Load(rules); }
            catch (InvalidOperationException) { return; }
            throw new InvalidOperationException($"The real actor hand-limit penalty loader accepted an invalid {name} contract.");
        }

        var accepted = Load(valid).Programs[skillId].Triggers.Single();
        if (accepted.Effects is not
            [{ Op: SkillProgramEffectOp.GrantActorHandLimitPenalty, Amount: 1 }, { Op: SkillProgramEffectOp.Draw, Amount: 1 }])
            throw new InvalidOperationException("A valid mandatory target penalty must retain its native Draw tail instead of being restricted to a single effect.");
        var positive = (JsonObject)valid.DeepClone();
        Penalty(positive)["amount"] = 3;
        Effects(positive).RemoveAt(1);
        if (Load(positive).Programs[skillId].Triggers.Single().Effects.Single().Amount != 3)
            throw new InvalidOperationException("A positive configured penalty amount must be retained by the real loader.");

        var categoryOnly = (JsonObject)valid.DeepClone();
        Trigger(categoryOnly).Remove("cardKinds");
        Trigger(categoryOnly)["cardCategories"] = new JsonArray("instantTrick");
        Trigger(categoryOnly)["condition"] = JsonNode.Parse("""{"kind":"cardActionCardIsBlack"}""");
        var categoryAccepted = Load(categoryOnly).Programs[skillId].Triggers.Single();
        if (categoryAccepted.CardKinds.Count != 0 || categoryAccepted.CardCategories is not [SkillProgramCardCategory.InstantTrick] ||
            categoryAccepted.Condition.Kind != SkillProgramTriggerConditionKind.CardActionCardIsBlack ||
            categoryAccepted.Effects.Count != 2)
            throw new InvalidOperationException("A black ordinary-trick category filter must load without requiring literal card kinds or dropping the native Draw tail.");
        var wrongCategoryRelation = (JsonObject)categoryOnly.DeepClone();
        Trigger(wrongCategoryRelation)["ownerRelation"] = "observer";
        Reject(wrongCategoryRelation, "category-only observer relation");

        var cases = new (string Name, Action<JsonObject> Change)[]
        {
            ("activation", rules =>
            {
                var effect = Penalty(rules).DeepClone();
                Skill(rules)["triggers"] = new JsonArray();
                Skill(rules)["activations"] = new JsonArray(new JsonObject
                {
                    ["id"] = "invalid-activation", ["minCards"] = 0, ["maxCards"] = 0,
                    ["minTargets"] = 0, ["maxTargets"] = 0, ["targetKind"] = "anyLiving",
                    ["usesPerTurn"] = null, ["effects"] = new JsonArray(effect)
                });
            }),
            ("wrong window", rules => Trigger(rules)["window"] = "cardUseCommitted"),
            ("actor relation", rules => Trigger(rules)["ownerRelation"] = "actor"),
            ("observer relation", rules => Trigger(rules)["ownerRelation"] = "observer"),
            ("optional", rules => Trigger(rules)["optional"] = true),
            ("missing single-action flag", rules => Trigger(rules).Remove("singleActionInstance")),
            ("false single-action flag", rules => Trigger(rules)["singleActionInstance"] = false),
            ("missing designated-target flag", rules => Trigger(rules).Remove("onlyDesignatedCardTargets")),
            ("non-designated targets", rules => Trigger(rules)["onlyDesignatedCardTargets"] = false),
            ("response uses", rules => Trigger(rules)["includeResponseUses"] = true),
            ("usage quota", rules =>
            {
                Trigger(rules)["usageScope"] = "turn";
                Trigger(rules)["usageLimit"] = 1;
            }),
            ("resolution-time condition", rules => Trigger(rules)["evaluateConditionAtResolution"] = true),
            ("zero amount", rules => Penalty(rules)["amount"] = 0),
            ("negative amount", rules => Penalty(rules)["amount"] = -1),
            ("non-owner effect", rules => Penalty(rules)["target"] = "actor"),
            ("conditional penalty", rules => Penalty(rules)["condition"] = JsonNode.Parse("""{"kind":"hpAtLeast","value":1}""")),
            ("duplicate penalty", rules => Effects(rules).Add(Penalty(rules).DeepClone()))
        };
        foreach (var (name, change) in cases)
        {
            var invalid = (JsonObject)valid.DeepClone();
            change(invalid);
            Reject(invalid, name);
        }
    }
}
