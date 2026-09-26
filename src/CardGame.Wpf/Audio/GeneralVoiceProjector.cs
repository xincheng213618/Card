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
            var (seat, kind, skillId, death) = envelope.Payload switch
            {
                ActiveSkillResolvedEvent e => (e.SourceSeat, (SkillKind?)e.Skill, (string?)null, false),
                DrawSkillResolvedEvent { Used: true } e => (e.SourceSeat, (SkillKind?)e.Skill, (string?)null, false),
                PhaseSkillResolvedEvent { Used: true } e => (e.SourceSeat, (SkillKind?)e.Skill, (string?)null, false),
                DamageSkillResolvedEvent { Used: true } e => (e.OwnerSeat, (SkillKind?)e.Skill, (string?)null, false),
                ProgramSkillResolvedEvent { Completed: true } e => (e.OwnerSeat, (SkillKind?)null, e.SkillId, false),
                ProgramBindingResolvedEvent { Activated: true, Completed: true } e => (e.OwnerSeat, (SkillKind?)null, e.SkillId, false),
                ProgramJudgmentTriggerResolvedEvent { Activated: true } e => (e.OwnerSeat, (SkillKind?)null, e.SkillId, false),
                ProgramJudgmentReplacementResolvedEvent { Activated: true } e => (e.OwnerSeat, (SkillKind?)null, e.SkillId, false),
                PlayerDiedEvent e => (e.VictimSeat, (SkillKind?)null, (string?)null, true),
                _ => (-1, (SkillKind?)null, (string?)null, false)
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
                var skill = skills.FirstOrDefault(skill => kind is not null ? skill.Kind == kind : skill.ContentId == skillId);
                if (!death && skill is null) return null;
                var matches = GameAudioCatalog.ForSkill(generalId, skinForGeneral(generalId), skill?.Name ?? "", death ? "death" : "skill");
                return matches.Count == 0 ? null : matches[(int)((ulong)envelope.Sequence % (ulong)matches.Count)];
            }
        }
        return result;
    }
}
