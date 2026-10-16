using System.Text.Json.Serialization;

namespace CardGame.Core;

internal interface ILiuPiProgramHost
{
    SkillProgramStepOutcome Exchange(ProgramSkillFrame frame);
}
internal sealed class ResolveYichengPublicExchangeDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveYichengPublicExchange;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler => new LiuPiExchangeHandler();
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    // An optional exchange can improve a known own hand without reading deck order.
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
}
internal sealed class LiuPiExchangeHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveYichengPublicExchange;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<ILiuPiProgramHost>().Exchange(frame);
}

public enum LiuPiExchangeStage { RevealChildren, HandSelection, TableSelection, EqualExchangeChildren, FullOffer, FullOrder, FullExchangeChildren, CleanupChildren }
public sealed record LiuPiTableEntry(int CardId, int Rank, int MovementSequence);
public sealed record LiuPiExchangeReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    LiuPiExchangeStage Stage, int RevealLimit, int SequenceBefore, int SequenceAfter,
    int EqualRankBefore = 0, int EqualRankAfter = 0)
{
    private readonly IReadOnlyList<LiuPiTableEntry> _table = Array.AsReadOnly(Array.Empty<LiuPiTableEntry>());
    private readonly IReadOnlyList<int> _selectedHand = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<int> _selectedTable = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<int> _fullHand = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<int> _fullOrder = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<LiuPiTableEntry> Table { get => _table; init => _table = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> SelectedHand { get => _selectedHand; init => _selectedHand = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> SelectedTable { get => _selectedTable; init => _selectedTable = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> FullHand { get => _fullHand; init => _fullHand = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> FullOrder { get => _fullOrder; init => _fullOrder = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LiuPiExchangeReceipt? LiuPiExchange { get; init; }
}
// Public receipts contain counts and rank totals, never an unpaid private selection.
public sealed record LiuPiExchangeCommittedEvent(long ProgramFrameId, CardConversionSource Source, bool FullExchange,
    int HandCardsGiven, int TableCardsTaken, int TableRankBefore, int TableRankAfter,
    int SequenceBefore, int SequenceAfter) : IGameEvent;
