namespace CardGame.Core;
public sealed record DuFuRenOwnedOriginEvent(long MovementSequence, int OwnerSeat, Suit Suit) : IGameEvent;
public sealed record DuFuRenTransferProducerEvent(long EntrySequence, long ProducerId, int OwnerSeat, int RecipientSeat) : IGameEvent;
public sealed partial class GameEngine
{
    private static bool IsDuFuRenOwnedZone(CardLocation from) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_du_fu_ren_ol_DuFuRenGains.IsDuFuRenOwnedZone(from);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 26520)]
    private bool QualifyDuFuRen(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context) =>
        IsDuFuRenYiseTrigger(t) ? CurrentDuFuRenGains(c, context) is not null && _players[context.TargetSeat!.Value].IsAlive :
        t.Effects is [{ Op: SkillProgramEffectOp.GiveForNextActualPhases }] ? IsDuFuRenGiftContext(c, context) : true;
}
