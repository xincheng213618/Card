namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasHengwuCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DrawHengwuFieldSuitEquipment);
    private bool ShouliIgnoresSlashLimit(CharacterState owner, CardConversionSource? source) =>
        source is { SkillId: ShenMaChaoComposition.Shouli, BindingId: ShenMaChaoComposition.Slash } && source.OwnerSeat == owner.Seat &&
        GetSkillBindingShard(owner).GetSingleCardConversions(CardKind.Slash).Any(binding =>
            binding.Source.SkillId == source.SkillId && binding.Source.SkillInstanceId == source.SkillInstanceId &&
            binding.Rule.Id == source.BindingId && binding.Rule.FieldMountConversion == true);
    private static CardKind? ShouliMountOutput(Card card) => !EquipmentCatalog.IsEquipment(card.Kind) ? null : EquipmentCatalog.Get(card.Kind).Slot switch
    { EquipmentSlot.OffensiveHorse => CardKind.Slash, EquipmentSlot.DefensiveHorse => CardKind.Dodge, _ => null };
    private bool IsShouliFieldMaterial(CharacterState actor, Card card, out CardLocation location)
    {
        location = _cardZones.GetLocation(card.Id);
        return actor.IsAlive && ShouliMountOutput(card) is not null && location.Zone is CardZoneKind.Equipment or CardZoneKind.Judgment &&
            location.OwnerSeat is { } owner && IsValidPlayerSeat(owner) && _players[owner].IsAlive &&
            _cardZones.CardsAt(location).Any(c => c.Id == card.Id && c.Kind == card.Kind);
    }
    private IEnumerable<(CardConversionSource Source, SkillProgramViewAs Rule)> ShouliSources(CharacterState actor, Card card, bool response)
    {
        if (!IsShouliFieldMaterial(actor, card, out _) || IsResponseEntityRestricted(actor.Seat, card.Id)) yield break;
        var output = ShouliMountOutput(card)!.Value;
        if (IsCardUseForbidden(actor.Seat, output, response ? CardActionType.Response : CardActionType.Use) ||
            IsProgramEntityPhysicalRestricted(actor.Seat, card.Id) || IsProgramMaterialUseForbidden(actor.Seat, output, [card.Id], response)) yield break;
        foreach (var (instance, rule) in GetSkillBindingShard(actor).GetSingleCardConversions(output))
        {
            if (instance.SkillId != ShenMaChaoComposition.Shouli || rule.FieldMountConversion != true || !rule.InputKinds.Contains(card.Kind) ||
                !(response ? rule.ForResponse : rule.ForPlay) || !rule.Condition.Evaluate(CreateSkillContext(actor))) continue;
            var source = new CardConversionSource(instance.SkillId, rule.Id, actor.Seat, instance.SkillInstanceId);
            if (IsProgramViewAsAllowed(actor, output, [card.Id], source, response) &&
                !CardLegality.IsConversionSourceForbidden(ProgramCardLegalityStage.ConversionSourceBan, new(actor, card, source, response, false)))
                yield return (source, rule);
        }
    }
    // The special pool never enters GetPlayableCards: unrelated hand-card costs,
    // equipment uses and recasts cannot acquire another player's field entity.
    private IReadOnlyList<CardConversionSource> ShouliFieldConversions(CharacterState actor, Card card, CardKind output, bool response, bool dyingUse)
    {
        if (dyingUse || !IsShouliFieldMaterial(actor, card, out var from) ||
            from == CardLocation.Equipment(actor.Seat) || ShouliMountOutput(card) != output) return [];
        return Array.AsReadOnly(ShouliSources(actor, card, response).Select(x => x.Source).ToArray());
    }
    private IEnumerable<Card> ShouliFieldCards(CharacterState actor) => _players.Where(p => p.IsAlive)
        .SelectMany(p => GetEquipment(p).Concat(GetJudgment(p))).Where(c => ShouliMountOutput(c) is not null).DistinctBy(c => c.Id);
    private IReadOnlyList<Card> ShouliFieldResponseCards(CharacterState actor, CardKind requiredKind) => requiredKind is not (CardKind.Slash or CardKind.Dodge) ? [] :
        Array.AsReadOnly(ShouliFieldCards(actor).Where(c => ShouliMountOutput(c) == requiredKind && ShouliSources(actor, c, true).Any()).ToArray());
    private Card? FindShouliFieldCard(CharacterState actor, int? id) => id is { } actual ?
        ShouliFieldCards(actor).SingleOrDefault(c => c.Id == actual && ShouliMountOutput(c) == CardKind.Slash && ShouliSources(actor, c, false).Any()) : null;
    private bool IsAcceptedShouliMaterial(CharacterState actor, Card card, CardConversionSource? source, bool response) =>
        source is { SkillId: ShenMaChaoComposition.Shouli } && source.OwnerSeat == actor.Seat &&
        ShouliSources(actor, card, response).Any(s => s.Source == source);
    private bool TryShouliFieldLocation(CharacterState actor, Card card, out CardLocation location)
    {
        if (!IsShouliFieldMaterial(actor, card, out location)) return false;
        if (IsAcceptedShouliMaterial(actor, card, _selectedUseConversion, false)) return true;
        var action = _resolutionStack.OfType<CardUseFrame>().LastOrDefault()?.Action;
        return action is not null && action.ProviderSeat == actor.Seat && action.PhysicalCards is [var cost] && cost.CardId == card.Id && cost.From == location &&
            action.ConversionChain.Any(s => IsAcceptedShouliMaterial(actor, card, s, false));
    }
    // Native response callers pass the already validated returned conversion;
    // its ephemeral selection has deliberately been cleared before real payment.
    private CardLocation FindOwnedCardLocation(CharacterState actor, Card card, CardConversionSource? acceptedConversion) =>
        IsAcceptedShouliMaterial(actor, card, acceptedConversion, true) && IsShouliFieldMaterial(actor, card, out var from)
            ? from : FindOwnedCardLocation(actor, card);
    [ProgramCardAction(ProgramCardActionPhase.BeforeProgram, 39000)]
    private void AddShouliForeignSlashActions(List<LegalAction> actions, CharacterState actor, int? selectedPhysicalCardId) =>
        actions.AddRange(ShouliForeignSlashActions(actor).Where(action => selectedPhysicalCardId is null || action.CardId == selectedPhysicalCardId));
    private IEnumerable<LegalAction> ShouliForeignSlashActions(CharacterState actor)
    {
        if (actor.Seat != _currentSeat || _phase != TurnPhase.Play || !actor.IsAlive) yield break;
        foreach (var card in ShouliFieldCards(actor).Where(c => _cardZones.GetLocation(c.Id) != CardLocation.Equipment(actor.Seat) && ShouliMountOutput(c) == CardKind.Slash))
        foreach (var (source, _) in ShouliSources(actor, card, false))
        foreach (var variant in GetSlashUseVariants(actor, card, CardKind.Slash, source))
        foreach (var target in GetFangtianOrderedSlashTargets(actor, card, source, effectiveKind: variant.EffectiveKind, ignoresSlashLimit: true))
            yield return new(LegalActionKind.Slash, card.Id, target.Seat,
                $"【狩骊】将 {_players[_cardZones.GetLocation(card.Id).OwnerSeat!.Value].Name} 场上的【{card.DisplayName}】当【{CardCatalog.Get(variant.EffectiveKind).DisplayName}】使用", variant.EffectiveKind)
                { ConversionSource = source, AdditionalConversionSources = variant.UsesZhuqueFan ? [] : null };
    }
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.AcceptedConversionCost, 39000)] private void IssueShouliBorrowedMountDebt(CardActionContext action)
    {
        if (action.PhysicalCards is not [var cost] || cost.From.Zone is not (CardZoneKind.Equipment or CardZoneKind.Judgment) ||
            cost.From.OwnerSeat is not { } originalOwner || action.ConversionChain.SingleOrDefault(s => s.SkillId == ShenMaChaoComposition.Shouli) is not { } source ||
            originalOwner == source.OwnerSeat || ProgramEventHistory<ShouliBorrowedMountEvent>().Any(e => e.ActionId == action.ActionId && e.Source == source)) return;
        if (!IsValidPlayerSeat(originalOwner) || !IsValidPlayerSeat(source.OwnerSeat) ||
            ViewAsRule(source) is not { FieldMountConversion: true } rule || !rule.InputKinds.Contains(cost.CardKind) ||
            (rule.OutputKind == CardKind.Slash ? !IsSlashCard(action.EffectiveKind) : action.EffectiveKind != CardKind.Dodge) ||
            !_cardMovements.Any(m => m.CardId == cost.CardId && m.CardKind == cost.CardKind && m.From == cost.From &&
                m.To == CardLocation.Processing && m.TurnNumber == _turnNumber && m.Reason == (action.Type == CardActionType.Use ? CardMoveReasons.Use : CardMoveReasons.Respond)))
            throw new InvalidOperationException("Shouli debt requires its exact accepted real field-mount conversion payment.");
        AdvanceEventRulesAndQueueFact(new ShouliBorrowedMountEvent(action.ActionId, source, _turnNumber, _currentSeat, cost.CardId, cost.From));
        if (!_players[originalOwner].IsAlive || _currentTurnSkillSuppressions.Any(s => s.TurnNumber == _turnNumber && s.TurnOwnerSeat == _currentSeat && s.TargetSeat == originalOwner)) return;
        var parentId = _resolutionStack.OfType<CardUseFrame>().LastOrDefault(u => u.Action?.ActionId == action.ActionId)?.Id ?? ActiveCardAttack?.ResolutionId ??
            throw new InvalidOperationException("Shouli's paid suppression lost its native original owner.");
        var fact = new CurrentTurnNonLockedSkillSuppression(_turnNumber, _currentSeat, parentId, 0,
            new(source.SkillId, source.BindingId, source.OwnerSeat, source.SkillInstanceId), originalOwner);
        _currentTurnSkillSuppressions.Add(fact); _currentTurnSkillSuppressionRevision++;
        AdvanceEventRulesAndQueueFact(new CurrentTurnNonLockedSkillSuppressionIssuedEvent(fact));
    }
    private bool HasShouliDamageDebt(int target) => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DealShouliStartingMounts) &&
        ProgramEventHistory<ShouliBorrowedMountEvent>().Any(e => e.TurnNumber == _turnNumber &&
        e.TurnOwnerSeat == _currentSeat && (e.Source.OwnerSeat == target || e.From.OwnerSeat == target));
    private DamageNature? ShouliThunderNature(IDamageAttempt attack) => HasShouliDamageDebt(attack.TargetSeat) ? DamageNature.Thunder : null;
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 39000)] private int ShouliReceivedDamageBonus(IDamageAttempt attack) => HasShouliDamageDebt(attack.TargetSeat) ? 1 : 0;
}
