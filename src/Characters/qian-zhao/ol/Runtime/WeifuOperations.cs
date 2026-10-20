using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum WeifuStage { PaymentChildren, Judging, JudgmentChildren, DrawChildren }
public sealed record WeifuReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex, int ActualTurn,
    int TurnOwnerSeat, int CostCardId, CardLocation CostFrom, SkillProgramCardCategory CostCategory, long Before, long After,
    WeifuStage Stage = WeifuStage.PaymentChildren, long? JudgmentId = null, int? ResultCardId = null,
    CardKind? ResultKind = null, SkillProgramCardCategory? ResultCategory = null, bool Issued = false, int DrawCount = 0);
public sealed record WeifuTargetDraft(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long UseId, long ActionId, int Maximum)
{
    private readonly IReadOnlyList<int> _selected = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
}
public sealed record WeifuUseReceipt(long UseId, long ActionId, int ActorSeat, int ActualTurn, int TurnOwnerSeat,
    SkillProgramCardCategory Category, bool Offered = false)
{
    public int ProviderSeat { get; init; }
    private readonly IReadOnlyList<WeifuGrantedEvent> _grants = Array.AsReadOnly(Array.Empty<WeifuGrantedEvent>());
    private readonly IReadOnlyList<int> _original = Array.AsReadOnly(Array.Empty<int>()), _added = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<WeifuGrantedEvent> Grants { get => _grants; init => _grants = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> OriginalTargets { get => _original; init => _original = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> AddedTargets { get => _added; init => _added = Array.AsReadOnly(value.ToArray()); }
}
public sealed record WeifuPaidEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn,
    int TurnOwnerSeat, int CardId, CardLocation From, SkillProgramCardCategory Category, long Before, long After) : IGameEvent;
public sealed record WeifuJudgedEvent(long FrameId, long JudgmentId, int CardId, CardKind Kind, SkillProgramCardCategory Category) : IGameEvent;
public sealed record WeifuGrantedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn,
    int TurnOwnerSeat, SkillProgramCardCategory Category, long JudgmentId) : IGameEvent;
public sealed record WeifuGrantConsumedEvent(long GrantFrameId, long UseId, long ActionId, int ActorSeat, SkillProgramCardCategory Category, bool Counterspell, CardKind? ResponseKind = null) : IGameEvent;
public sealed record WeifuGrantExpiredEvent(long GrantFrameId, int ActualTurn, int TurnOwnerSeat) : IGameEvent;
public sealed record WeifuDrawPaidEvent(long FrameId, int Count) : IGameEvent;
public sealed record WeifuUseIssuedEvent(long UseId, long ActionId, int ActorSeat, int ActualTurn, int TurnOwnerSeat,
    SkillProgramCardCategory Category, int GrantCount, int ProviderSeat) : IGameEvent;
public sealed record WeifuActorChangedEvent(long UseId, long ActionId, long ProgramFrameId, CardConversionSource Producer,
    string GameplayHash, int InstructionIndex, int PreviousActor, int Actor, int Provider) : IGameEvent;
public sealed record WeifuTargetAddedEvent(long FrameId, long UseId, long ActionId, CardConversionSource Source, int TargetSeat, int Index) : IGameEvent;
public sealed record WeifuRedirectedEvent(long FrameId, long UseId, long ActionId, CardConversionSource Source, string GameplayHash,
    int InstructionIndex, int TargetIndex, int PreviousSeat, int TargetSeat) : IGameEvent;
internal sealed class ResolveWeifuJudgmentDescriptor() : QianZhaoDescriptor(SkillProgramEffectOp.ResolveWeifuJudgment)
{
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ConsumeSelectedCards(1)];
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (e, c) => c.DiscardSelected(e));
}
internal sealed class OfferWeifuCategoryTargetsDescriptor() : QianZhaoDescriptor(SkillProgramEffectOp.OfferWeifuCategoryTargets)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)]; }
