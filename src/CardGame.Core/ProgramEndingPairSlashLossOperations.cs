namespace CardGame.Core;

public sealed record ProgramOneCardDrawInvoice(int RecipientSeat, long SequenceBefore, long SequenceAfter, int ActualCount);
public sealed record ProgramEndingPairDrawReceipt(int InstructionIndex, string StateId, CardConversionSource Source,
    string GameplayHash, int ActualTurnNumber, int ActualTurnOwnerSeat, int RoundNumber, long EndingFrameId,
    int CurrentActorSeat, int Cursor, bool AwaitingMovement,
    ProgramOneCardDrawInvoice? FirstDraw = null, ProgramOneCardDrawInvoice? SecondDraw = null);
public sealed record ProgramPlaySlashRecastReceipt(int InstructionIndex, CardConversionSource Source, string GameplayHash,
    int ActualTurnNumber, int ActualTurnOwnerSeat, int PhaseActorSeat, int PhaseInstanceId,
    int CardId, CardKind CardKind, long SequenceBefore, long SequenceAfter,
    bool CostDrained = false, bool DrawIssued = false, bool AwaitingMovement = true,
    long DrawSequenceBefore = 0, long DrawSequenceAfter = 0, int ActualDrawCount = 0);

// Public facts intentionally contain no concealed hand entity or per-loss card kind.
public sealed record EndingPairDrawStartedEvent(long ProgramFrameId, string StateId, CardConversionSource Source,
    string GameplayHash, int ActualTurnNumber, int ActualTurnOwnerSeat, int RoundNumber, long EndingFrameId, int CurrentActorSeat) : IGameEvent;
public sealed record EndingPairOneDrawIssuedEvent(long ProgramFrameId, int Cursor, int RecipientSeat,
    long SequenceBefore, long SequenceAfter, int ActualCount) : IGameEvent;
public sealed record EndingPairDrawComparedEvent(long ProgramFrameId, string StateId, CardConversionSource Source,
    int RoundNumber, int OwnerHandCount, int CurrentActorHandCount, bool BlockedForRound) : IGameEvent;
// A recast entity is already in the public discard pile before this fact issues.
public sealed record PlaySlashRecastPaidEvent(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurnNumber, int ActualTurnOwnerSeat, int PhaseActorSeat, int PhaseInstanceId,
    int CardId, CardKind CardKind, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record PlaySlashRecastDrawIssuedEvent(long ProgramFrameId, int OwnerSeat,
    long SequenceBefore, long SequenceAfter, int ActualCount) : IGameEvent;

internal interface IEndingPairSlashLossHost
{
    SkillProgramStepOutcome DrawEndingPair(ProgramSkillFrame f, string state);
    SkillProgramStepOutcome RecastSelectedPhysicalSlash(ProgramSkillFrame f);
}
internal sealed class DrawEndingPairThenBlockRoundIfUnequalDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawEndingPairThenBlockRoundIfUnequal;
    public override ISkillProgramEffectHandler Handler { get; } = new DrawEndingPairThenBlockRoundIfUnequalHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) =>
    {
        c.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, new(SkillProgramConditionKind.Always, 0, [])));
        c.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Actor, 1, new(SkillProgramConditionKind.Always, 0, [])));
    });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stateId", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), stateId: r.RequiredIdentifier("stateId"));
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}
internal sealed class RecastSelectedPhysicalSlashDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecastSelectedPhysicalSlash;
    public override ISkillProgramEffectHandler Handler { get; } = new RecastSelectedPhysicalSlashHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) =>
    {
        c.DiscardSelected(new(SkillProgramEffectOp.DiscardSelected, SkillProgramEffectTarget.Owner, 1, new(SkillProgramConditionKind.Always, 0, [])));
        c.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, new(SkillProgramConditionKind.Always, 0, [])));
    });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ConsumeSelectedCards(1)];
}
public sealed class DrawEndingPairThenBlockRoundIfUnequalHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DrawEndingPairThenBlockRoundIfUnequal;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => ((IEndingPairSlashLossHost)h).DrawEndingPair(f, e.StateId!);
}
public sealed class RecastSelectedPhysicalSlashHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RecastSelectedPhysicalSlash;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => ((IEndingPairSlashLossHost)h).RecastSelectedPhysicalSlash(f);
}

internal static class EndingPairSlashLossComposition
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects, SkillProgramTriggerWindow? window, int selectedCardCount)
    {
        if (effects.Any(e => e.Op == SkillProgramEffectOp.DrawEndingPairThenBlockRoundIfUnequal) &&
            (window != SkillProgramTriggerWindow.TurnEnding || selectedCardCount != 0 || effects is not
                [{ Op: SkillProgramEffectOp.DrawEndingPairThenBlockRoundIfUnequal, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }]))
            throw new InvalidOperationException($"{path}: ending pair drawing is one exact own/foreign actual Ending node.");
        if (effects.Any(e => e.Op == SkillProgramEffectOp.RecastSelectedPhysicalSlash) &&
            (window is not null || selectedCardCount != 1 || effects is not
                [{ Op: SkillProgramEffectOp.RecastSelectedPhysicalSlash, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }]))
            throw new InvalidOperationException($"{path}: active Slash recast requires one physical selected owner hand card and one exact node.");
    }
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.DrawEndingPairThenBlockRoundIfUnequal) &&
            (trigger.Subject != SkillProgramTriggerSubject.Owner || !trigger.Optional || trigger.UsageScope is not null ||
             trigger.TurnOwnerScope is not (SkillProgramTurnOwnerScope.Own or SkillProgramTurnOwnerScope.OtherLiving)))
            throw new InvalidOperationException($"{path}: ending pair uses its shared Round block and optional owner candidate, without per-turn/phase allowance.");
        if (ProgramInstructionResolver.Default.Features(trigger).UsesValue(SkillProgramTriggerValueKind.CurrentActualPlayPhysicalSlashLossCount) &&
            (trigger.Window != SkillProgramTriggerWindow.PlayEnding || trigger.Subject != SkillProgramTriggerSubject.Owner || trigger.TurnOwnerScope != SkillProgramTurnOwnerScope.Own))
            throw new InvalidOperationException($"{path}: actual Play Slash loss facts require the original owner's PlayEnded window.");
    }
}
