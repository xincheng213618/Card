using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record MobileShiTianFengGiftEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int TargetSeat, int CardCount) : IGameEvent;
public sealed record MobileShiTianFengGiftSettledEvent(long GiftFrameId, long FrameId, int TargetSeat, bool Maximum) : IGameEvent;
public sealed record MobileShiTianFengDebtIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int TargetSeat, long AfterActionId) : IGameEvent;
public sealed record MobileShiTianFengDebtClaimedEvent(long IssuingFrameId, long FrameId, long ActionId, int TargetSeat) : IGameEvent;
public sealed record MobileShiTianFengBackwaterEvent(long FrameId, CardConversionSource Source, int Ordinal) : IGameEvent;

public sealed record MobileShiTianFengReceipt(string Operation, int ActualTurn, int Stage = 0, int TargetSeat = -1,
    bool Backwater = false, int HpLoss = 0, long ClaimId = 0, long ActionId = 0, long Before = 0,
    int? CardId = null, NativeDrawOutcome? Draw = null);

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MobileShiTianFengReceipt? MobileShiTianFeng { get; init; }
}

internal interface IMobileShiTianFengHost
{ SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }

internal sealed class ResolveMobileShiTianFengDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShiTianFeng;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    // Both the post-gift activation and three distinct native windows enter this
    // operation. It creates no DSL binding; its typed frame retains each branch.
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } =
        new(ProgramOperationAiSemantic.GainCards, static (_, context) => context.PublicControlValue(1));
    public override ISkillProgramEffectHandler Handler { get; } = new MobileShiTianFengHandler();
}
internal sealed class MobileShiTianFengHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShiTianFeng;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IMobileShiTianFengHost>().Resolve(frame);
}
