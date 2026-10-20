using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record YisuanNullificationQualifiedEvent(long BatchId,long WindowId,long ActionId,int OwnerSeat,int ActualTurn,int PhaseInstance,int ChainDepth,long FirstSequence,long LastSequence,bool DeferredByNamedAmbush):IGameEvent;
public sealed record YisuanMaximumHpPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,long WindowId,long NativeUseId,long NativeActionId,int MaxBefore,int MaxAfter,int HpBefore,int HpAfter):IGameEvent;
public sealed record YisuanGainPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,long NativeUseId,long NativeActionId,long Before,long After,int ActualGain):IGameEvent;
