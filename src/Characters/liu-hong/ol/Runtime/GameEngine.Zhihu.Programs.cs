namespace CardGame.Core;

public sealed partial class GameEngine
{

    private static string ZhihuGrantSourcePrefix => "acquired:zhihu:";

    // 执笏 expiry: grants sourced "acquired:zhihu:{grantorSeat}:…" are removed
    // when the grantor's next turn begins; the grant lives through every other
    // character's turn in between.
    private SkillProgramStepOutcome ZhihuProgramExpire(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var holder = _players[active.OwnerSeat];
        if (_winner != Winner.None || !holder.IsAlive)
            return SkillProgramStepOutcome.Continue;
        foreach (var grant in holder.SkillGrants.Grants
                     .Where(grant => grant.IsEnabled &&
                         grant.SourceId.StartsWith(ZhihuGrantSourcePrefix, StringComparison.Ordinal))
                     .ToArray())
        {
            var segment = grant.SourceId[ZhihuGrantSourcePrefix.Length..];
            var separator = segment.IndexOf(':');
            if (separator > 0 &&
                int.TryParse(segment[..separator], System.Globalization.CultureInfo.InvariantCulture, out var grantor) &&
                grantor == _currentSeat)
            {
                holder.SkillGrants.RemoveGrant(grant.GrantId);
                AddLog("SkillEffect",
                    $"{holder.Name} 的“执笏”随 {_players[grantor].Name} 的回合开始而失效。",
                    holder.Seat);
            }
        }
        return SkillProgramStepOutcome.Continue;
    }
}
