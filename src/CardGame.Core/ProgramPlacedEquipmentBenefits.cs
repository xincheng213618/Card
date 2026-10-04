namespace CardGame.Core;

public enum PlacedEquipmentBenefitStage { EquipmentChoice, RecipientChoice, PlacementChildren, WeaponTarget, WeaponCard, DiscardChildren, DrawChildren, RecoveryChildren }
// All members are scalars or scalar records. No exposed collection is added to snapshots or committed events.
public sealed record PlacedEquipmentPayment(int CardId, CardKind PrintedKind, CardLocation From, int RecipientSeat,
    EquipmentSlot Slot, int? ReplacedCardId, bool ReplacedGeneralWeapon, long SequenceBefore, long SequenceAfter);
public sealed record PlacedEquipmentDiscard(int TargetSeat, int CardId, CardKind PrintedKind, CardLocation From,
    bool GeneralWeapon, long SequenceBefore, long SequenceAfter);
public sealed record ProgramPlacedEquipmentBenefitReceipt(int InstructionIndex, CardConversionSource Source, string GameplayHash,
    int TurnNumber, int TurnOwnerSeat, long EndingFrameId, int OccurrenceIndex, PlacedEquipmentBenefitStage Stage,
    int? SelectedCardId = null, CardLocation? SelectedFrom = null, PlacedEquipmentPayment? Placement = null,
    int? WeaponTargetSeat = null, PlacedEquipmentDiscard? Discard = null, ProgramOneCardDrawInvoice? Draw = null,
    bool RecoveryIssued = false, int ActualRecoveryRequest = 0);
public sealed record PlacedEquipmentBenefitStartedEvent(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    int TurnNumber, int TurnOwnerSeat, long EndingFrameId, int OccurrenceIndex) : IGameEvent;
public sealed record PlacedEquipmentBenefitPaidEvent(long ProgramFrameId, PlacedEquipmentPayment Payment) : IGameEvent;
public sealed record PlacedEquipmentBenefitDiscardedEvent(long ProgramFrameId, PlacedEquipmentDiscard Payment) : IGameEvent;
public sealed record PlacedEquipmentBenefitDrawnEvent(long ProgramFrameId, ProgramOneCardDrawInvoice Invoice) : IGameEvent;
public sealed record PlacedEquipmentBenefitRecoveryIssuedEvent(long ProgramFrameId, int RecipientSeat, int RequestedAmount) : IGameEvent;
public sealed record PlacedEquipmentBenefitFinishedEvent(long ProgramFrameId, bool Placed) : IGameEvent;

internal interface IPlacedEquipmentBenefitProgramHost
{ SkillProgramStepOutcome PlaceOwnedEquipmentThenResolveSlotBenefit(ProgramSkillFrame frame); }
internal sealed class PlaceOwnedEquipmentThenResolveSlotBenefitDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PlaceOwnedEquipmentThenResolveSlotBenefit;
    public override ISkillProgramEffectHandler Handler { get; } = new PlaceOwnedEquipmentThenResolveSlotBenefitHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, c) => c.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, new(SkillProgramConditionKind.Always, 0, []))));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}
public sealed class PlaceOwnedEquipmentThenResolveSlotBenefitHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.PlaceOwnedEquipmentThenResolveSlotBenefit;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IPlacedEquipmentBenefitProgramHost)host).PlaceOwnedEquipmentThenResolveSlotBenefit(f);
}
internal static class PlacedEquipmentBenefitComposition
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects, SkillProgramTriggerWindow window,
        SkillProgramTriggerSubject? subject, SkillProgramTurnOwnerScope scope, bool optional)
    {
        if (!effects.Any(e => e.Op == SkillProgramEffectOp.PlaceOwnedEquipmentThenResolveSlotBenefit)) return;
        if (window != SkillProgramTriggerWindow.TurnEnding || subject != SkillProgramTriggerSubject.Owner ||
            scope != SkillProgramTurnOwnerScope.Own || !optional || effects is not
                [{ Op: SkillProgramEffectOp.PlaceOwnedEquipmentThenResolveSlotBenefit, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException($"{path}: equipment placement benefits require one optional own actual Ending instruction.");
    }
}
