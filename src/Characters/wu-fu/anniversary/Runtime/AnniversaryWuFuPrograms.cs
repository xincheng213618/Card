using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum AnniversaryWuFuStage { AdjustDiscard, AdjustDraw, AdjustChildren, Target, Commit, Item, Drawing, Children, Issuing, NativeUse, NativeChildren }
public sealed record AnniversaryWuFuVote(int Seat, bool Draw);
public sealed record AnniversaryWuFuTask(int Actor, int Target, bool Draw);
public sealed record AnniversaryWuFuReceipt(CardConversionSource Source, string Hash, ActualDiscardRecoveryPhaseKey Phase)
{
    public AnniversaryWuFuStage Stage { get; init; }
    public int Peer { get; init; } = -1;
    public int Count { get; init; }
    public int Cursor { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public bool Returned { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    private IReadOnlyList<int> _cards = [];
    public IReadOnlyList<int> Cards { get => _cards; init => _cards = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<AnniversaryWuFuVote> _votes = [];
    public IReadOnlyList<AnniversaryWuFuVote> Votes { get => _votes; init => _votes = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<long> _batches = [];
    public IReadOnlyList<long> NativeBatches { get => _batches; init => _batches = Array.AsReadOnly(value.ToArray()); }
}
public sealed record AnniversaryWuFuSlashOrigin(long FrameId, CardConversionSource Source, string Hash, int Cursor, long UseId, long ActionId, int Actor, int Target);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryWuFuReceipt? AnniversaryWuFu { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryWuFuSlashOrigin? AnniversaryWuFuSlash { get; init; }
}
public sealed record AnniversaryWuFuStartedEvent(long FrameId, CardConversionSource Source, string Hash, ActualDiscardRecoveryPhaseKey Phase) : IGameEvent;
public sealed record AnniversaryWuFuVoteRevealedEvent(long FrameId, int Index, int Seat, bool Draw) : IGameEvent;
public sealed record AnniversaryWuFuAdjustedEvent(long FrameId, int Requested, long Before, long After, bool Draw) : IGameEvent;
public sealed record AnniversaryWuFuDrawReturnedEvent(long FrameId, int Cursor, int Recipient, int Requested, int Actual, int Replacement, long? AttemptId) : IGameEvent;
public sealed record AnniversaryWuFuItemReturnedEvent(long FrameId, int Cursor, bool Used) : IGameEvent;
public sealed record AnniversaryWuFuSlashIssuedEvent(AnniversaryWuFuSlashOrigin Origin) : IGameEvent;
public sealed record AnniversaryWuFuSlashReturnedEvent(long FrameId, int Cursor, long UseId, long ActionId) : IGameEvent;
public sealed record AnniversaryWuFuNativeBatchEvent(long FrameId, long UseId, long BatchId, long Before, long After) : IGameEvent;
public sealed record AnniversaryWuFuCompletedEvent(long FrameId) : IGameEvent;
public sealed record AnniversaryWuFuPhaseGainEvent(ActualDiscardRecoveryPhaseKey Phase, int Recipient, int Sequence) : IGameEvent;
public sealed record AnniversaryWuFuJuekaiEvent(long FrameId, CardConversionSource Source, string Hash, ActualDiscardRecoveryPhaseKey Phase, int PreviousZhongeUses) : IGameEvent;

internal interface IAnniversaryWuFuHost { SkillProgramStepOutcome Begin(ProgramSkillFrame frame, SkillProgramEffectOp op); }
internal sealed class ResolveAnniversaryWuFuZhongeDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryWuFuZhonge;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new AnniversaryWuFuHandler(SkillProgramEffectOp.ResolveAnniversaryWuFuZhonge);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard | ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeActionAppearance);
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class ResolveAnniversaryWuFuJuekaiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryWuFuJuekai;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new AnniversaryWuFuHandler(SkillProgramEffectOp.ResolveAnniversaryWuFuJuekai);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.IssuePlayUsePolicy, static (_, c) => c.PublicControlValue(2));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [];
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase);
}
internal sealed class AnniversaryWuFuHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IAnniversaryWuFuHost>().Begin(f, op);
}
internal static class AnniversaryWuFuComposition
{
    internal const string Zhonge = "anniversary:zhonge", Juekai = "anniversary:juekai";
    [ProgramCatalogValidation(46600)] internal static void Validate(string path, SkillProgram p)
    {
        var owns = p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => e.Op is SkillProgramEffectOp.ResolveAnniversaryWuFuZhonge or SkillProgramEffectOp.ResolveAnniversaryWuFuJuekai);
        if (p.Id is not (Zhonge or Juekai) && !owns) return;
        var zhonge = p.Id == Zhonge;
        if (p.Id is not (Zhonge or Juekai) || p.Triggers.Count != 0 || p.ViewAs.Count != 0 || p.CardPolicies.Count != 0 || p.CardIdentities.Count != 0 || p.Modifiers.Count != 0 || p.BooleanStates.Count != 0 || p.HpSkillGrants.Count != 0 ||
            p.Activations is not [var a] || a.Id != (zhonge ? "adjust-and-commit" : "reset-and-restrict") || a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 0 || a.MaxTargets != 0 || a.UsesPerTurn is not null ||
            a.UsesPerPhase != (zhonge ? 1 : null) || a.UsesPerGame != (zhonge ? null : 1) || a.Effects is not [{ Target: SkillProgramEffectTarget.Owner } e] ||
            e.Op != (zhonge ? SkillProgramEffectOp.ResolveAnniversaryWuFuZhonge : SkillProgramEffectOp.ResolveAnniversaryWuFuJuekai) || e.Condition.Kind != SkillProgramConditionKind.Always)
            throw new InvalidOperationException(path + ": Wu Fu requires the complete actual-Play adjustment/commit and limited reset/restriction definitions.");
    }
}
