namespace CardGame.Core;

public sealed record ProgramPairObtainPayment(int Cursor, int CardId, CardLocation From, int RecipientSeat,
    long SequenceBefore, long SequenceAfter, bool SameHand, bool Delivered);
public sealed record ProgramPairObtainDraft(int InstructionIndex, CardConversionSource Source, string GameplayHash,
    int ActualTurnNumber, int ActualTurnOwnerSeat, int PhaseInstanceId, int FirstSeat, int SecondSeat,
    int Cursor, bool AwaitingMovement, ProgramPairObtainPayment? FirstPayment = null, ProgramPairObtainPayment? SecondPayment = null);
public enum ProgramShownPairGiftStage { ChoosingRecipient, AwaitingGift, AwaitingReward, Complete }
public sealed record ProgramShownPairGiftReceipt(int InstructionIndex, string SourceBind, int CardId, Suit FrozenSuit,
    int FirstSeat, int SecondSeat, int FrozenFirstHandCount, int FrozenSecondHandCount,
    int? RecipientSeat, ProgramShownPairGiftStage Stage, long SequenceBefore, long SequenceAfter,
    bool SameHand = false, bool Delivered = false, long DrawSequenceBefore = 0, long DrawSequenceAfter = 0, int ActualDrawCount = 0);
public sealed record ProgramFixedRecipientReceipt(int InstructionIndex, string StateId, long IssuanceProgramFrameId,
    CardConversionSource Source, string GameplayHash, int RecipientSeat, int ActualTurnNumber,
    int ActualTurnOwnerSeat, bool DeathReplay, long? OwnerDeathWindowFrameId,
    bool DrawIssued = false, long DrawSequenceBefore = 0, long DrawSequenceAfter = 0, int ActualDrawCount = 0,
    int? RecoveryHpBefore = null, int RecoveryAmount = 0);

// All payloads are scalar. A private entity enters only its owning receipt;
// obtaining a concealed hand card does not publish its identity.
public sealed record PairObtainStartedEvent(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurnNumber, int ActualTurnOwnerSeat, int PhaseInstanceId, int FirstSeat, int SecondSeat) : IGameEvent;
public sealed record PairObtainStepCommittedEvent(long ProgramFrameId, int Cursor, int CardOwnerSeat, int RecipientSeat,
    long SequenceBefore, long SequenceAfter, bool SameHand, bool Delivered) : IGameEvent;
public sealed record ShownPairGiftCommittedEvent(long ProgramFrameId, string SourceBind, int ShownCardId, Suit FrozenSuit,
    int RecipientSeat, int FrozenFirstHandCount, int FrozenSecondHandCount, long SequenceBefore, long SequenceAfter,
    bool SameHand, bool Delivered) : IGameEvent;
public sealed record ShownPairGiftRewardIssuedEvent(long ProgramFrameId, int OwnerSeat, int RequestedCount, int ActualCount,
    long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record FixedRecipientBenefitIssuedEvent(long ProgramFrameId, string StateId, CardConversionSource Source,
    string GameplayHash, int RecipientSeat, int ActualTurnNumber, int ActualTurnOwnerSeat) : IGameEvent;
public sealed record FixedRecipientDeathBenefitStartedEvent(long ProgramFrameId, long IssuanceProgramFrameId,
    long OwnerDeathWindowFrameId, string StateId, CardConversionSource Source, string GameplayHash, int RecipientSeat) : IGameEvent;
public sealed record FixedRecipientBenefitDrawIssuedEvent(long ProgramFrameId, long IssuanceProgramFrameId,
    int RecipientSeat, bool DeathReplay, long SequenceBefore, long SequenceAfter, int ActualCount) : IGameEvent;
public sealed record FixedRecipientBenefitRecoveryRequestedEvent(long ProgramFrameId, long IssuanceProgramFrameId,
    int RecipientSeat, bool DeathReplay, int HpBefore, int Amount) : IGameEvent;

internal interface IPairObtainFixedRecipientProgramHost
{
    SkillProgramStepOutcome ObtainOneFromEachSelectedTarget(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome GiveShownCardToLeastOriginalTarget(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome IssueFixedRecipientBenefit(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome SelectIssuedFixedRecipient(ProgramSkillFrame frame, SkillProgramEffect effect);
}
internal abstract class PairObtainFixedRecipientDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, c) => c.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, new(SkillProgramConditionKind.Always, 0, []))));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "zones", "sourceBind", "stateId", "condition");
        var zones = r.OptionalEnumArray<CardZoneKind>("zones") ?? [];
        var bind = r.Has("sourceBind") ? r.RequiredIdentifier("sourceBind") : null;
        var state = r.Has("stateId") ? r.RequiredIdentifier("stateId") : null;
        if (Op == SkillProgramEffectOp.ObtainOneFromEachSelectedTarget
            ? !zones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment]) || bind is not null || state is not null
            : zones.Count != 0 || (Op == SkillProgramEffectOp.GiveShownCardToLeastOriginalTarget ? bind is null || state is not null : bind is not null || state is null))
            throw new InvalidOperationException($"Invalid pair/fixed-recipient operation at {r.Path}.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), zones: zones, sourceBind: bind, stateId: state);
        RequireAlways(effect, r.Path); return effect;
    }
}
internal sealed class ObtainOneFromEachSelectedTargetDescriptor : PairObtainFixedRecipientDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ObtainOneFromEachSelectedTarget;
    public override ISkillProgramEffectHandler Handler { get; } = new ObtainOneFromEachSelectedTargetHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadTargetSet(2, 2)];
}
internal sealed class GiveShownCardToLeastOriginalTargetDescriptor : PairObtainFixedRecipientDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GiveShownCardToLeastOriginalTarget;
    public override ISkillProgramEffectHandler Handler { get; } = new GiveShownCardToLeastOriginalTargetHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new ReadTargetSet(2, 2), new RequireOwnedCardSet(e.SourceBind!, SkillProgramEffectTarget.Owner, 1, [CardZoneKind.Hand]),
         new RequirePublicOwnedGiftSet(e.SourceBind!), new MoveCardSet(e.SourceBind!, null, SkillProgramCardDestination.SelectedTargetHand)];
}
internal sealed class IssueFixedRecipientBenefitDescriptor : PairObtainFixedRecipientDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.IssueFixedRecipientBenefit;
    public override ISkillProgramEffectHandler Handler { get; } = new IssueFixedRecipientBenefitHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.SelectTarget, static (_, _) => { });
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding), new ReadSelectedTarget(), new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving)];
}
internal sealed class SelectIssuedFixedRecipientDescriptor : PairObtainFixedRecipientDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectIssuedFixedRecipient;
    public override ISkillProgramEffectHandler Handler { get; } = new SelectIssuedFixedRecipientHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.SelectTarget, static (_, _) => { });
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.OwnerDied), new SelectSingleTarget()];
}
public sealed class ObtainOneFromEachSelectedTargetHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ObtainOneFromEachSelectedTarget;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => ((IPairObtainFixedRecipientProgramHost)h).ObtainOneFromEachSelectedTarget(f, e);
}
public sealed class GiveShownCardToLeastOriginalTargetHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GiveShownCardToLeastOriginalTarget;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => ((IPairObtainFixedRecipientProgramHost)h).GiveShownCardToLeastOriginalTarget(f, e);
}
public sealed class IssueFixedRecipientBenefitHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.IssueFixedRecipientBenefit;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => ((IPairObtainFixedRecipientProgramHost)h).IssueFixedRecipientBenefit(f, e);
}
public sealed class SelectIssuedFixedRecipientHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SelectIssuedFixedRecipient;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => ((IPairObtainFixedRecipientProgramHost)h).SelectIssuedFixedRecipient(f, e);
}
