namespace CardGame.Core;

public sealed partial class GameEngine
{
    // A legal-action read is pure. Share its derived answers only until that read
    // returns; no revision cache can survive a command or a skill/seat mutation.
    private ActionQuery? _actionQuery;

    private sealed class ActionQuery(int players, (int Seat, Card Card)? printedTargetTemplate = null)
    {
        internal readonly (int Seat, Card Card)? PrintedTargetTemplate = printedTargetTemplate;
        internal readonly SkillBindingShard?[] Bindings = new SkillBindingShard?[players];
        internal readonly Dictionary<(int Seat, Card Card), IReadOnlyList<ProgramCardIdentityMatch>> Identities = [];
        internal readonly Dictionary<(int Seat, Card Card, CardKind Kind, bool Response, bool Dying),
            IReadOnlyList<CardConversionSource>> Conversions = [];
        internal readonly Dictionary<(int Source, int Target, int? Excluded), RuleQueryEvaluation> Distances = [];
        internal readonly Dictionary<(int Seat, int? Excluded), RuleQueryEvaluation> AttackRanges = [];
        internal readonly Dictionary<(int Seat, bool IgnoreSuitBan), IReadOnlyList<Card>> SlashCards = [];
    }

    private ActionQueryScope BeginActionQuery()
    {
        if (_actionQuery is not null) return default;
        _actionQuery = new ActionQuery(_players.Count);
        return new ActionQueryScope(this);
    }

    private readonly struct ActionQueryScope(GameEngine? engine, ActionQuery? previous = null) : IDisposable
    {
        public void Dispose()
        {
            if (engine is not null) engine._actionQuery = previous;
        }
    }

    private IReadOnlyList<ProgramCardIdentityMatch> GetProgramCardIdentityMatches(CharacterState owner, Card card)
    {
        if (_actionQuery is not { } query) return FindProgramCardIdentityMatches(owner, card);
        if (query.PrintedTargetTemplate is { } printed && printed.Seat == owner.Seat && printed.Card == card)
            return [];
        var key = (owner.Seat, card);
        if (!query.Identities.TryGetValue(key, out var result))
            query.Identities[key] = result = FindProgramCardIdentityMatches(owner, card);
        return result;
    }

    // Read only the original entity's native target choices. The separate cache
    // prevents temporary identity suppression from leaking into the enclosing
    // legal-action read, response choices, or a later command.
    private IReadOnlyList<LegalAction> BuildMobileWumouPrintedTargetActions(CharacterState actor, Card card)
    {
        var previous = _actionQuery;
        _actionQuery = new ActionQuery(_players.Count, (actor.Seat, card));
        using var scope = new ActionQueryScope(this, previous);
        return Array.AsReadOnly(BuildLegalActionsCore(actor, includeProgramActions: false,
                selectedPhysicalCardId: card.Id, nativeOriginalCard: card)
            .Where(action => action.CardId == card.Id && action.ConversionSource is null &&
                (action.AdditionalConversionSources is null || action.AdditionalConversionSources.Count == 0) &&
                action.Kind is not (LegalActionKind.Recast or LegalActionKind.EndPlay or
                    LegalActionKind.UseEquipmentEffect or LegalActionKind.UseProgramSkill or LegalActionKind.RevealGeneral) &&
                action.MaxCardCount <= 1 && (action.PlayedCardKind ?? card.Kind) == card.Kind)
            .ToArray());
    }

    private IReadOnlyList<CardConversionSource> GetProgramViewAsConversions(CharacterState owner, Card card,
        CardKind outputKind, bool forResponse, bool dyingUse = false)
    {
        if (_actionQuery is not { } query)
            return FindProgramViewAsConversions(owner, card, outputKind, forResponse, dyingUse);
        var key = (owner.Seat, card, outputKind, forResponse, dyingUse);
        if (!query.Conversions.TryGetValue(key, out var result))
            query.Conversions[key] = result = FindProgramViewAsConversions(owner, card, outputKind, forResponse, dyingUse);
        return result;
    }

    private RuleQueryEvaluation EvaluateDistance(CharacterState source, CharacterState target, int? excludedEquipmentId = null, EquipmentSlot? excludedEquipmentSlot = null)
    {
        if (excludedEquipmentSlot is not null || _actionQuery is not { } query) return CalculateDistance(source, target, excludedEquipmentId, excludedEquipmentSlot);
        var key = (source.Seat, target.Seat, excludedEquipmentId);
        if (!query.Distances.TryGetValue(key, out var result))
            query.Distances[key] = result = CalculateDistance(source, target, excludedEquipmentId);
        return result;
    }

    private RuleQueryEvaluation EvaluateAttackRange(CharacterState player, int? excludedEquipmentId = null, EquipmentSlot? excludedEquipmentSlot = null)
    {
        if (excludedEquipmentSlot is not null || _actionQuery is not { } query) return CalculateAttackRange(player, excludedEquipmentId, excludedEquipmentSlot);
        var key = (player.Seat, excludedEquipmentId);
        if (!query.AttackRanges.TryGetValue(key, out var result))
            query.AttackRanges[key] = result = CalculateAttackRange(player, excludedEquipmentId);
        return result;
    }

    private IReadOnlyList<Card> GetSlashUseCards(CharacterState owner, bool ignoreSuitUseProhibition = false)
    {
        if (_actionQuery is not { } query) return FindSlashUseCards(owner, ignoreSuitUseProhibition);
        var key = (owner.Seat, ignoreSuitUseProhibition);
        if (!query.SlashCards.TryGetValue(key, out var result))
            query.SlashCards[key] = result = FindSlashUseCards(owner, ignoreSuitUseProhibition);
        return result;
    }
}
