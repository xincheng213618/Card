namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_wang_yun_ol_WangYunObservers
{
    internal static bool WangYunOriginsEqual(WangYunNativeUseOrigin a,WangYunNativeUseOrigin b)=>a.ProgramFrameId==b.ProgramFrameId && a.InstructionIndex==b.InstructionIndex && a.Source==b.Source &&
        a.GameplayHash==b.GameplayHash && a.Turn==b.Turn && a.PhaseInstance==b.PhaseInstance && a.Kind==b.Kind && a.UseId==b.UseId && a.ActionId==b.ActionId && a.Actor==b.Actor &&
        a.OutputKind==b.OutputKind && a.Before==b.Before && a.After==b.After && a.PaymentCompleted==b.PaymentCompleted && a.RoundOncePayment==b.RoundOncePayment && a.Materials.SequenceEqual(b.Materials) && a.Targets.SequenceEqual(b.Targets);
}
