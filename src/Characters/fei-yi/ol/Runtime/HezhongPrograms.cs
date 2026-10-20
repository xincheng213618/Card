using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record FeiYiSingletonTransition(int OwnerSeat,int Before,int After,int CardId,int Rank);
public sealed record FeiYiEffectRounds(long ActionId,int Turn,int TurnOwner,int AcceptedActorSeat,ProgramTrickContinuation Trick,ExtraCardEffectState Native,int Started=0)
{
    private IReadOnlyList<long> _grants=[];
    public IReadOnlyList<long> Grants { get=>_grants; init=>_grants=Array.AsReadOnly(value.ToArray()); }
}
public sealed record FeiYiRankEffectIssuedEvent(long FrameId,CardConversionSource Source,string GameplayHash,int Turn,int TurnOwner,long BatchId,int CardId,int Rank,bool Greater) : IGameEvent;
public sealed record FeiYiHezhongPaidEvent(long FrameId,CardConversionSource Source,string GameplayHash,int Turn,int TurnOwner,long BatchId,int CardId,int Rank,bool Greater) : IGameEvent;
public sealed record FeiYiRankEffectBoundEvent(long GrantId,long UseFrameId,long ActionId,int EffectiveRank,int AcceptedActorSeat) : IGameEvent;
public sealed record FeiYiEffectRoundStartedEvent(long UseFrameId,long ActionId,int Round) : IGameEvent;
internal sealed class RevealSingletonAndArmRankEffectsDescriptor():FeiYiDescriptor(SkillProgramEffectOp.RevealSingletonAndArmRankEffects)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.CardsMoved)]; }
