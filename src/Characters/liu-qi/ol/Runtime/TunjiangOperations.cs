namespace CardGame.Core;
public sealed record TunjiangDrawPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,long WindowId,int Requested,int Actual,long Before,long After):IGameEvent;
