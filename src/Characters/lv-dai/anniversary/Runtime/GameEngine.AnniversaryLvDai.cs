namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 44800)]
    private bool CanOfferAnniversaryLvDaiQinguo(
        ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (candidate.SkillId != "anniversary:qinguo" ||
            trigger.Window != SkillProgramTriggerWindow.CardsGained) return true;
        if (context.MovementBatch is not { } batch) return false;
        var equipment = CardLocation.Equipment(candidate.OwnerSeat);
        // An atomic batch may both remove and add equipment. Its source branch
        // already owns this one count change; a merely declared source is not a loss.
        return !batch.Movements.Any(move => move.From == equipment && move.To != equipment);
    }
}
