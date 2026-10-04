namespace CardGame.Core;

public enum EquipmentDonationStage { PaidMovement, RecipientChoice, SelectingTargets, Damaging, RecoveryIssued, Complete }
public sealed record ProgramEquipmentDonationReceipt(int InstructionIndex, CardConversionSource Source,
    string GameplayHash, string UsageId, int TurnNumber, int TurnOwnerSeat, int RecipientSeat,
    IReadOnlyList<int> PaidCardIds, int ActualDeliveredCount, long SequenceBefore, long SequenceAfter,
    EquipmentDonationStage Stage, IReadOnlyList<int> DamageTargets, int DamageCursor = 0)
{
    private readonly IReadOnlyList<int> _paidCardIds = Array.AsReadOnly(PaidCardIds.ToArray());
    public IReadOnlyList<int> PaidCardIds { get => _paidCardIds; init => _paidCardIds = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<int> _damageTargets = Array.AsReadOnly(DamageTargets.ToArray());
    public IReadOnlyList<int> DamageTargets { get => _damageTargets; init => _damageTargets = Array.AsReadOnly(value.ToArray()); }
}
public sealed record EquipmentDonationEntityPaidEvent(long ProgramFrameId, CardConversionSource Source,
    int RecipientSeat, int CardId, int PaidIndex, long MovementSequence, bool Delivered) : IGameEvent;
public sealed record EquipmentDonationPaidEvent(long ProgramFrameId, CardConversionSource Source,
    string GameplayHash, string UsageId, int TurnNumber, int TurnOwnerSeat, int RecipientSeat,
    int PaidCount, int ActualDeliveredCount, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record EquipmentDonationBenefitIssuedEvent(long ProgramFrameId, CardConversionSource Source,
    int RecipientSeat, string Benefit, int ActualDeliveredCount, int TargetSeat, int Ordinal) : IGameEvent;

// One scalar per real target. No accepted action is invented for legacy virtual use.
public sealed record ActualTurnForeignUseTargetEvent(int TurnNumber, int TurnOwnerSeat, long CardUseFrameId,
    long? CardActionId, int ActorSeat, int ProviderSeat, CardKind EffectiveKind, int TargetSeat,
    long? LegacyProducerProgramId) : IGameEvent;
public sealed record ActualEndedTurnEquipmentContext(int TurnNumber, int TurnOwnerSeat,
    bool DamagedAnother, bool UsedOnAnother, int MaximumDistinctOptions);
public enum ActualEndedTurnEquipmentStage { Offering, Moving, Complete }
public sealed record ProgramActualEndedTurnEquipmentReceipt(int InstructionIndex, CardConversionSource Source,
    string GameplayHash, long ParentFrameId, int OccurrenceIndex, ActualEndedTurnEquipmentContext Qualification,
    ActualEndedTurnEquipmentStage Stage, bool EquipmentIssued = false, bool DrawIssued = false,
    ActualEndedTurnEquipmentPayment? LastPayment = null);
public sealed record ActualEndedTurnEquipmentPayment(string Option, int? EquipmentCardId,
    long SequenceBefore, long SequenceAfter, int ActualDrawCount = 0,
    int? ReplacedEquipmentCardId = null, bool ReplacedGeneralWeapon = false);
public sealed record ActualEndedTurnEquipmentOptionIssuedEvent(long ProgramFrameId, CardConversionSource Source,
    string GameplayHash, int TurnNumber, int TurnOwnerSeat, int MaximumDistinctOptions,
    string Option, int? EquipmentCardId, long SequenceBefore, long SequenceAfter, int ActualDrawCount,
    int? ReplacedEquipmentCardId = null, bool ReplacedGeneralWeapon = false) : IGameEvent;

internal interface IEquipmentDonationProgramHost
{
    SkillProgramStepOutcome DonateAllEquipmentAndOfferRecipientBenefits(ProgramSkillFrame frame, string usageId);
    SkillProgramStepOutcome ChooseEquipmentOrDrawAfterOtherActualTurn(ProgramSkillFrame frame);
}
internal sealed class DonateAllEquipmentAndOfferRecipientBenefitsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DonateAllEquipmentAndOfferRecipientBenefits;
    public override ISkillProgramEffectHandler Handler { get; } = new DonateAllEquipmentAndOfferRecipientBenefitsHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift,
        static (_, c) => c.PlaceSelectedEquipment());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stateId", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(),
            stateId: r.RequiredIdentifier("stateId"));
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget()];
}
internal sealed class ChooseEquipmentOrDrawAfterOtherActualTurnDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseEquipmentOrDrawAfterOtherActualTurn;
    public override ISkillProgramEffectHandler Handler { get; } = new ChooseEquipmentOrDrawAfterOtherActualTurnHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, c) => c.Draw(new SkillProgramEffect(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1,
            new(SkillProgramConditionKind.Always, 0, []))));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterTurnEnded)];
}
public sealed class DonateAllEquipmentAndOfferRecipientBenefitsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DonateAllEquipmentAndOfferRecipientBenefits;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int s, ISkillProgramEffectHost h) =>
        ((IEquipmentDonationProgramHost)h).DonateAllEquipmentAndOfferRecipientBenefits(f, e.StateId!);
}
public sealed class ChooseEquipmentOrDrawAfterOtherActualTurnHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ChooseEquipmentOrDrawAfterOtherActualTurn;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int s, ISkillProgramEffectHost h) =>
        ((IEquipmentDonationProgramHost)h).ChooseEquipmentOrDrawAfterOtherActualTurn(f);
}
internal static class EquipmentDonationComposition
{
    internal static void ValidateActivation(string path, SkillProgramActivation a)
    {
        if (!a.Effects.Any(e => e.Op == SkillProgramEffectOp.DonateAllEquipmentAndOfferRecipientBenefits)) return;
        if (a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 1 || a.MaxTargets != 1 ||
            a.TargetKind != SkillProgramTargetKind.OtherLiving || a.UsesPerTurn is not null || a.UsesPerPhase is not null ||
            a.UsesPerGame is not null || a.MarkerCost is not null || a.ContinueAfterOwnerDeath ||
            a.Condition.Kind != SkillProgramConditionKind.Always || a.Effects.Count != 1 ||
            a.Effects[0].Op != SkillProgramEffectOp.DonateAllEquipmentAndOfferRecipientBenefits)
            throw new InvalidOperationException($"Invalid skill program at {path}: all-equipment donation owns its post-payment game usage and one exact recipient.");
    }
    internal static void ValidateTrigger(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow window, SkillProgramTriggerSubject? subject, SkillProgramTurnOwnerScope scope, bool optional)
    {
        if (!effects.Any(e => e.Op == SkillProgramEffectOp.ChooseEquipmentOrDrawAfterOtherActualTurn)) return;
        if (window != SkillProgramTriggerWindow.AfterTurnEnded || subject != SkillProgramTriggerSubject.Owner ||
            scope != SkillProgramTurnOwnerScope.OtherLiving || !optional || effects.Count != 1 ||
            effects[0].Op != SkillProgramEffectOp.ChooseEquipmentOrDrawAfterOtherActualTurn)
            throw new InvalidOperationException($"Invalid skill program at {path}: ended-turn equipment options require one optional other-living actual-turn binding.");
    }
}
