using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record LangxiRandomResolvedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,long WindowId,int TargetSeat,int Amount):IGameEvent;
