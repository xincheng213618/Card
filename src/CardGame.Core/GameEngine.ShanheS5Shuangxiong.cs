namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsRoundScaledPaidColorSource(CardUseEffectSource source) =>
        _contentRegistry.GetSkill(source.SkillId).Program?.Triggers.SingleOrDefault(t => t.Id == source.BindingId)
            ?.Effects.Any(e => e.Op == SkillProgramEffectOp.DiscardBoundCardForRoundScaledTurnDuel) == true;

    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 5720)]
    private void RecordShanheS5ShuangxiongPayment(ProgramPaidColorConversionPaidEvent paid)
    {
        var program = _contentRegistry.GetSkill(paid.Source.SkillId).Program;
        var trigger = program?.Triggers.SingleOrDefault(t => t.Id == paid.Source.BindingId);
        if (trigger?.Effects.ElementAtOrDefault(paid.EffectIndex)?.Op != SkillProgramEffectOp.DiscardBoundCardForRoundScaledTurnDuel) return;
        var root = _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == paid.ProgramFrameId);
        if (_roundNumber < 1 || root is null || root.OwnerSeat != paid.Source.OwnerSeat ||
            root.SkillId != paid.Source.SkillId || root.SkillInstanceId != paid.Source.SkillInstanceId ||
            root.GameplayHash != paid.Origin.GameplayHash || root.PaidColorConversion?.Origin != paid.Origin ||
            !MatchesPaidColorDrawOwner(root) || !IsActualPaidColorMovement(root.OwnerSeat, paid.Origin) ||
            CompleteProgramEventHistory().OfType<ShanheS5ShuangxiongPaidEvent>().Any(e => e.ProgramFrameId == root.Id))
            throw new InvalidOperationException("A round-scaled Duel activation requires one exact actual completed-draw payment.");
        // A new skill instance does not reset this character's real round count.
        var count = CompleteProgramEventHistory().OfType<ShanheS5ShuangxiongPaidEvent>().Count(e =>
            e.RoundNumber == _roundNumber && e.Source.OwnerSeat == root.OwnerSeat && e.Source.SkillId == root.SkillId);
        AdvanceEventRulesAndQueueFact(new ShanheS5ShuangxiongPaidEvent(root.Id, paid.Source, root.GameplayHash,
            _roundNumber, _turnNumber, checked(count + 1)));
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 5720)]
    private void RecordShanheS5ShuangxiongDuel(CardUseDeclaredEvent declared)
    {
        if (!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DiscardBoundCardForRoundScaledTurnDuel)) return;
        if (declared.CardKind != CardKind.Duel || LifecycleCardUse(declared.ResolutionId) is not { Action: { } action } use ||
            action.Type != CardActionType.Use || action.ActorSeat != use.SourceSeat || action.EffectiveKind != CardKind.Duel ||
            use.CardId != declared.CardId || use.SourceSeat != declared.SourceSeat) return;
        var grants = _turnCardUseEffects.Conversions.Where(g => g.PaidColorOrigin is not null &&
            g.TurnNumber == _turnNumber && g.TurnSeat == _currentSeat && g.Source.OwnerSeat == use.SourceSeat &&
            IsRoundScaledPaidColorSource(g.Source)).GroupBy(g => (g.Source.SkillId, g.Source.SkillInstanceId))
            .Select(group => group.OrderByDescending(g => g.GrantSequence).First())
            .Where(g => action.ConversionChain.Contains(new(g.Source.SkillId, $"{g.Source.BindingId}.turn-{g.EffectIndex}",
                g.Source.OwnerSeat, g.Source.SkillInstanceId))).ToArray();
        if (grants.Length == 0) return;
        if (grants is not [var grant] || action.PhysicalCards.Count != 1 ||
            CompleteProgramEventHistory().OfType<ShanheS5ShuangxiongDuelIssuedEvent>().Any(e => e.UseFrameId == use.Id))
            throw new InvalidOperationException("A round-scaled Duel must retain one exact original conversion source.");
        var paid = CompleteProgramEventHistory().OfType<ShanheS5ShuangxiongPaidEvent>()
            .Single(e => e.ProgramFrameId == grant.ParentFrameId);
        if (paid.ActualTurnNumber != grant.TurnNumber || paid.RoundNumber != _roundNumber || paid.FrozenDamage < 1 ||
            paid.GameplayHash != grant.PaidColorOrigin!.GameplayHash || paid.Source != new CardConversionSource(
                grant.Source.SkillId, grant.Source.BindingId, grant.Source.OwnerSeat, grant.Source.SkillInstanceId))
            throw new InvalidOperationException("A round-scaled Duel lost its frozen original round activation.");
        AdvanceEventRulesAndQueueFact(new ShanheS5ShuangxiongDuelIssuedEvent(use.Id, action.ActionId,
            grant.ParentFrameId, grant.GrantSequence, paid.Source, paid.FrozenDamage));
    }

    private bool TryGetShanheS5ShuangxiongDuelBaseDamage(long useId, out int damage)
    {
        damage = 0;
        if (!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DiscardBoundCardForRoundScaledTurnDuel)) return false;
        var issued = CompleteProgramEventHistory().OfType<ShanheS5ShuangxiongDuelIssuedEvent>()
            .SingleOrDefault(e => e.UseFrameId == useId);
        if (issued is null) return false;
        var use = LifecycleCardUse(useId);
        var paid = CompleteProgramEventHistory().OfType<ShanheS5ShuangxiongPaidEvent>()
            .SingleOrDefault(e => e.ProgramFrameId == issued.ProducerFrameId);
        if (use is not { CardKind: CardKind.Duel, Action: { Type: CardActionType.Use } action } ||
            action.ActionId != issued.CardActionId || action.ActorSeat != issued.Source.OwnerSeat ||
            action.EffectiveKind != CardKind.Duel || action.PhysicalCards.Count != 1 ||
            paid is null || paid.Source != issued.Source || paid.FrozenDamage != issued.FrozenDamage || issued.FrozenDamage < 1 ||
            !action.ConversionChain.Contains(issued.Source with { BindingId = $"{issued.Source.BindingId}.turn-1" }))
            throw new InvalidOperationException("A native Duel lost its original paid conversion and frozen round damage.");
        damage = issued.FrozenDamage; return true;
    }

    private bool TryGetShanheS5ShuangxiongPublicDuelBaseDamage(int viewerSeat, LegalAction action, out int damage)
    {
        damage = 0;
        if (!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DiscardBoundCardForRoundScaledTurnDuel) ||
            !IsValidPlayerSeat(viewerSeat) || action.ConversionSource is not { } source || source.OwnerSeat != viewerSeat ||
            action.CardId is not { } cardId) return false;
        // Look only in this viewer's own visible HE zones. An arbitrary foreign
        // card ID or conversion source must not probe hidden physical entities.
        var owner = _players[viewerSeat];
        var material = GetHand(owner).Concat(GetEquipment(owner)).SingleOrDefault(c => c.Id == cardId);
        if (material is null || !GetPaidColorTurnDuelConversions(owner, material, CardKind.Duel, false,
                _cardZones.GetLocation(material.Id).Zone).Contains(source)) return false;
        var grant = _turnCardUseEffects.Conversions.Where(g => g.PaidColorOrigin is not null &&
            g.TurnNumber == _turnNumber && g.TurnSeat == _currentSeat && g.Source.OwnerSeat == viewerSeat &&
            g.Source.SkillId == source.SkillId && g.Source.SkillInstanceId == source.SkillInstanceId &&
            source.BindingId == $"{g.Source.BindingId}.turn-{g.EffectIndex}" && IsRoundScaledPaidColorSource(g.Source))
            .OrderByDescending(g => g.GrantSequence).FirstOrDefault();
        if (grant is null) return false;
        var paid = CompleteProgramEventHistory().OfType<ShanheS5ShuangxiongPaidEvent>()
            .Single(e => e.ProgramFrameId == grant.ParentFrameId);
        damage = paid.FrozenDamage; return true;
    }
}
