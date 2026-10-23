using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_DamageTargetRegionObtain
{
    internal static int DamageTargetObtainedCount(ProgramDamageTargetObtain receipt) =>
        receipt.RegionObtain?.ActualObtainedCount ?? (receipt.Receipt?.To == CardLocation.Hand(receipt.OwnerSeat) ? 1 : 0);

    internal static bool DamageTargetObtainedEquipment(ProgramDamageTargetObtain receipt) =>
        receipt.RegionObtain?.ContainsEquipment ?? (receipt.Receipt is { IsEquipment: true } material && material.To == CardLocation.Hand(receipt.OwnerSeat));

    internal static long DamageTargetObtainAnchor(ProgramDamageTargetObtain receipt) =>
        receipt.RegionObtain?.Movements.FirstOrDefault()?.MovementSequence ?? receipt.ObtainMovementSequence ?? 0;

    internal static bool SameDamageTargetRegionReceipt(ProgramDamageTargetRegionObtainReceipt left,
        ProgramDamageTargetRegionObtainReceipt right) => left.SelectedZone == right.SelectedZone &&
        left.RecipientSeat == right.RecipientSeat && left.SequenceBefore == right.SequenceBefore &&
        left.SequenceAfter == right.SequenceAfter && left.MovementBatchId == right.MovementBatchId && left.Paid == right.Paid &&
        left.Materials.SequenceEqual(right.Materials) && left.Movements.SequenceEqual(right.Movements);
}
