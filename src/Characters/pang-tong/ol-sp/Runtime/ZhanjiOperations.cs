using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record ZhanjiDrawEntitlement(CardConversionSource Source,string GameplayHash);
public sealed record ZhanjiDrawEntitlementsCapturedEvent(long InvocationId,int OwnerSeat,long SequenceBefore,int Count):IGameEvent;
public sealed record ZhanjiDrawSourceCapturedEvent(long InvocationId,int OwnerSeat,long SequenceBefore,ZhanjiDrawEntitlement Entitlement):IGameEvent;
