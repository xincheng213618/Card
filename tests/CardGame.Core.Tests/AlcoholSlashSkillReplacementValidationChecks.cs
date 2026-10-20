using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class AlcoholSlashSkillReplacementValidationChecks
{
    private const string Source = "fixture:permanent-alcohol-source";
    private const string Lost = "fixture:permanent-alcohol-lost";
    private const string Gained = "fixture:permanent-alcohol-gained";

    public static void LoaderAndRegistryRejectInvalidPermanentAlcoholReplacementContracts()
    {
        var valid = Rules();
        var program = Load(valid).Programs[Source];
        Require(program.Triggers.Single().Effects.Single() is
            { Op: SkillProgramEffectOp.ReplaceOwnSkillsAfterAlcoholSlashDamage, Target: SkillProgramEffectTarget.Owner, SourceBind: Gained },
            "The generic forced per-damage source operation must load independently of any character or activity ID.");
        _ = Build();

        RejectEdit("optional", document => Trigger(document)["optional"] = true);
        RejectEdit("wrong window", document => Trigger(document)["window"] = "playEnding");
        RejectEdit("wrong subject", document => Trigger(document)["subject"] = "owner");
        RejectEdit("per-point", document => Trigger(document)["damageOccurrence"] = "perDamagePoint");
        RejectEdit("non-owner", document => Effect(document)["target"] = "selectedTarget");
        RejectEdit("conditional payment", document => Effect(document)["condition"] = new JsonObject { ["kind"] = "wounded" });
        RejectEdit("same lost and granted skill", document => Effect(document)["sourceBind"] = Lost);
        RejectEdit("duplicate loss", document => Effect(document)["skillIds"] = new JsonArray(Lost, Lost));
        RejectEdit("empty loss", document => Effect(document)["skillIds"] = new JsonArray());
        RejectEdit("missing granted skill", document => Effect(document).Remove("sourceBind"));
        RejectEdit("unrelated amount", document => Effect(document)["amount"] = 1);
        RejectEdit("extra effect", document => Trigger(document)["effects"]!.AsArray().Add(
            JsonNode.Parse("""{"op":"draw","target":"owner","amount":1}""")));
        RejectEdit("activation", document =>
        {
            var effect = Effect(document).DeepClone();
            var skill = document["skills"]![0]!.AsObject();
            skill["triggers"] = new JsonArray();
            var activation = JsonNode.Parse("""
                {"id":"run","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,
                 "targetKind":"anyLiving","usesPerTurn":1,"effects":[]}
                """)!.AsObject();
            activation["effects"]!.AsArray().Add(effect);
            skill["activations"] = new JsonArray(activation);
        });

        Reject(() => Build(includeLost: false), "unknown lost skill registry reference");
        Reject(() => Build(includeGained: false), "unknown granted skill registry reference");
        Reject(() => Build(status: SkillImplementationStatus.Partial), "partial granted program");
        Reject(() => Build(status: SkillImplementationStatus.Planned, includeGrantedProgram: false), "planned granted program");
        Reject(() => Build(includeGrantedProgram: false), "granted definition without an executable program");
    }

    private static JsonObject Rules() => JsonNode.Parse($$$"""
        {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
          {"id":"{{{Source}}}","revision":1,"triggers":[
            {"id":"exchange","window":"afterDamageApplied","subject":"damageSource","optional":false,
             "damageOccurrence":"perDamage","priority":0,"condition":{"kind":"always"},"effects":[
              {"op":"replaceOwnSkillsAfterAlcoholSlashDamage","target":"owner","skillIds":["{{{Lost}}}"],
               "sourceBind":"{{{Gained}}}","condition":{"kind":"always"}}]}]},
          {"id":"{{{Lost}}}","revision":1,"cardPolicies":[{"id":"leaf","kind":"outgoingDistanceOne"}]},
          {"id":"{{{Gained}}}","revision":1,"cardPolicies":[{"id":"leaf","kind":"outgoingDistanceOne"}]}
        ]}
        """)!.AsObject();

    private static SkillProgramCatalog Load(JsonObject document) => SkillProgramCatalog.Load(document.ToJsonString(),
        JsonSerializer.Serialize(new
        {
            schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
            skills = new[] { Source, Lost, Gained }.ToDictionary(id => id, _ => new { name = "Exchange", description = "Shared validation fixture" })
        }));
    private static JsonObject Trigger(JsonObject document) => document["skills"]![0]!["triggers"]![0]!.AsObject();
    private static JsonObject Effect(JsonObject document) => Trigger(document)["effects"]![0]!.AsObject();
    private static void RejectEdit(string boundary, Action<JsonObject> edit)
    {
        var document = Rules();
        edit(document);
        Reject(() => Load(document), boundary);
    }
    private static void Reject(Action action, string boundary)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("The permanent Alcohol Slash contract accepted " + boundary + ".");
    }
    private static ContentRegistry Build(bool includeLost = true, bool includeGained = true,
        SkillImplementationStatus status = SkillImplementationStatus.Complete, bool includeGrantedProgram = true) =>
        ContentRegistry.Build(new StandardContentPackage(), new Fixture(includeLost, includeGained, status, includeGrantedProgram));

    private sealed class Fixture(bool includeLost, bool includeGained, SkillImplementationStatus status, bool includeGrantedProgram) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("permanent-alcohol-validation", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = Load(Rules());
            foreach (var id in new[] { Source, Lost, Gained })
            {
                if (id == Lost && !includeLost || id == Gained && !includeGained) continue;
                builder.AddSkill(new ContentSkillDefinition(id, "Exchange", "Generic registry references")
                {
                    Program = id == Gained && !includeGrantedProgram ? null : catalog.Programs[id],
                    ImplementationStatus = id == Gained ? status : SkillImplementationStatus.Complete,
                    PendingImplementation = id == Gained && status != SkillImplementationStatus.Complete ? "validation fixture" : null
                });
            }
        }
    }
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
