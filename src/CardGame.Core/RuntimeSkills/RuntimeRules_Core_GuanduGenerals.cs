using System.Globalization;
using System.Text.Json.Serialization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_GuanduGenerals
{
    internal static IReadOnlyList<int> FreezeGuanduSeats(IEnumerable<int> seats) => Array.AsReadOnly(seats.ToArray());
}
