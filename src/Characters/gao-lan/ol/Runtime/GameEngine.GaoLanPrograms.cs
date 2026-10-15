namespace CardGame.Core;

public sealed partial class GameEngine
{
    // 袭阵: the single alive XiZhen marker holder recovers one; the owner draws
    // one when the holder is still wounded after the recovery, otherwise two.
    private void XizhenProgramResponseBenefit(ProgramSkillFrame frame)
    {
        var holders = _players.Where(player => player.IsAlive && player.Markers.ContainsKey(PlayerMarkerKind.XiZhen)).ToArray();
        if (holders.Length != 1) return;
        var holder = holders[0];
        if (!holder.HasHpValue) return;
        var requested = Math.Min(1, holder.MaxHp - holder.Hp);
        if (requested > 0)
        {
            if (TryQueueRecoveryReplacement(frame.Id, frame.OwnerSeat, holder.Seat, requested,
                    new(RecoveryAttemptProducer.Program, frame.InstructionIndex))) return;
            var recovery = BeginRecovery(frame.Id, frame.OwnerSeat, holder.Seat, requested);
            holder.Hp += requested;
            AdvanceEventRulesAndQueueFact(new RecoveryAppliedEvent(frame.OwnerSeat, holder.Seat, requested, holder.Hp));
            PopResolutionFrame(recovery, ResolutionFrameKind.Recovery);
        }
        var draw = holder.Hp < holder.MaxHp ? 1 : 2;
        DrawProgramCards(frame.Id, frame.OwnerSeat, draw, null, null,
            SkillProgramCardSetVisibility.Private, new($"skill-program.{frame.SkillId}.xizhen"));
    }

    [ProgramCapability]
    private sealed class GaoLanProgramCapability(GameEngine engine) : IGaoLanProgramHost
    {
        public void XizhenResponseBenefit(ProgramSkillFrame frame) =>
            engine.XizhenProgramResponseBenefit(frame);
    }
}
