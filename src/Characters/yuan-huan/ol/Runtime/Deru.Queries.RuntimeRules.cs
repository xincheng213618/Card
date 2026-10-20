using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_yuan_huan_ol_Deru_Queries
{
    internal static int DeruNameBit(CardKind kind) => CardGame.Core.RuntimeSkills.RuntimeRules_Core_AlternativeViewAsCosts.ProgramBasicCardName(kind) switch { CardKind.Slash => 1, CardKind.Dodge => 2, CardKind.Peach => 4, CardKind.Alcohol => 8, _ => 0 };
}
