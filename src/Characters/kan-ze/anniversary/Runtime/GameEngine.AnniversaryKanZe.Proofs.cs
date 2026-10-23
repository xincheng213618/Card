namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static ProgramTriggerCandidate AnniversaryKanZeCandidate(ProgramSkillFrame f) => new(f.OwnerSeat, f.SkillId, f.TriggerId!,
        f.SkillInstanceId, f.GameplayHash, 0, f.WindowContext?.OccurrenceIndex ?? 0);

    private bool HasIssuedAnniversaryKuanshiFrame(ProgramSkillFrame f)
    {
        if (f.SkillId != AnniversaryKanZeComposition.Kuanshi || f.TriggerId != AnniversaryKanZeComposition.Reward || f.WindowContext is not { } w ||
            f.InstructionIndex is < 0 or > 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            AnniversaryKuanshiWitness(AnniversaryKanZeCandidate(f), w) is not { } witness) return false;
        if (f.AnniversaryKuanshi is not { } receipt)
            return f.InstructionIndex is 0 or 1 && !ProgramEventHistory<AnniversaryKuanshiRecoveryBoundEvent>().Any(e => e.GrantId == witness.Grant.FrameId && e.DamageFrameId == witness.Damage.DamageFrameId);
        return receipt == (witness with { InstructionIndex = 1 }) && f.InstructionIndex == 1 &&
            ProgramEventHistory<AnniversaryKuanshiRecoveryBoundEvent>().Count(e => e == new AnniversaryKuanshiRecoveryBoundEvent(
                f.Id, receipt.Grant.FrameId, receipt.Damage.DamageFrameId, receipt.Damage.ActualTurn)) == 1;
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 42900)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 42900)]
    private bool ContinueAnniversaryKuanshiIssuedFrame(ProgramSkillFrame f) => HasIssuedAnniversaryKuanshiFrame(f);

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 42900)]
    private void RequireAnniversaryKuanshiRecoveryFrame(ProgramSkillFrame f)
    {
        if (f.AnniversaryKuanshi is not null && !HasIssuedAnniversaryKuanshiFrame(f))
            throw new InvalidOperationException("Anniversary Kuanshi lost its original issued grant, native damage candidate or typed recovery receipt.");
    }
}
