using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record MouDaQiaoLiuliReceipt(CardConversionSource Source, string GameplayHash, long WindowId,
    int ActualTurn, int OriginalActorSeat, int RedirectTargetSeat, int PaidCardId, Suit PaidSuit,
    bool Choosing, int? MarkTargetSeat = null);
public sealed record MouDaQiaoLiuliPhaseReceipt(long GrantId, CardConversionSource Source,
    string GameplayHash, int RecipientSeat, int ActualTurn, long WindowId);
public sealed record MouDaQiaoLiuliCostFrozenEvent(long FrameId, CardConversionSource Source, int ActualTurn,
    int OriginalActorSeat, int RedirectTargetSeat, int CardId, Suit Suit) : IGameEvent;
public sealed record MouDaQiaoLiuliMarkGrantedEvent(long GrantId, CardConversionSource Source, string GameplayHash,
    int RecipientSeat, int ActualTurn, long? PreviousGrantId) : IGameEvent;
public sealed record MouDaQiaoLiuliPhaseIssuedEvent(long FrameId, long GrantId, int RecipientSeat, int ActualTurn, long WindowId) : IGameEvent;
public sealed record MouDaQiaoLiuliPhaseReturnedEvent(long FrameId, long GrantId, int RecipientSeat, int ActualTurn) : IGameEvent;
public sealed record MouDaQiaoLiuliMarkConsumedEvent(long FrameId, long GrantId) : IGameEvent;
internal sealed class ResolveMouDaQiaoLiuliHeartDescriptor : MouDaQiaoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouDaQiaoLiuliHeart; }
internal sealed class InsertMouDaQiaoLiuliPlayDescriptor : MouDaQiaoDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.InsertMouDaQiaoLiuliPlay; }
