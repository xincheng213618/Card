using System.Text.Json.Serialization;
namespace CardGame.Core;
internal sealed class ResolveMuzhenExchangeDescriptor:XiangChongDescriptor{
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase);
public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveMuzhenExchange;}
