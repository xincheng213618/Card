using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum MoQuanyuEffect { None, Baihong, Qingming, Bixie, Zidian, Baili, Liuxing }
public enum MoQuanyuInvocationKind { Round, TargetOnly }
public enum MoQuanyuStage { Choosing, Drawing, DrawChildren }
public sealed record MoQuanyuParticipant(int Index, int Seat, int HistoryMask, int AvailableMask);
public sealed record MoQuanyuVote(int Seat, MoQuanyuEffect Effect);
public sealed record MoQuanyuTargetRequest(long ParentFrameId, int ParentInstructionIndex,
    CardConversionSource ParentSource, string ParentGameplayHash, long UseId, long ActionId,
    int TargetSeat, int RoundNumber, int ActualTurn, int TurnOwner,
    CardConversionSource QuanyuSource, string QuanyuGameplayHash, long DiscardBefore, long DiscardAfter);
public sealed record MoQuanyuTargetChoiceReturn(long ChildFrameId, MoQuanyuTargetRequest Request,
    MoQuanyuEffect ChosenEffect, int DrawCount);
public sealed record MoQuanyuPublicChoice(int IssuerSeat, int ParticipantSeat, int RoundNumber,
    MoQuanyuEffect CurrentChoice, int HistoryMask);
public sealed record MoQuanyuReceipt(CardConversionSource Source, string GameplayHash,
    MoQuanyuInvocationKind Kind, int RoundNumber, int ActualTurn, int TurnOwner,
    MoQuanyuEffect OwnerChoiceBefore)
{
    public MoQuanyuStage Stage { get; init; }
    private readonly IReadOnlyList<MoQuanyuParticipant> _participants = Array.AsReadOnly(Array.Empty<MoQuanyuParticipant>());
    public IReadOnlyList<MoQuanyuParticipant> Participants
    { get => _participants; init => _participants = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<MoQuanyuVote> _votes = Array.AsReadOnly(Array.Empty<MoQuanyuVote>());
    public IReadOnlyList<MoQuanyuVote> Votes
    { get => _votes; init => _votes = Array.AsReadOnly(value.ToArray()); }
    public int DrawCount { get; init; }
    public NativeDrawOutcome? DrawOutcome { get; init; }
    public MoQuanyuTargetRequest? TargetRequest { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MoQuanyuReceipt? MoQuanyu { get; init; }
}
public sealed record MoQuanyuChoicesStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    MoQuanyuInvocationKind Kind, int RoundNumber, int ActualTurn, int TurnOwner,
    MoQuanyuEffect OwnerChoiceBefore, int ParticipantCount) : IGameEvent;
public sealed record MoQuanyuParticipantFrozenEvent(long FrameId, int Index, int Seat, int HistoryMask, int AvailableMask) : IGameEvent;
public sealed record MoQuanyuTargetRequestIssuedEvent(long ChildFrameId, MoQuanyuTargetRequest Request) : IGameEvent;
public sealed record MoQuanyuChoiceRevealedEvent(long FrameId, int Index, int IssuerSeat, int ParticipantSeat,
    int RoundNumber, MoQuanyuEffect Effect, int HistoryBefore, int HistoryAfter) : IGameEvent;
public sealed record MoQuanyuRevealCompletedEvent(long FrameId, MoQuanyuEffect OwnerChoice, int MatchCount, int DrawCount) : IGameEvent;
public sealed record MoQuanyuDrawRequestedEvent(long FrameId, int RecipientSeat, int RequestedCount) : IGameEvent;
public sealed record MoQuanyuDrawReturnedEvent(long FrameId, int RequestedCount, int ActualCount,
    int ReplacementCount, long Before, long After, long? AttemptFrameId) : IGameEvent;
public sealed record MoQuanyuChoicesCompletedEvent(long FrameId, MoQuanyuInvocationKind Kind, int DrawCount) : IGameEvent;
public sealed record MoQuanyuTargetChoiceReturnedEvent(MoQuanyuTargetChoiceReturn Result) : IGameEvent;

internal interface IMoSunQuanQuanyuHost
{
    SkillProgramStepOutcome BeginRound(ProgramSkillFrame frame);
    SkillProgramStepOutcome BeginTarget(ProgramSkillFrame frame);
}
internal abstract class MoQuanyuDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Automatic;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        op == SkillProgramEffectOp.ResolveMoSunQuanQuanyuRoundChoices ? [new RequireTriggerWindow(SkillProgramTriggerWindow.RoundStarting)] : [];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, c) => c.PublicControlValue(2));
    public override ISkillProgramEffectHandler Handler { get; } = new MoQuanyuHandler(op);
}
internal sealed class ResolveMoSunQuanQuanyuRoundChoicesDescriptor() : MoQuanyuDescriptor(SkillProgramEffectOp.ResolveMoSunQuanQuanyuRoundChoices);
internal sealed class ResolveMoSunQuanQuanyuTargetChoiceDescriptor() : MoQuanyuDescriptor(SkillProgramEffectOp.ResolveMoSunQuanQuanyuTargetChoice);
internal sealed class MoQuanyuHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target,
        ISkillProgramEffectHost host) => op == SkillProgramEffectOp.ResolveMoSunQuanQuanyuRoundChoices
        ? host.GetCapability<IMoSunQuanQuanyuHost>().BeginRound(frame) : host.GetCapability<IMoSunQuanQuanyuHost>().BeginTarget(frame);
}

internal static class MoQuanyuComposition
{
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (p.Id != "ol:mo-quanyu" && !p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects))
            .Any(e => e.Op is SkillProgramEffectOp.ResolveMoSunQuanQuanyuRoundChoices or SkillProgramEffectOp.ResolveMoSunQuanQuanyuTargetChoice or SkillProgramEffectOp.ResolveMoSunQuanQuanyuSlashEffects)) return;
        if (p.Triggers.SingleOrDefault(t => t.Id == "singleton-slash-effects") is { } nativeSlash)
            MoQuanyuSlashComposition.ValidateTrigger(path, nativeSlash);
        if (p.Id != "ol:mo-quanyu" || p.Triggers.Count != 2 || p.Activations is not [var a] ||
            p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 ||
            p.HpSkillGrants.Count != 0 || p.DamageModifiers.Count != 0 || p.BooleanStates.Count != 0 || p.Contributions.Count != 0 ||
            p.Triggers.SingleOrDefault(t => t.Id == "round-choices") is not { } round ||
            p.Triggers.SingleOrDefault(t => t.Id == "singleton-slash-effects") is not { } slash ||
            !ExactTrigger(round, false) || !ExactTrigger(slash, true) || a.Id != "target-only-choices" ||
            a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 0 || a.MaxTargets != 0 ||
            a.TargetKind != SkillProgramTargetKind.AnyLiving || !a.SourceZones.SequenceEqual([CardZoneKind.Hand]) ||
            a.UsesPerTurn is not null || a.UsesPerPhase is not null || a.UsesPerGame is not null || a.UsageGroup != a.Id ||
            a.Condition.Kind != SkillProgramConditionKind.Always || a.MarkerCost is not null || a.CategoryTargetLedgerId is not null ||
            a.TargetPhaseLedgerId is not null || a.CardCountExpression is not null || a.SelectedCardsSameSuit || a.SelectedCardsDistinctSuits ||
            a.CardKinds.Count != 0 || a.CardSuits.Count != 0 || a.CardCategories.Count != 0 || a.EquipmentSlots.Count != 0 ||
            a.TargetRequiresEmptyEquipmentSlot || a.OtherCharactersOnly || a.ContinueAfterOwnerDeath ||
            a.Effects is not [{ Op: SkillProgramEffectOp.ResolveMoSunQuanQuanyuTargetChoice, Target: SkillProgramEffectTarget.Owner,
                Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException(path + ": Mo Quanyu requires its complete simultaneous round choices, private native target-only child, and exact singleton Slash effects.");
    }
    private static bool ExactTrigger(SkillProgramTrigger t, bool slash) =>
        t.Window == (slash ? SkillProgramTriggerWindow.CardUseTargetsFinalized : SkillProgramTriggerWindow.RoundStarting) &&
        !t.Optional && t.Subject == (slash ? null : SkillProgramTriggerSubject.Owner) &&
        t.Condition.Kind == SkillProgramTriggerConditionKind.Always && t.UsageLimit is null && t.UsageScope is null &&
        t.DynamicUsageLimit is null && t.NamedUsageGroup is null && t.MarkerCost is null && t.ChoiceGroup is null &&
        t.OrderGroup is null && !t.EvaluateConditionAtResolution &&
        t.OwnerRelation == (slash ? SkillProgramCardActionOwnerRelation.Actor : null) &&
        t.SourceSkillId is null && t.SourceViewAsId is null &&
        (slash ? t.CardKinds.SequenceEqual([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]) : t.CardKinds.Count == 0) &&
        t.CardCategories.Count == 0 && t.DamageCardKinds.Count == 0 && t.Suits.Count == 0 &&
        t.SourceZones.Count == 0 && t.DestinationZones.Count == 0 && t.MovementReasons.Count == 0 &&
        t.ExcludedMovementReasons.Count == 0 && t.ExcludedReasons.Count == 0 && t.MovementOccurrence is null &&
        !t.MovementDiscardOnly && !t.IncludeResponseUses && t.SingleActionInstance == slash && !t.DeferredTurnEndOnly &&
        !t.IgnoreOwnSkillMovements && t.GainPhaseQualification is null && t.DamageOccurrence is null &&
        !t.NoDyingAtActivation && t.RequireDamageSource is null && t.RequireNoCardConversion is null &&
        !t.AllowNoEventTarget && !t.OnlyDesignatedCardTargets && !t.AllowOwnDiscardPhaseEnded &&
        t.Effects is [var e] && e.Op == (slash ? SkillProgramEffectOp.ResolveMoSunQuanQuanyuSlashEffects : SkillProgramEffectOp.ResolveMoSunQuanQuanyuRoundChoices) &&
        e.Target == SkillProgramEffectTarget.Owner && e.Condition.Kind == SkillProgramConditionKind.Always;
}
