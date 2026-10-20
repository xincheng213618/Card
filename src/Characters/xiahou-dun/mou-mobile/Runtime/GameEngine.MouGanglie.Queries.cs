using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private int[] MouXiahouDunGanglieTargets(int seat) => _players.Where(p => p.IsAlive &&
        ProgramEventHistory<DamageAppliedEvent>().Any(e => !e.SourceLess && e.Amount > 0 && e.TargetSeat == seat && e.SourceSeat == p.Seat) &&
        !ProgramEventHistory<MouXiahouDunMobileGanglieSpentEvent>().Any(e => e.OwnerSeat == seat && e.TargetSeat == p.Seat)).Select(p => p.Seat).ToArray();
    private int MouXiahouDunPhaseUses(int seat, ActualDiscardRecoveryPhaseKey phase) => ProgramEventHistory<MouXiahouDunMobileStartedEvent>().Count(e =>
        e.Source.OwnerSeat == seat && e.Op == SkillProgramEffectOp.ResolveMouXiahouDunGanglie && e.Phase == phase);
    [ProgramPhaseUsedProjection(60)]
    private int MouXiahouDunProjectedUses(int seat, string skill, int fallback) => skill == MouXiahouDunGanglie && CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase
        ? MouXiahouDunPhaseUses(seat, phase) : fallback;
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart, 31200, SkillProgramEffectOp.ResolveMouXiahouDunGanglie)]
    private bool CanStartMouXiahouDunGanglie(CharacterState owner) => CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase &&
        phase.ActorSeat == owner.Seat && IsRecordedActualDiscardRecoveryPhase(phase) && MouXiahouDunPhaseUses(owner.Seat, phase) == 0 && MouXiahouDunGanglieTargets(owner.Seat).Length > 0;
    private void IssueMouXiahouDunDamage(ProgramSkillFrame f)
    {
        var r = f.MouXiahouDunMobile!; var index = r.Index;
        while (index < r.Targets.Count && !_players[r.Targets[index]].IsAlive) index++;
        if (index == r.Targets.Count || _winner != Winner.None) { FinishMouXiahouDun(f); return; }
        ReplaceRuntimeTop(f = f with { MouXiahouDunMobile = r with { Index = index } });
        AdvanceEventRulesAndQueueFact(new MouXiahouDunMobileDamageIssuedEvent(f.Id, index, f.OwnerSeat, r.Targets[index]));
        BeginProgramSkillDamage(f, r.Targets[index], 2, new(ProgramParticipantRef.Owner));
    }
}
