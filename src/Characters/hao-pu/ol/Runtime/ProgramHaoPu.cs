using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum ZhenyingStage { ChoosingCount, ChoosingKeptCards, AdjustmentChildren, DuelIssued }
public sealed record ZhenyingHandPlan(int Seat, IReadOnlyList<int> OriginalIds, int? DesiredCount = null,
    IReadOnlyList<int>? KeptIds = null, bool Committed = false, IReadOnlyList<int>? ProtectedIds = null)
{
    private IReadOnlyList<int> _originalIds = Array.AsReadOnly(OriginalIds.ToArray());
    public IReadOnlyList<int> OriginalIds { get => _originalIds; init => _originalIds = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<int>? _keptIds = KeptIds is null ? null : Array.AsReadOnly(KeptIds.ToArray());
    public IReadOnlyList<int>? KeptIds { get => _keptIds; init => _keptIds = value is null ? null : Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<int>? _protectedIds = ProtectedIds is null ? null : Array.AsReadOnly(ProtectedIds.ToArray());
    public IReadOnlyList<int>? ProtectedIds { get => _protectedIds; init => _protectedIds = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed record ZhenyingReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    int ActualTurn, int PhaseInstanceId, int OtherSeat, long StartMovementSequence,
    ZhenyingHandPlan OwnerPlan, ZhenyingHandPlan OtherPlan, ZhenyingStage Stage = ZhenyingStage.ChoosingCount,
    int SelectionIndex = 0, int SlotIndex = 0, long PaymentBefore = 0, long PaymentAfter = 0, int OwnerDrawCount = 0, int OtherDrawCount = 0,
    ZhenyingDuelOrigin? Duel = null);
public sealed record ZhenyingDuelOrigin(long ProgramFrameId, int InstructionIndex, long CardUseFrameId,
    CardConversionSource Source, string GameplayHash, int ActualTurn, int PhaseInstanceId,
    int ActorSeat, int TargetSeat, int OwnerCount, int OtherCount, bool AttackStarted = false);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ZhenyingReceipt? Zhenying { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ZhenyingDuelOrigin? ZhenyingDuel { get; init; }
}
// Sealed choices stay on the owning frame. Before both commit, no chosen count,
// kept entity or other private hand information enters an event.
public sealed record ZhenyingStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int PhaseInstanceId, int OtherSeat, long MovementSequence, int OwnerHandCount, int OtherHandCount) : IGameEvent;
public sealed record ZhenyingChoiceCommittedEvent(long FrameId, int Seat) : IGameEvent;
public sealed record ZhenyingAdjustmentPaidEvent(long FrameId, long Before, long After,
    int OwnerDesiredCount, int OtherDesiredCount, int OwnerDrawCount, int OtherDrawCount,
    int OwnerActualCount, int OtherActualCount) : IGameEvent;
public sealed record ZhenyingDuelIssuedEvent(ZhenyingDuelOrigin Origin) : IGameEvent;
public sealed record ZhenyingCompletedEvent(long FrameId, bool DuelIssued) : IGameEvent;

internal interface IHaoPuProgramHost { SkillProgramStepOutcome ResolveZhenying(ProgramSkillFrame frame); }
internal sealed class ResolveZhenyingSimultaneousHandsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveZhenyingSimultaneousHands;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveZhenyingHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget(), new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving)];
}
internal sealed class ResolveZhenyingHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveZhenyingSimultaneousHands;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IHaoPuProgramHost>().ResolveZhenying(frame);
}
internal static class HaoPuContract
{
    internal static void Activation(string path, SkillProgramActivation a)
    {
        if (!a.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveZhenyingSimultaneousHands)) return;
        if (a.Effects is not [{ Op: SkillProgramEffectOp.ResolveZhenyingSimultaneousHands, Condition.Kind: SkillProgramConditionKind.Always }] ||
            a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 1 || a.MaxTargets != 1 ||
            a.TargetKind != SkillProgramTargetKind.OtherLiving || a.UsesPerTurn is not null || a.UsesPerPhase != 2 ||
            a.Condition.Kind != SkillProgramConditionKind.Always)
            throw new InvalidOperationException(path + ": Zhenying requires one complete zero-card, one-other-target, twice-per-Play simultaneous adjustment and native Duel.");
    }
    internal static void Trigger(string path, SkillProgramTrigger trigger)
    {
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveZhenyingSimultaneousHands))
            throw new InvalidOperationException(path + ": Zhenying requires its own Play activation.");
    }
}
