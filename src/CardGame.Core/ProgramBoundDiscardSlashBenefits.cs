namespace CardGame.Core;

/// <summary>One real discard; the suit is frozen before equipment removal and its observers.</summary>
public sealed record ProgramBoundDiscardSlashReceipt(int InstructionIndex, string SourceBind,
    int CardId, CardKind CardKind, CardLocation SourceLocation, Suit EffectiveSuit, int MovementSequence);
public sealed record TurnSlashSuitAllowance(long GrantSequence, int TurnNumber, int TurnSeat,
    long ParentFrameId, int EffectIndex, CardUseEffectSource Source, Suit Suit);
public sealed record TurnSlashSuitAllowanceGrantedEvent(TurnSlashSuitAllowance Policy) : IGameEvent;
public sealed record FirstRoundGameUsageRefund(long GrantSequence, int TurnNumber, int TurnSeat,
    long ParentFrameId, int EffectIndex, CardUseEffectSource Source, string UsageId, int RoundNumber);
public sealed record FirstRoundGameUsageRefundScheduledEvent(FirstRoundGameUsageRefund Refund) : IGameEvent;
public sealed record FirstRoundGameUsageRefundResolvedEvent(FirstRoundGameUsageRefund Refund, bool Refunded) : IGameEvent;

public sealed partial record PlayerSnapshot
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TurnSlashSuitAllowance>? TurnSlashSuitAllowances { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<FirstRoundGameUsageRefund>? FirstRoundGameUsageRefunds { get; init; }
}

internal interface IBoundDiscardSlashBenefitsHost
{
    SkillProgramStepOutcome DiscardBoundCardForTurnSlashBenefits(ProgramSkillFrame frame, string sourceBind);
    void ScheduleFirstRoundGameUsageRefund(ProgramSkillFrame frame);
}
internal sealed record RequireFirstLimitedActivation : ProgramResourceOperation;

internal sealed class DiscardBoundCardForTurnSlashBenefitsDescriptor : TurnEffectProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardBoundCardForTurnSlashBenefits;
    public override ISkillProgramEffectHandler Handler { get; } = new DiscardBoundCardForTurnSlashBenefitsHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Move,
        static (effect, context) =>
        {
            context.Move(new(SkillProgramEffectOp.MoveBoundCards, SkillProgramEffectTarget.Owner, 0,
                new(SkillProgramConditionKind.Always, 0, []), sourceBind: effect.SourceBind,
                destination: SkillProgramCardDestination.DiscardPile));
            context.PublicControlValue(12d);
        });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "sourceBind", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0,
            reader.Condition(), sourceBind: reader.RequiredIdentifier("sourceBind"));
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireActivationEntry(), new RequireOwnedCardSet(effect.SourceBind!, SkillProgramEffectTarget.Owner, 1,
            [CardZoneKind.Hand, CardZoneKind.Equipment]), new ReadSingleCardSet(effect.SourceBind!),
         new MoveCardSet(effect.SourceBind!, null, SkillProgramCardDestination.DiscardPile)];
}
public sealed class DiscardBoundCardForTurnSlashBenefitsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardBoundCardForTurnSlashBenefits;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        ((IBoundDiscardSlashBenefitsHost)host).DiscardBoundCardForTurnSlashBenefits(frame, effect.SourceBind!);
}

internal sealed class ScheduleFirstRoundGameUsageRefundDescriptor : TurnEffectProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ScheduleFirstRoundGameUsageRefund;
    public override ISkillProgramEffectHandler Handler { get; } = new ScheduleFirstRoundGameUsageRefundHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnRuleModifier,
        static (_, context) => context.PublicControlValue(2d));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0, reader.Condition());
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireFirstLimitedActivation()];
}
public sealed class ScheduleFirstRoundGameUsageRefundHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ScheduleFirstRoundGameUsageRefund;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    {
        ((IBoundDiscardSlashBenefitsHost)host).ScheduleFirstRoundGameUsageRefund(frame);
        return SkillProgramStepOutcome.Continue;
    }
}

internal sealed partial class TurnCardUseEffectStore
{
    private readonly List<TurnSlashSuitAllowance> _slashSuitAllowances = [];
    private readonly List<FirstRoundGameUsageRefund> _firstRoundGameUsageRefunds = [];
    internal IReadOnlyList<TurnSlashSuitAllowance> SlashSuitAllowances => Array.AsReadOnly(_slashSuitAllowances.ToArray());
    internal IReadOnlyList<FirstRoundGameUsageRefund> FirstRoundGameUsageRefunds => Array.AsReadOnly(_firstRoundGameUsageRefunds.ToArray());
    internal TurnSlashSuitAllowance GrantSlashSuitAllowance(int turn, int turnSeat, long parent, int effect, CardUseEffectSource source, Suit suit)
    {
        ValidateBoundSlashTurnSource(turn, turnSeat, parent, effect, source);
        if (!Enum.IsDefined(suit)) throw new InvalidOperationException("A turn suit grant requires a defined frozen suit.");
        var existing = _slashSuitAllowances.SingleOrDefault(p => p.ParentFrameId == parent && p.EffectIndex == effect);
        if (existing is not null)
        {
            if (existing.TurnNumber != turn || existing.TurnSeat != turnSeat || existing.Source != source || existing.Suit != suit)
                throw new InvalidOperationException("A turn Slash suit grant changed its frozen meaning.");
            return existing;
        }
        var issued = new TurnSlashSuitAllowance(++_grantSequence, turn, turnSeat, parent, effect, source, suit);
        _slashSuitAllowances.Add(issued);
        return issued;
    }
    internal FirstRoundGameUsageRefund ScheduleGameUsageRefund(int turn, int turnSeat, long parent, int effect,
        CardUseEffectSource source, string usage)
    {
        ValidateBoundSlashTurnSource(turn, turnSeat, parent, effect, source);
        if (string.IsNullOrWhiteSpace(usage)) throw new InvalidOperationException("A limited refund requires its exact usage key.");
        var existing = _firstRoundGameUsageRefunds.SingleOrDefault(p => p.ParentFrameId == parent && p.EffectIndex == effect);
        if (existing is not null)
        {
            if (existing.TurnNumber != turn || existing.TurnSeat != turnSeat || existing.Source != source || existing.UsageId != usage)
                throw new InvalidOperationException("A limited refund changed its frozen original usage.");
            return existing;
        }
        var issued = new FirstRoundGameUsageRefund(++_grantSequence, turn, turnSeat, parent, effect, source, usage, 1);
        _firstRoundGameUsageRefunds.Add(issued);
        return issued;
    }
    internal bool HasSlashSuitAllowance(int turn, int turnSeat, int owner, Suit? suit) => suit is { } actual && actual != Suit.None &&
        _slashSuitAllowances.Any(p => p.TurnNumber == turn && p.TurnSeat == turnSeat && p.Source.OwnerSeat == owner && p.Suit == actual);
    private static void ValidateBoundSlashTurnSource(int turn, int turnSeat, long parent, int effect, CardUseEffectSource source)
    {
        if (turn <= 0 || turnSeat < 0 || parent <= 0 || effect < 0 || source.OwnerSeat != turnSeat ||
            string.IsNullOrWhiteSpace(source.SkillId) || string.IsNullOrWhiteSpace(source.BindingId) || string.IsNullOrWhiteSpace(source.SkillInstanceId))
            throw new InvalidOperationException("A suit or limited refund grant requires an exact actual-turn source.");
    }
    private IEnumerable<long> ExpiringBoundSlashBenefits(int turn, int turnSeat) =>
        _slashSuitAllowances.Where(p => p.TurnNumber == turn && p.TurnSeat == turnSeat).Select(p => p.GrantSequence)
            .Concat(_firstRoundGameUsageRefunds.Where(p => p.TurnNumber == turn && p.TurnSeat == turnSeat).Select(p => p.GrantSequence));
    private void ExpireBoundSlashBenefits(HashSet<long> expired)
    {
        _slashSuitAllowances.RemoveAll(p => expired.Contains(p.GrantSequence));
        _firstRoundGameUsageRefunds.RemoveAll(p => expired.Contains(p.GrantSequence));
    }
    private static bool BoundSlashSourceIsInvalid(CardUseEffectSource source, int turnSeat) =>
        source.OwnerSeat != turnSeat || string.IsNullOrWhiteSpace(source.SkillId) ||
        string.IsNullOrWhiteSpace(source.BindingId) || string.IsNullOrWhiteSpace(source.SkillInstanceId);
    private bool BoundSlashBenefitsAreInvalid() => _slashSuitAllowances.Any(p => !Enum.IsDefined(p.Suit) || p.TurnNumber <= 0 || p.TurnSeat < 0 || p.ParentFrameId <= 0 || p.EffectIndex < 0 || BoundSlashSourceIsInvalid(p.Source, p.TurnSeat)) ||
        _firstRoundGameUsageRefunds.Any(p => p.RoundNumber != 1 || p.TurnNumber <= 0 || p.TurnSeat < 0 || p.ParentFrameId <= 0 || p.EffectIndex < 0 || BoundSlashSourceIsInvalid(p.Source, p.TurnSeat) || string.IsNullOrWhiteSpace(p.UsageId));
}
