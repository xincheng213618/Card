namespace CardGame.Core;

public sealed partial class GameEngine
{
    /// <summary>Recovers the owned source of a discard completed through Processing without rewriting the movement ledger.</summary>
    private CardLocation? GetProgramDiscardSource(CardMovementRecord movement)
    {
        if (movement.To != CardLocation.DiscardPile) return null;
        if (IsOwnedDiscardSource(movement.From))
            return IsProgramDiscardOriginReason(movement.Reason) ? movement.From : null;
        if (movement.From != CardLocation.Processing) return null;
        var previous = _cardMovements.LastOrDefault(item => item.CardId == movement.CardId && item.Sequence < movement.Sequence);
        if (previous is null || previous.To != CardLocation.Processing || !IsOwnedDiscardSource(previous.From) ||
            !IsProgramDiscardOriginReason(previous.Reason)) return null;
        var begin = previous.Reason.Value;
        return movement.Reason.Value == begin || movement.Reason.Value == begin + "-finished" || movement.Reason.Value == begin + "-discard"
            ? previous.From : null;
    }

    private static bool IsOwnedDiscardSource(CardLocation location) => location.OwnerSeat is not null &&
        location.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment;

    private CardMovementSourceCount GetProgramDiscardOriginSourceCount(CardLocation source,
        IReadOnlyList<CardMovementRecord> completions)
    {
        if (!IsOwnedDiscardSource(source) || completions.Count == 0 || completions.Any(move => GetProgramDiscardSource(move) != source))
            throw new InvalidOperationException("Discard-origin counts require a frozen owned discard batch.");
        var firstEntry = completions.Min(move => move.From == CardLocation.Processing
            ? _cardMovements.Last(item => item.CardId == move.CardId && item.Sequence < move.Sequence).Sequence
            : move.Sequence);
        var lastCompletion = completions.Max(move => move.Sequence);
        int CountAt(int sequence) => _cardMovements.Where(move => move.Sequence <= sequence)
            .Sum(move => (move.To == source ? 1 : 0) - (move.From == source ? 1 : 0));
        // Owned zones begin empty. Replaying their recorded movements reconstructs
        // exact frozen counts even if other effects have changed the live hand.
        var before = CountAt(firstEntry - 1);
        var after = CountAt(lastCompletion);
        if (before < 0 || after < 0) throw new InvalidOperationException("Discard-origin counts lost their movement ledger.");
        return new(source, before, after);
    }

    private static bool IsProgramDiscardOriginReason(CardMoveReason reason)
    {
        var value = reason.Value;
        // A cleanup move can resemble a discard suffix but is never the paid discard entry.
        if (value.StartsWith("card.use", StringComparison.Ordinal) || value.StartsWith("card.respond", StringComparison.Ordinal) ||
            value.StartsWith("card.response", StringComparison.Ordinal) || value.StartsWith("card.recast.", StringComparison.Ordinal) ||
            value.Contains("replacement", StringComparison.Ordinal) || value.Contains("replace", StringComparison.Ordinal) ||
            value.Contains("death", StringComparison.Ordinal) || value.Contains("harvest", StringComparison.Ordinal) ||
            value.Contains("finished", StringComparison.Ordinal)) return false;
        if (value.StartsWith("skill-program.", StringComparison.Ordinal))
        {
            var operation = value[(value.LastIndexOf('.') + 1)..];
            if (operation is nameof(SkillProgramEffectOp.ChooseCategoryAlternativeDiscard) or
                nameof(SkillProgramEffectOp.EscalatingDiscardOrDamage) or nameof(SkillProgramEffectOp.RequestAttackRangeAid) or
                nameof(SkillProgramEffectOp.PayEquipmentColorDiscard)) return true;
        }
        return IsDiscardMovementReason(reason);
    }
}
