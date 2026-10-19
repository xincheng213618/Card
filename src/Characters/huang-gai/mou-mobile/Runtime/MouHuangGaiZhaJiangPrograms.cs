using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record MouZhaJiangAdmission(CardConversionSource? Source, string? GameplayHash,
    int ActorSeat, int ActualTurn, int TurnOwner, int Hp, int MaxHp, int UseOrdinal,
    long MovementSequence, bool Applies);
public sealed record MouZhaJiangUseReceipt(long UseId, long ActionId, CardKind OriginalKind,
    MouZhaJiangAdmission Admission, long? DeclarationId, int? ProgramInstructionIndex = null);
public sealed record MouZhaJiangResponseReceipt(int Index, int ActorSeat, CardKind Kind,
    MouZhaJiangAdmission Admission, long ActionId = 0, long? DeclarationId = null)
{
    private readonly IReadOnlyList<CardActionCost> _materials = Array.AsReadOnly(Array.Empty<CardActionCost>());
    public IReadOnlyList<CardActionCost> Materials
    { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record DeclaredCardPayment
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MouZhaJiangAdmission? MouZhaJiang { get; init; }
}
public abstract partial record ResolutionFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MouZhaJiangUseReceipt? MouZhaJiangUse { get; init; }
    private readonly IReadOnlyList<MouZhaJiangResponseReceipt>? _mouZhaJiangResponses;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<MouZhaJiangResponseReceipt>? MouZhaJiangResponses
    { get => _mouZhaJiangResponses; init => _mouZhaJiangResponses = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}
public sealed record MouZhaJiangUseAdmittedEvent(long UseId, long ActionId, CardKind Kind,
    MouZhaJiangAdmission Admission, long? DeclarationId, int? ProgramInstructionIndex = null) : IGameEvent;
public sealed record MouZhaJiangDeclarationAdmittedEvent(long DeclarationId, long OwnerFrameId,
    int ProviderSeat, int CardId, CardKind PrintedKind, CardLocation From, CardKind DeclaredKind,
    MouZhaJiangAdmission Admission) : IGameEvent;
public sealed record MouZhaJiangResponsePreparedEvent(long OwnerFrameId, int Index, int ActorSeat,
    CardKind Kind, MouZhaJiangAdmission Admission, int MaterialCount, long? DeclarationId = null) : IGameEvent;
public sealed record MouZhaJiangResponseMaterialEvent(long OwnerFrameId, int Index, int MaterialIndex,
    int CardId, CardKind PrintedKind, CardLocation From) : IGameEvent;
public sealed record MouZhaJiangResponseAdmittedEvent(long OwnerFrameId, int Index, long ActionId,
    int ActorSeat, CardKind Kind, MouZhaJiangAdmission Admission, long? DeclarationId = null) : IGameEvent;
