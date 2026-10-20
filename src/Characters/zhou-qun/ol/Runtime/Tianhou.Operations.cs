using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed record TianhouInvokedEvent(long FrameId, CardConversionSource Source, string GameplayHash) : IGameEvent;
public sealed record TianhouViewedEvent(long FrameId, int Index, int CardId) : IGameEvent;
public sealed record TianhouCardShownEvent(long FrameId, int OwnerSeat, int CardId, Suit Suit) : IGameEvent;
public sealed record TianhouWeatherGrantedEvent(long FrameId, CardConversionSource Source, int RecipientSeat, string SkillId, string GrantId, string SkillInstanceId, string GrantSourceId) : IGameEvent;
public sealed record TianhouWeatherExpiredEvent(long FrameId, long OriginalFrameId, int RecipientSeat, string GrantId) : IGameEvent;
internal sealed class TianhouDescriptor() : ZhouQunDescriptor(SkillProgramEffectOp.ResolveTianhouTopExchangeAndGrant){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.ZhouQun is { Paid: true };
}
