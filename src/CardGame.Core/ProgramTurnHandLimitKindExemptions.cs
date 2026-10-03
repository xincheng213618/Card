namespace CardGame.Core;

public sealed record TurnHandLimitCardKindExemption(long GrantSequence, int TurnNumber, int TurnSeat,
    long ParentFrameId, int EffectIndex, CardUseEffectSource Source, IReadOnlyList<CardKind> CardKinds);
public sealed record TurnHandLimitCardKindExemptionGrantedEvent(TurnHandLimitCardKindExemption Policy) : IGameEvent;

public sealed partial record PlayerSnapshot
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TurnHandLimitCardKindExemption>? TurnHandLimitCardKindExemptions { get; init; }
}

internal interface ITurnHandLimitKindExemptionProgramHost
{
    void GrantTurnHandLimitCardKindExemption(ProgramSkillFrame frame, IReadOnlyList<CardKind> cardKinds);
}
internal sealed class GrantTurnHandLimitCardKindExemptionDescriptor : TurnEffectProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnHandLimitCardKindExemption;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantTurnHandLimitCardKindExemptionHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnRuleModifier,
        static (_, context) => context.PublicControlValue(4d));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "cardKinds", "condition");
        var owner = FilterBoundCardsProgramOperationDescriptor.Owner(reader);
        var kinds = reader.RequiredEnumArray<CardKind>("cardKinds");
        if (kinds.Count == 0)
            throw new InvalidOperationException($"Invalid skill program at {reader.Path}.cardKinds: requires at least one card kind.");
        return new(Op, owner, 0, reader.Condition(), cardKinds: kinds);
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}
public sealed class GrantTurnHandLimitCardKindExemptionHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnHandLimitCardKindExemption;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    {
        ((ITurnHandLimitKindExemptionProgramHost)host).GrantTurnHandLimitCardKindExemption(frame, effect.CardKinds);
        return SkillProgramStepOutcome.Continue;
    }
}

internal sealed partial class TurnCardUseEffectStore
{
    private readonly List<TurnHandLimitCardKindExemption> _handLimitKindExemptions = [];
    internal IReadOnlyList<TurnHandLimitCardKindExemption> HandLimitKindExemptions =>
        Array.AsReadOnly(_handLimitKindExemptions.ToArray());

    internal TurnHandLimitCardKindExemption GrantHandLimitKindExemption(int turn, int turnSeat, long parent,
        int effect, CardUseEffectSource source, IReadOnlyList<CardKind> cardKinds)
    {
        if (turn <= 0 || turnSeat < 0 || parent <= 0 || effect < 0 || source.OwnerSeat < 0 ||
            string.IsNullOrWhiteSpace(source.SkillId) || string.IsNullOrWhiteSpace(source.BindingId) ||
            string.IsNullOrWhiteSpace(source.SkillInstanceId) || cardKinds.Count == 0 ||
            cardKinds.Any(kind => !Enum.IsDefined(kind)) || cardKinds.Distinct().Count() != cardKinds.Count)
            throw new InvalidOperationException("A hand-limit kind exemption requires a valid actual-turn source and distinct card kinds.");
        var frozenKinds = Array.AsReadOnly(cardKinds.Order().ToArray());
        var existing = _handLimitKindExemptions.SingleOrDefault(policy => policy.ParentFrameId == parent && policy.EffectIndex == effect);
        if (existing is not null)
        {
            if (existing.TurnNumber != turn || existing.TurnSeat != turnSeat || existing.Source != source ||
                !existing.CardKinds.SequenceEqual(frozenKinds))
                throw new InvalidOperationException("A hand-limit kind exemption grant key changed its actual-turn meaning.");
            return existing;
        }
        var policy = new TurnHandLimitCardKindExemption(++_grantSequence, turn, turnSeat, parent, effect, source, frozenKinds);
        _handLimitKindExemptions.Add(policy);
        return policy;
    }
    internal IReadOnlySet<CardKind> GetHandLimitExemptCardKinds(int turn, int turnSeat, int owner) =>
        _handLimitKindExemptions.Where(policy => policy.TurnNumber == turn && policy.TurnSeat == turnSeat &&
            policy.Source.OwnerSeat == owner).SelectMany(policy => policy.CardKinds).ToHashSet();
    private IEnumerable<long> ExpiringHandLimitKindExemptions(int turn, int turnSeat) =>
        _handLimitKindExemptions.Where(policy => policy.TurnNumber == turn && policy.TurnSeat == turnSeat)
            .Select(policy => policy.GrantSequence);
    private void ExpireHandLimitKindExemptions(HashSet<long> expired) =>
        _handLimitKindExemptions.RemoveAll(policy => expired.Contains(policy.GrantSequence));
    private bool HandLimitKindExemptionsAreInvalid() => _handLimitKindExemptions.Any(policy =>
        policy.TurnNumber <= 0 || policy.TurnSeat < 0 || policy.ParentFrameId <= 0 || policy.EffectIndex < 0 ||
        policy.Source.OwnerSeat < 0 || string.IsNullOrWhiteSpace(policy.Source.SkillId) ||
        string.IsNullOrWhiteSpace(policy.Source.BindingId) || string.IsNullOrWhiteSpace(policy.Source.SkillInstanceId) ||
        policy.CardKinds.Count == 0 || policy.CardKinds.Distinct().Count() != policy.CardKinds.Count ||
        policy.CardKinds.Any(kind => !Enum.IsDefined(kind)));
}
