using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record ShiluHolderChosenEvent(long FrameId,int HolderSeat,long MovementCutoff):IGameEvent;
public sealed record ShiluCandidateFrozenEvent(long FrameId,int Index,WangGuanHandEntity Entity):IGameEvent;
public sealed record ShiluHandIdentityGrantedEvent(long FrameId,CardConversionSource Source,string GameplayHash,long DamageFrameId,int HolderSeat,int CardId,long HandEntry,long MovementCutoff):IGameEvent;
internal sealed class ResolveShiluDrawRevealIdentityDescriptor:WangGuanDescriptor
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
 public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveShiluDrawRevealIdentity; }
