using System.Text.Json.Serialization;

namespace CardGame.Core;

// The selected real entity stays on its existing native producer while the
// public-reveal reward resolves. A claim never represents physical payment.
public sealed record PublicHandRevealSelection(string ReturnKey, int InstructionIndex,
    long ActionId, int CardId, CardKind PrintedKind, CardLocation From, CardLocation To,
    int ActorSeat, int ChooserSeat, string? ResultBind = null, Suit? FrozenSuit = null,
    int BeforeCoverage = 0, NativeLogicalDiscardInvocationDraft? DiscardInvocation = null,
    CardKind? EffectiveKind = null);

public abstract partial record ResolutionFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PublicHandRevealSelection? PublicHandRevealSelection { get; init; }
}
