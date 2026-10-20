using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record CaoYuRecipientHistoryEvent(long FrameId,int OwnerSeat,string SkillId,int RecipientSeat,int ActualTurn):IGameEvent;
internal sealed class ResolveGongjieRoundFirstGiftsDescriptor:CaoYuDescriptor {public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveGongjieRoundFirstGifts;}
