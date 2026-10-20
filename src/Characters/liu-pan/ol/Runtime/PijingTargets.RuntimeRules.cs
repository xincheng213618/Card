using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_liu_pan_ol_PijingTargets
{
    internal static IReadOnlyList<int> PijingFoldTargets(IReadOnlyList<int> original,IReadOnlyList<PijingToggle> toggles,CardKind kind)
    {
        var targets=original.ToList();foreach(var toggle in toggles){if(!toggle.Remove){targets.AddRange(toggle.Pair);continue;}var at=kind==CardKind.BorrowedSword?Enumerable.Range(0,targets.Count/2).Select(i=>i*2).FirstOrDefault(i=>targets[i]==toggle.Seat,-1):targets.IndexOf(toggle.Seat);if(at<0)throw new InvalidOperationException("Pijing cannot remove an absent original target.");targets.RemoveRange(at,kind==CardKind.BorrowedSword?2:1);}return Array.AsReadOnly(targets.ToArray());
    }
}
