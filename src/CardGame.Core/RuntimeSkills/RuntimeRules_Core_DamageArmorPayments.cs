using System.Text.Json.Serialization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_DamageArmorPayments
{
    internal static bool DamageAppliedMatchesArmorPayment(DamageAppliedEvent applied,
        DamageArmorPaymentReceipt payment) =>
        applied.ArmorPayment == payment && applied.SourceSeat == payment.SourceSeat &&
        applied.TargetSeat == payment.TargetSeat && applied.Amount == payment.DamageAmount &&
        applied.RemainingHp == payment.HpAfter && applied.Nature == payment.Nature &&
        applied.SourceLess == payment.SourceLess;
}
