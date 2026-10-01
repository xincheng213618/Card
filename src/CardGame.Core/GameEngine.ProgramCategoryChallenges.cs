namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool AreProgramBoundCardsSameColor(ProgramSkillFrame frame, string sourceBind)
    {
        var binding = GetProgramCardSet(frame, sourceBind);
        var colors = binding.CardIds.Select(cardId =>
        {
            var location = _cardZones.GetLocation(cardId);
            var card = _cardZones.CardsAt(location).Single(candidate => candidate.Id == cardId);
            return IsRedSuit(card.Suit);
        }).Distinct().ToArray();
        return binding.CardIds.Count > 0 && colors.Length == 1;
    }

    private bool DoProgramBoundCardsMatchCategories(
        ProgramSkillFrame frame,
        string sourceBind,
        IReadOnlyList<SkillProgramCardCategory> categories)
    {
        var binding = GetProgramCardSet(frame, sourceBind);
        return binding.CardIds.Count > 0 && binding.CardIds.All(cardId =>
        {
            var location = _cardZones.GetLocation(cardId);
            var card = _cardZones.CardsAt(location).Single(candidate => candidate.Id == cardId);
            return categories.Contains(GetProgramCardCategory(card.Kind));
        });
    }

    private bool DoProgramBoundCardCategoryMatchAction(ProgramSkillFrame frame, string sourceBind)
    {
        var actionCategory = frame.WindowContext?.Facts?.CardActionCategory;
        if (actionCategory is null) return false;
        var binding = GetProgramCardSet(frame, sourceBind);
        return binding.CardIds.Count > 0 && binding.CardIds.All(cardId =>
        {
            var location = _cardZones.GetLocation(cardId);
            var card = _cardZones.CardsAt(location).Single(candidate => candidate.Id == cardId);
            return GetProgramCardCategory(card.Kind) == actionCategory;
        });
    }

    private bool DoProgramBoundCardsMatchKinds(
        ProgramSkillFrame frame,
        string sourceBind,
        IReadOnlyList<CardKind> kinds)
    {
        var binding = GetProgramCardSet(frame, sourceBind);
        return binding.CardIds.Count > 0 && binding.CardIds.All(cardId =>
        {
            var location = _cardZones.GetLocation(cardId);
            var card = _cardZones.CardsAt(location).Single(candidate => candidate.Id == cardId);
            return kinds.Contains(card.Kind);
        });
    }

    private bool DoProgramBoundCardsMatchSuits(
        ProgramSkillFrame frame,
        string sourceBind,
        IReadOnlyList<Suit> suits)
    {
        var binding = GetProgramCardSet(frame, sourceBind);
        if (binding.Visibility == SkillProgramCardSetVisibility.Public &&
            binding.CardIds.Count == 1 && binding.FrozenRevealedSuit is { } effectiveSuit)
            return suits.Contains(effectiveSuit);
        return binding.Visibility == SkillProgramCardSetVisibility.Public &&
            binding.CardIds.Count > 0 && binding.CardIds.All(cardId =>
            {
                var location = _cardZones.GetLocation(cardId);
                var card = _cardZones.CardsAt(location).Single(candidate => candidate.Id == cardId);
                return suits.Contains(card.Suit);
            });
    }

    private void CaptureProgramSelectedCards(ProgramSkillFrame frame, string resultBind)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.TriggerId is not null)
            throw new InvalidOperationException("Only an active play binding can capture its initial cards.");
        var program = _contentRegistry!.GetSkill(active.SkillId).Program!;
        var activation = ProgramInstructionResolver.Default.Resolve(program,
            ProgramInstructionSourceKind.Activation, active.ActivationId).Activation!;
        if (active.SelectedCardIds.Count < activation.MinCards ||
            active.SelectedCardIds.Count > activation.MaxCards || active.SelectedCardIds.Count == 0)
            throw new InvalidOperationException("The activation-card selection is outside its declared bounds.");

        var selected = active.SelectedCardIds.Select(cardId =>
        {
            var locations = activation.SourceZones
                .Select(zone => new CardLocation(zone, active.OwnerSeat))
                .Where(location => _cardZones.CardsAt(location).Any(card => card.Id == cardId))
                .ToArray();
            if (locations.Length != 1)
                throw new InvalidOperationException("A captured activation card no longer has one valid owner location.");
            return (CardId: cardId, Location: locations[0]);
        }).ToArray();
        SetProgramCardSet(active.Id, resultBind,
            selected.Select(item => item.CardId).ToArray(),
            SkillProgramCardSetVisibility.Private,
            selected.Select(item => item.Location).ToArray());
    }

    private void RevealProgramBoundCards(ProgramSkillFrame frame, string sourceBind)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var binding = GetProgramCardSet(active, sourceBind);
        var cards = binding.CardIds.Select(cardId =>
        {
            var location = _cardZones.GetLocation(cardId);
            return _cardZones.CardsAt(location).Single(card => card.Id == cardId);
        }).ToArray();
        ReplaceRuntimeTop(active with
        {
            CardSetBindings = Array.AsReadOnly(active.CardSetBindings.Select(item =>
                item.Name == sourceBind
                    ? item with { Visibility = SkillProgramCardSetVisibility.Public }
                    : item).ToArray())
        });
        AdvanceEventRulesAndQueueFact(new ProgramCardsRevealedEvent(
            active.Id, active.SkillId, GetProgramBindingId(active), active.OwnerSeat, sourceBind,
            Array.AsReadOnly(cards.Select(ToSnapshot).ToArray())));
    }

    private SkillProgramStepOutcome ChooseProgramDifferentCategoryDiscard(
        ProgramSkillFrame frame,
        ProgramParticipantReference chooser,
        ProgramParticipantReference cardOwner,
        IReadOnlyList<CardZoneKind> zones,
        string sourceBind,
        string resultBind,
        CardMoveReason reason)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.ChoiceBindings.Any(binding => binding.Name == resultBind))
            throw new InvalidOperationException("A category challenge cannot be answered twice.");
        var chooserSeat = ResolveProgramParticipant(active, chooser);
        var cardOwnerSeat = ResolveProgramParticipant(active, cardOwner);
        if (chooserSeat != cardOwnerSeat)
            throw new InvalidOperationException("A category challenge may expose only the chooser's own private cards.");
        var categories = GetProgramCardSet(active, sourceBind).CardIds
            .Select(cardId => GetProgramCardCategory(
                _cardZones.CardsAt(_cardZones.GetLocation(cardId)).Single(card => card.Id == cardId).Kind))
            .Distinct()
            .Order()
            .ToArray();
        var choices = BuildDifferentCategoryDiscardChoices(
            active.Id, chooserSeat, cardOwnerSeat, zones, sourceBind, resultBind, reason, categories).ToList();
        if (!_players[chooserSeat].IsAlive || choices.Count == 0)
        {
            CommitProgramCategoryDiscard(active, chooserSeat, sourceBind, resultBind, null);
            return SkillProgramStepOutcome.Continue;
        }

        choices.Add(new PromptChoice(
            new ChoiceId($"program-category-discard.frame-{active.Id}.{resultBind}.decline"),
            "不弃置。",
            [], [],
            new Dictionary<string, string>
            {
                ["program-action"] = "different-category-decline",
                ["frame-id"] = active.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["source-bind"] = sourceBind,
                ["result-bind"] = resultBind,
                ["move-reason"] = reason.Value
            }));
        var skill = _contentRegistry!.GetSkill(active.SkillId);
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger,
            chooserSeat,
            $"【{skill.Name}】请选择弃置一张与展示牌类别不同的牌，或不弃置。",
            choices.SelectMany(choice => choice.Cards).Distinct().Order().ToArray(),
            [],
            active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = cardOwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, skill.Name,
                $"{skill.Name} · 类别响应", skill.Description),
            Choices = Array.AsReadOnly(choices.ToArray())
        };
        _status = _players[chooserSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> BuildDifferentCategoryDiscardChoices(
        long frameId,
        int chooserSeat,
        int cardOwnerSeat,
        IReadOnlyList<CardZoneKind> zones,
        string sourceBind,
        string resultBind,
        CardMoveReason reason,
        IReadOnlyCollection<SkillProgramCardCategory> excludedCategories)
    {
        var result = new List<PromptChoice>();
        foreach (var zone in zones)
        {
            var cards = zone switch
            {
                CardZoneKind.Hand => GetHand(_players[cardOwnerSeat]),
                CardZoneKind.Equipment => GetEquipment(_players[cardOwnerSeat]),
                CardZoneKind.Judgment => GetJudgment(_players[cardOwnerSeat]),
                _ => throw new InvalidOperationException("Unsupported category-challenge zone.")
            };
            foreach (var card in cards.Where(card => !excludedCategories.Contains(GetProgramCardCategory(card.Kind))))
            {
                result.Add(new PromptChoice(
                    new ChoiceId($"program-category-discard.frame-{frameId}.{resultBind}.card-{card.Id}"),
                    $"弃置【{card.DisplayName}】。",
                    [card.Id], [],
                    new Dictionary<string, string>
                    {
                        ["program-action"] = "different-category-discard",
                        ["frame-id"] = frameId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["card-owner-seat"] = cardOwnerSeat.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["source-zone"] = zone.ToString(),
                        ["source-bind"] = sourceBind,
                        ["result-bind"] = resultBind,
                        ["move-reason"] = reason.Value
                    }));
            }
        }
        return Array.AsReadOnly(result.OrderBy(choice => choice.Cards.Single()).ToArray());
    }

    private void ResolveProgramDifferentCategoryDiscardChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The category challenge lost its program frame.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame,
            _contentRegistry!.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.ChooseDifferentCategoryDiscard ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("source-bind") != effect.SourceBind ||
            selected.Parameters.GetValueOrDefault("result-bind") != effect.ResultBind ||
            frame.ChoiceBindings.Any(binding => binding.Name == effect.ResultBind))
            throw new InvalidOperationException("The category-discard choice does not match its suspended instruction.");

        var chooserSeat = ResolveProgramParticipant(frame, effect.ChooserRef!);
        var ownerSeat = ResolveProgramParticipant(frame, effect.CardOwnerRef!);
        if (chooserSeat != ownerSeat || _pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != chooserSeat)
            throw new InvalidOperationException("The category-discard participants changed while suspended.");
        if (!_players[frame.OwnerSeat].IsAlive || !_players[chooserSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
        {
            ClearPendingDecision();
            CancelProgramBindingAndCleanup(frame, "类别响应参与者或技能实例已失效，技能剩余步骤取消。");
            return;
        }

        var action = selected.Parameters.GetValueOrDefault("program-action");
        int? discardedCardId = null;
        if (action == "different-category-discard")
        {
            if (selected.Cards.Count != 1 ||
                !Enum.TryParse<CardZoneKind>(selected.Parameters.GetValueOrDefault("source-zone"), out var zone) ||
                !effect.Zones.Contains(zone))
                throw new InvalidOperationException("The category-discard card selection is malformed.");
            var source = new CardLocation(zone, ownerSeat);
            var card = _cardZones.CardsAt(source).SingleOrDefault(item => item.Id == selected.Cards.Single()) ??
                throw new InvalidOperationException("The category-discard card is no longer available.");
            var excluded = GetProgramCardSet(frame, effect.SourceBind!).CardIds.Select(cardId =>
                GetProgramCardCategory(_cardZones.CardsAt(_cardZones.GetLocation(cardId))
                    .Single(item => item.Id == cardId).Kind)).ToHashSet();
            if (excluded.Contains(GetProgramCardCategory(card.Kind)))
                throw new InvalidOperationException("The selected response card now matches a forbidden category.");
            MoveCard(card, source, CardLocation.DiscardPile,
                new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"));
            discardedCardId = card.Id;
        }
        else if (action != "different-category-decline" || selected.Cards.Count != 0)
        {
            throw new InvalidOperationException("The category-discard branch is unsupported.");
        }

        ClearPendingDecision();
        CommitProgramCategoryDiscard(frame, chooserSeat, effect.SourceBind!, effect.ResultBind!, discardedCardId);
        AdvanceRuntimeProgram(frame.Id);
    }

    private void CommitProgramCategoryDiscard(
        ProgramSkillFrame frame,
        int chooserSeat,
        string sourceBind,
        string resultBind,
        int? discardedCardId)
    {
        var option = discardedCardId is null
            ? ChooseDifferentCategoryDiscardProgramOperationDescriptor.DeclinedOption
            : ChooseDifferentCategoryDiscardProgramOperationDescriptor.DiscardedOption;
        var active = GetActiveProgramFrame(frame.Id);
        ReplaceRuntimeTop(active with
        {
            ChoiceBindings = Array.AsReadOnly(active.ChoiceBindings.Append(
                new ProgramChoiceResultBinding(resultBind, option, chooserSeat)).ToArray())
        });
        AdvanceEventRulesAndQueueFact(new ProgramCategoryDiscardResolvedEvent(
            active.Id, active.SkillId, GetProgramBindingId(active), active.OwnerSeat,
            chooserSeat, sourceBind, resultBind, discardedCardId));
    }

    private PromptChoice SelectAiProgramCategoryDiscard(PendingDecision decision, ProgramSkillFrame frame)
    {
        var decline = decision.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "different-category-decline");
        var plan = ProgramInstructionResolver.Default.Resolve(frame,
            _contentRegistry!.GetSkill(frame.SkillId).Program!);
        var paused = plan.GetPausedInstruction(frame.InstructionIndex).Effect;
        var shouldRestoreFace = _players[decision.PlayerSeat].IsFaceDown &&
            plan.Instructions.Skip(frame.InstructionIndex).Any(effect =>
                effect.Op == SkillProgramEffectOp.TurnOver &&
                effect.Target == SkillProgramEffectTarget.SelectedTarget &&
                effect.Condition.Kind == SkillProgramConditionKind.ChoiceIs &&
                effect.Condition.SourceBind == paused.ResultBind &&
                effect.Condition.OptionId == ChooseDifferentCategoryDiscardProgramOperationDescriptor.DeclinedOption);
        if (shouldRestoreFace) return decline;
        return decision.Choices
            .Where(choice => choice.Parameters.GetValueOrDefault("program-action") == "different-category-discard")
            .OrderBy(choice => CardCatalog.Get(
                _cardZones.CardsAt(_cardZones.GetLocation(choice.Cards.Single()))
                    .Single(card => card.Id == choice.Cards[0]).Kind).HandKeepValue)
            .ThenBy(choice => choice.Cards[0])
            .FirstOrDefault() ?? decline;
    }
}
