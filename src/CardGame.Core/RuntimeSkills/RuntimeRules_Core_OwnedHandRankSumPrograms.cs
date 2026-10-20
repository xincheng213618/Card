using System.Collections.ObjectModel;
using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_OwnedHandRankSumPrograms
{
    internal static int OwnedHandSelectedRankSum(ProgramOwnedHandRankSumSelection draft) =>
        draft.CandidateCardIds.Select((id, index) => (id, rank: draft.CandidateRanks[index]))
            .Where(c => draft.SelectedCardIds.Contains(c.id)).Sum(c => c.rank);
}
