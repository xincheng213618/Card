namespace CardGame.Core;

public sealed partial class GameEngine
{

    private bool HasBianFuRenYuejian(int owner) => IsValidPlayerSeat(owner) && _players[owner].IsAlive &&
        GetSkillBindingShard(_players[owner]).GetInstanceTriggers(SkillProgramTriggerWindow.DiscardPileReceived)
            .Any(binding => binding.Trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.YuejianRevealAndClaimUsedCard));

    private int[] MatchingBianFuRenUsedDiscardIndexes(CardMovementBatchContext batch, ProgramTriggerCandidate candidate) =>
        (batch.BianFuRenUsedDiscards ?? []).Where(receipt => receipt.Action.ActorSeat != candidate.OwnerSeat &&
            receipt.Action.EffectiveDesignatedTargetSeats.Contains(candidate.OwnerSeat) &&
            receipt.Materials.Any(BianFuRenMaterialAvailable))
        .Select(receipt => batch.Movements.Select((move, index) => (move, index)).First(pair =>
            receipt.Materials.Any(material => material.CompletionMovementSequence == pair.move.Sequence)).index).Distinct().ToArray();
}
