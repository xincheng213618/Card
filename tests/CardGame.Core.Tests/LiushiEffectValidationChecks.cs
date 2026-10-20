using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;

internal static class LiushiEffectValidationChecks
{
    private const string Source = "fixture:liushi-validation";
    private const string Clear = "fixture:liushi-clear-validation";

    public static void RejectsUnpaidForeignAndWrongWindowDefinitions()
    {
        var valid = Load(Rules());
        if (valid.Programs[Source].Activations.Single().Effects.Count != 3 ||
            valid.Programs[Source].Triggers.Single().Effects.Single().Op != SkillProgramEffectOp.ApplyLiushiEffect ||
            valid.Programs[Clear].Triggers.Single().Effects.Last().Op != SkillProgramEffectOp.ClearLiushiEffect)
            throw new InvalidOperationException("The paid activation, exact conversion-source damage and discard clear contracts must load together.");

        Reject("empty topdeck cost", d => ActivationEffect(d, 1)["exceptBind"] = "cost");
        Reject("foreign movement target", d => ActivationEffect(d, 1)["target"] = "selectedTarget");
        Reject("foreign movement participant", d => ActivationEffect(d, 1)["targetRef"] = JsonNode.Parse("""{"kind":"eventTarget"}"""));
        Reject("non-selected virtual Slash", d => ActivationEffect(d, 2)["target"] = "owner");
        Reject("another skill's conversion source", d => Trigger(d, 0)["sourceSkillId"] = Clear);
        Reject("another activation binding", d => Trigger(d, 0)["sourceViewAsId"] = "other-activation");
        Reject("wrong damage window", d => Trigger(d, 0)["window"] = "cardUseCompleted");
        Reject("optional damage payment", d => Trigger(d, 0)["optional"] = true);
        Reject("per-point marker payment", d => Trigger(d, 0)["damageOccurrence"] = "perDamagePoint");
        Reject("wrong clear window", d => Trigger(d, 1)["window"] = "turnEnding");
        Reject("unpaid clear", d => Trigger(d, 1)["effects"]!.AsArray().RemoveAt(0));
        foreach (var op in new[] { "applyLiushiEffect", "clearLiushiEffect" })
            Reject("activation " + op, d => Activation(d)["effects"]!.AsArray().Add(
                new JsonObject { ["op"] = op, ["target"] = "owner" }));
    }

    private static JsonObject Rules()
    {
        var document = JsonNode.Parse("""
            {"schemaVersion":0,"skills":[
              {"id":"fixture:liushi-validation","revision":1,"activations":[
                {"id":"paid-slash","minCards":1,"maxCards":1,"sourceZones":["hand","equipment"],"cardSuits":["heart"],
                 "minTargets":1,"maxTargets":1,"targetKind":"otherLivingVirtualSlashTarget","usesPerTurn":null,"effects":[
                  {"op":"captureSelectedCards","target":"owner","resultBind":"cost"},
                  {"op":"moveBoundCards","target":"owner","sourceBind":"cost","destination":"drawPileTop","awaitMovementTriggers":true},
                  {"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","useCardActionWindows":true,"targetRestriction":"distanceUnlimitedAgainstTarget"}]}],
               "triggers":[{"id":"apply","window":"afterDamageApplied","subject":"source","sourceSkillId":"fixture:liushi-validation",
                "sourceViewAsId":"paid-slash","damageOccurrence":"perDamage","optional":false,
                "effects":[{"op":"applyLiushiEffect","target":"owner"}]}]},
              {"id":"fixture:liushi-clear-validation","revision":1,"triggers":[
                {"id":"clear","window":"discardPhaseEnded","turnOwnerScope":"otherLiving","subject":"owner","optional":false,
                 "condition":{"kind":"all","children":[
                  {"kind":"compare","left":{"kind":"eventTargetMarkerCount","marker":"liuShi"},"operator":"greaterThanOrEqual","right":{"kind":"integerConstant","value":1}},
                  {"kind":"compare","left":{"kind":"turnOwnerDiscardPhaseHandDiscardCount"},"operator":"greaterThanOrEqual","right":{"kind":"integerConstant","value":1}}]},
                 "effects":[{"op":"draw","target":"owner","numberExpression":"turnOwnerDiscardPhaseHandDiscardCount"},
                            {"op":"clearLiushiEffect","target":"owner"}]}]}]}
            """)!.AsObject();
        document["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
        return document;
    }

    private static SkillProgramCatalog Load(JsonObject rules) => SkillProgramCatalog.Load(rules.ToJsonString(),
        JsonSerializer.Serialize(new
        {
            schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
            skills = new[] { Source, Clear }.ToDictionary(id => id, _ => new { name = "Liushi", description = "Shared paid effect validation" })
        }));
    private static JsonObject Activation(JsonObject d) => d["skills"]![0]!["activations"]![0]!.AsObject();
    private static JsonObject ActivationEffect(JsonObject d, int index) => Activation(d)["effects"]![index]!.AsObject();
    private static JsonObject Trigger(JsonObject d, int skill) => d["skills"]![skill]!["triggers"]![0]!.AsObject();
    private static void Reject(string boundary, Action<JsonObject> edit)
    {
        var document = Rules(); edit(document);
        try { _ = Load(document); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Liushi accepted " + boundary + ".");
    }
}
