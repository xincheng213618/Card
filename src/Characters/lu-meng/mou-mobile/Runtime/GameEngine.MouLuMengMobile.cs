namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MouKeji = MouLuMengComposition.Keji, MouDujiang = MouLuMengComposition.Dujiang, MouDuojing = MouLuMengComposition.Duojing;
    private bool HasMouLuMengCapability => _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveMouLuMengKeji) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouLuMengDujiang) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouLuMengDuojing);
    private static CardConversionSource MouLuMengSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private ActualDiscardRecoveryPhaseKey MouLuMengPhase() => CurrentActualDiscardRecoveryPhase() is { } phase && IsRecordedActualDiscardRecoveryPhase(phase)
        ? phase : throw new InvalidOperationException("Mobile Lu Meng requires the recorded actual phase, including inserted phases.");

    [ProgramCapability]
    private sealed class MouLuMengCapability(GameEngine engine) : IMouLuMengHost
    {
        public SkillProgramStepOutcome Keji(ProgramSkillFrame f) => engine.ResolveMouKeji(f);
        public SkillProgramStepOutcome Dujiang(ProgramSkillFrame f) => engine.ResolveMouDujiang(f);
        public SkillProgramStepOutcome Duojing(ProgramSkillFrame f) => engine.BeginMouDuojing(f);
    }

    [ProgramCardLegality(ProgramCardLegalityStage.BeforeTurnUseBan, 60)]
    private bool MouLuMengCardUseForbidden(ProgramCardUseQuery query) =>
        IsMouLuMengPeachForbidden(query.ActorSeat, query.Kind, query.ActionType);
}
