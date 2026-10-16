namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void CaptureExtraEffectMaterialClaims(ProgramSkillFrame producer, IDamageAttempt attack, IReadOnlyList<Card> cards, long before, CardMoveReason reason)
    {
        if (LifecycleCardUse(attack.ResolutionId) is not { Action: { } action } use || NativeExtraEffectState(use) is not { } extra) return;
        if (producer.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, ParentFrameId: var windowId, DamageFrameId: { } damageId } ||
            ActiveDamageTrigger is not { } window || window.Id != windowId || window.ParentFrameId != damageId ||
            GetDamageTriggerAttack(window).ResolutionId != use.Id || producer.OwnerSeat != attack.TargetSeat ||
            action.ActionId != extra.ActionId || cards.Any(c => !action.PhysicalCards.Any(p => p.CardId == c.Id)) ||
            extra.MaterialClaims.Any(c => cards.Any(card => card.Id == c.CardId)))
            throw new InvalidOperationException("Extra-effect material claims require the original actual damage use and unclaimed physical costs.");
        var moves = _cardMovements.Where(m => m.Sequence > before && m.Reason == reason &&
            m.From == CardLocation.Processing && m.To == CardLocation.Hand(producer.OwnerSeat)).ToArray();
        if (moves.Length != cards.Count || !moves.Select(m => m.CardId).Order().SequenceEqual(cards.Select(c => c.Id).Order()))
            throw new InvalidOperationException("Extra-effect material claims lost their exact one-time Processing-to-Hand movements.");
        var receipts = moves.Select(m => new ExtraEffectMaterialClaim(use.Id, action.ActionId, m.CardId, producer.Id,
            producer.SkillId, producer.TriggerId!, producer.SkillInstanceId, producer.GameplayHash, producer.InstructionIndex - 1,
            producer.OwnerSeat, window.Id, damageId, m.Sequence, reason.Value));
        UpdateNativeExtraEffectState(use.Id, current => current with { MaterialClaims = extra.MaterialClaims.Concat(receipts).ToArray() });
    }
    private bool HasExtraEffectMaterialClaim(long useId, int cardId)
    {
        if (LifecycleCardUse(useId) is not { Action: { } action } use || NativeExtraEffectState(use) is not { } extra ||
            extra.MaterialClaims.SingleOrDefault(c => c.CardId == cardId) is not { } claim) return false;
        var trigger = _contentRegistry.Skills.GetValueOrDefault(claim.SkillId)?.Program is { } program && program.GameplayHash == claim.GameplayHash
            ? ProgramInstructionResolver.Default.FindTrigger(program, claim.BindingId) : null;
        if (claim.CardUseFrameId != use.Id || claim.ActionId != action.ActionId || extra.ActionId != action.ActionId ||
            !action.PhysicalCards.Any(c => c.CardId == claim.CardId) || !IsValidPlayerSeat(claim.RecipientSeat) ||
            trigger is null || claim.EffectIndex < 0 || claim.EffectIndex >= trigger.Effects.Count || trigger.Effects[claim.EffectIndex].Op != SkillProgramEffectOp.ClaimDamageCards ||
            !_cardMovements.Any(m => m.Sequence == claim.MovementSequence && m.CardId == claim.CardId &&
                m.From == CardLocation.Processing && m.To == CardLocation.Hand(claim.RecipientSeat) && m.Reason.Value == claim.MovementReason) ||
            CompleteProgramEventHistory().OfType<DamageTriggerWindowOpenedEvent>().Count(e => e.ResolutionId == claim.DamageWindowFrameId &&
                e.DamageFrameId == claim.DamageFrameId && e.TargetSeat == claim.RecipientSeat && e.CardId == use.CardId && e.CardKind == use.CardKind &&
                e.Candidates.Any(c => c.ToProgramCandidate() is { } p && p.OwnerSeat == claim.RecipientSeat && p.SkillId == claim.SkillId &&
                    p.BindingId == claim.BindingId && p.SkillInstanceId == claim.SkillInstanceId && p.GameplayHash == claim.GameplayHash)) != 1 ||
            CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Count(e => e.FrameId == claim.ProgramFrameId &&
                e.OwnerSeat == claim.RecipientSeat && e.SkillId == claim.SkillId && e.BindingId == claim.BindingId && e.SkillInstanceId == claim.SkillInstanceId &&
                e.Window == SkillProgramTriggerWindow.AfterDamageApplied) != 1 ||
            CompleteProgramEventHistory().OfType<ProgramDamageCardsClaimedEvent>().Count(e => e.FrameId == claim.ProgramFrameId &&
                e.SkillId == claim.SkillId && e.BindingId == claim.BindingId && e.OwnerSeat == claim.RecipientSeat && e.CardIds.Contains(claim.CardId)) != 1)
            throw new InvalidOperationException("An extra-effect material claim lost its exact original use, producer and actual claim movement.");
        return _cardZones.GetLocation(cardId).Zone is CardZoneKind.Hand or CardZoneKind.DrawPile or CardZoneKind.DiscardPile;
    }
    private void RestoreExtraEffectGroupMaterialClaims(GroupCardHandle group)
    {
        var ids = (LifecycleCardUse(group.ResolutionId) is { } use ? NativeExtraEffectState(use) : null)?.MaterialClaims.Select(c => c.CardId)
            .Where(id => HasExtraEffectMaterialClaim(group.ResolutionId, id)).ToArray();
        if (ids is { Length: > 0 }) group.SetClaimedPhysicalCards(group.DamageClaimedPhysicalCardIds.Concat(ids).Distinct().ToArray());
    }
}
