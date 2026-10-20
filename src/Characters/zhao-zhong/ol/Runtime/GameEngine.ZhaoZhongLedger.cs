namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasZhaoZhongCapability => _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveDiciGiftRecoveryAndNeighborChain) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolvePengbiFirstActualTurn) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveIssuedDiciNextChainChoice) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveYintianFirstRecovery) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveBiriFirstNonDrawGain);
    private static CardConversionSource ZhaoZhongSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_zhao_zhong_ol_ZhaoZhongLedger.ZhaoZhongSource(f);
    private bool ZhaoZhongActualTurnOpen => _turnNumber > 0 && _turnNumber == _turnProgression.TurnNumber && !ProgramEventHistory<ActualTurnCursorReturnedEvent>().Any(e => e.ActualTurn == _turnNumber && e.OwnerSeat == _turnProgression.OwnerSeat);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 28110)]
    private bool CanOfferZhaoZhong(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context)
    {
        if (IsPengbiTrigger(t)) return c.OwnerSeat == _turnProgression.OwnerSeat && context.OwnerSeat == c.OwnerSeat && context.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow &&
            FirstPengbiActualTurn(c.OwnerSeat) == _turnNumber && !ProgramEventHistory<PengbiCommittedEvent>().Any(e => e.Source.OwnerSeat == c.OwnerSeat);
        if (IsBiriTrigger(t)) return IsIssuedBiriCandidate(c, context);
        if (IsIssuedDiciChainTrigger(t)) return IsIssuedDiciChainCandidate(c, context);
        return true;
    }
}
