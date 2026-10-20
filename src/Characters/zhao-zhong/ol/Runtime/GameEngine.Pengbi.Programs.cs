using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void GrantPengbiSkill(ProgramSkillFrame f, int recipient, string skill)
    {
        foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
        var sourceId = $"zhao-zhong.pengbi:{f.Id}:{recipient}"; var grantId = sourceId + ":" + skill;
        _players[recipient].SkillGrants.Grant(new(grantId, skill, grantId, sourceId));
        AdvanceEventRulesAndQueueFact(new PengbiSkillGrantedEvent(f.Id, recipient, skill, grantId, grantId, sourceId));
    }
}
