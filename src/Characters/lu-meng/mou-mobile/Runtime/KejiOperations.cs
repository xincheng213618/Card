using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record MouLuMengKejiReceipt(CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, int? SelectedCardId, int MovementBefore, bool CostReturned = false, bool Paid = false);
public sealed record MouLuMengKejiReservedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record MouLuMengKejiCostReturnedEvent(long FrameId, string Branch, bool Paid) : IGameEvent;
internal sealed class ResolveMouLuMengKejiDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouLuMengKeji;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantArmor, static (_, c) => c.PublicControlValue(0));
    public override ISkillProgramEffectHandler Handler { get; } = new MouLuMengHandler(SkillProgramEffectOp.ResolveMouLuMengKeji);
}
