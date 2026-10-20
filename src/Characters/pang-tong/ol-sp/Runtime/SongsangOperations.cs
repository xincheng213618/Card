using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record SongsangHealthIssuedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int VictimSeat,bool Wounded,int HpBefore,int MaximumBefore,[property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] bool? RuleWounded=null):IGameEvent;
internal sealed class ResolveSongsangLimitedOtherDeathDescriptor():SpPangTongDescriptor(SkillProgramEffectOp.ResolveSongsangLimitedOtherDeath)
{public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CharacterDied)];}
