using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record WenYangQingjiaoDebtIssuedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int ActualTurn,int TurnOwner,int Gained):IGameEvent;
public sealed record WenYangQingjiaoDebtConsumedEvent(long DebtId,long FrameId,long ParentId,int ActualTurn):IGameEvent;
internal sealed class ResolveQingjiaoWholeHandExchangeDescriptor():WenYangDescriptor(SkillProgramEffectOp.ResolveQingjiaoWholeHandExchange)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)]; }
internal sealed class ResolveQingjiaoIssuedEndingDiscardDescriptor():WenYangDescriptor(SkillProgramEffectOp.ResolveQingjiaoIssuedEndingDiscard)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)]; }
