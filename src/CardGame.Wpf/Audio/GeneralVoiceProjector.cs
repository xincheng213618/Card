using CardGame.Core;

namespace CardGame.Wpf.Audio;

public sealed class GeneralVoiceEventArgs(GeneralVoice voice) : EventArgs
{
    public GeneralVoice Voice { get; } = voice;
}

public static class GeneralVoiceProjector
{
    public static GeneralVoice? Project(IEnumerable<EventEnvelope> events, IReadOnlyList<PlayerSnapshot> players,
        Func<string, string?> skinForGeneral)
    {
        GeneralVoice? result = null;
        foreach (var envelope in events)
        {
            var (seat, skillId, death) = envelope.Payload switch
            {
                ProgramSkillResolvedEvent { Completed: true } e => (e.OwnerSeat, e.SkillId, false),
                ProgramBindingResolvedEvent { Activated: true, Completed: true } e => (e.OwnerSeat, e.SkillId, false),
                ProgramJudgmentTriggerResolvedEvent { Activated: true } e => (e.OwnerSeat, e.SkillId, false),
                ProgramJudgmentReplacementResolvedEvent { Activated: true } e => (e.OwnerSeat, e.SkillId, false),
                ProgramCardPolicyResolvedEvent { Applied: true } e => (e.OwnerSeat, e.SkillId, false),
                ProgramRecoveryPolicyAppliedEvent e => (e.OwnerSeat, e.SkillId, false),
                FactionDefenseRequestedEvent e => (e.OwnerSeat, e.SkillId, false),
                FactionSlashRequestedEvent e => (e.OwnerSeat, e.SkillId, false),
                PlayerDiedEvent e => (e.VictimSeat, (string?)null, true),
                _ => (-1, (string?)null, false)
            };
            var player = players.FirstOrDefault(player => player.Seat == seat);
            if (player is null) continue;
            // Only the public snapshot supplies identity/skills; never inspect the engine's hidden players.
            if (player.IsGeneralPublic)
                result = Find(player.GeneralId, player.Skills ?? []) ?? result;
            if (player.IsSecondaryGeneralPublic && player.SecondaryGeneralId is { } secondary)
                result = Find(secondary, player.SecondarySkills ?? []) ?? result;

            GeneralVoice? Find(string generalId, IReadOnlyList<GeneralSkillDefinition> skills)
            {
                var skill = skills.FirstOrDefault(skill => skill.ContentId == skillId);
                if (!death && skill is null) return null;
                var matches = GameAudioCatalog.ForSkill(generalId, skinForGeneral(generalId), skill?.Name ?? "", death ? "death" : "skill");
                return matches.Count == 0 ? null : matches[(int)((ulong)envelope.Sequence % (ulong)matches.Count)];
            }
        }
        return result;
    }
}
