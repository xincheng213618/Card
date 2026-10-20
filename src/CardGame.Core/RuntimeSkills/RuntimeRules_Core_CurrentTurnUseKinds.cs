using System.Text.Json.Serialization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_CurrentTurnUseKinds
{
    internal static int ActualTurnSuitBit(Suit? suit) => suit switch
    {
        Suit.Spade => 1, Suit.Heart => 2, Suit.Club => 4, Suit.Diamond => 8, _ => 0
    };
}
