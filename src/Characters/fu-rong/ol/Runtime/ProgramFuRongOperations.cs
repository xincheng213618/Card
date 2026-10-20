using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum FuRongPairStage { OwnerChoice, OwnerChildren, TargetChoice, TargetChildren, DrawChildren, UseChoice, NativeUse, NativeChildren, Complete }
public sealed record FuRongPayment(int Seat, int CardId, CardKind PrintedKind, CardKind EffectiveKind, CardLocation From, long Sequence);
public sealed record FuRongUseReturn(long ProgramFrameId, long CardUseFrameId, int CardId, CardKind Kind, long ActionId);
public sealed record ProgramFuRongPairReceipt
{
    private IReadOnlyList<int> _used = Array.AsReadOnly(Array.Empty<int>());
    public required CardConversionSource Source { get; init; }
    public required string GameplayHash { get; init; }
    public required string UsageGroup { get; init; }
    public int InstructionIndex { get; init; }
    public int TargetSeat { get; init; }
    public int TurnNumber { get; init; }
    public int PhaseInstanceId { get; init; }
    public FuRongPairStage Stage { get; init; }
    public FuRongPayment? OwnerPayment { get; init; }
    public FuRongPayment? TargetPayment { get; init; }
    public bool TargetUnable { get; init; }
    public bool DrawIssued { get; init; }
    public int ActualDraw { get; init; }
    public long DrawBefore { get; init; }
    public long DrawAfter { get; init; }
    public int? SelectedUseCardId { get; init; }
    public long? ReservedUseId { get; init; }
    public FuRongUseReturn? NativeReturn { get; init; }
    public bool NativeCompleted { get; init; }
    public IReadOnlyList<int> UsedCardIds { get => _used; init => _used = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramFuRongPairReceipt? FuRongPair { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FuRongUseReturn? FuRongReturn { get; init; }
}
public sealed record FuRongPairStartedEvent(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    string UsageGroup, int TargetSeat, int TurnNumber, int PhaseInstanceId) : IGameEvent;
public sealed record FuRongPairPaidEvent(long ProgramFrameId, bool Owner, FuRongPayment Payment) : IGameEvent;
public sealed record FuRongTargetUnableEvent(long ProgramFrameId, int TargetSeat) : IGameEvent;
public sealed record FuRongCompensationDrawEvent(long ProgramFrameId, int OwnerSeat, int ActualCount) : IGameEvent;
public sealed record FuRongNativeIssuedEvent(FuRongUseReturn Return) : IGameEvent;
public sealed record FuRongNativeReturnedEvent(FuRongUseReturn Return) : IGameEvent;
public sealed record FuRongPairCompletedEvent(long ProgramFrameId, bool OwnerPaid, bool TargetPaid, int UsedCount) : IGameEvent;

internal interface IFuRongProgramHost { SkillProgramStepOutcome DiscardBasicPairAndReuse(ProgramSkillFrame frame, int target); }
internal sealed class DiscardBasicPairAndReuseDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardBasicPairAndReuse;
    public override ISkillProgramEffectHandler Handler { get; } = new DiscardBasicPairAndReuseHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: paired basic discard requires selectedTarget.");
        var e = new SkillProgramEffect(Op, SkillProgramEffectTarget.SelectedTarget, 0, r.Condition()); RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLivingWithHand), new ReadTargetSet(1, 1)];
}
public sealed class DiscardBasicPairAndReuseHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardBasicPairAndReuse;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) =>
        host.GetCapability<IFuRongProgramHost>().DiscardBasicPairAndReuse(f, target);
}
internal static class FuRongComposition
{
    [ProgramCatalogValidation(140)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (p.Triggers.Any(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.DiscardBasicPairAndReuse)))
            throw new InvalidOperationException($"Invalid skill program at {path}: paired basic discard is activation only.");
        foreach (var a in p.Activations.Where(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.DiscardBasicPairAndReuse)))
            if (a.Effects is not [{ Op: SkillProgramEffectOp.DiscardBasicPairAndReuse, Target: SkillProgramEffectTarget.SelectedTarget,
                    Condition.Kind: SkillProgramConditionKind.Always }] || a.MinCards != 0 || a.MaxCards != 0 ||
                a.MinTargets != 1 || a.MaxTargets != 1 || a.TargetKind != SkillProgramTargetKind.OtherLivingWithHand ||
                a.UsesPerPhase != 1 || a.UsesPerTurn is not null || a.UsesPerGame is not null || a.ContinueAfterOwnerDeath ||
                a.MarkerCost is not null || a.CardCountExpression is not null || a.CardKinds.Count != 0 || a.CardSuits.Count != 0 ||
                a.CardCategories.Count != 0 || a.EquipmentSlots.Count != 0 || a.SelectedCardsSameSuit || a.SelectedCardsDistinctSuits ||
                a.TargetRequiresEmptyEquipmentSlot || a.CategoryTargetLedgerId is not null || a.TargetPhaseLedgerId is not null ||
                a.Condition.Kind != SkillProgramConditionKind.Always)
                throw new InvalidOperationException($"Invalid skill program at {path}: paired basic discard requires its sole phase-limited zero-card activation and one other living target with hand cards.");
    }
}
