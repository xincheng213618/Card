using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record LianjuEntityUsedEvent(int ActualTurn, int ActorSeat, long? ActionId, long UseFrameId, int CardId) : IGameEvent;
public sealed record LianjuUseActorChangedEvent(long UseFrameId, long ActionId, long ProducerFrameId, CardConversionSource Producer,
    string GameplayHash, int InstructionIndex, int PreviousActor, int Actor, int Provider) : IGameEvent;
public sealed record LianjuEndingReachedEvent(int Serial, int ActualTurn, int ActorSeat, long NativePhaseToken) : IGameEvent;
public sealed record LianjuPromiseGrantedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int EndingSerial,
    int ActualTurn, int RecipientSeat, bool IsRed) : IGameEvent;
public sealed record LianjuPromiseConsumedEvent(long PromiseFrameId, long ParentFrameId, int EndingSerial) : IGameEvent;
// These are trusted rule facts. Hidden draw identities are projected only through their owner's native card snapshots.
public sealed record SilvEntityMarkedEvent(int CardId, CardConversionSource Source, string GameplayHash, long ProducerFrameId,
    long GainMovementSequence) : IGameEvent;
public sealed record LiWanStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op,
    long ParentId, int ActualTurn, int EndingSerial, long? PromiseFrameId, long? RefundSequence) : IGameEvent;
public sealed record LiWanPaidEvent(long FrameId, int RecipientSeat, long Before, long After, int Count) : IGameEvent;
public sealed record LiWanCompletedEvent(long FrameId) : IGameEvent;
public sealed record LiWanDamageReceipt(long DamageFrameId, long DamageParentId, int ActualAmount, int TargetSeat);
public enum LiWanStage { Color, Recipient, Cards, Children }
public sealed record LiWanReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Op, int InstructionIndex,
    long ParentId, int ActualTurn, int EndingSerial, LiWanStage Stage, bool? IsRed = null, int? RecipientSeat = null,
    long? PromiseFrameId = null, long? RefundSequence = null, long Before = 0, long After = 0, int Count = 0,
    IReadOnlyList<int>? ChosenCards = null, LiWanDamageReceipt? Damage = null)
{
    private IReadOnlyList<int>? _chosenCards = ChosenCards is null ? null : Array.AsReadOnly(ChosenCards.ToArray());
    public IReadOnlyList<int>? ChosenCards { get => _chosenCards; init => _chosenCards = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public LiWanReceipt? LiWan { get; init; }
}
internal interface ILiWanProgramHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class LiWanDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler => new LiWanHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(Op switch {
            SkillProgramEffectOp.DrawSilvMarkedCardAfterDamage => SkillProgramTriggerWindow.AfterDamageApplied,
            SkillProgramEffectOp.ReturnDiscardedSilvEntity => SkillProgramTriggerWindow.DiscardPileReceived,
            _ => SkillProgramTriggerWindow.TurnEnding })];
}
internal sealed class OfferLianjuSameColorGiftDescriptor : LiWanDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.OfferLianjuSameColorGift; }
internal sealed class ResolveLianjuOppositeColorReturnDescriptor : LiWanDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveLianjuOppositeColorReturn; }
internal sealed class DrawSilvMarkedCardAfterDamageDescriptor : LiWanDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawSilvMarkedCardAfterDamage; }
internal sealed class ReturnDiscardedSilvEntityDescriptor : LiWanDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReturnDiscardedSilvEntity; }
internal sealed class LiWanHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<ILiWanProgramHost>().Resolve(frame, op);
}
internal static class LiWanComposition
{
    internal static bool Handles(SkillProgramEffectOp op) => op is SkillProgramEffectOp.OfferLianjuSameColorGift or
        SkillProgramEffectOp.ResolveLianjuOppositeColorReturn or SkillProgramEffectOp.DrawSilvMarkedCardAfterDamage or SkillProgramEffectOp.ReturnDiscardedSilvEntity;
    internal static void Activation(string path, SkillProgramActivation a)
    { if (a.Effects.Any(e => Handles(e.Op))) throw new InvalidOperationException(path + ": Li Wan requires native event triggers."); }
    internal static void Trigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => Handles(e.Op))) return;
        if (t.Effects is not [{ Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } e] ||
            !Handles(e.Op) || t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.UsageLimit is not null || t.MarkerCost is not null ||
            !(e.Op switch {
                SkillProgramEffectOp.OfferLianjuSameColorGift => t.Window == SkillProgramTriggerWindow.TurnEnding && t.Optional && t.TurnOwnerScope == SkillProgramTurnOwnerScope.Own,
                SkillProgramEffectOp.ResolveLianjuOppositeColorReturn => t.Window == SkillProgramTriggerWindow.TurnEnding && t.Optional && t.TurnOwnerScope == SkillProgramTurnOwnerScope.OtherLiving,
                SkillProgramEffectOp.DrawSilvMarkedCardAfterDamage => t.Window == SkillProgramTriggerWindow.AfterDamageApplied && !t.Optional && t.Subject == SkillProgramTriggerSubject.Owner && t.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage,
                _ => t.Window == SkillProgramTriggerWindow.DiscardPileReceived && !t.Optional && t.MovementDiscardOnly && t.MovementOccurrence == SkillProgramMovementOccurrence.PerCard }))
            throw new InvalidOperationException(path + ": Li Wan requires her complete event-bound native operation.");
    }
}
