namespace CardGame.Core;

public sealed partial class GameEngine
{
    private NativeDrawProducerRuntime NativeDrawProducer => GetRuntimeModule<NativeDrawProducerRuntime>();

    private sealed partial class ProgramSkillHost : IResumableProgramDrawHost
    {
        public SkillProgramStepOutcome DrawInstruction(ProgramSkillFrame frame, DrawProgramInstruction instruction,
            int targetSeat, CardMoveReason reason) =>
            engine.ExecuteProgramDrawInstruction(frame, instruction, targetSeat, reason);
    }

    private SkillProgramStepOutcome ExecuteProgramDrawInstruction(ProgramSkillFrame frame,
        DrawProgramInstruction instruction, int targetSeat, CardMoveReason reason) =>
        NativeDrawProducer.ExecuteProgramDrawInstruction(frame, instruction, targetSeat, reason);

    private SkillProgramStepOutcome BeginOperationProgramDraw(ProgramSkillFrame frame,
        int recipientSeat, int requestedCount, CardMoveReason reason) =>
        ProgramCapabilities.Get<IProgramOperationDraws>().BeginOperationProgramDraw(frame, recipientSeat, requestedCount, reason);

    private bool ResumeProgramNativeDraw(long id) => NativeDrawProducer.ResumeProgramNativeDraw(id);
    private bool ContinueProgramNativeDraw(long id) => NativeDrawProducer.ContinueProgramNativeDraw(id);
    private void AssertProgramNativeDraw(ProgramSkillFrame frame, ProgramExecutionPlan plan) =>
        NativeDrawProducer.AssertProgramNativeDraw(frame, plan);
}
