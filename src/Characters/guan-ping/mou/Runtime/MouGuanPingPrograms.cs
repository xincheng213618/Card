using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record MouGuanPingUseReceipt(long UseId, long ActionId, long ActivationId, CardConversionSource Source,
    string GameplayHash, int ActualTurn, int TurnOwner, int PhaseId, int TypeCount, bool Extra, int OptionMask = 0);
public enum MouGuanPingStage { Choosing, Drawing, Children }
public sealed record MouGuanPingReceipt(MouGuanPingUseReceipt Use, long WindowId, MouGuanPingStage Stage, int Cursor = 0, int Mask = 0)
{
    private readonly IReadOnlyList<int> _targets = [];
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public NativeDrawOutcome? Draw { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouGuanPingUseReceipt? MouGuanPing { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MouGuanPingReceipt? MouGuanPing { get; init; }
}
public sealed record MouGuanPingUseIssuedEvent(MouGuanPingUseReceipt Receipt) : IGameEvent;
public sealed record MouGuanPingOptionEvent(long FrameId, long UseId, long ActionId, int OwnerSeat, int ActualTurn, int TurnOwner, int Index, int Option) : IGameEvent;
public sealed record MouGuanPingDrawReturnedEvent(long FrameId, int Index, int Actual, int Replacement, long? AttemptId) : IGameEvent;
public sealed record MouGuanPingCompletedEvent(long FrameId, long UseId, int Count, int Mask) : IGameEvent;
internal interface IMouGuanPingRuntime : IProgramRuntimeOperations { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal interface IMouGuanPingFacts : IProgramSelectionQuery
{
    int Turn { get; } int TurnOwner { get; } int PhaseId { get; }
    bool Finished { get; } bool Alive(int seat); bool Human(int seat);
    SkillProgram Program(); int PhaseUsage(int owner);
    IEnumerable<T> History<T>() where T : IGameEvent;
    IReadOnlyList<ProgramMultiCardViewAsSelection> Groups(int owner);
    IReadOnlyList<int> Targets(int owner, IReadOnlyList<Card> cards);
    bool WholeColor(int owner, IReadOnlyList<int> cards);
    CardUseFrame? Use(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context);
    CardUseFrame? Use(long id);
}
internal interface IMouGuanPingNative : IProgramRuntimeOperations
{
    void PublishState(); void Finish(ProgramSkillFrame frame); bool Drain(ProgramSkillFrame frame);
    void SetMask(ProgramSkillFrame frame, int mask); void Suppress(ProgramSkillFrame frame, int target);
}
internal sealed class ResolveMouGuanPingWuweiDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouGuanPingWuwei;
    public override ISkillProgramEffectHandler Handler { get; } = new MouGuanPingHandler();
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeActionAppearance);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted)];
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
}
internal sealed class MouGuanPingHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouGuanPingWuwei;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IMouGuanPingRuntime>().Resolve(f);
}

[ProgramSelectionModule(ProgramSelectionStage.Sources, 6, SkillProgramEffectOp.UseSelectedCardsAs)]
internal sealed class MouGuanPingSelection : IProgramSelectionSourceProvider
{
    public bool TryBuild(ProgramSelectionContext c, ProgramSelectionCandidates result, IProgramSelectionQueries queries)
    {
        if (c.SkillId != MouGuanPingComposition.Skill) return false;
        var q = queries.Get<IMouGuanPingFacts>(); var groups = q.Groups(c.OwnerSeat);
        result.VariableInput = true; result.MultiCardUses = groups;
        result.Cards = groups.SelectMany(g => g.Cards).Select(x => x.Id).Distinct().Order().ToArray(); return true;
    }
    public void BuildTargets(ProgramSelectionContext c, ProgramSelectionCandidates result, IProgramSelectionQueries queries) =>
        result.Targets = result.MultiCardUses.SelectMany(g => queries.Get<IMouGuanPingFacts>().Targets(c.OwnerSeat, g.Cards)).Distinct().Order().ToArray();
}
[ProgramSelectionModule(ProgramSelectionStage.PhaseLimit, 35000, SkillProgramEffectOp.UseSelectedCardsAs)]
internal sealed class MouGuanPingPhaseLimit : IProgramSelectionPhaseLimitProvider
{
    public int? GetLimit(ProgramSelectionContext c, IProgramSelectionQueries queries)
    {
        if (c.SkillId != MouGuanPingComposition.Skill) return c.Activation.UsesPerPhase;
        var q = queries.Get<IMouGuanPingFacts>();
        var starts = q.History<MouGuanPingUseIssuedEvent>().Select(e => e.Receipt).Where(r => r.Source.OwnerSeat == c.OwnerSeat && r.ActualTurn == q.Turn && r.TurnOwner == q.TurnOwner).ToArray();
        var bonus = q.History<MouGuanPingOptionEvent>().Count(e => e.OwnerSeat == c.OwnerSeat && e.ActualTurn == q.Turn && e.TurnOwner == q.TurnOwner && e.Option == 3);
        var remaining = Math.Max(0, bonus - starts.Count(r => r.Extra));
        return q.PhaseUsage(c.OwnerSeat) + (starts.Any(r => r.PhaseId == q.PhaseId) ? 0 : 1) + remaining;
    }
}
[ProgramSelectionValidator(ProgramSelectionValidationStage.AfterCardFilters, 35000, SkillProgramEffectOp.UseSelectedCardsAs)]
internal sealed class MouGuanPingSelectionValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest r, IProgramSelectionQueries queries) => r.Context.SkillId != MouGuanPingComposition.Skill ||
        queries.Get<IMouGuanPingFacts>().WholeColor(r.Context.OwnerSeat, r.Cards) ? null : new(CommandErrorCode.InvalidCard, "武威须选择当前一种颜色的全部手牌。");
}
