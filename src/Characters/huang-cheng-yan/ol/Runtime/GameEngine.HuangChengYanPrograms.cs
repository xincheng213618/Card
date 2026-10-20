namespace CardGame.Core;

public sealed partial class GameEngine
{
    // 解阵 conversions survive across turns until the owner's next turn start or
    // the converted character's 八卦阵 judgment; replay rebuilds the map while the
    // accepted commands re-execute the converting op.
    private readonly Dictionary<int, JiezhenConversionState> _jiezhenConversions = [];

    private string GetSkillDisplayName(string skillId)
    {
        try { return _contentRegistry!.GetSkill(skillId).Name; }
        catch (InvalidOperationException) { return skillId; }
    }

    // Grants sourced "acquired:round:{N}:…" stay enabled during round N and are
    // removed once a later round has begun (the "直到下一轮游戏结束" scope).
    private void PurgeExpiredRoundGrants()
    {
        foreach (var player in _players)
            foreach (var grant in player.SkillGrants.Grants
                         .Where(grant => grant.SourceId.StartsWith("acquired:round:", StringComparison.Ordinal))
                         .ToArray())
            {
                var segment = grant.SourceId["acquired:round:".Length..];
                var separator = segment.IndexOf(':');
                if (separator > 0 &&
                    int.TryParse(segment[..separator], System.Globalization.CultureInfo.InvariantCulture, out var round) &&
                    round < _roundNumber)
                    player.SkillGrants.RemoveGrant(grant.GrantId);
            }
    }


    [ProgramCapability]
    private sealed class HuangChengYanProgramCapability(GameEngine engine) : IHuangChengYanProgramHost
    {
        public SkillProgramStepOutcome JiezhenReplaceSkills(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.JiezhenProgramReplaceSkills(frame, effect);
        public SkillProgramStepOutcome JiezhenRestoreSkills(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.JiezhenProgramRestoreSkills(frame, effect);
        public SkillProgramStepOutcome ZecaiRoundSettlement(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ZecaiProgramRoundSettlement(frame, effect);
        public SkillProgramStepOutcome YinshiPreventSourcelessDamage(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.YinshiProgramPreventSourcelessDamage(frame, effect);
        public SkillProgramStepOutcome YinshiClaimBaguaJudgmentCard(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.YinshiProgramClaimBaguaJudgmentCard(frame, effect);
    }
}
