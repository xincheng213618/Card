using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record CaoTengFactionChosenEvent(long FrameId,string Faction,bool Setup):IGameEvent;
public sealed record CaoTengFactionCompletedEvent(long FrameId,CardConversionSource Source,string Faction,bool Setup):IGameEvent;
internal sealed class ResolveQingliuFactionChoiceDescriptor:CaoTengDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveQingliuFactionChoice;}
