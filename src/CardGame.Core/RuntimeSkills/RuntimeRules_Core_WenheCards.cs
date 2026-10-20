using System.Text.Json.Serialization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_WenheCards
{
    internal static bool IsWenheCard(CardKind kind) => kind is CardKind.StarShift or CardKind.BeamSwap or CardKind.HandRedistribution or CardKind.WenheChaos;

    internal static LegalActionKind WenheCardAction(CardKind kind) => kind switch
    {
        CardKind.StarShift => LegalActionKind.StarShift, CardKind.BeamSwap => LegalActionKind.BeamSwap,
        CardKind.HandRedistribution => LegalActionKind.HandRedistribution, CardKind.WenheChaos => LegalActionKind.WenheChaos,
        _ => throw new InvalidOperationException("The card is not a Wenhe ordinary trick.")
    };

    internal static CardMoveReason WenheRedistributionReason(CardKind kind) => new($"card.effect.wenhe.{kind}");
}
