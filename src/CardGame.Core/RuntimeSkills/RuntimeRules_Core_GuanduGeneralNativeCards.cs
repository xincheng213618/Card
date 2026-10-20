using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_GuanduGeneralNativeCards
{
    internal static string GuanduKindName(CardKind kind) => char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..];
}
