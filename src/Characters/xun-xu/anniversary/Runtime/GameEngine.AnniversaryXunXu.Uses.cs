namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string XunXuPlayPrefix = "anniversary-boqia.";
    private bool IsAnniversaryXunXuBoqiaSource(CardConversionSource? s) => s is { SkillId: AnniversaryXunXuComposition.Boqia } && AnniversaryXunXuComposition.IsBoqiaRuleId(s.BindingId) && ViewAsRule(s) is { InputCount: 3, ExtendedUse: true };
    private bool XunXuAllowance(int owner, CardKind kind)
    {
        var uses = ProgramEventHistory<AnniversaryXunXuUseEvent>().Where(e => e.Source.OwnerSeat == owner && e.Turn == _turnNumber).ToArray();
        if (uses.Any(e => e.Name == ProgramBasicCardName(kind))) return false;
        return uses.Length == 0 || ProgramEventHistory<AnniversaryXunXuRearmedEvent>().Any(e => e.ActionId == uses[^1].ActionId && e.OwnerSeat == owner && e.Turn == _turnNumber);
    }
    private bool XunXuUseDirection(CharacterState owner, CardKind kind, bool response, bool dying) => !response ||
        kind == CardKind.Nullification && ActiveNullificationWindow is not null || kind == CardKind.Dodge && IsProgramResponseCardUse(owner, kind) ||
        kind is CardKind.Peach or CardKind.Alcohol && dying && ActiveDying?.ResponderSeat == owner.Seat;
    [ProgramCardLegality(ProgramCardLegalityStage.ViewAsBan, 46100)]
    private bool BanUnavailableXunXuConversion(ProgramViewAsLegalityQuery q) => q.Source.SkillId == AnniversaryXunXuComposition.Boqia &&
        (!IsAnniversaryXunXuBoqiaSource(q.Source) || q.Source.OwnerSeat != q.Owner.Seat || q.CardIds.Count != 3 || q.CardIds.Distinct().Count() != 3 ||
         !HasRuntimeSkillInstance(q.Owner, q.Source.SkillId, q.Source.SkillInstanceId) || !XunXuAllowance(q.Owner.Seat, q.Kind) || !XunXuUseDirection(q.Owner, q.Kind, q.ForResponse, q.DyingUse) ||
         q.CardIds.Any(id => id <= 0 || _cardZones.GetLocation(id).OwnerSeat != q.Owner.Seat || _cardZones.GetLocation(id).Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)));
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 46100)]
    private void RecordActualXunXuUse(IGameEvent payload)
    {
        CardActionContext action;
        if (payload is CardUseDeclaredEvent declared && LifecycleCardUse(declared.ResolutionId) is { Action: { } a }) action = a;
        else if (payload is CardActionAcceptedEvent accepted) action = accepted.Action;
        else return;
        var sources = action.ConversionChain.Where(s => s.SkillId == AnniversaryXunXuComposition.Boqia).ToArray();
        if (sources.Length == 0 || ProgramEventHistory<AnniversaryXunXuUseEvent>().Any(e => e.ActionId == action.ActionId)) return;
        if (sources is not [var source] || source.OwnerSeat != action.ActorSeat || source.OwnerSeat != action.ProviderSeat || action.RequesterSeat is not null ||
            !IsAnniversaryXunXuBoqiaSource(source) || action.PhysicalCards.Count != 3 || action.PhysicalCards.Select(c => c.CardId).Distinct().Count() != 3 ||
            action.PhysicalCards.Any(c => c.From.OwnerSeat != source.OwnerSeat || c.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) ||
            action.EffectiveKind != ViewAsRule(source)!.OutputKind || action.Type == CardActionType.Response && !XunXuUseDirection(_players[source.OwnerSeat], action.EffectiveKind, true, ActiveDying?.ResponderSeat == source.OwnerSeat))
            throw new InvalidOperationException("Boqia requires a true native Use, its accepted three HE entities, owner and exact conversion source.");
        if (!XunXuAllowance(source.OwnerSeat, action.EffectiveKind)) throw new InvalidOperationException("Boqia cannot reuse this actual-turn allowance or card name.");
        var cards = action.PhysicalCards.Select(c => GetAdvancedCard(c.CardId)).ToArray();
        var reward = cards.Select(c => EffectiveSuit(_players[source.OwnerSeat], c)).Distinct().Count() == 3 || cards.Select(c => GetProgramCardCategory(EffectiveConversionInputKind(_players[source.OwnerSeat], c))).Distinct().Count() == 3;
        AdvanceEventRulesAndQueueFact(new AnniversaryXunXuUseEvent(source, action.ActionId, _turnNumber, ProgramBasicCardName(action.EffectiveKind), reward));
    }
    [ProgramActionSource(ProgramActionSourcePhase.Cardless, 46100)]
    private IEnumerable<LegalAction> XunXuPlayActions(CharacterState owner)
    {
        if (!owner.IsAlive || _winner != Winner.None || _currentSeat != owner.Seat || _phase != TurnPhase.Play || !HasRuntimeSkill(owner, AnniversaryXunXuComposition.Boqia)) yield break;
        foreach (var kind in AnniversaryXunXuComposition.Kinds.Where(k => k is not (CardKind.Dodge or CardKind.Nullification)))
        foreach (var selection in GetProgramMultiCardViewAsSelections(owner, kind, false).Where(s => IsAnniversaryXunXuBoqiaSource(s.Source)))
        {
            var ids = selection.Cards.Select(c => c.Id).Order().ToArray();
            foreach (var plan in XunXuPlayPlans(owner, selection))
            {
                var key = XunXuPlayPrefix + selection.Source.BindingId + "." + string.Join('-', ids) + "." + string.Join('-', plan.Targets) + "." + (plan.Card?.ToString() ?? "none");
                yield return new(LegalActionKind.UseProgramSkill, null, null, DescribeConversion(selection.Source, "将三张牌当【" + CardCatalog.Get(kind).DisplayName + "】使用"), kind,
                    MinCardCount: 3, MaxCardCount: 3, MinTargetCount: plan.Targets.Count, MaxTargetCount: plan.Targets.Count)
                { ProgramSkillId = selection.Source.SkillId, ProgramActivationId = key, ConversionSource = selection.Source, SelectableCardIds = Array.AsReadOnly(ids), SelectableTargetSeats = Array.AsReadOnly(plan.Targets.ToArray()),
                    TargetCardId = plan.Card, ProgramAiHint = new(0, kind == CardKind.Peach ? 1 : 0, 0, 0, 0, IsSlashCard(kind) ? 1 : 0, false, false) };
            }
        }
    }
    private sealed record XunXuUsePlan(LegalActionKind Action, IReadOnlyList<int> Targets, int? Card = null, CardKind? Required = null);
    private IReadOnlyList<XunXuUsePlan> XunXuPlayPlans(CharacterState owner, ProgramMultiCardViewAsSelection s)
    {
        var kind = s.OutputKind; var ids = s.Cards.Select(c => c.Id).ToArray(); var suit = ProgramMultiCardSuit(owner, s); var color = ProgramMultiCardColor(owner, s);
        if (IsSlashCard(kind)) return _players.Where(p => CanUseVirtualSlashTarget(owner, p, kind, physicalSuit: suit, effectiveColor: color, physicalCardIds: ids, conversionSource: s.Source) &&
            !IsDirectedCardTargetProhibited(owner.Seat, p.Seat, kind) && !IsCardTargetProhibited(p, kind, suit, color) && !HasBeneficiarySuitShield(owner.Seat, p.Seat, suit)).Select(p => new XunXuUsePlan(LegalActionKind.Slash, [p.Seat])).ToArray();
        if (kind == CardKind.Peach) return IsRuleWounded(owner) && !HasSelfCardTargetProhibition(owner.Seat) && !IsCardTargetProhibited(owner, kind, suit, color) && !HasBeneficiarySuitShield(owner.Seat, owner.Seat, suit) ? [new(LegalActionKind.Peach, [owner.Seat])] : [];
        if (kind == CardKind.Alcohol) return !owner.HasAlcoholEffect && !HasSelfCardTargetProhibition(owner.Seat) && CanUseJianjiOrdinaryAlcohol(owner, null) && !IsCardTargetProhibited(owner, kind, suit, color) && !HasBeneficiarySuitShield(owner.Seat, owner.Seat, suit) ? [new(LegalActionKind.Alcohol, [])] : [];
        return BuildProgramOrdinaryTrickUseOptions(owner, kind, suit, enforceUsePermission: true, actualEffectiveColor: color, hasActualColor: true, physicalCardIds: ids, conversionSource: s.Source)
            .Select(p => new XunXuUsePlan(p.ActionKind, p.TargetSeats, p.TargetCardId, p.RequiredCardKind)).ToArray();
    }
    [ProgramPipelineHook(ProgramPipelinePhase.FilterPlayPhaseActions, 46100)]
    private IReadOnlyList<LegalAction> FilterXunXuNativeDuplicates(CharacterState actor, IReadOnlyList<LegalAction> actions) => actions.Where(a => a.ConversionSource?.SkillId != AnniversaryXunXuComposition.Boqia || a.ProgramActivationId?.StartsWith(XunXuPlayPrefix, StringComparison.Ordinal) == true).ToArray();
    private CommandError? ValidateXunXuPlay(CharacterState owner, LegalAction action, IReadOnlyList<int> ids, IReadOnlyList<int> targets)
    {
        if (ValidateProgramSelection(action, ids, targets) is { } invalid) return invalid;
        if (action.ConversionSource is not { } source || action.PlayedCardKind is not { } kind || !IsAnniversaryXunXuBoqiaSource(source) || FindProgramMultiCardViewAsSelection(owner, ids, kind, false, source) is not { } selection) return new(CommandErrorCode.InvalidCard, "博洽的原生牌名、三张实际材料或本回合额度已变化。");
        return XunXuPlayPlans(owner, selection).Any(p => p.Targets.SequenceEqual(targets) && p.Card == action.TargetCardId) ? null : new(CommandErrorCode.InvalidTarget, "博洽仍须满足原生目标、距离、次数与禁止使用限制。");
    }
    [ProgramActionHandler(ProgramActionHandlerKind.Submit, 46100)]
    private bool SubmitXunXuPlay(UseProgramSkillCommand command, out CommandResult result)
    {
        result = null!; if (!command.ActivationId.StartsWith(XunXuPlayPrefix, StringComparison.Ordinal)) return false;
        var invalid = ValidateHumanPrompt(command.ActorSeat, DecisionKind.PlayCard, command.PromptId, CommandErrorCode.IllegalAction);
        if (invalid is not null) { result = Reject(invalid.Code, invalid.Message); return true; }
        var owner = _players[command.ActorSeat]; var action = XunXuPlayActions(owner).SingleOrDefault(a => a.ProgramSkillId == command.SkillId && a.ProgramActivationId == command.ActivationId);
        if (command.SkillOwnerSeat is not null || action is null) { result = Reject(CommandErrorCode.IllegalAction, "博洽的实际使用菜单已失效。"); return true; }
        if (ValidateXunXuPlay(owner, action, command.CardIds, command.TargetSeats) is { } error) { result = Reject(error.Code, error.Message); return true; }
        result = Accept(() => { ClearPendingDecision(); ExecuteXunXuPlay(owner, action, command.CardIds, command.TargetSeats); AdvanceRulesAndPublishState(); if (_options.AdvanceAfterHumanCommands) AdvanceToHumanBoundary(); }); return true;
    }
    [ProgramActionHandler(ProgramActionHandlerKind.Execute, 46100)]
    private bool ExecuteXunXuPlay(CharacterState owner, LegalAction action, IReadOnlyList<int> ids, IReadOnlyList<int> targets)
    {
        if (action.ProgramActivationId?.StartsWith(XunXuPlayPrefix, StringComparison.Ordinal) != true || action.ConversionSource is not { } source || !IsAnniversaryXunXuBoqiaSource(source)) return false;
        if (ValidateXunXuPlay(owner, action, ids, targets) is { } invalid) throw new InvalidOperationException(invalid.Message);
        var kind = action.PlayedCardKind!.Value; var s = FindProgramMultiCardViewAsSelection(owner, ids, kind, false, source)!; var plan = XunXuPlayPlans(owner, s).Single(p => p.Card == action.TargetCardId && p.Targets.SequenceEqual(targets));
        if (IsSlashCard(kind)) ResolveSlashCore(owner, _players[targets[0]], s.Cards[0], kind, owner.Seat, physicalCards: s.Cards, conversionSource: source);
        else if (kind == CardKind.Peach) ResolveRecoveryCard(owner, owner, s.Cards[0], "桃", kind, conversionSource: source, physicalCards: s.Cards);
        else
        {
            var use = BeginCardUse(s.Cards[0], owner.Seat, targets, kind, physicalCardIds: ids, conversionSource: source);
            MoveProgramCardsFromMultipleSources(ids, CardLocation.Processing, CardMoveReasons.Use, disclosure: CardMovementDisclosure.FaceUp);
            AdvanceEventRulesAndQueueFact(new ProgramViewAsConvertedEvent(use, source.SkillId, source.BindingId, owner.Seat, ids, kind, true, targets));
            if (kind == CardKind.Alcohol) { RecordMouZhaJiangAwareAlcoholDebit(use, owner); BeginSimpleCardUse(use, new(s.Cards[0].Id, SimpleCardUseEffect.Alcohol)); }
            else BeginJizhiOrNullificationWindow(use, s.Cards[0], owner.Seat, targets, plan.Action, plan.Card, plan.Required, kind);
        }
        return true;
    }
    private bool TryResolveAnniversaryXunXuDyingAlcohol(CharacterState owner, ProgramMultiCardViewAsSelection s)
    {
        if (s.OutputKind != CardKind.Alcohol || !IsAnniversaryXunXuBoqiaSource(s.Source)) return false;
        if (ActiveDying is not { } d || d.VictimSeat != owner.Seat || d.ResponderSeat != owner.Seat || FindProgramMultiCardViewAsSelection(owner, s.Cards.Select(c => c.Id).ToArray(), CardKind.Alcohol, true, s.Source) is null || HasSelfCardTargetProhibition(owner.Seat)) throw new InvalidOperationException("Boqia Alcohol requires its native current self-rescue and three real HE materials.");
        ClearPendingDecision(); ResolveRecoveryCard(owner, owner, s.Cards[0], "酒", CardKind.Alcohol, conversionSource: s.Source,
            dyingResponse: new(d.FrameId, owner.Seat, false, null, true, s.Cards[0].Id), physicalCards: s.Cards); return true;
    }
    private bool TryFinishAnniversaryXunXuAlcoholCost(CardUseFrame use, Card card)
    {
        if (use.CardKind != CardKind.Alcohol || use.CardId != card.Id) return false;
        return TryFinishAnniversaryXunXuUseMaterials(use);
    }
    private bool TryFinishAnniversaryXunXuUseMaterials(CardUseFrame use)
    {
        if (use.Action is not { Type: CardActionType.Use } action || action.ConversionChain.SingleOrDefault(s => s.SkillId == AnniversaryXunXuComposition.Boqia) is not { } source || !IsAnniversaryXunXuBoqiaSource(source)) return false;
        if (use.PhysicalCardIds is not { Count: 3 } ids || !ids.SequenceEqual(action.PhysicalCards.Select(c => c.CardId)) || ids.Any(id => id <= 0) ||
            action.ActorSeat != source.OwnerSeat || action.ProviderSeat != source.OwnerSeat || action.EffectiveKind != use.CardKind ||
            ProgramEventHistory<AnniversaryXunXuUseEvent>().Count(e => e.ActionId == action.ActionId && e.Source == source) != 1)
            throw new InvalidOperationException("Boqia lost its exact accepted three-material native Use before final payment cleanup.");
        var remaining = ids.Where(id => _cardZones.GetLocation(id) == CardLocation.Processing && !IsXinPiPhysicalClaim(use.Id, id) && !IsHuaManClaimedEntity(use.Id, id) && !IsCurrentUsePhysicalCardClaim(use.Id, id) && !IsXuRongCausalEntityClaim(use.Id, id)).ToArray();
        if (remaining.Length > 0) MoveProgramCardsFromMultipleSources(remaining, CardLocation.DiscardPile, CardMoveReasons.UseFinished, disclosure: CardMovementDisclosure.FaceUp);
        return true;
    }
    private bool TryFinishAnniversaryXunXuAcceptedResponse(CardActionContext action)
    {
        var sources = action.ConversionChain.Where(s => s.SkillId == AnniversaryXunXuComposition.Boqia).ToArray();
        if (sources.Length == 0) return false;
        if (action.Type != CardActionType.Response || sources is not [var source] || !IsAnniversaryXunXuBoqiaSource(source) || action.PhysicalCards.Count != 3 ||
            action.ActorSeat != source.OwnerSeat || action.ProviderSeat != source.OwnerSeat || action.RequesterSeat is not null)
            throw new InvalidOperationException("Boqia accepted-response cleanup must retain its native owner and exact three-entity source.");
        return TryFinishAnniversaryXunXuResponseMaterials(new(action.PhysicalCards.Select(c => GetAdvancedCard(c.CardId)).ToArray(), source, action.EffectiveKind));
    }
    private bool TryFinishAnniversaryXunXuResponseMaterials(ProgramMultiCardViewAsSelection selection)
    {
        if (!IsAnniversaryXunXuBoqiaSource(selection.Source)) return false;
        var ids = selection.Cards.Select(c => c.Id).ToArray();
        var action = ProgramEventHistory<CardActionAcceptedEvent>().Select(e => e.Action).LastOrDefault(a => a.Type == CardActionType.Response && a.ActorSeat == selection.Source.OwnerSeat &&
            a.ProviderSeat == selection.Source.OwnerSeat && a.EffectiveKind == selection.OutputKind && a.ConversionChain.Contains(selection.Source) && a.PhysicalCards.Select(c => c.CardId).SequenceEqual(ids));
        if (ids.Length != 3 || ids.Any(id => id <= 0) || ids.Distinct().Count() != 3 || action is null || ProgramEventHistory<AnniversaryXunXuUseEvent>().Count(e => e.ActionId == action.ActionId && e.Source == selection.Source) != 1)
            throw new InvalidOperationException("Boqia response cleanup requires its once-accepted genuine three-material response Use.");
        var parent = _resolutionStack.OfType<CardUseFrame>().LastOrDefault(f => f.Action?.ActionId == action.ParentActionId);
        var remaining = ids.Where(id => _cardZones.GetLocation(id) == CardLocation.Processing && !IsExchangedCardClaim(action.ActionId, id) && (parent is null || !IsXinPiPhysicalClaim(parent.Id, id) && !IsHuaManClaimedEntity(parent.Id, id) && !IsCurrentUsePhysicalCardClaim(parent.Id, id) && !IsXuRongCausalEntityClaim(parent.Id, id))).ToArray();
        if (remaining.Length > 0) MoveProgramCardsFromMultipleSources(remaining, CardLocation.DiscardPile, CardMoveReasons.ResponseFinished, disclosure: CardMovementDisclosure.FaceUp);
        return true;
    }
}
