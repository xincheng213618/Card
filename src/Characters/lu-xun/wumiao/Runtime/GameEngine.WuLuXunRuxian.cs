namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasWuLuXunZhangcaiCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveWuLuXunZhangcai);
    private bool HasWuLuXunRuxianCapability => _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveWuLuXunRuxian);
    private static CardConversionSource WuLuXunActionSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private int WuLuXunRuxianUsage(int ownerSeat) => !HasWuLuXunRuxianCapability ? 0 : _skillRuntimeState.GetUsage(ownerSeat,
        "wumiao:ruxian", _contentRegistry.GetSkill("wumiao:ruxian").Program!.Activations.Single().UsageGroup, SkillUsageScope.Game);
    private bool WuLuXunRuxianSpent(int ownerSeat) => WuLuXunRuxianUsage(ownerSeat) > 0;
    private WuLuXunRuxianActivatedEvent? WuLuXunActiveRuxian(int ownerSeat)
    {
        if (!HasWuLuXunRuxianCapability) return null;
        var issued = ProgramEventHistory<WuLuXunRuxianActivatedEvent>().LastOrDefault(e => e.Source.OwnerSeat == ownerSeat);
        return issued is not null && !ProgramEventHistory<TurnStartedEvent>().Any(e => e.ActorSeat == ownerSeat && e.TurnNumber > issued.ActualTurn)
            ? issued : null;
    }
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 29003, SkillProgramEffectOp.ResolveWuLuXunRuxian)]
    private bool CanActivateWuLuXunRuxian(ProgramActivationLegalityContext gate) =>
        gate.Activation.Id == "all-ranks-until-next-turn" && _setupComplete && _winner == Winner.None &&
        gate.Owner.IsAlive && _phase == TurnPhase.Play && _currentSeat == gate.OwnerSeat &&
        !WuLuXunRuxianSpent(gate.OwnerSeat) && CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase &&
        phase.ActorSeat == gate.OwnerSeat && IsRecordedActualDiscardRecoveryPhase(phase);

    [ProgramCapability]
    private sealed class WuLuXunZhangcaiCapability(GameEngine engine) : IWuLuXunZhangcaiHost
    {
        public SkillProgramStepOutcome Zhangcai(ProgramSkillFrame frame) => engine.BeginWuLuXunZhangcai(frame);
        public SkillProgramStepOutcome Ruxian(ProgramSkillFrame frame) => engine.ActivateWuLuXunRuxian(frame);
    }
    private SkillProgramStepOutcome ActivateWuLuXunRuxian(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != "wumiao:ruxian" || f.ActivationId != "all-ranks-until-next-turn" || f.TriggerId is not null ||
            f.WindowContext is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            _winner != Winner.None || !_players[f.OwnerSeat].IsAlive || _phase != TurnPhase.Play || _currentSeat != f.OwnerSeat ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) || WuLuXunRuxianUsage(f.OwnerSeat) != 1 ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            ProgramEventHistory<ProgramSkillStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat &&
                e.SkillId == f.SkillId && e.ActivationId == f.ActivationId) != 1 ||
            ProgramEventHistory<WuLuXunRuxianActivatedEvent>().Any(e => e.FrameId == f.Id) ||
            CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase ||
            phase.ActorSeat != f.OwnerSeat || !IsRecordedActualDiscardRecoveryPhase(phase))
            throw new InvalidOperationException("Ruxian requires its unpaid actual own Play activation; source replacement never restores its lifetime limit.");
        AdvanceEventRulesAndQueueFact(new WuLuXunRuxianActivatedEvent(f.Id, WuLuXunActionSource(f), f.GameplayHash,
            phase, _turnNumber, _turnProgression.OwnerSeat));
        AddLog("SkillTriggered", $"{_players[f.OwnerSeat].Name} 发动【儒贤】，至下回合开始前使用或打出任意点数的牌均可触发彰才。", f.OwnerSeat);
        return SkillProgramStepOutcome.Continue;
    }
}
