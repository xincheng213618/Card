namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_PublicDamagePrediction
{
    internal static HashSet<string> VisiblePredictionSkillIds(PlayerSnapshot player) =>
        (player.Skills ?? []).Concat(player.SecondarySkills ?? []).Select(skill => skill.ContentId)
            .OfType<string>().ToHashSet(StringComparer.Ordinal);
}
