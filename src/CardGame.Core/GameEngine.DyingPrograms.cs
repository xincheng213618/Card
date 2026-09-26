namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void RevealProgramUniqueRankForDying(ProgramSkillFrame frame, CardZoneKind zone, int rescueHp)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var dying = _pendingDying ??
            throw new InvalidOperationException("The unique-rank rescue has no dying occurrence.");
        if (active.WindowContext is not
            { Window: SkillProgramTriggerWindow.SelfDyingResponse } context ||
            context.ParentFrameId != dying.FrameId || dying.VictimSeat != active.OwnerSeat ||
            dying.ResponderSeat != active.OwnerSeat ||
            !_players[active.OwnerSeat].IsAlive || _players[active.OwnerSeat].Hp > 0)
            throw new InvalidOperationException("The unique-rank rescue lost its dying owner.");
        if (!EnsureDrawPile()) return;

        var owner = _players[active.OwnerSeat];
        var location = new CardLocation(zone, owner.Seat);
        var card = _cardZones.CardsAt(CardLocation.DrawPile)[^1];
        var unique = _cardZones.CardsAt(location).All(existing => existing.Rank != card.Rank);
        MoveCard(card, CardLocation.DrawPile, unique ? location : CardLocation.DiscardPile,
            new CardMoveReason(unique ? "skill-program.dying-rank.retain" : "skill-program.dying-rank.duplicate"));
        if (unique) owner.Hp = Math.Min(owner.MaxHp, rescueHp);
        QueueGameEvent(new ProgramUniqueRankDyingResolvedEvent(
            dying.FrameId, active.SkillId, owner.Seat, zone, card.Id, card.Rank, unique,
            Array.AsReadOnly(_cardZones.CardsAt(location).Select(item => item.Id).ToArray())));
        AddLog("SkillTriggered", unique
            ? $"{owner.Name} 亮出点数 {card.Rank}，与已有牌不同，回复至 {owner.Hp} 点体力。"
            : $"{owner.Name} 亮出重复点数 {card.Rank}，该牌置入弃牌堆并继续濒死结算。",
            owner.Seat);
    }

    private void UseProgramBoundCardAsDyingAlcohol(
        ProgramSkillFrame frame, string sourceBind, CardMoveReason reason)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var dying = _pendingDying ??
            throw new InvalidOperationException("The configured rescue has no dying occurrence.");
        if (active.WindowContext is not
            { Window: SkillProgramTriggerWindow.DyingResponse } context ||
            context.ParentFrameId != dying.FrameId || context.TargetSeat != dying.VictimSeat ||
            dying.ResponderSeat != active.OwnerSeat ||
            !_players[active.OwnerSeat].IsAlive || !_players[dying.VictimSeat].IsAlive ||
            _players[dying.VictimSeat].Hp > 0)
            throw new InvalidOperationException("The configured rescue lost its responder or dying victim.");

        var binding = GetProgramCardSet(active, sourceBind);
        if (binding.CardIds.Count != 1 || binding.SourceLocations.Count != 1)
            throw new InvalidOperationException("A configured dying rescue requires exactly one bound card.");
        var location = binding.SourceLocations[0];
        if (location.OwnerSeat != active.OwnerSeat || location.Zone is not
            (CardZoneKind.WoodenOxGrain or CardZoneKind.BuquWound or
             CardZoneKind.Authority or CardZoneKind.Chunlao) ||
            _cardZones.GetLocation(binding.CardIds[0]) != location)
            throw new InvalidOperationException("The configured rescue card left its owner pile.");

        var card = _cardZones.CardsAt(location).Single(item => item.Id == binding.CardIds[0]);
        var victim = _players[dying.VictimSeat];
        var source = new CardConversionSource(
            active.SkillId, active.TriggerId ?? active.ActivationId,
            active.OwnerSeat, active.SkillInstanceId);
        var resolutionId = BeginCardUse(
            card, victim.Seat, [victim.Seat], CardKind.Alcohol, conversionSource: source);
        MoveCard(card, location, CardLocation.Processing, reason);
        SetCardUseStep(resolutionId, ResolutionFrameStep.ResolvingEffect);
        var recoveryFrameId = BeginRecovery(resolutionId, victim.Seat, victim.Seat, 1);
        try
        {
            victim.Hp = Math.Min(victim.MaxHp, victim.Hp + 1);
            QueueGameEvent(new RecoveryAppliedEvent(victim.Seat, victim.Seat, 1, victim.Hp));
        }
        finally
        {
            PopResolutionFrame(recoveryFrameId, ResolutionFrameKind.Recovery);
        }
        MoveCard(card, CardLocation.Processing, CardLocation.DiscardPile, reason);
        FinishCardUse(resolutionId, card, CardKind.Alcohol);
        QueueGameEvent(new ProgramDyingRescueEvent(
            dying.FrameId, active.SkillId, active.OwnerSeat, victim.Seat,
            card.Id, 1, victim.Hp));
        var skill = _contentRegistry!.GetSkill(active.SkillId);
        AddLog("SkillTriggered",
            $"{_players[active.OwnerSeat].Name} 发动【{skill.Name}】，令 {victim.Name} 视为使用【酒】并回复1点体力。",
            active.OwnerSeat, victim.Seat);
    }
}
