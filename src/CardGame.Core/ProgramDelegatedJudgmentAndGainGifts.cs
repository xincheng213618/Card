using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum DelegatedJudgmentStage { Offered, Choosing, Chosen, Paid }
public sealed record DelegatedJudgmentDraft(long JudgmentFrameId, CardConversionSource Source,
    string GameplayHash, string TriggerId, int SubjectSeat, int OldCardId,
    DelegatedJudgmentStage Stage, IReadOnlyList<CardSnapshot> Material, IReadOnlyList<CardSnapshot> HandView,
    int? SelectedCardId = null)
{
    private readonly IReadOnlyList<CardSnapshot> _material = Array.AsReadOnly(Material.ToArray());
    public IReadOnlyList<CardSnapshot> Material { get => _material; init => _material = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<CardSnapshot> _handView = Array.AsReadOnly(HandView.ToArray());
    public IReadOnlyList<CardSnapshot> HandView { get => _handView; init => _handView = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record JudgmentFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DelegatedJudgmentDraft? DelegatedReplacement { get; init; }
}
public sealed record RedOwnedLossMaterial(int OwnerSeat, CardSnapshot Card, Suit EffectiveSuit,
    CardMovementTiming Timing, long MovementSequence);
public sealed partial record CardMovementRecord
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RedOwnedLossMaterial? RedOwnedLoss { get; init; }
}
public enum GainGiftStage { Choosing, GiftChildren, RedDrawChildren, ReplacementChildren }
public sealed record GainGiftProgramReceipt(int InstructionIndex, SkillProgramEffectOp Operation,
    GainGiftStage Stage, long OriginalParentId, long BatchId, ActualDiscardRecoveryPhaseKey? Phase,
    IReadOnlyList<int> GivenTargets, IReadOnlyList<int> GivenCards, long Before, long After,
    IReadOnlyList<RedOwnedLossMaterial> RedLosses, int LossIndex = 0,
    CardLocation? PaymentFrom = null, int? PaymentCardId = null, int? PaymentTarget = null,
    int ActualDrawCount = 0)
{
    private readonly IReadOnlyList<int> _targets = Array.AsReadOnly(GivenTargets.ToArray());
    public IReadOnlyList<int> GivenTargets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<int> _cards = Array.AsReadOnly(GivenCards.ToArray());
    public IReadOnlyList<int> GivenCards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<RedOwnedLossMaterial> _losses = Array.AsReadOnly(RedLosses.ToArray());
    public IReadOnlyList<RedOwnedLossMaterial> RedLosses { get => _losses; init => _losses = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GainGiftProgramReceipt? GainGiftReceipt { get; init; }
}
public sealed record DelegatedJudgmentViewedEvent(long JudgmentFrameId, CardConversionSource Source,
    int SubjectSeat, int ViewedHandCount) : IGameEvent;
public sealed record GainGiftPhaseIssuedEvent(long ProgramFrameId, CardConversionSource Source,
    string GameplayHash, long BatchId, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record GainGiftPaidEvent(long ProgramFrameId, int CardId, CardLocation From,
    int TargetSeat, long Before, long After) : IGameEvent;
public sealed record RedOwnedLossRevealedEvent(long ProgramFrameId, int OwnerSeat,
    CardSnapshot Card, Suit EffectiveSuit, long OriginalMovementSequence) : IGameEvent;
public sealed record GainGiftDrawPaidEvent(long ProgramFrameId, long OriginalMovementSequence,
    int ActualCount, long Before, long After) : IGameEvent;

internal interface IDelegatedJudgmentAndGainGiftHost
{
    SkillProgramStepOutcome ExecuteGainGift(ProgramSkillFrame frame, SkillProgramEffect effect);
}
internal sealed class DelegateJudgmentReplacementDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DelegateJudgmentReplacement;
    public override ISkillProgramEffectHandler Handler { get; } = new DelegatedJudgmentAndGainGiftHandler(SkillProgramEffectOp.DelegateJudgmentReplacement);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.JudgmentReplacement;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ReplaceJudgment, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "zones", "suits", "oldCardDestination", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(),
            zones: r.RequiredEnumArray<CardZoneKind>("zones"), suits: r.RequiredEnumArray<Suit>("suits"),
            oldCardDestination: r.RequiredEnum<SkillProgramOldJudgmentCardDestination>("oldCardDestination"));
        if (!e.Zones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) ||
            !e.Suits.Order().SequenceEqual(new[] { Suit.Spade, Suit.Club, Suit.Heart, Suit.Diamond }.Order()) ||
            e.OldCardDestination != SkillProgramOldJudgmentCardDestination.DiscardPile)
            throw new InvalidOperationException(r.Path + ": delegated judgment requires all suits, transferable HE and discard-old semantics.");
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.JudgmentReplacing)];
}
internal abstract class GainGiftDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new DelegatedJudgmentAndGainGiftHandler(Op);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(e, r.Path); return e;
    }
}
internal sealed class GiveAfterBatchGainDescriptor : GainGiftDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GiveAfterBatchGain;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift, static (_, _) => { });
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsGained)];
}
internal sealed class RevealRedLossAndDrawDescriptor : GainGiftDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RevealRedLossAndDraw;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (e, c) => c.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, e.Condition)));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsMoved)];
}
internal sealed class DelegatedJudgmentAndGainGiftHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IDelegatedJudgmentAndGainGiftHost)host).ExecuteGainGift(f, e);
}
