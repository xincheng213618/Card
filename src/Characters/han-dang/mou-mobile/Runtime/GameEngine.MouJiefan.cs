using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int MouHanDangPhaseUses(int owner, ActualDiscardRecoveryPhaseKey phase) => ProgramEventHistory<MouHanDangStartedEvent>().Count(e => !e.Gongqi && e.Source.OwnerSeat == owner && e.Phase == phase);
    private bool MouHanDangJiefanDisabled(int owner)
    {
        if (!HasMouHanDangCapability) return false;
        var history = CompleteProgramEventHistory().ToArray();
        var index = Array.FindLastIndex(history, e => e is MouHanDangJiefanDisabledEvent d && d.OwnerSeat == owner);
        return index >= 0 && !history.Skip(index + 1).OfType<PlayerDiedEvent>().Any(e => e.KillerSeat == owner && e.VictimSeat != owner);
    }
    [ProgramPhaseUsedProjection(50)]
    private int MouHanDangProjectedUses(int owner, string skill, int fallback) => skill == MouHanDangJiefan && CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } p
        ? MouHanDangPhaseUses(owner, p) : fallback;
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart, 30700, SkillProgramEffectOp.ResolveMouHanDangJiefan)]
    private bool CanStartMouHanDangJiefan(CharacterState owner) => !MouHanDangJiefanDisabled(owner.Seat) &&
        CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } p && p.ActorSeat == owner.Seat &&
        IsRecordedActualDiscardRecoveryPhase(p) && MouHanDangPhaseUses(owner.Seat, p) == 0;
    private IReadOnlyList<int> MouHanDangRangeSeats(int target, int first) => Array.AsReadOnly(_players.Where(p => p.IsAlive && p.Seat != target && IsWithinAttackRange(p.Seat, target))
        .OrderBy(p => (p.Seat - first + _playerCount) % _playerCount).Select(p => p.Seat).ToArray());
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouHanDangJiefan)]
    private void ReturnMouHanDangDraw(ProgramSkillFrame f, NativeDrawOutcome d)
    {
        if (!ValidMouHanDang(f) || f.MouHanDang is not { Stage: MouHanDangStage.Drawing, Draw: null, Gongqi: false } r || d.RecipientSeat != r.TargetSeat || d.RequestedCount != r.RequestedDraw)
            throw new InvalidOperationException("Jiefan requires its exact one issued native Draw return.");
        ReplaceRuntimeTop(f = f with { MouHanDang = r with { Stage = MouHanDangStage.DrawChildren, Draw = d, Before = d.SequenceBefore, After = d.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MouHanDangDrawReturnedEvent(f.Id, r.TargetSeat, d.RequestedCount, d.ActualDrawIds.Count, d.ReplacementGainedIds.Count, d.AttemptFrameId));
        SealMouHanDangMovements(f.Id, d.SequenceBefore); ContinueMouHanDang(f.Id);
    }
}
