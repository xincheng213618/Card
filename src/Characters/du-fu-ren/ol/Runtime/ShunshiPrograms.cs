using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record DuFuRenGiftIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int Turn, int TurnOwner, int RecipientSeat, int CardId, CardLocation From, long MovementSequence) : IGameEvent;
public sealed record DuFuRenPhaseIssuedEvent(long GrantId, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record DuFuRenPhaseConfirmedEvent(ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
internal sealed class GiveForNextActualPhasesDescriptor() : DuFuRenDescriptor(SkillProgramEffectOp.GiveForNextActualPhases)
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindows([SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,SkillProgramTriggerWindow.AfterDamageApplied])];
}
