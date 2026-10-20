using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_PairedHandRecast
{
    internal static PairedHandRecastParticipantReceipt CurrentPairedRecast(ProgramPairedHandRecastReceipt r) =>
        r.Cursor == 0 ? r.Owner : r.Cursor == 1 ? r.Partner :
            throw new InvalidOperationException("A completed pair has no current recast participant.");

    internal static ProgramPairedHandRecastReceipt WithCurrentPairedRecast(ProgramPairedHandRecastReceipt r,
        PairedHandRecastParticipantReceipt participant) => r.Cursor == 0 ? r with { Owner = participant } :
        r.Cursor == 1 ? r with { Partner = participant } :
            throw new InvalidOperationException("A completed pair cannot replace a current participant.");
}
