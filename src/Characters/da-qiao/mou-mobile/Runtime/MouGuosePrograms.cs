using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouDaQiaoGuoseStage { Selecting, Using, MovementChildren, Drawing, DrawChildren }
public sealed record MouDaQiaoGuoseReceipt(CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, string Branch, int TargetSeat, MouDaQiaoGuoseStage Stage,
    int? CardId = null, CardLocation? From = null, long? UseId = null, long Before = 0, long After = 0, bool Paid = false);
public sealed record MouDaQiaoGuoseReservedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, string Branch, int TargetSeat, int PhaseLimit) : IGameEvent;
public sealed record MouDaQiaoGuosePaidEvent(long FrameId, string Branch, int CardId, CardLocation From, CardLocation To, long MovementSequence) : IGameEvent;
public sealed record MouDaQiaoGuoseDrawReturnedEvent(long FrameId, long? AttemptFrameId, int ActualCount, int ReplacementCount) : IGameEvent;
public sealed record MouDaQiaoGuoseCompletedEvent(long FrameId, bool Paid) : IGameEvent;
internal sealed class ResolveMouDaQiaoGuoseDescriptor : MouDaQiaoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouDaQiaoGuose;
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);
}
internal sealed class ResolveMouDaQiaoGuoseRemoveDescriptor : MouDaQiaoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouDaQiaoGuoseRemove; }
