using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouZhenJiLuoshenStage { StartingSeat, RevealChoice, MovementChildren }
public sealed record MouZhenJiLuoshenReceipt(CardConversionSource Source, string GameplayHash, long WindowId, int TurnNumber, int TurnOwnerSeat)
{
    public MouZhenJiLuoshenStage Stage { get; init; }
    public int? StartingSeat { get; init; }
    public int Cursor { get; init; }
    private IReadOnlyList<int> _seats = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Seats { get => _seats; init => _seats = Array.AsReadOnly(value.ToArray()); }
    public int? RevealedCardId { get; init; }
    public Suit? RevealedSuit { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouZhenJiLuoshenReceipt? MouZhenJiLuoshen { get; init; } }
public sealed record MouZhenJiLuoshenStartedEvent(long FrameId, CardConversionSource Source, int TurnNumber, int TurnOwnerSeat, int StartingSeat, int Count) : IGameEvent;
public sealed record MouZhenJiLuoshenSeatQueuedEvent(long FrameId, int Index, int Seat) : IGameEvent;
public sealed record MouZhenJiLuoshenRevealedEvent(long FrameId, int Index, int Seat, int CardId, Suit EffectiveSuit) : IGameEvent;
public sealed record MouZhenJiLuoshenMovedEvent(long FrameId, int Index, int CardId, CardLocation From, CardLocation To, long Sequence, bool Black) : IGameEvent;
public sealed record MouZhenJiLuoshenCompletedEvent(long FrameId, int ProcessedCount) : IGameEvent;
internal interface IMouZhenJiLuoshenHost { SkillProgramStepOutcome Start(ProgramSkillFrame frame); }
internal sealed class ResolveMouZhenJiLuoshenDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouZhenJiLuoshen;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(4));
    public override ISkillProgramEffectHandler Handler { get; } = new MouZhenJiLuoshenHandler();

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class MouZhenJiLuoshenHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouZhenJiLuoshen;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouZhenJiLuoshenHost>().Start(f);
}
