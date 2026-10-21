using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MouDongZhuoOlStage { OwnCost, CostChildren, PeerChoice, PeerChildren, Damage, GiftCard, GiftRecipient, GiftChildren, Wine, WineChildren, ZhibingMaximum, ZhibingRecovery, ZhibingGrants, Finishing }
public sealed record MouDongZhuoOlCard(int Id, CardLocation From, Suit Suit);
public sealed record MouDongZhuoOlGift(int CardId, int Recipient);
public sealed record MouDongZhuoOlReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation, int Turn, int Round, long ParentId)
{
    public MouDongZhuoOlStage Stage { get; init; }
    public bool Paid { get; init; }
    public bool GuanbianRemoved { get; init; }
    public int Cursor { get; init; }
    public int Payer { get; init; }
    public int SelectedCard { get; init; }
    public Suit Suit { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long? WineUseId { get; init; }
    public bool WineReturned { get; init; }
    public int BlackCount { get; init; }
    private IReadOnlyList<int> _participants = [];
    private IReadOnlyList<MouDongZhuoOlCard> _materials = [];
    private IReadOnlyList<MouDongZhuoOlGift> _gifts = [];
    public IReadOnlyList<int> Participants { get => _participants; init => _participants = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<MouDongZhuoOlCard> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<MouDongZhuoOlGift> Gifts { get => _gifts; init => _gifts = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouDongZhuoOlReceipt? MouDongZhuoOl { get; init; } }
public sealed record MouDongZhuoOlWine(long ParentId, int Instruction, CardConversionSource Source, string Hash, bool SelfDying);
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouDongZhuoOlWine? MouDongZhuoWine { get; init; } }
public sealed record MouDongZhuoOlStartedEvent(long FrameId, CardConversionSource Source, string Hash, SkillProgramEffectOp Operation, int Turn, int Round, long ParentId) : IGameEvent;
public sealed record MouDongZhuoOlGuanbianEvent(long FrameId, int Owner, int Amount, bool Removed) : IGameEvent;
public sealed record MouDongZhuoOlBlackUseEvent(int Actor, string? Faction, long FrameId, long? ActionId, int Turn, bool Black) : IGameEvent;
public sealed record MouDongZhuoOlPaidEvent(long FrameId, int Payer, int CardId, CardLocation From, Suit Suit, long Sequence) : IGameEvent;
public sealed record MouDongZhuoOlDamageIssuedEvent(long FrameId, int Target, int Cursor) : IGameEvent;
public sealed record MouDongZhuoOlFengshangEvent(long FrameId, int Owner, int Turn, int Round, int Phase, bool Dying, Suit Suit) : IGameEvent;
public sealed record MouDongZhuoOlGiftEvent(long FrameId, int CardId, int Recipient, long Sequence) : IGameEvent;
public sealed record MouDongZhuoOlWineIssuedEvent(long FrameId, long UseId, long ActionId, bool SelfDying) : IGameEvent;
public sealed record MouDongZhuoOlWineReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record MouDongZhuoOlZhibingEvent(long FrameId, int Owner, int Turn, int BlackCount) : IGameEvent;
public sealed record MouDongZhuoOlCompletedEvent(long FrameId) : IGameEvent;

internal interface IMouDongZhuoOlRuntime : IProgramRuntimeOperations
{ SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class MouDongZhuoOlDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new MouDongZhuoOlHandler(Op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.WholeActualUseDeclaration | ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeActionAppearance,
        Activated: ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeActionAppearance);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
}
internal sealed class InitializeMouDongZhuoGuanbianDescriptor : MouDongZhuoOlDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.InitializeMouDongZhuoGuanbian; }
internal sealed class RemoveMouDongZhuoGuanbianDescriptor : MouDongZhuoOlDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RemoveMouDongZhuoGuanbian; }
internal sealed class ResolveMouDongZhuoXiongniDescriptor : MouDongZhuoOlDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouDongZhuoXiongni; }
internal sealed class ResolveMouDongZhuoFengshangDescriptor : MouDongZhuoOlDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouDongZhuoFengshang; }
internal sealed class ResolveMouDongZhuoZhibingDescriptor : MouDongZhuoOlDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouDongZhuoZhibing; }
internal sealed class MouDongZhuoOlHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host) => host.GetCapability<IMouDongZhuoOlRuntime>().Resolve(frame, operation);
}
