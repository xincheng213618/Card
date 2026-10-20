using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record QujiaChoiceIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn, long ParentId, string Choice) : IGameEvent;
public sealed record QujiaSlashBonusGrantedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn) : IGameEvent;
internal sealed class ResolveQujiaLimitedTurnEndDescriptor : YangFengDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveQujiaLimitedTurnEnd; }
