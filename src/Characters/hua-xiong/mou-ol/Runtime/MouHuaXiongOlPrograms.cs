using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum MouHxBojueStage { Commit, Branch, Discard, Drawing, Children, Followup, OtherDiscard, Issuing, NativeUse, NativeChildren }
public sealed record MouHxBojueVote(int Seat, bool Draw);
public sealed record MouHxBojueReceipt(CardConversionSource Source, string GameplayHash, int Turn, int Peer, int InitialHandSum)
{
    public MouHxBojueStage Stage { get; init; }
    public int Cursor { get; init; }
    public int? Delta { get; init; }
    public int? PaidCard { get; init; }
    public int PaidBy { get; init; } = -1;
    public long Before { get; init; }
    public long After { get; init; }
    public long UseId { get; init; }
    public long ActionId { get; init; }
    public bool Returned { get; init; }
    public NativeDrawOutcome? DrawOutcome { get; init; }
    private IReadOnlyList<MouHxBojueVote> _votes = [];
    public IReadOnlyList<MouHxBojueVote> Votes { get => _votes; init => _votes = Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouHxBojueSlashOrigin(long FrameId, CardConversionSource Source, string GameplayHash, int Cursor, long UseId, long ActionId, int Actor, int Target);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouHxBojueReceipt? MouHxBojue { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouHxBojueSlashOrigin? MouHxBojueSlash { get; init; }
}
public sealed record MouHxBojueStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int Turn, int Peer, int InitialHandSum) : IGameEvent;
public sealed record MouHxBojueVoteRevealedEvent(long FrameId, int Index, int Seat, bool Draw) : IGameEvent;
public sealed record MouHxBojueDiscardedEvent(long FrameId, int Cursor, int Executor, int CardId, CardLocation From, CardLocation To, int Sequence, bool Followup) : IGameEvent;
public sealed record MouHxBojueDrawReturnedEvent(long FrameId, int Cursor, int Recipient, int Actual, int Replacement, long? AttemptId) : IGameEvent;
public sealed record MouHxBojueBranchReturnedEvent(long FrameId, int Cursor, int Actor) : IGameEvent;
public sealed record MouHxBojueDeltaEvent(long FrameId, int Before, int After, int AbsoluteChange) : IGameEvent;
public sealed record MouHxBojueSlashIssuedEvent(MouHxBojueSlashOrigin Origin) : IGameEvent;
public sealed record MouHxBojueSlashReturnedEvent(long FrameId, int Cursor, long UseId, long ActionId) : IGameEvent;
public sealed record MouHxBojueFollowupReturnedEvent(long FrameId, int Cursor, bool Used) : IGameEvent;
public sealed record MouHxBojueCompletedEvent(long FrameId, int AbsoluteChange) : IGameEvent;
public sealed record MouHxYangweiTicketEvent(CardConversionSource Source, int Turn, bool Outgoing, long InvocationId, int Total) : IGameEvent;
public sealed record MouHxYangweiConsumedEvent(CardConversionSource Source, int IssuedTurn, bool Outgoing, long InvocationId, long AttackId, int SourceSeat, int TargetSeat, bool Chain, int Cursor) : IGameEvent;
public sealed record MouHxYangweiChainIssuedEvent(long AttackId, int Cursor, int Inherited) : IGameEvent;
public sealed record MouHxYangweiChainReturnedEvent(long AttackId, int Cursor) : IGameEvent;

internal interface IMouHxBojueHost { SkillProgramStepOutcome Begin(ProgramSkillFrame frame); }
internal sealed class ResolveMouHuaXiongBojueDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouHuaXiongBojue;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new MouHxBojueHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget()];
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.NativeLogicalDiscard);
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class MouHxBojueHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouHuaXiongBojue;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouHxBojueHost>().Begin(f);
}
internal static class MouHuaXiongOlComposition
{
    internal const string Bojue = "mou-ol:bojue", Yangwei = "mou-ol:yangwei";
    [ProgramCatalogValidation(1550)]
    internal static void Validate(string path, SkillProgram p)
    {
        var owns = p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects)).Any(e => e.Op == SkillProgramEffectOp.ResolveMouHuaXiongBojue);
        if (p.Id != Bojue && !owns) return;
        var plain = p.Triggers.Count == 0 && p.ViewAs.Count == 0 && p.CardPolicies.Count == 0 && p.CardIdentities.Count == 0 && p.Modifiers.Count == 0 && p.BooleanStates.Count == 0 && p.HpSkillGrants.Count == 0;
        var valid = p.Id == Bojue && p.Activations is [var a] && a.Id == "simultaneous-choice" &&
            a.MinCards == 0 && a.MaxCards == 0 && a.MinTargets == 1 && a.MaxTargets == 1 && a.TargetKind == SkillProgramTargetKind.OtherLiving &&
            a.UsesPerTurn is null && a.UsesPerPhase == 2 && a.Effects is [{ Op: SkillProgramEffectOp.ResolveMouHuaXiongBojue, Target: SkillProgramEffectTarget.Owner } e] && e.Condition.Kind == SkillProgramConditionKind.Always;
        if (!plain || !valid) throw new InvalidOperationException(path + ": OL Hua Xiong requires the complete twice-per-Play Bojue and passive Yangwei definitions.");
    }
}
