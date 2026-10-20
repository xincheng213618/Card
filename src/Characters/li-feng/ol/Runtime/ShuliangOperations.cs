using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed record ShuliangGrainDistributedEvent(long FrameId, int CardId, int RecipientSeat, CardLocation From, long MovementSequence) : IGameEvent;
public sealed record ShuliangDistributionEntityFrozenEvent(long FrameId, int Index, int CardId, CardKind PrintedKind, CardLocation From) : IGameEvent;
public sealed record ShuliangGrainPaidEvent(long ParentFrameId, long ActionId, CardConversionSource Source, int ProviderSeat, int ActualTurn, int TurnOwner, int CardId, CardLocation From, long MovementSequence, bool Played) : IGameEvent;
internal sealed class DistributeShuliangEndingGrainDescriptor() : LiFengDescriptor(SkillProgramEffectOp.DistributeShuliangEndingGrain){
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeTrueUseProcessingEntry);

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.Frame.LiFeng is { Paid: true };
}
