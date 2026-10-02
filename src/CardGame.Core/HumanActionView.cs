namespace CardGame.Core;

/// <summary>
/// Local-player action choices and hand explanations from one rules query.
/// This is a read-only preview; commands still validate their revision and prompt.
/// </summary>
public sealed class HumanActionView
{
    internal HumanActionView(long revision, IReadOnlyList<LegalAction> actions, IReadOnlyList<HandCardGuidance> handGuidance)
    {
        Revision = revision;
        LegalActions = Array.AsReadOnly(actions.Select(action => action with
        {
            TargetSeats = Array.AsReadOnly(action.TargetSeats.ToArray()),
            SelectableCardIds = Array.AsReadOnly(action.SelectableCardIds.ToArray()),
            SelectableTargetSeats = Array.AsReadOnly(action.SelectableTargetSeats.ToArray()),
            AdditionalConversionSources = action.AdditionalConversionSources is { } sources
                ? Array.AsReadOnly(sources.ToArray()) : null
        }).ToArray());
        HandGuidance = Array.AsReadOnly(handGuidance.ToArray());
    }

    public long Revision { get; }
    public IReadOnlyList<LegalAction> LegalActions { get; }
    public IReadOnlyList<HandCardGuidance> HandGuidance { get; }
}
