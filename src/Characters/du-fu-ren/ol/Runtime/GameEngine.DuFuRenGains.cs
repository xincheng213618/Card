namespace CardGame.Core;
public sealed record DuFuRenOwnedOriginEvent(long MovementSequence, int OwnerSeat, Suit Suit) : IGameEvent;
public sealed record DuFuRenTransferProducerEvent(long EntrySequence, long ProducerId, int OwnerSeat, int RecipientSeat) : IGameEvent;
public sealed partial class GameEngine
{
    private static bool IsDuFuRenOwnedZone(CardLocation from) => from.OwnerSeat is not null && from.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.WoodenOxGrain;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 26520)]
    private bool QualifyDuFuRen(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context) =>
        IsDuFuRenYiseTrigger(t) ? CurrentDuFuRenGains(c, context) is not null && _players[context.TargetSeat!.Value].IsAlive :
        t.Effects is [{ Op: SkillProgramEffectOp.GiveForNextActualPhases }] ? IsDuFuRenGiftContext(c, context) : true;
}
