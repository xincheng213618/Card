using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record ForeignActualTurnStartWindowFrame(long Id, int ActualTurnNumber, int OwnerSeat,
    IReadOnlyList<ProgramTriggerCandidate> Candidates, IReadOnlyList<ProgramSkillWindowContext> Contexts,
    int CandidateIndex = 0, ResolutionFrameStep Step = ResolutionFrameStep.ResolvingEffect)
    : ResolutionFrame(Id, ResolutionFrameKind.ForeignActualTurnStartWindow, Step)
{
    private readonly IReadOnlyList<ProgramTriggerCandidate> _candidates = Array.AsReadOnly(Candidates.ToArray());
    public IReadOnlyList<ProgramTriggerCandidate> Candidates { get => _candidates; init => _candidates = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<ProgramSkillWindowContext> _contexts = PaidTargetFrameCollections.FreezeContexts(Contexts);
    public IReadOnlyList<ProgramSkillWindowContext> Contexts { get => _contexts; init => _contexts = PaidTargetFrameCollections.FreezeContexts(value); }
}

// All new public payloads are scalar. PindianResult itself contains no collection.
public sealed record ForeignTurnPindianOrigin(long PindianFrameId, long ProgramFrameId,
    int ActualTurnNumber, int ActualTurnOwnerSeat, string GameplayHash, PindianResult Result);
public enum ForeignTurnContestStage { ClaimChildren, SlashIssued, Complete }
public sealed record ForeignTurnContestSlashReturn(long ProgramFrameId, int InstructionIndex,
    CardConversionSource Source, ForeignTurnPindianOrigin Origin, long CardUseFrameId, long CardActionId);
public sealed record ForeignTurnContestReceipt(int InstructionIndex, string SourceBind,
    ForeignTurnPindianOrigin Origin, ForeignTurnContestStage Stage, bool RestrictionIssued = false,
    int? ClaimedCardId = null, int? MovementSequence = null, ForeignTurnContestSlashReturn? SlashReturn = null);
public sealed record ForeignTurnContestOriginFrozenEvent(ForeignTurnPindianOrigin Origin,
    CardConversionSource Source, string ResultBind) : IGameEvent;
public sealed record ForeignTurnContestCardClaimedEvent(long ProgramFrameId, CardConversionSource Source,
    ForeignTurnPindianOrigin Origin, int CardId, int MovementSequence) : IGameEvent;
public sealed record ForeignTurnContestRestrictedEvent(long ProgramFrameId, CardConversionSource Source,
    ForeignTurnPindianOrigin Origin, long GrantSequence) : IGameEvent;
public sealed record ForeignTurnContestSlashIssuedEvent(ForeignTurnContestSlashReturn Return) : IGameEvent;
public sealed record ForeignTurnContestSlashReturnedEvent(ForeignTurnContestSlashReturn Return) : IGameEvent;

public enum SameTypeAidStage { ChoosingRecipient, ChoosingAnswer, GiftChildren, Complete }
public sealed record SameTypeAidIdentity(long CardUseFrameId, long? CardActionId, int ActorSeat,
    int ProviderSeat, CardKind EffectiveKind, int OriginalOwnerTarget, int ActualTurnNumber,
    int ActualTurnOwnerSeat, long? LegacyProducerProgramId = null);
public sealed record SameTypeAidPayment(int CardId, CardKind PrintedKind, CardLocation From,
    int MovementSequence, int SequenceAfter, bool Delivered);
public sealed record SameTypeAidReceipt(int InstructionIndex, CardConversionSource Source,
    string GameplayHash, SameTypeAidIdentity Use, SameTypeAidStage Stage,
    int? RecipientSeat = null, SameTypeAidPayment? Payment = null, bool AddedTarget = false);
public sealed record SameTypeAidOfferedEvent(long ProgramFrameId, CardConversionSource Source,
    string GameplayHash, SameTypeAidIdentity Use, int RecipientSeat) : IGameEvent;
public sealed record SameTypeAidGiftPaidEvent(long ProgramFrameId, CardConversionSource Source,
    SameTypeAidIdentity Use, int RecipientSeat, int MovementSequence, int SequenceAfter, bool Delivered) : IGameEvent;
public sealed record SameTypeAidTargetAddedEvent(long ProgramFrameId, CardConversionSource Source,
    SameTypeAidIdentity Use, int RecipientSeat) : IGameEvent;

internal interface IForeignTurnContestAidProgramHost
{
    SkillProgramStepOutcome ResolveForeignTurnPindian(ProgramSkillFrame frame, string resultBind);
    SkillProgramStepOutcome OfferSameTypeDifferentNameOrExtraTarget(ProgramSkillFrame frame);
}
internal sealed class ResolveForeignTurnPindianDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveForeignTurnPindian;
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveForeignTurnPindianHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnCardTargetRestriction,
        static (_, c) => c.PublicControlValue(12));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0,
            r.Condition(), sourceBind: r.RequiredIdentifier("sourceBind"));
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.OtherActualTurnStarted), new ReadPindianResult(e.SourceBind!)];
}
internal sealed class OfferSameTypeDifferentNameOrExtraTargetDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.OfferSameTypeDifferentNameOrExtraTarget;
    public override ISkillProgramEffectHandler Handler { get; } = new OfferSameTypeDifferentNameOrExtraTargetHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, c) => c.PublicControlValue(10));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindows([SkillProgramTriggerWindow.CardUseTargetsFinalized, SkillProgramTriggerWindow.OtherActualUseTargeted])];
}
public sealed class ResolveForeignTurnPindianHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveForeignTurnPindian;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int s, ISkillProgramEffectHost h) =>
        ((IForeignTurnContestAidProgramHost)h).ResolveForeignTurnPindian(f, e.SourceBind!);
}
public sealed class OfferSameTypeDifferentNameOrExtraTargetHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.OfferSameTypeDifferentNameOrExtraTarget;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int s, ISkillProgramEffectHost h) =>
        ((IForeignTurnContestAidProgramHost)h).OfferSameTypeDifferentNameOrExtraTarget(f);
}
internal static class ForeignTurnContestAidComposition
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow window, SkillProgramTriggerSubject? subject, bool optional,
        SkillProgramCardActionOwnerRelation? relation, IReadOnlyList<CardKind> kinds)
    {
        if (window == SkillProgramTriggerWindow.OtherActualTurnStarted ||
            effects.Any(e => e.Op == SkillProgramEffectOp.ResolveForeignTurnPindian))
        {
            if (window != SkillProgramTriggerWindow.OtherActualTurnStarted || subject != SkillProgramTriggerSubject.Owner ||
                !optional || effects.Count != 3 ||
                effects[0] is not { Op: SkillProgramEffectOp.SelectTarget, TargetKind: SkillProgramTargetKind.EventSource, Condition.Kind: SkillProgramConditionKind.Always } ||
                effects[1] is not { Op: SkillProgramEffectOp.StartPindian, OpponentReference.Kind: ProgramParticipantRef.SelectedTarget,
                    Visibility: SkillProgramCardSetVisibility.Public, Condition.Kind: SkillProgramConditionKind.Always } contest ||
                effects[2] is not { Op: SkillProgramEffectOp.ResolveForeignTurnPindian, Condition.Kind: SkillProgramConditionKind.Always } result ||
                result.SourceBind != contest.ResultBind)
                throw new InvalidOperationException($"Invalid skill program at {path}: foreign turn contest requires the exact optional source-target/Pindian/result pipeline.");
        }
        if (effects.Any(e => e.Op == SkillProgramEffectOp.OfferSameTypeDifferentNameOrExtraTarget) &&
            (!optional || effects.Count != 1 || effects[0].Op != SkillProgramEffectOp.OfferSameTypeDifferentNameOrExtraTarget ||
             window is not (SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.OtherActualUseTargeted) ||
             window == SkillProgramTriggerWindow.OtherActualUseTargeted && subject != SkillProgramTriggerSubject.Owner ||
             window == SkillProgramTriggerWindow.CardUseTargetsFinalized && (relation != SkillProgramCardActionOwnerRelation.Target ||
                 kinds.Count != 7 || !kinds.ToHashSet().SetEquals(new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash,
                     CardKind.Duel, CardKind.FireAttack, CardKind.BarbarianAssault, CardKind.ArrowBarrage }))))
            throw new InvalidOperationException($"Invalid skill program at {path}: actual-use aid requires one optional target instruction.");
    }
}
