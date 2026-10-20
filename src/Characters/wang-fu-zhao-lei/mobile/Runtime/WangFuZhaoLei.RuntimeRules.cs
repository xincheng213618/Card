namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_wang_fu_zhao_lei_mobile_WangFuZhaoLei
{
    internal static CardConversionSource XunyiSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);

    internal static int XunyiBranches(XunyiDamageReceipt receipt) => (receipt.DiscardRecipientSeat is null ? 0 : 1) + (receipt.DrawRecipientSeat is null ? 0 : 1);

    internal static int XunyiStepCount(XunyiDamageReceipt receipt) => checked(receipt.ActualAmount * CardGame.Core.RuntimeSkills.RuntimeRules_wang_fu_zhao_lei_mobile_WangFuZhaoLei.XunyiBranches(receipt));

    internal static (int Seat, bool Draw) XunyiStep(XunyiDamagePending pending) =>
        pending.Receipt.DrawRecipientSeat is { } draw && (pending.Receipt.DiscardRecipientSeat is null || pending.StepIndex % CardGame.Core.RuntimeSkills.RuntimeRules_wang_fu_zhao_lei_mobile_WangFuZhaoLei.XunyiBranches(pending.Receipt) == 1)
            ? (draw, true) : (pending.Receipt.DiscardRecipientSeat!.Value, false);
}
