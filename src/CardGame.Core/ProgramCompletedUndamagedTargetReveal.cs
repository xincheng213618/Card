using System.Text.Json.Serialization;

namespace CardGame.Core;

/// <summary>Independent whole-use damage evidence. It is never consumed by a completion observer.</summary>
public sealed record CompletedUndamagedUseDamageRecordedEvent(long CardUseFrameId, long? CardActionId,
    long DamageFrameId, int ActorSeat, int SourceSeat, int TargetSeat, CardKind EffectiveKind,
    int Amount, DamageNature Nature, bool SourceLess, bool ChainPropagation, bool Redirected) : IGameEvent;

public enum CompletedUndamagedTargetRevealStage { ChoosingTarget, ChoosingCards, MovementChildren, Complete }
public sealed record CompletedUndamagedHandMaterial(int CardId, CardKind PrintedKind, int Slot);
public sealed record CompletedUndamagedRevealedMaterial(int CardId, CardKind PrintedKind, int Slot,
    Suit EffectiveSuit, CardColor? EffectiveColor);

public sealed record ProgramCompletedUndamagedTargetRevealReceipt
{
    private IReadOnlyList<int> _targets = Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<int> _eligible = Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<CompletedUndamagedHandMaterial> _hand = Array.AsReadOnly(Array.Empty<CompletedUndamagedHandMaterial>());
    private IReadOnlyList<int> _slots = Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<CompletedUndamagedRevealedMaterial> _revealed = Array.AsReadOnly(Array.Empty<CompletedUndamagedRevealedMaterial>());
    private IReadOnlyList<CompletedUndamagedRevealedMaterial> _paid = Array.AsReadOnly(Array.Empty<CompletedUndamagedRevealedMaterial>());
    public int InstructionIndex { get; init; }
    public required CardConversionSource Source { get; init; }
    public required string GameplayHash { get; init; }
    public long WindowFrameId { get; init; }
    public long CardUseFrameId { get; init; }
    public long CardActionId { get; init; }
    public CardKind EffectiveKind { get; init; }
    public IReadOnlyList<int> FinalTargets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> EligibleTargets { get => _eligible; init => _eligible = Array.AsReadOnly(value.ToArray()); }
    public CompletedUndamagedTargetRevealStage Stage { get; init; }
    public int? TargetSeat { get; init; }
    /// <summary>Trusted material invoice; player prompts expose its slots only.</summary>
    public IReadOnlyList<CompletedUndamagedHandMaterial> TargetHand { get => _hand; init => _hand = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> SelectedSlots { get => _slots; init => _slots = Array.AsReadOnly(value.ToArray()); }
    public bool SelectionIssued { get; init; }
    public IReadOnlyList<CompletedUndamagedRevealedMaterial> RevealedMaterials { get => _revealed; init => _revealed = Array.AsReadOnly(value.ToArray()); }
    public bool SameColor { get; init; }
    public bool MovementIssued { get; init; }
    public IReadOnlyList<CompletedUndamagedRevealedMaterial> PaidMaterials { get => _paid; init => _paid = Array.AsReadOnly(value.ToArray()); }
    public long SequenceBefore { get; init; }
    public long SequenceAfter { get; init; }
    public long? BatchId { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramCompletedUndamagedTargetRevealReceipt? CompletedUndamagedTargetReveal { get; init; }
}
public sealed record CompletedUndamagedTargetRevealStartedEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, long WindowFrameId, long CardUseFrameId, long CardActionId) : IGameEvent;
public sealed record CompletedUndamagedTargetRevealSelectionIssuedEvent(long FrameId, int TargetSeat,
    int SelectedCount, int RevealedCount, bool SameColor) : IGameEvent;
public sealed record CompletedUndamagedTargetRevealMovementIssuedEvent(long FrameId, int TargetSeat,
    int ActualCount, long SequenceBefore, long SequenceAfter, long BatchId) : IGameEvent;
public sealed record CompletedUndamagedTargetRevealCompletedEvent(long FrameId, int SelectedCount,
    int ActualDiscardCount) : IGameEvent;

internal interface ICompletedUndamagedTargetRevealProgramHost
{
    SkillProgramStepOutcome CompletedUndamagedTargetReveal(ProgramSkillFrame frame);
}
internal sealed class RevealUndamagedUseTargetHandAndDiscardSameColorDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RevealUndamagedUseTargetHandAndDiscardSameColor;
    public override ISkillProgramEffectHandler Handler { get; } = new RevealUndamagedUseTargetHandAndDiscardSameColorHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "condition");
        if (r.RequiredInt("amount") != 3)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: completed-target reveal requires amount 3.");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 3, r.Condition());
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCompleted)];
}
public sealed class RevealUndamagedUseTargetHandAndDiscardSameColorHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RevealUndamagedUseTargetHandAndDiscardSameColor;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        ((ICompletedUndamagedTargetRevealProgramHost)host).CompletedUndamagedTargetReveal(frame);
}
internal static class CompletedUndamagedTargetRevealComposition
{
    internal static bool IsOperation(SkillProgramEffectOp op) => op == SkillProgramEffectOp.RevealUndamagedUseTargetHandAndDiscardSameColor;
    internal static void ValidateProgram(string path, SkillProgram program)
    {
        if (program.Activations.Any(a => a.Effects.Any(e => IsOperation(e.Op))))
            throw new InvalidOperationException($"Invalid skill program at {path}: completed-target reveal requires its completed actual Use window.");
        foreach (var t in program.Triggers.Where(t => t.Effects.Any(e => IsOperation(e.Op))))
            if (t.Effects is not [{ Target: SkillProgramEffectTarget.Owner, Amount: 3, Condition.Kind: SkillProgramConditionKind.Always }] ||
                t.Window != SkillProgramTriggerWindow.CardUseCompleted || t.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor ||
                !t.Optional || t.IncludeResponseUses || t.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
                t.UsageScope is not null || t.UsageLimit is not null || t.MarkerCost is not null || t.ChoiceGroup is not null ||
                t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null || t.SourceSkillId is not null || t.SourceViewAsId is not null ||
                t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.Suits.Count != 0 || t.OnlyDesignatedCardTargets || t.RequireNoCardConversion == true)
                throw new InvalidOperationException($"Invalid skill program at {path}: completed-target reveal requires one unconditional unlimited optional actor Use-completion operation, without card or source filters.");
    }
}
