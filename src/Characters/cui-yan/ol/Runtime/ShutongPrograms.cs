using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record ShutongNextUseGrantedEvent(long FrameId,CardConversionSource Source,string GameplayHash,long UseId,long ActionId,int ActualTurn,int TurnOwner,int RecipientSeat,Suit Suit):IGameEvent;
public sealed record ShutongNextUseConsumedEvent(long GrantFrameId,int RecipientSeat,long UseId,long ActionId,bool ResponseUse,int ActualTurn,int TurnOwner):IGameEvent;
internal sealed class ResolveShutongFinalizedUseDescriptor():CuiYanDescriptor(SkillProgramEffectOp.ResolveShutongFinalizedUse,SkillProgramTriggerWindow.CardUseTargetsFinalized) { }
