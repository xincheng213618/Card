namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool AdvancedTriggerPrerequisites(CharacterState owner, string skillId, SkillProgramTrigger trigger)
    {
        foreach (var effect in trigger.Effects)
        {
            if (effect.Op == SkillProgramEffectOp.ReplaceSkillsOnAwakening &&
                (AdvancedOwnedSkillIds(owner).Count <= owner.MaxHp ||
                 _skillRuntimeState.GetUsage(owner.Seat, skillId, "awakening", SkillUsageScope.Game) > 0)) return false;
            if (effect.Op == SkillProgramEffectOp.ObtainDeckRankSum &&
                (effect.Marker is not { } marker || owner.Markers.GetValueOrDefault(marker) <= _cardZones.Count(CardLocation.DrawPile) ||
                 !RankSubsetSearch.CanComplete(_cardZones.CardsAt(CardLocation.DrawPile).Select(card => card.Rank), effect.MaximumRankSum))) return false;
            if (effect.Op == SkillProgramEffectOp.DamageAfterDeckShuffle &&
                !EventsSinceLastBoundary(item => item is TurnStartedEvent).OfType<CardMovedEvent>().Any(item => item.Reason == CardMoveReasons.Reshuffle)) return false;
            if (effect.Op == SkillProgramEffectOp.InheritWeapon &&
                !_players.Where(player => player.IsAlive).SelectMany(GetEquipment).Concat(GetHand(owner))
                    .Any(card => EquipmentCatalog.IsEquipment(card.Kind) && EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Weapon)) return false;
            if (effect.Op == SkillProgramEffectOp.ReclaimNamedWeapon && !CanReclaimNamedWeapon(owner, effect)) return false;
        }
        return true;
    }
    private bool CanReclaimNamedWeapon(CharacterState owner, SkillProgramEffect effect)
    {
        var turns = EventsSinceLastBoundary(item => item is TurnStartedEvent).ToArray();
        return (turns.OfType<DamageAppliedEvent>().Any(item => !item.SourceLess && item.SourceSeat == owner.Seat && item.Amount > 0) ||
            turns.OfType<CardUseDeclaredEvent>().Count(item => item.SourceSeat == owner.Seat && item.CardKind is
                CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.Duel or CardKind.FireAttack or CardKind.BarbarianAssault or CardKind.ArrowBarrage) >= 2) &&
            _players.Where(player => player.IsAlive && player.Seat != owner.Seat).SelectMany(GetEquipment).Any(card => card.Kind == effect.OutputKind);
    }
}
