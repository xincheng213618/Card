using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IWushuangAuditCombatHost
{
    SkillProgramStepOutcome Execute(ProgramSkillFrame frame, SkillProgramEffectOp op);
}

internal abstract class WushuangAuditCombatDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new WushuangAuditCombatHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChangeAttributedMarker, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0, reader.Condition());
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(Op == SkillProgramEffectOp.ClaimWushuangEndedCampHand ? SkillProgramTriggerWindow.AfterTurnEnded : SkillProgramTriggerWindow.CardUseCommitted)];
}
internal sealed class ClaimWushuangEndedCampHandDescriptor : WushuangAuditCombatDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ClaimWushuangEndedCampHand; }
internal sealed class CaptureWushuangWangongUseDescriptor : WushuangAuditCombatDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.CaptureWushuangWangongUse; }
internal sealed class WushuangAuditCombatHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IWushuangAuditCombatHost>().Execute(frame, op);
}

public sealed record WushuangCampClaimReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long EndedFrameId, int EndedSeat, int ActualTurn, long SequenceBefore, long SequenceAfter = 0, long BatchId = 0)
{
    private readonly IReadOnlyList<int> _cards = Array.Empty<int>();
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
}
public sealed record WushuangCampClaimedEvent(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    long EndedFrameId, int EndedSeat, int ActualTurn, int Count, long SequenceBefore, long SequenceAfter, long BatchId) : IGameEvent;
public sealed record WushuangWangongUseReceipt(CardConversionSource Source, string GameplayHash, long UseFrameId,
    long ActionId, long PreviousOriginFrameId, long? PreviousActionId);
public sealed record WushuangWangongUseCapturedEvent(WushuangWangongUseReceipt Receipt) : IGameEvent;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WushuangCampClaimReceipt? WushuangCampClaim { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WushuangWangongUseReceipt? WushuangWangong { get; init; }
}
