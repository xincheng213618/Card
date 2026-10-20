using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_SimultaneousPindian
{
    internal static int[] SimultaneousNonwinners(PindianResult p) => p.SourceRank == p.OpponentRank ? [p.SourceSeat, p.OpponentSeat] :
        p.SourceWon ? [p.OpponentSeat] : [p.SourceSeat];
}
