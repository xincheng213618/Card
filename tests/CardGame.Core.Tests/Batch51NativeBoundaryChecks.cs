using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class Batch51NativeBoundaryChecks
{
    public static void LeiluanPolicyKeepsDynamicAdmissionAndLegacyFingerprints()
    {
        var currentJson = MinimalLeiluanRules();
        var admitted = Load(currentJson);
        Require(admitted.ViewAs.Count == 6 && admitted.ViewAs.Select(r => r.OutputKind).ToHashSet().SetEquals(
                [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Dodge, CardKind.Peach, CardKind.Alcohol]) &&
            admitted.ViewAs.All(r => r.LeiluanBasicUse is { StateId: "leiluan" } && r.InputCount == 0 &&
                !r.VariableInputCount && r.UseOnly && r.ExtendedUse &&
                r.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.WoodenOxGrain]) &&
                r.ForResponse == (r.OutputKind == CardKind.Dodge) && r.ForPlay != r.ForResponse),
            "The additive policy admits all six real use directions with dynamic native material admission, not a fixed one-card or zero-material conversion.");

        Reject(currentJson, "wrong state identity", root =>
            Rule(root, "slash")["leiluanBasicUse"]!["stateId"] = "another-state");
        Reject(currentJson, "a fixed positive input count cannot masquerade as dynamic X", root =>
            Rule(root, "slash")["inputCount"] = 1);
        Reject(currentJson, "old tiered conversion cannot stack with dynamic X", root =>
            Rule(root, "slash")["tieredRoundConversion"] = JsonNode.Parse("""{"stateId":"legacy-tier","usageId":"legacy-usage"}"""));
        Reject(currentJson, "old fixed pair round conversion cannot stack with dynamic X", root =>
            Rule(root, "slash")["roundDistinctBasicUse"] = JsonNode.Parse("""{"stateId":"legacy-pair","ledgerId":"legacy-ledger"}"""));
        Reject(currentJson, "an ordinary trick is not a dynamic basic output", root =>
            Rule(root, "slash")["outputKind"] = "dismantlement");
        Reject(currentJson, "played Slash direction is not a true basic Use", root =>
        {
            var rule = Rule(root, "slash");
            rule["forPlay"] = false;
            rule["forResponse"] = true;
        });
        Reject(currentJson, "Dodge cannot acquire a proactive play direction", root =>
        {
            var rule = Rule(root, "dodge");
            rule["forPlay"] = true;
            rule["forResponse"] = false;
        });
        Reject(currentJson, "pure played admission cannot disable use-only classification", root =>
            Rule(root, "slash")["useOnly"] = false);
        Reject(currentJson, "the complete HE and grain source contract cannot shrink", root =>
            Rule(root, "slash")["sourceZones"] = new JsonArray("hand"));
        Reject(currentJson, "a foreign source zone cannot join the material contract", root =>
            Rule(root, "slash")["sourceZones"] = new JsonArray("hand", "equipment", "judgment"));

        // This retained fixed-pair rule predates the new marker. Its canonical
        // bytes deliberately contain no marker, rather than an invented JSON-null
        // policy: Leiluan's schema requires an object when the property is present.
        const string legacyCanonical = """{"id":"fixture:batch51-legacy","revision":1,"viewAs":[{"forPlay":true,"forResponse":false,"id":"pair","inputCount":2,"inputKinds":[],"inputSuits":[],"outputKind":"slash","sourceZones":["hand"]}]}""";
        const string legacyReordered = """
            {
              "viewAs": [{
                "sourceZones": ["hand"], "outputKind": "slash", "inputSuits": [],
                "inputKinds": [], "inputCount": 2, "id": "pair",
                "forResponse": false, "forPlay": true
              }],
              "revision": 1,
              "id": "fixture:batch51-legacy"
            }
            """;
        var legacy = Load(Envelope(JsonNode.Parse(legacyCanonical)!));
        var reordered = Load(Envelope(JsonNode.Parse(legacyReordered)!));
        var expectedLegacyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            SkillProgramCatalog.RuntimeVersion + "\n" + legacyCanonical))).ToLowerInvariant();
        Require(legacy.GameplayHash == expectedLegacyHash && reordered.GameplayHash == expectedLegacyHash &&
            legacy.ViewAs.Single() is { InputCount: 2, VariableInputCount: false, LeiluanBasicUse: null } &&
            reordered.ViewAs.Single().LeiluanBasicUse is null,
            "Omitting the new policy preserves the retained fixed-pair canonical gameplay input and hash, independently of JSON formatting and without injecting a dynamic policy default.");
        var legacyZero = Envelope(JsonNode.Parse(legacyCanonical)!);
        legacyZero["skills"]![0]!["viewAs"]![0]!["inputCount"] = 0;
        RejectPrepared(legacyZero, "an absent marker still rejects zero input in the legacy positive-material grammar");
    }

    private static JsonObject MinimalLeiluanRules()
    {
        using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream(
            "CardGame.Content.Standard.SkillPrograms.ol-kong-shu.rules.json")
            ?? throw new InvalidOperationException("The current Leiluan rules resource is missing.");
        using var reader = new StreamReader(stream);
        var current = JsonNode.Parse(reader.ReadToEnd())!;
        var leiluan = current["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == "ol:leiluan")!;
        // One complete minimal native program, not a general definition snapshot
        // or a dependency on Fuchao's separate RoundEnding implementation.
        return Envelope(leiluan.DeepClone());
    }

    private static JsonObject Envelope(JsonNode skill) => new()
    {
        ["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion,
        ["skills"] = new JsonArray(skill)
    };

    private static JsonObject Rule(JsonObject root, string outputKind) => root["skills"]![0]!["viewAs"]!
        .AsArray().Single(n => n!["outputKind"]!.GetValue<string>() == outputKind)!.AsObject();

    private static SkillProgram Load(JsonObject rules)
    {
        var id = rules["skills"]![0]!["id"]!.GetValue<string>();
        var presentation = JsonSerializer.Serialize(new
        {
            schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
            skills = new Dictionary<string, object>
            {
                [id] = new { name = "Shared native admission", description = "Dynamic policy boundary" }
            }
        });
        return SkillProgramCatalog.Load(rules.ToJsonString(), presentation).Programs[id];
    }

    private static void Reject(JsonObject valid, string boundary, Action<JsonObject> mutate)
    {
        var invalid = valid.DeepClone().AsObject();
        mutate(invalid);
        RejectPrepared(invalid, boundary);
    }

    private static void RejectPrepared(JsonObject invalid, string boundary)
    {
        var rejected = false;
        try { _ = Load(invalid); }
        catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "The loader must reject " + boundary + ".");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
