namespace CardGame.Core;

/// <summary>
/// Local-player action choices and hand explanations from one rules query.
/// This is a read-only preview; commands still validate their revision and prompt.
/// </summary>
public sealed class HumanActionView
{
    internal HumanActionView(long revision, IReadOnlyList<LegalAction> actions, IReadOnlyList<HandCardGuidance> handGuidance,
        WushuangJinzhiMaterialSelection? jinzhiMaterials = null)
    {
        Revision = revision;
        LegalActions = Array.AsReadOnly(actions.Select(action => action with
        {
            TargetSeats = Array.AsReadOnly(action.TargetSeats.ToArray()),
            SelectableCardIds = Array.AsReadOnly(action.SelectableCardIds.ToArray()),
            SelectableTargetSeats = Array.AsReadOnly(action.SelectableTargetSeats.ToArray()),
            SelectableCardTargets = action.SelectableCardTargets is { } cardTargets
                ? Array.AsReadOnly(cardTargets.ToArray()) : null,
            SelectableTargetSequences = action.SelectableTargetSequences is { } targetSequences
                ? Array.AsReadOnly(targetSequences.Select(sequence =>
                    (IReadOnlyList<int>)Array.AsReadOnly(sequence.ToArray())).ToArray()) : null,
            AdditionalConversionSources = action.AdditionalConversionSources is { } sources
                ? Array.AsReadOnly(sources.ToArray()) : null
        }).ToArray());
        HandGuidance = Array.AsReadOnly(handGuidance.ToArray());
        JinzhiMaterials = jinzhiMaterials is null ? null : jinzhiMaterials with
        { CardIds = Array.AsReadOnly(jinzhiMaterials.CardIds.ToArray()) };
    }

    public long Revision { get; }
    public IReadOnlyList<LegalAction> LegalActions { get; }
    public IReadOnlyList<HandCardGuidance> HandGuidance { get; }
    public WushuangJinzhiMaterialSelection? JinzhiMaterials { get; }
}
