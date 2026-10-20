using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record FeiYiYanruPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,int Turn,int TurnOwner,long PhaseInstance,bool Odd) : IGameEvent;
internal sealed class YanruParityDrawDiscardDescriptor():FeiYiDescriptor(SkillProgramEffectOp.YanruParityDrawDiscard)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireActivationEntry()]; }
