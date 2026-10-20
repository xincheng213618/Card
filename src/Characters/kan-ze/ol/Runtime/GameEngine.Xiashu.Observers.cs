namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 350)]
    private bool AllowsGiveAllHandAndResolveXiashuTrigger(ProgramTriggerLegalityContext gate)
    {
        var features = gate.Features;
        var candidate = gate.Candidate;
        return !(features.HasOperation(SkillProgramEffectOp.GiveAllHandAndResolveXiashu) &&
            !CanOfferXiashu(candidate.OwnerSeat));
    }

 private bool CanOfferXiashu(int ownerSeat)=>_players[ownerSeat].IsAlive&&_cardZones.CardsAt(CardLocation.Hand(ownerSeat)).Count>0;
 [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 120)]
 private bool IsXiashuMovement(ProgramSkillFrame f,SkillProgramEffect? effect,ProgramMovementContinuation p)=>f.XiashuPaid is {Stage:XiashuStage.GiftChildren or XiashuStage.ClaimChildren}&&effect?.Op==SkillProgramEffectOp.GiveAllHandAndResolveXiashu&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null&&CanContinueIssuedKanZe(f);
}
