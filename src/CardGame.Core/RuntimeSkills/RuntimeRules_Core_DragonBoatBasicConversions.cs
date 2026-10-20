namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_DragonBoatBasicConversions
{
    internal static string DragonBoatBasicViewAsFamily(SkillProgramViewAs rule)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(rule))!.AsObject();
        foreach (var name in new[] { nameof(SkillProgramViewAs.Id), nameof(SkillProgramViewAs.OutputKind),
            nameof(SkillProgramViewAs.ForPlay), nameof(SkillProgramViewAs.ForResponse) }) node.Remove(name);
        return node.ToJsonString();
    }
}
