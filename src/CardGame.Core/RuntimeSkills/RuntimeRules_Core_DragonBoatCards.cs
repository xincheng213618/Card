using System.Globalization;
using System.Text.Json.Serialization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_DragonBoatCards
{
    internal static bool IsDragonBoatCard(CardKind kind) => kind is CardKind.Zong or CardKind.RealgarWine or
        CardKind.SameBoat or CardKind.StriveUpstream or CardKind.AgainstCurrent;

    internal static LegalActionKind DragonBoatCardAction(CardKind kind) => kind switch
    {
        CardKind.Zong => LegalActionKind.Zong, CardKind.RealgarWine => LegalActionKind.RealgarWine,
        CardKind.SameBoat => LegalActionKind.SameBoat, CardKind.StriveUpstream => LegalActionKind.StriveUpstream,
        CardKind.AgainstCurrent => LegalActionKind.AgainstCurrent,
        _ => throw new InvalidOperationException("This is not a Dragon Boat card.")
    };
}
