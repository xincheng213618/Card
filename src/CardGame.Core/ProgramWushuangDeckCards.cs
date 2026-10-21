using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record WushuangDeckGainReceipt(int InstructionIndex, CardConversionSource Source,
    string GameplayHash, int Requested, CardKind? Kind, SkillProgramCardCategory? Category, long SequenceBefore)
{
    private IReadOnlyList<int> _cardIds = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> CardIds { get => _cardIds; init => _cardIds = Array.AsReadOnly(value.ToArray()); }
    public long BatchId { get; init; }
    public long SequenceAfter { get; init; }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WushuangDeckGainReceipt? WushuangDeckGain { get; init; }
}

public sealed record WushuangDeckCardsObtainedEvent(long ProgramFrameId, int InstructionIndex,
    int OwnerSeat, int Count, long BatchId, long SequenceBefore, long SequenceAfter) : IGameEvent;

internal interface IWushuangDeckCardsHost
{
    SkillProgramStepOutcome ObtainWushuangDeckCards(ProgramSkillFrame frame, SkillProgramEffect effect);
}

internal sealed class ObtainWushuangDeckCardsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ObtainWushuangDeckCards;
    public override ISkillProgramEffectHandler Handler { get; } = new ObtainWushuangDeckCardsHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "outputKind", "category", "condition");
        var amount = r.RequiredInt("amount");
        if (amount is < 1 or > 4 || r.Has("outputKind") && r.Has("category"))
            throw new InvalidOperationException("A single-duel pile gain requires 1..4 cards and at most one kind/category filter.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), amount, r.Condition(),
            outputKind: r.Has("outputKind") ? r.RequiredEnum<CardKind>("outputKind") : null,
            cardCategories: r.Has("category") ? [r.RequiredEnum<SkillProgramCardCategory>("category")] : []);
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

public sealed class ObtainWushuangDeckCardsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ObtainWushuangDeckCards;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IWushuangDeckCardsHost>().ObtainWushuangDeckCards(frame, effect);
}
