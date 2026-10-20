using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IsNiuFuZonglueDamage(int owner, ProgramSkillWindowContext c)
    {
        if (c is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0, DamageFrameId: { } id, TargetSeat: { } target, SourceSeat: { } source } || source != owner ||
            NiuFuOriginalAttack(id) is not { IsSourceLess: false, IsChainPropagation: false, DamageWasApplied: true } attack || attack.SourceSeat != owner || attack.CardUserSeat != owner ||
            attack.TargetSeat != target || attack.DamageAmount != c.Amount || attack.EffectiveCardKind is not { } kind || !IsSlashCard(kind) ||
            LifecycleCardUse(attack.ResolutionId) is not { } use || use.SourceSeat != owner || !use.TargetSeats.Contains(target) || !IsSlashCard(use.CardKind)) return false;
        var cards = attack.PhysicalCards;
        if (cards.Count > 0 && cards.All(card => IsSlashCard(card.Kind))) return false;
        if (use.Action is { } action) return action.Type == CardActionType.Use && action.ActorSeat == owner && action.EffectiveKind == use.CardKind &&
            action.PhysicalCards.Select(x => x.CardId).SequenceEqual(cards.Select(x => x.Id)) && action.PhysicalCards.Select(x => x.CardKind).SequenceEqual(cards.Select(x => x.Kind)) &&
            ProgramEventHistory<CardActionAcceptedEvent>().Any(e => e.Action.ActionId == action.ActionId && e.Action.PhysicalCards.SequenceEqual(action.PhysicalCards));
        return use.CardId == 0 && cards.Count == 0 && use.PhysicalCardIds is { Count: 0 } &&
            ProgramEventHistory<CardUseDeclaredEvent>().Any(e => e.ResolutionId == use.Id && e.CardId == 0 && e.CardKind == use.CardKind && e.SourceSeat == owner);
    }
}
