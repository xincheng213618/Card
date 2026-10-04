namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string SpGuanYuWushengSkillId = "sp:guan-yu-wusheng";
    private const string NuzhanSkillId = "sp:nuzhan";

    private bool IgnoresSpGuanYuWushengDistance(CharacterState player, Card card) =>
        HasRuntimeSkill(player, SpGuanYuWushengSkillId) &&
        card.Suit == Suit.Diamond;

    private NuzhanModifiers GetNuzhanModifiers(long frameId, CharacterState source)
    {
        if (!HasRuntimeSkill(source, NuzhanSkillId))
            return default;

        var action = _resolutionStack
            .OfType<CardUseFrame>()
            .Single(frame => frame.Id == frameId)
            .Action;
        if (action is null ||
            action.EffectiveKind != CardKind.Slash ||
            action.PhysicalCards.Count != 1 ||
            !action.ConversionChain.Any(conversion =>
                conversion.OwnerSeat == source.Seat &&
                string.Equals(conversion.SkillId, SpGuanYuWushengSkillId, StringComparison.Ordinal)))
        {
            return default;
        }

        var physical = action.PhysicalCards[0];
        var modifiers = new NuzhanModifiers(
            IgnoresSlashLimit: CardCatalog.Get(physical.CardKind).CategoryName == "锦囊牌",
            DamageBonus: EquipmentCatalog.IsEquipment(physical.CardKind) ? 1 : 0,
            PhysicalCardId: physical.CardId);
        if (!modifiers.IgnoresSlashLimit && modifiers.DamageBonus == 0) return default;

        AdvanceEventRulesAndQueueFact(new NuzhanAppliedEvent(
            frameId,
            source.Seat,
            modifiers.PhysicalCardId,
            modifiers.IgnoresSlashLimit,
            modifiers.DamageBonus));
        AddLog(
            "SkillTriggered",
            modifiers.IgnoresSlashLimit
                ? $"{source.Name} 的【怒斩】令此【杀】不计入出牌阶段次数。"
                : $"{source.Name} 的【怒斩】令此【杀】的伤害值+1。",
            source.Seat);
        return modifiers;
    }

    private void AddNuzhanUnlimitedTrickSlashActions(
        ICollection<LegalAction> actions,
        CharacterState actor,
        IReadOnlyList<Card> playableCards)
    {
        if (!HasRuntimeSkill(actor, NuzhanSkillId)) return;

        foreach (var converted in playableCards.Where(card =>
                     CardCatalog.Get(card.Kind).CategoryName == "锦囊牌"))
        {
            var source = GetProgramViewAsConversions(
                    actor,
                    converted,
                    CardKind.Slash,
                    forResponse: false)
                .SingleOrDefault(candidate =>
                    string.Equals(candidate.SkillId, SpGuanYuWushengSkillId, StringComparison.Ordinal));
            if (source is null) continue;

            var targets = GetFangtianOrderedSlashTargets(
                actor,
                converted,
                source,
                ignoresSlashLimit: true);
            foreach (var target in targets)
            {
                actions.Add(new LegalAction(
                    LegalActionKind.Slash,
                    converted.Id,
                    target.Seat,
                    DescribeConversion(source,
                        $"将【{converted.DisplayName}】当作【杀】对 {target.Name} 使用（【怒斩】不计次数）"),
                    PlayedCardKind: CardKind.Slash)
                {
                    ConversionSource = source
                });
            }

            AddFangtianHalberdSlashActions(
                actions,
                actor,
                converted,
                targets,
                "杀",
                CardKind.Slash,
                source);
            AddProgramTargetCountSlashActions(
                actions,
                actor,
                converted,
                targets,
                "杀",
                CardKind.Slash,
                source);
        }
    }

    private readonly record struct NuzhanModifiers(
        bool IgnoresSlashLimit,
        int DamageBonus,
        int PhysicalCardId);

    private sealed record ProgramCardIdentityMatch(
        SkillProgram Program,
        SkillProgramCardIdentity Identity,
        CardConversionSource Source);

    private sealed record ProgramMultiCardViewAsSelection(
        IReadOnlyList<Card> Cards,
        CardConversionSource Source,
        CardKind OutputKind);

    private CardConversionSource? _selectedResponseConversion;
    private CardConversionSource? _selectedUseConversion;
    private bool _hasSelectedResponseConversionChoice;
    private bool _hasSelectedUseConversionChoice;

    private string DescribeConversion(CardConversionSource? source, string description)
    {
        if (source is null) return description;
        if (ViewAsRule(source)?.DeclarationValidation is not null)
            description = System.Text.RegularExpressions.Regex.Replace(description, "将【[^】]*】(?:当作|当|改为)【[^】]*】",
                "声明【" + CardCatalog.Get(ViewAsRule(source)!.OutputKind).DisplayName + "】");
        return $"【{_contentRegistry!.Skills[source.SkillId].Name}】{description}";
    }

    private IReadOnlyList<ProgramCardIdentityMatch> GetProgramCardIdentityMatches(
        CharacterState owner,
        Card card)
    {
        var identityHand = _cardZones.GetLocation(card.Id) == CardLocation.Hand(owner.Seat);
        if (!identityHand && !IsAlcoholIdentityOwnedLocation(owner, card)) return [];

        var context = CreateSkillContext(owner);
        return GetSkillBindingShard(owner).ProgramInstances
            .Where(instance => instance.Program.CardIdentities.Count != 0)
            .SelectMany(instance => instance.Program.CardIdentities
                .Where(identity => (identityHand || instance.Program.CardPolicies.Any(p => p.Kind == SkillProgramCardPolicyKind.AlcoholKingIdentityRank) &&
                                    identity.InputKinds.SequenceEqual([CardKind.Alcohol]) && identity.OutputKind == CardKind.Slash) && identity.Zones.Contains(CardZoneKind.Hand) &&
                                   identity.Condition.Evaluate(context) &&
                                   (identity.InputKinds.Count == 0 || identity.InputKinds.Contains(card.Kind)) &&
                                   (identity.InputSuits.Count == 0 || identity.InputSuits.Contains(card.Suit)))
                .Select(identity => new ProgramCardIdentityMatch(
                    instance.Program,
                    identity,
                    new CardConversionSource(
                        instance.SkillId,
                        identity.Id,
                        owner.Seat,
                        instance.SkillInstanceId))))
            .OrderBy(match => match.Source.SkillId, StringComparer.Ordinal)
            .ThenBy(match => match.Source.BindingId, StringComparer.Ordinal)
            .ThenBy(match => match.Source.SkillInstanceId, StringComparer.Ordinal)
            .ToArray();
    }

    private IReadOnlyList<CardConversionSource> GetProgramCardIdentitySources(
        CharacterState owner,
        Card card,
        CardKind effectiveKind,
        bool forResponse)
    {
        var matches = GetProgramCardIdentityMatches(owner, card);
        if (matches.Count == 0) return [];
        return matches
            .Where(match => match.Identity.OutputKind == effectiveKind ||
                            forResponse && MatchesRequiredCard(match.Identity.OutputKind, effectiveKind) ||
                            !forResponse && match.Identity.OutputKind == CardKind.Slash &&
                            IsSlashCard(effectiveKind))
            .Select(match => match.Source)
            .ToArray();
    }

    private bool HasProgramCardIdentity(CharacterState owner, Card card) =>
        GetProgramCardIdentityMatches(owner, card).Count != 0;

    private bool IgnoresProgramSlashDistance(
        CharacterState owner,
        CardConversionSource? source)
    {
        if (source is null || source.OwnerSeat != owner.Seat) return false;
        var context = CreateSkillContext(owner);
        return GetSkillBindingShard(owner).GetNumericModifiers(SkillRuleQuery.SlashDistanceLimit).Any(binding =>
            binding.Source.SkillId == source.SkillId &&
            binding.Source.SkillInstanceId == source.SkillInstanceId &&
            binding.Modifier is { } modifier &&
                modifier.Query == SkillRuleQuery.SlashDistanceLimit &&
                modifier.Operation == SkillRuleOperation.Unlimited &&
                modifier.SourceCardIdentityId == source.BindingId &&
                modifier.Condition.Evaluate(context));
    }

    private bool IgnoresProgramSlashLimit(CharacterState owner, CardConversionSource? source) =>
        source is not null && source.OwnerSeat == owner.Seat &&
        GetSkillBindingShard(owner).GetNumericModifiers(SkillRuleQuery.SlashLimit).Any(binding =>
            binding.Source.SkillId == source.SkillId && binding.Source.SkillInstanceId == source.SkillInstanceId &&
            binding.Modifier.Operation == SkillRuleOperation.Unlimited && binding.Modifier.SourceCardIdentityId == source.BindingId &&
            binding.Modifier.Condition.Evaluate(CreateSkillContext(owner)));

    private IReadOnlyList<CardConversionSource> GetProgramViewAsConversions(
        CharacterState owner,
        Card card,
        CardKind outputKind,
        bool forResponse, bool dyingUse = false)
    {
        if (UnclaimedDeclarationPayment(owner.Seat, card.Id) is { } paid &&
            (paid.DeclaredKind == outputKind || forResponse && outputKind == CardKind.Slash && IsSlashCard(paid.DeclaredKind))) return [paid.Source];
        if (IsResponseEntityRestricted(owner.Seat,card.Id)) return [];
        if (HasProgramCardIdentity(owner, card))
        {
            return [];
        }

        var location = _cardZones.GetLocation(card.Id);
        var zone = location == CardLocation.Hand(owner.Seat)
            ? CardZoneKind.Hand
            : location == CardLocation.Equipment(owner.Seat)
                ? CardZoneKind.Equipment
                : location == CardLocation.Authority(owner.Seat)
                    ? CardZoneKind.Authority
                    : location == CardLocation.WoodenOxGrain(owner.Seat) ? CardZoneKind.WoodenOxGrain : (CardZoneKind?)null;
        if (zone is null) return [];
        var context = CreateSkillContext(owner);
        var configured = GetSkillBindingShard(owner).ProgramInstances
            .SelectMany(instance => instance.Program.ViewAs
                .Where(rule => rule.InputCount == 1 &&
                               (rule.OutputKind == outputKind || forResponse && rule.ExtendedUse && outputKind == CardKind.Slash && rule.OutputKind == CardKind.FireSlash || forResponse && rule.DeclarationValidation is not null && outputKind == CardKind.Slash && IsSlashCard(rule.OutputKind)) &&
                               rule.SourceZones.Contains(zone.Value) &&
                               (dyingUse ? rule.ForResponse || rule.UseOnly && rule.ForPlay : forResponse ? rule.ForResponse && (!rule.UseOnly || IsProgramResponseCardUse(owner, outputKind) || (rule.ConversionStateId is not null || rule.UnusedOutputNameThisGame) && outputKind == CardKind.Nullification) : rule.ForPlay) &&
                               (rule.DeclarationValidation is null || CanDeclareCard(owner, instance.SkillId)) &&
                               IsNamedUseConversionAvailable(owner, instance, rule) &&
                               (rule.TieredRoundConversion is null || !card.IsGeneralWeapon) &&
                               CanUseTieredRoundConversion(owner, instance, rule, forResponse, dyingUse) &&
                               IsConfiguredConversionAvailable(owner, card, instance, rule, dyingUse) &&
                               (!rule.UnusedOutputThisTurn || !HasProgramUsedBasicCardThisTurn(owner.Seat, outputKind)) &&
                               (card.Kind != outputKind || rule.InheritPreviousPlaySuit || rule.ConversionStateId is not null || rule.AllowSameKind) &&
                               CanUsePhaseLimitedViewAs(instance, rule, owner) &&
                               rule.Condition.Evaluate(context) &&
                               (rule.InputKinds.Count == 0 || rule.InputKinds.Contains(card.Kind)) &&
                               (rule.InputCategories.Count == 0 ||
                                rule.InputCategories.Contains(GetProgramCardCategory(card.Kind))) &&
                               (rule.InputSuits.Count == 0 || rule.InputSuits.Contains(rule.UseEffectiveInputSuit == true ? EffectiveSuit(owner,card) : card.Suit)))
                .Select(rule => new CardConversionSource(
                    instance.SkillId,
                    rule.Id,
                    owner.Seat,
                    instance.SkillInstanceId)))
            .ToArray();
        var turnScoped = forResponse || zone != CardZoneKind.Hand
            ? Array.Empty<CardConversionSource>()
            : _turnCardUseEffects.GetConversions(
                    _turnNumber,
                    _currentSeat,
                    owner.Seat,
                    outputKind,
                    IsRedSuit(EffectiveSuit(owner, card)))
                .Select(item => new CardConversionSource(
                    item.Source.SkillId,
                    $"{item.Source.BindingId}.turn-{item.EffectIndex}",
                    item.Source.OwnerSeat,
                    item.Source.SkillInstanceId))
                .ToArray();
        return configured.Concat(turnScoped).Concat(GetPaidColorTurnDuelConversions(owner, card, outputKind, forResponse, zone.Value))
            .Distinct()
            .OrderBy(source => source.SkillId, StringComparer.Ordinal)
            .ThenBy(source => source.BindingId, StringComparer.Ordinal)
            .ThenBy(source => source.SkillInstanceId, StringComparer.Ordinal)
            .ToArray();
    }

    private bool IsDirectProgramFireSlashConversion(
        CharacterState owner, Card card, CardConversionSource? source) =>
        source is not null && (GetProgramViewAsConversions(
            owner, card, CardKind.FireSlash, forResponse: false).Contains(source) ||
            GetProgramCardIdentitySources(owner, card, CardKind.FireSlash, forResponse: false).Contains(source));

    private IReadOnlyList<ProgramMultiCardViewAsSelection> GetProgramMultiCardViewAsSelections(
        CharacterState owner,
        CardKind outputKind,
        bool forResponse, bool ignoreSuitUseProhibition = false)
    {
        if (!owner.IsAlive ||
            IsCardUseForbidden(owner.Seat, outputKind,
                forResponse ? CardActionType.Response : CardActionType.Use))
            return [];

        var context = CreateSkillContext(owner);
        var eligibleHand = GetHand(owner).Concat(GetEquipment(owner))
            .Where(card => !IsTurnHandCardRestricted(owner, card) && !HasProgramCardIdentity(owner, card))
            .OrderBy(card => card.Id)
            .ToArray();
        var selections = new List<ProgramMultiCardViewAsSelection>();
        foreach (var instance in GetSkillBindingShard(owner).ProgramInstances.Where(instance =>
                     instance.Program.ViewAs.Count != 0))
        foreach (var rule in instance.Program.ViewAs.Where(rule =>
                     rule.InputCount > 1 && !rule.VariableInputCount &&
                     (rule.OutputKind == outputKind || forResponse && rule.ExtendedUse && outputKind == CardKind.Slash && rule.OutputKind == CardKind.FireSlash) &&
                     (forResponse ? rule.ForResponse : rule.ForPlay) &&
                     rule.Condition.Evaluate(context)))
        {
            var candidates = eligibleHand.Where(card =>
                rule.SourceZones.Contains(_cardZones.GetLocation(card.Id).Zone) &&
                (rule.InputKinds.Count == 0 || rule.InputKinds.Contains(card.Kind)) &&
                (rule.InputSuits.Count == 0 || rule.InputSuits.Contains(rule.UseEffectiveInputSuit == true ? EffectiveSuit(owner,card) : card.Suit))).ToArray();
            if (candidates.Length < rule.InputCount) continue;
            var source = new CardConversionSource(
                instance.SkillId,
                rule.Id,
                owner.Seat,
                instance.SkillInstanceId);
            foreach (var cards in EnumerateCardCombinations(candidates, rule.InputCount))
                if (!rule.SameSuit || cards.Select(card => card.Suit).Distinct().Count() == 1)
                    selections.Add(new(cards, source, outputKind));
        }
        return selections
            .Where(item => forResponse || ignoreSuitUseProhibition || !IsTurnPhysicalUseForbidden(owner.Seat,item.Cards.Select(c=>c.Id).ToArray()))
            .OrderBy(item => item.Source.SkillId, StringComparer.Ordinal)
            .ThenBy(item => item.Source.BindingId, StringComparer.Ordinal)
            .ThenBy(item => string.Join(',', item.Cards.Select(card => card.Id)), StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<IReadOnlyList<Card>> EnumerateCardCombinations(
        IReadOnlyList<Card> cards,
        int count)
    {
        var result = new List<IReadOnlyList<Card>>();
        var selected = new Card[count];
        void Visit(int sourceIndex, int selectedIndex)
        {
            if (selectedIndex == count)
            {
                result.Add(Array.AsReadOnly(selected.ToArray()));
                return;
            }
            for (var index = sourceIndex; index <= cards.Count - (count - selectedIndex); index++)
            {
                selected[selectedIndex] = cards[index];
                Visit(index + 1, selectedIndex + 1);
            }
        }
        Visit(0, 0);
        return result;
    }

    private ProgramMultiCardViewAsSelection? FindProgramMultiCardViewAsSelection(
        CharacterState owner,
        IReadOnlyList<int> cardIds,
        CardKind outputKind,
        bool forResponse,
        CardConversionSource source)
    {
        if (cardIds.Count == 0 || cardIds.Distinct().Count() != cardIds.Count) return null;
        var ordered = cardIds.Order().ToArray();
        var variable = FindProgramVariableMultiCardViewAsSelection(owner, ordered, outputKind, forResponse, source);
        if (variable is not null) return variable;
        return GetProgramMultiCardViewAsSelections(owner, outputKind, forResponse)
            .FirstOrDefault(candidate =>
                candidate.Source == source &&
                candidate.Cards.Select(card => card.Id).Order().SequenceEqual(ordered));
    }

    /// <summary>
    /// Validates a known selection against a variable-input conversion directly.
    /// Variable rules accept any non-empty eligible hand-card count, so they are
    /// matched by validating the chosen ids instead of enumerating every subset.
    /// </summary>
    private ProgramMultiCardViewAsSelection? FindProgramVariableMultiCardViewAsSelection(
        CharacterState owner,
        int[] orderedCardIds,
        CardKind outputKind,
        bool forResponse,
        CardConversionSource source)
    {
        if (!owner.IsAlive ||
            IsCardUseForbidden(owner.Seat, outputKind,
                forResponse ? CardActionType.Response : CardActionType.Use))
            return null;
        var instance = GetSkillBindingShard(owner).ProgramInstances.FirstOrDefault(item =>
            item.SkillId == source.SkillId && item.SkillInstanceId == source.SkillInstanceId);
        if (instance is null) return null;
        var rule = instance.Program.ViewAs.FirstOrDefault(item =>
            item.Id == source.BindingId && item.VariableInputCount &&
            item.OutputKind == outputKind && (forResponse ? item.ForResponse : item.ForPlay));
        if (rule is null || !rule.Condition.Evaluate(CreateSkillContext(owner))) return null;
        var cards = new List<Card>();
        foreach (var cardId in orderedCardIds)
        {
            var location = _cardZones.GetLocation(cardId);
            if (location.OwnerSeat != owner.Seat) return null;
            var card = _cardZones.CardsAt(location).SingleOrDefault(item => item.Id == cardId);
            if (card is null ||
                !rule.SourceZones.Contains(location.Zone) ||
                IsTurnHandCardRestricted(owner, card) || HasProgramCardIdentity(owner, card) ||
                rule.InputKinds.Count != 0 && !rule.InputKinds.Contains(card.Kind) ||
                rule.InputSuits.Count != 0 && !rule.InputSuits.Contains(
                    rule.UseEffectiveInputSuit == true ? EffectiveSuit(owner, card) : card.Suit))
                return null;
            cards.Add(card);
        }
        if (IsTurnPhysicalUseForbidden(owner.Seat, orderedCardIds)) return null;
        return new(cards, source, outputKind);
    }

    private string ProgramConversionName(CardConversionSource source) =>
        _contentRegistry.Skills.GetValueOrDefault(source.SkillId)?.Name ?? source.SkillId;

    private void ResolveProgramMultiCardSlash(
        CharacterState source,
        CharacterState target,
        ProgramMultiCardViewAsSelection selection,
        BorrowedSwordHandle? borrowedSword = null,
        bool enforceOwnTurnSlashLimit = true)
    {
        var current = FindProgramMultiCardViewAsSelection(
            source,
            selection.Cards.Select(card => card.Id).ToArray(),
            CardKind.Slash,
            forResponse: false,
            selection.Source);
        if (current is null || !(borrowedSword is null
                ? CanUseVirtualSlashTarget(source, target, physicalSuit: PhysicalGroupSuit(source,current.Cards), effectiveColor:PhysicalGroupColor(source,current.Cards), physicalCardIds:current.Cards.Select(c=>c.Id).ToArray())
                : IsLegalBorrowedSwordSlashTarget(source, target, physicalSuit:PhysicalGroupSuit(source,current.Cards), allowAnyPhysicalSuit:false, effectiveColor:PhysicalGroupColor(source,current.Cards), physicalCardIds:current.Cards.Select(c=>c.Id).ToArray())))
            throw new InvalidOperationException("The configured multi-card Slash is no longer legal.");

        ResolveSlashCore(
            source,
            target,
            current.Cards[0],
            CardKind.Slash,
            source.Seat,
            borrowedSword: borrowedSword,
            physicalCards: current.Cards,
            countsTowardSlashLimit: enforceOwnTurnSlashLimit,
            conversionSource: current.Source);
    }

    private CardActionContext MoveProgramMultiCardResponse(
        CharacterState responder,
        ProgramMultiCardViewAsSelection selection,
        long resolutionId,
        int responseTargetSeat,
        int? actorSeat = null)
    {
        var current = FindProgramMultiCardViewAsSelection(
            responder,
            selection.Cards.Select(card => card.Id).ToArray(),
            selection.OutputKind,
            forResponse: true,
            selection.Source) ?? throw new InvalidOperationException(
                "The configured multi-card response is no longer legal.");
        var costs = current.Cards.Select(card => new CardActionCost(
            card.Id,
            card.Kind,
            FindOwnedCardLocation(responder, card),CapturePhysicalCardColor(responder.Seat,card))).ToArray();
        var completedResponseUseSuit = actorSeat is null || actorSeat == responder.Seat
            ? FreezeCompletedResponseUseSuit(responder, current.Cards, selection.OutputKind) : null;
        foreach (var card in current.Cards)
        {
            MoveCard(card, FindOwnedCardLocation(responder, card), CardLocation.Processing, CardMoveReasons.Respond);
            AdvanceEventRulesAndQueueFact(new CardRespondedEvent(
                card.Id,
                responder.Seat,
                responseTargetSeat,
                selection.OutputKind));
        }
        var actionActorSeat = actorSeat ?? responder.Seat;
        var parent = _resolutionStack.OfType<CardUseFrame>()
            .LastOrDefault(frame => frame.Id == resolutionId);
        var action = CaptureFactionAction(new CardActionContext(
            ++_cardActionSequence,
            parent?.Action?.ActionId,
            CardActionType.Response,
            actionActorSeat,
            responder.Seat,
            actionActorSeat == responder.Seat ? null : actionActorSeat,
            responder.Seat,
            responseTargetSeat,
            selection.OutputKind,
            [],
            costs,
            [selection.Source], effectiveSuit: completedResponseUseSuit,effectiveIsRed:CaptureActionColor(costs,completedResponseUseSuit)));
        if (action.ActorSeat == action.ProviderSeat && action.RequesterSeat is null &&
            (selection.OutputKind == CardKind.Nullification || IsProgramResponseCardUse(responder, selection.OutputKind)))
            RecordActualPlayPhaseUse(action);
        AdvanceEventRulesAndQueueFact(new CardActionAcceptedEvent(action));
        AdvanceEventRulesAndQueueFact(new ProgramViewAsConvertedEvent(
            resolutionId,
            selection.Source.SkillId,
            selection.Source.BindingId,
            responder.Seat,
            Array.AsReadOnly(current.Cards.Select(card => card.Id).ToArray()),
            selection.OutputKind,
            IsUse: false,
            [responseTargetSeat]));
        return action;
    }

    private void FinishProgramMultiCardResponse(ProgramMultiCardViewAsSelection selection)
    {
        foreach (var card in selection.Cards)
            MoveCard(card, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.ResponseFinished);
    }

    private void ResolveDuelProgramMultiCardResponse(
        DuelHandle duel,
        CharacterState responder,
        ProgramMultiCardViewAsSelection selection)
    {
        if (!SameContinuationOwner(ActiveDuel, duel) || responder.Seat != duel.ResponderSeat)
            throw new InvalidOperationException("The configured Duel response is not current.");
        var responseAction = MoveProgramMultiCardResponse(responder, selection, duel.ResolutionId, duel.OpponentSeat);
        AddLog("CardResponded",
            $"{responder.Name} 发动【{ProgramConversionName(selection.Source)}】，将 {selection.Cards.Count} 张手牌当【杀】应战【决斗】。",
            responder.Seat, duel.OpponentSeat);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(responder.Seat, CardKind.Slash);
        AdvanceEventRulesAndQueueFact(new DuelResponseEvent(
            duel.ResolutionId, responder.Seat, UsedSlash: true,
            SlashCardId: selection.Cards[0].Id, ResponseCardKind: CardKind.Slash));
        if (HasResponseEntityExchangeObservers() && TryBeginProgramCardWindow(duel.Attack,responseAction,SkillProgramTriggerWindow.CardResponseAccepted,[duel.OpponentSeat],ProgramCardContinuation.DuelSlash)) return;
        FinishProgramMultiCardResponse(selection);
        ContinueDuelAfterSuccessfulSlash(duel, responder.Seat);
    }

    private void ResolveGroupProgramMultiCardResponse(
        GroupCardHandle group,
        CharacterState responder,
        ProgramMultiCardViewAsSelection selection)
    {
        if (!SameContinuationOwner(ActiveGroupCard, group) ||
            group.Effect != GroupCardEffect.ResponseAttack ||
            group.RequiredCardKind != selection.OutputKind ||
            group.CurrentAttack is not { } attack ||
            responder.Seat != attack.TargetSeat)
            throw new InvalidOperationException("The configured group response is not current.");
        var responseAction = MoveProgramMultiCardResponse(responder, selection, group.ResolutionId, group.SourceSeat);
        AddLog("CardResponded",
            $"{responder.Name} 发动【{ProgramConversionName(selection.Source)}】，将 {selection.Cards.Count} 张手牌当【杀】响应【{group.Card.DisplayName}】。",
            responder.Seat, group.SourceSeat);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(responder.Seat, CardKind.Slash);
        AdvanceEventRulesAndQueueFact(new GroupResponseEvent(
            group.ResolutionId, group.Card.Kind, CardKind.Slash, responder.Seat,
            UsedResponse: true, ResponseCardId: selection.Cards[0].Id,
            ResponseCardKind: CardKind.Slash));
        if (HasResponseEntityExchangeObservers() && TryBeginProgramCardWindow(attack,responseAction,SkillProgramTriggerWindow.CardResponseAccepted,[group.SourceSeat],ProgramCardContinuation.GroupResponse)) return;
        FinishProgramMultiCardResponse(selection);
        CompleteAttack(attack);
    }

    private IReadOnlyList<Card> GetSlashUseCards(CharacterState owner, bool ignoreSuitUseProhibition = false)
    {
        if (SlashKinds.All(kind => IsCardUseForbidden(owner.Seat, kind, CardActionType.Use))) return [];
        var cards = GetPlayableCards(owner).Where(card =>
        {
            if (IsTurnHandCardRestricted(owner, card)) return false;
            var identities = GetProgramCardIdentityMatches(owner, card);
            return identities.Count != 0
                ? identities.Any(match => IsSlashCard(match.Identity.OutputKind))
                : IsSlashCard(card.Kind) ||
                  GetProgramViewAsConversions(owner, card, CardKind.Slash, forResponse: false).Count != 0;
        });
        cards = cards.Concat(GetEquipment(owner).Where(card =>
            GetProgramViewAsConversions(owner, card, CardKind.Slash, forResponse: false).Count != 0));
        return cards.Where(card => ignoreSuitUseProhibition || !IsTurnSuitUseForbidden(owner.Seat,EffectiveSuit(owner,card))).DistinctBy(card => card.Id).ToArray();
    }

    private static bool IsFactionSlashUse(FactionCardRequestHandle pending) =>
        pending.IsProgramSkillUse || pending.IsBorrowedSwordUse || pending.IsQinglongCrescentBladeUse;

    private IReadOnlyList<Card> GetFactionSlashSlashCards(FactionCardRequestHandle pending, CharacterState provider) =>
        (pending.IsAssistedProgramUse
            ? GetAssistedFactionSlashCards(pending, provider).Select(variant => variant.Card).DistinctBy(card => card.Id).ToArray()
            : IsFactionSlashUse(pending) ? GetSlashUseCards(provider,ignoreSuitUseProhibition:true) : GetResponseCards(provider, CardKind.Slash))
        .Where(card => (!IsFactionSlashUse(pending) || !IsTurnPhysicalUseForbidden(pending.OwnerSeat,[card.Id])) && IsRedSlashProviderPaymentLegal(pending,GetFactionSlashEffectiveSlashKind(pending,provider,card),[card])).ToArray();

    private CardKind GetFactionSlashEffectiveSlashKind(
        FactionCardRequestHandle pending,
        CharacterState provider,
        Card card)
    {
        var identity = GetProgramCardIdentityMatches(provider, card).FirstOrDefault();
        if (identity is not null) return identity.Identity.OutputKind;
        return IsFactionSlashUse(pending)
            ? IsSlashCard(card.Kind) ? card.Kind : CardKind.Slash
            : GetEffectiveResponseKind(provider, card, CardKind.Slash);
    }

    private static void AddConversionParameters(
        IDictionary<string, string> parameters,
        CardConversionSource source)
    {
        parameters["conversion-skill-id"] = source.SkillId;
        parameters["conversion-binding-id"] = source.BindingId;
        parameters["conversion-owner-seat"] = source.OwnerSeat.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        parameters["conversion-instance-id"] = source.SkillInstanceId;
    }

    private IEnumerable<PromptChoice> CreateConversionChoiceVariants(
        CharacterState owner,
        Card card,
        CardKind effectiveKind,
        bool forResponse,
        string baseChoiceId,
        string description,
        IReadOnlyList<int> cards,
        IReadOnlyList<int> targets,
        IReadOnlyDictionary<string, string> baseParameters,
        bool dyingUse = false)
    {
        var hasIdentity = HasProgramCardIdentity(owner, card);
        var identitySources = GetProgramCardIdentitySources(owner, card, effectiveKind, forResponse);
        var programSources = GetProgramViewAsConversions(owner, card, effectiveKind, forResponse, dyingUse);
        var includeUnspecified = !hasIdentity &&
            (card.Kind == effectiveKind || programSources.Count == 0);
        var sources = new List<CardConversionSource?>();
        if (includeUnspecified) sources.Add(null);
        if (hasIdentity) sources.AddRange(identitySources);
        else
        {
            sources.AddRange(programSources);
        }
        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            var parameters = new Dictionary<string, string>(baseParameters);
            if (source is not null) AddConversionParameters(parameters, source);
            var suffix = source is null ? string.Empty : $".conversion-{index}";
            yield return new PromptChoice(
                new ChoiceId(baseChoiceId + suffix), DescribeConversion(source, description), cards, targets, parameters);
        }
    }

    private static bool TryReadConversionSource(
        IReadOnlyDictionary<string, string> parameters,
        out CardConversionSource? source)
    {
        source = null;
        if (!parameters.TryGetValue("conversion-skill-id", out var skillId) ||
            !parameters.TryGetValue("conversion-binding-id", out var bindingId) ||
            !parameters.TryGetValue("conversion-owner-seat", out var ownerSeatText) ||
            !int.TryParse(ownerSeatText, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var ownerSeat) ||
            !parameters.TryGetValue("conversion-instance-id", out var instanceId))
        {
            return false;
        }

        source = new CardConversionSource(skillId, bindingId, ownerSeat, instanceId);
        return true;
    }

    private static CardConversionSource RequireConversionSource(PromptChoice choice) =>
        TryReadConversionSource(choice.Parameters, out var source) && source is not null
            ? source
            : throw new InvalidOperationException("The configured conversion choice lost its source identity.");

    private void CaptureSelectedResponseConversion(PromptChoice choice)
    {
        if (choice.Cards.Count != 1 ||
            !choice.Parameters.ContainsKey("response-card-kind")) return;
        if (TryReadConversionSource(choice.Parameters, out var source))
        {
            _selectedResponseConversion = source;
            _hasSelectedResponseConversionChoice = true;
            return;
        }

        var startsCardAction = choice.Parameters.TryGetValue("response", out var response) &&
                               response is "dodge" or "slash" or "faction-defense-dodge" or
                                   "faction-slash-slash" or "borrowed-sword-slash" ||
                               choice.Parameters.GetValueOrDefault("action") == "qinglong-slash";
        if (startsCardAction && choice.Cards.Count == 1)
        {
            _selectedResponseConversion = null;
            _hasSelectedResponseConversionChoice = true;
        }
    }

    private void CaptureAiCardResponseChoice(PendingDecision decision, Card card, CardKind effectiveKind)
    {
        var choice = decision.Choices.FirstOrDefault(candidate =>
            candidate.Cards is [var cardId] && cardId == card.Id &&
            candidate.Parameters.GetValueOrDefault("response-card-kind") == effectiveKind.ToString()) ??
            throw new InvalidOperationException("The AI response has no published card choice.");
        CaptureSelectedResponseConversion(choice);
    }

    private CardConversionSource? GetSelectedResponseConversion(
        CharacterState provider,
        Card responseCard,
        CardKind effectiveKind)
    {
        var hasIdentity = HasProgramCardIdentity(provider, responseCard);
        var identitySources = GetProgramCardIdentitySources(
            provider, responseCard, effectiveKind, forResponse: true);
        var candidates = hasIdentity
            ? identitySources.ToArray()
            : GetProgramViewAsConversions(provider, responseCard, effectiveKind, forResponse: true,
                dyingUse: ActiveDying is not null && effectiveKind is CardKind.Peach or CardKind.Alcohol).ToArray();
        var selected = _selectedResponseConversion;
        var selectedChoice = _hasSelectedResponseConversionChoice;
        _selectedResponseConversion = null;
        _hasSelectedResponseConversionChoice = false;
        if (selectedChoice)
        {
            if (selected is null && !hasIdentity && IsNativeResponseCard(responseCard, effectiveKind))
                return null;
            if (selected is not null && candidates.Contains(selected)) return selected;
            throw new InvalidOperationException("The selected response conversion is no longer legal.");
        }
        throw new InvalidOperationException("A card response requires its published source choice.");
    }

    private void SelectUseConversion(LegalAction action)
    {
        _selectedUseConversion = action.ConversionSource;
        _hasSelectedUseConversionChoice = true;
    }

    private CardConversionSource? GetSelectedUseConversion(
        CharacterState actor,
        CharacterState provider,
        Card card,
        CardKind effectiveKind)
    {
        var hasIdentity = HasProgramCardIdentity(provider, card);
        var fanConvertsSlash = effectiveKind == CardKind.FireSlash && HasZhuqueFan(actor);
        var identitySources = GetProgramCardIdentitySources(
            provider, card, effectiveKind, forResponse: false);
        var candidates = hasIdentity
            ? identitySources.ToArray()
            : GetProgramViewAsConversions(provider, card, effectiveKind, forResponse: false)
                .Concat(fanConvertsSlash
                    ? GetProgramViewAsConversions(provider, card, CardKind.Slash, forResponse: false)
                    : [])
                .Distinct()
                .ToArray();
        var selected = _selectedUseConversion ?? _selectedResponseConversion;
        var selectedChoice = _hasSelectedUseConversionChoice || _hasSelectedResponseConversionChoice;
        _selectedUseConversion = null;
        _selectedResponseConversion = null;
        _hasSelectedResponseConversionChoice = false;
        _hasSelectedUseConversionChoice = false;
        if (selected is not null)
        {
            return candidates.Contains(selected)
                ? selected
                : throw new InvalidOperationException("The selected use conversion is no longer legal.");
        }

        if (!hasIdentity &&
            (card.Kind == effectiveKind || fanConvertsSlash && card.Kind == CardKind.Slash) &&
            (selectedChoice || candidates.Length == 0)) return null;
        throw new InvalidOperationException("A converted card use requires its published source choice.");
    }
}
