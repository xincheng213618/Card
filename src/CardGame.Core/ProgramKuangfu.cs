using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum KuangfuStage { EquipmentChoice, EquipmentChildren, SlashTargetChoice, SlashIssued, HandDiscardChoice, OutcomeChildren }

public sealed record KuangfuReceipt
{
    private IReadOnlyList<int> _selected = Array.Empty<int>();
    private IReadOnlyList<int> _discarded = Array.Empty<int>();
    public int InstructionIndex { get; init; }
    public CardConversionSource Source { get; init; } = null!;
    public string GameplayHash { get; init; } = "";
    public int ActualTurn { get; init; }
    public int ActualTurnOwnerSeat { get; init; }
    public KuangfuStage Stage { get; init; }
    public int? EquipmentOwnerSeat { get; init; }
    public int? EquipmentCardId { get; init; }
    public CardKind? EquipmentKind { get; init; }
    public long PaymentBefore { get; init; }
    public long PaymentAfter { get; init; }
    public KuangfuSlashReturn? SlashReturn { get; init; }
    public bool CausedDamage { get; init; }
    public int DiscardRequired { get; init; }
    public IReadOnlyList<int> SelectedHandIds { get => _selected; init => _selected = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray()); }
    public IReadOnlyList<int> DiscardedHandIds { get => _discarded; init => _discarded = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray()); }
    public long OutcomeBefore { get; init; }
    public long OutcomeAfter { get; init; }
    public int ActualDraw { get; init; }
}

public sealed record KuangfuSlashReturn(long ProgramFrameId, int InstructionIndex, CardConversionSource Source,
    string GameplayHash, int ActualTurn, int ActualTurnOwnerSeat, int EquipmentOwnerSeat, int EquipmentCardId,
    long CardUseFrameId, long CardActionId, int OriginalTargetSeat);

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public KuangfuReceipt? Kuangfu { get; init; }
}

public sealed record KuangfuStartedEvent(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int ActualTurnOwnerSeat) : IGameEvent;
public sealed record KuangfuEquipmentPaidEvent(long ProgramFrameId, int OriginalOwnerSeat, int CardId,
    CardKind PrintedKind, long Before, long After) : IGameEvent;
public sealed record KuangfuSlashIssuedEvent(KuangfuSlashReturn Return) : IGameEvent;
public sealed record KuangfuActualDamageObservedEvent(long ProgramFrameId, long CardUseFrameId,
    long DamageFrameId, int SourceSeat, int TargetSeat, int Amount, bool SourceLess) : IGameEvent;
public sealed record KuangfuSlashResolvedEvent(long ProgramFrameId, long CardUseFrameId, long CardActionId,
    bool CausedDamage) : IGameEvent;
public sealed record KuangfuHandDiscardPaidEvent(long ProgramFrameId, IReadOnlyList<int> CardIds,
    long Before, long After) : IGameEvent
{
    private IReadOnlyList<int> _cards = Array.AsReadOnly(CardIds.ToArray());
    public IReadOnlyList<int> CardIds { get => _cards; init => _cards = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray()); }
}
public sealed record KuangfuDrawIssuedEvent(long ProgramFrameId, int Requested, int Actual,
    long Before, long After) : IGameEvent;
public sealed record KuangfuFinishedEvent(long ProgramFrameId, bool EquipmentPaid, bool SlashIssued) : IGameEvent;

internal interface IKuangfuProgramHost
{
    SkillProgramStepOutcome DiscardEquipmentThenSlashAndOwnershipOutcome(ProgramSkillFrame frame);
}
internal sealed class DiscardEquipmentThenSlashAndOwnershipOutcomeDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardEquipmentThenSlashAndOwnershipOutcome;
    public override ISkillProgramEffectHandler Handler { get; } = new DiscardEquipmentThenSlashAndOwnershipOutcomeHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage,
        static (e, c) => c.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, e.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 1, r.Condition(),
            targetRestriction: SkillProgramCardTargetRestriction.DistanceUnlimitedAgainstTarget,
            outputKind: CardKind.Slash, useCardActionWindows: true);
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireActivationEntry()];
}
public sealed class DiscardEquipmentThenSlashAndOwnershipOutcomeHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardEquipmentThenSlashAndOwnershipOutcome;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        ((IKuangfuProgramHost)h).DiscardEquipmentThenSlashAndOwnershipOutcome(f);
}
internal static class KuangfuComposition
{
    internal static void Validate(string path, SkillProgramActivation a)
    {
        if (!a.Effects.Any(e => e.Op == SkillProgramEffectOp.DiscardEquipmentThenSlashAndOwnershipOutcome)) return;
        if (a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 0 || a.MaxTargets != 0 ||
            a.TargetKind != SkillProgramTargetKind.AnyLiving || a.UsesPerTurn is not null || a.UsesPerPhase != 1 ||
            a.UsesPerGame is not null || a.MarkerCost is not null || a.Condition.Kind != SkillProgramConditionKind.Always ||
            a.ContinueAfterOwnerDeath || a.Effects is not [{ Op: SkillProgramEffectOp.DiscardEquipmentThenSlashAndOwnershipOutcome,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException(path + ": equipment Slash requires one unconditional zero-input once-per-Play activation.");
    }
}
