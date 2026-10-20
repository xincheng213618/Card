using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record QiejianRoundBlockedEvent(long FrameId,int OwnerSeat,int TargetSeat,int RoundNumber):IGameEvent;
internal sealed class ResolveQiejianLastHandLossDescriptor:QuHuangDescriptor {
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);
public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveQiejianLastHandLoss;}
