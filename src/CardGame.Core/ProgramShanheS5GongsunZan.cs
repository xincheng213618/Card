using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface IShanheS5GongsunProgramHost
{
    SkillProgramStepOutcome DiscardAttackRangeCardsFromSlashTarget(ProgramSkillFrame frame);
}

internal sealed class DiscardAttackRangeCardsFromSlashTargetDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardAttackRangeCardsFromSlashTarget;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized),
         new RequireCardActionRelation(SkillProgramCardActionOwnerRelation.Actor, [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash])];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.DiscardSelected,
        static (_, c) => c.PublicControlValue(14));
    public override ISkillProgramEffectHandler Handler { get; } = new DiscardAttackRangeCardsFromSlashTargetHandler();
}

public sealed class DiscardAttackRangeCardsFromSlashTargetHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardAttackRangeCardsFromSlashTarget;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int t, ISkillProgramEffectHost h) =>
        h.GetCapability<IShanheS5GongsunProgramHost>().DiscardAttackRangeCardsFromSlashTarget(f);
}

public sealed record ShanheS5QiaomengState(long UseFrameId, long ActionId, int TargetSeat, int FrozenRange, int RequiredCount)
{
    private IReadOnlyList<int> _ids = Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<CardLocation> _from = Array.AsReadOnly(Array.Empty<CardLocation>());
    public IReadOnlyList<int> SelectedIds { get => _ids; init => _ids = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardLocation> OriginalLocations { get => _from; init => _from = Array.AsReadOnly(value.ToArray()); }
    public bool Paid { get; init; }
    public int Before { get; init; }
    public int After { get; init; }
    public long? BatchId { get; init; }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ShanheS5QiaomengState? ShanheS5Qiaomeng { get; init; }
}

public sealed record ShanheS5QiaomengStartedEvent(long FrameId, long UseFrameId, long ActionId,
    int OwnerSeat, int TargetSeat, int FrozenRange, int RequiredCount) : IGameEvent;
public sealed record ShanheS5QiaomengPaidEvent(long FrameId, long UseFrameId, int TargetSeat,
    int Count, int Before, int After) : IGameEvent;

internal static class ShanheS5GongsunContract
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        if (t.Effects.Any(e => e.Op == SkillProgramEffectOp.DiscardAttackRangeCardsFromSlashTarget) &&
            (t.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor ||
             !t.Optional || !ProgramCardKindMatch.IsFullSlashFamily(t.CardKinds) ||
             t.Effects is not [{ Op: SkillProgramEffectOp.DiscardAttackRangeCardsFromSlashTarget }]))
            throw new InvalidOperationException($"Invalid skill program at {path}: range discard requires one optional actor final-target Slash-family operation.");
    }
}
