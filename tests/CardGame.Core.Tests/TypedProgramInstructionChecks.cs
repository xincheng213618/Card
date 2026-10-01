using System.Text.Json;
using CardGame.Core;

internal static class TypedProgramInstructionChecks
{
    public static void DrawAndRecoverCompileDistinctAmountSources()
    {
        var fixedDraw = Parse("""{"op":"draw","target":"selectedTargets","amount":2}""");
        Require(fixedDraw.CompiledInstruction is DrawProgramInstruction
        {
            Target: SkillProgramEffectTarget.SelectedTargets,
            Amount: FixedProgramAmount { Value: 2 }
        }, "Fixed selected-target draw must compile its own amount.");

        var expressionDraw = Parse("""{"op":"draw","target":"owner","numberExpression":"ownerLostHp"}""");
        Require(expressionDraw.CompiledInstruction is DrawProgramInstruction
        {
            Amount: ExpressionProgramAmount { Expression: SkillProgramNumberExpression.OwnerLostHp }
        }, "Computed draw must retain its number expression.");

        var boundDraw = Parse("""{"op":"draw","target":"owner","numberExpression":"boundCardCount","sourceBind":"cards"}""");
        Require(boundDraw.CompiledInstruction is DrawProgramInstruction
        {
            Amount: BoundCardCountProgramAmount { SourceBind: "cards" }
        }, "Bound-card draw must retain the validated source binding.");

        var fixedRecover = Parse("""{"op":"recover","target":"selectedTargets","amount":1}""");
        Require(fixedRecover.CompiledInstruction is RecoverProgramInstruction
        {
            Target: SkillProgramEffectTarget.SelectedTargets,
            Amount: FixedProgramAmount { Value: 1 }
        }, "Fixed selected-target recovery must compile its own amount.");

        var boundRecover = Parse("""{"op":"recover","target":"owner","numberExpression":"boundCardCount","sourceBind":"cards"}""");
        Require(boundRecover.CompiledInstruction is RecoverProgramInstruction
        {
            Amount: BoundCardCountProgramAmount { SourceBind: "cards" }
        }, "Bound-card recovery must retain the validated source binding.");

        Require(!JsonSerializer.Serialize(boundDraw).Contains("CompiledInstruction", StringComparison.Ordinal),
            "The internal instruction must not enter the public serialized definition.");

        var reducedMaximum = Parse("""{"op":"changeMaximumHp","target":"owner","amount":-2}""");
        var increasedMaximum = Parse("""{"op":"changeMaximumHp","target":"owner","amount":3}""");
        Require(reducedMaximum.CompiledInstruction is ChangeMaximumHpProgramInstruction { Delta: -2 } &&
                increasedMaximum.CompiledInstruction is ChangeMaximumHpProgramInstruction { Delta: 3 },
            "Signed maximum-HP changes must compile their validated deltas without runtime effect reads.");

        var populationGrowth = Parse("""{"op":"growMaximumHpAndHp","target":"owner","numberExpression":"livingFactionCount"}""");
        Require(populationGrowth.CompiledInstruction is GrowMaximumHpAndHpProgramInstruction
                { PopulationExpression: SkillProgramNumberExpression.LivingFactionCount },
            "Population growth must compile its validated game-start amount expression.");
        Require(!JsonSerializer.Serialize(reducedMaximum).Contains("CompiledInstruction", StringComparison.Ordinal) &&
                !JsonSerializer.Serialize(populationGrowth).Contains("CompiledInstruction", StringComparison.Ordinal),
            "Maximum-HP instructions must stay outside the serialized rules definition and content fingerprint.");
    }

    private static SkillProgramEffect Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ProgramOperationCatalog.Default.Parse(document.RootElement, "fixture.effects[0]",
            (_, _) => new(SkillProgramConditionKind.Always, 0, []));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
