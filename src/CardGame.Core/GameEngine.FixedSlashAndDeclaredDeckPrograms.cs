namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string FixedTargetFactionResultBind = "fixed-target-slash";
    private const string DeclaredDeckCardBind = "declared-deck-card";
    private static readonly string[] DeckCriteria = ["basic", "trick", "equipment", "red", "black"];

    private sealed partial class ProgramSkillHost : IFixedSlashAndDeclaredDeckProgramHost
    {
        public SkillProgramStepOutcome UseOwnerSlashAgainstTurnOwner(ProgramSkillFrame frame, bool ignoreDistance) =>
            engine.BeginOwnerSlashAgainstTurnOwner(frame, ignoreDistance);
        public SkillProgramStepOutcome DeclareDeckCriterionAndGiveMatchingCard(ProgramSkillFrame frame) =>
            engine.BeginDeclaredDeckCriterion(frame);
    }

    private SkillProgramStepOutcome BeginOwnerSlashAgainstTurnOwner(ProgramSkillFrame frame, bool ignoreDistance)
    {
        var target = frame.WindowContext?.TargetSeat;
        if (frame.WindowContext?.Window != SkillProgramTriggerWindow.TurnEnding || target is not { } seat ||
            seat != _currentSeat || seat == frame.OwnerSeat || frame.FixedTargetSlash is not null)
            throw new InvalidOperationException("A fixed-target Slash requires the other ending-turn owner.");
        if (!DeckProgramSourceValid(frame) || !_players[seat].IsAlive || _winner != Winner.None)
            return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(frame = frame with { FixedTargetSlash = new(seat, ignoreDistance) });
        PublishFixedSlashAndDeclaredDeckPrompt(frame, FixedTargetSlashChoices(frame), "选择一张【杀】对本回合角色使用。", [seat]);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private Dictionary<string, string> FixedSlashAndDeclaredDeckParameters(ProgramSkillFrame frame, string action) => new()
    { ["program-action"] = action, ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) };

    private IReadOnlyList<PromptChoice> FixedTargetSlashChoices(ProgramSkillFrame frame)
    {
        var d = frame.FixedTargetSlash ?? throw new InvalidOperationException("Missing fixed Slash draft.");
        var actor = _players[frame.OwnerSeat];
        var target = _players[d.TargetSeat];
        Dictionary<string, string> Parameters(string option)
        {
            var p = FixedSlashAndDeclaredDeckParameters(frame, "fixed-target-slash");
            p["request-option"] = option;
            return p;
        }
        bool Legal(Card card, CardKind kind, CardConversionSource? source = null, bool noRank = false, int? rank = null, IReadOnlyList<int>? materials = null) =>
            actor.IsAlive && target.IsAlive && !d.Issued &&
            CanUseSlashTarget(actor, target, card, source, kind, ignoreDistance: d.IgnoreDistance,
                noEffectiveRank: noRank, specificEffectiveRank: rank, physicalCardIds:materials);
        bool PaymentRange(IReadOnlyList<Card> cards) => d.IgnoreDistance || AssistedSlashPaymentHasRange(actor, target.Seat, cards);
        var choices = new List<PromptChoice>();
        foreach (var card in GetSlashUseCards(actor))
        {
            var identity = GetProgramCardIdentityMatches(actor, card).FirstOrDefault(item => IsSlashCard(item.Identity.OutputKind));
            var baseKind = identity?.Identity.OutputKind ?? (IsSlashCard(card.Kind) ? card.Kind : CardKind.Slash);
            foreach (var kind in GetSlashUseKinds(actor, baseKind))
            {
                if (!PaymentRange([card]) || !Legal(card, kind)) continue;
                var p = Parameters("use"); p["effective-kind"] = kind.ToString();
                choices.AddRange(CreateConversionChoiceVariants(actor, card, kind, false,
                    $"fixed-slash.{frame.Id}.card-{card.Id}.{kind}", $"使用【{CardCatalog.Get(kind).DisplayName}】",
                    [card.Id], [target.Seat], p));
            }
        }
        foreach (var card in GetPlayableCards(actor).Concat(GetEquipment(actor)).DistinctBy(card => card.Id))
        foreach (var kind in SlashKinds.Where(kind => kind != CardKind.Slash))
        foreach (var conversion in GetProgramViewAsConversions(actor, card, kind, forResponse: false))
        {
            if (IsTurnHandCardRestricted(actor, card) || !PaymentRange([card]) || !Legal(card, kind, conversion)) continue;
            var p = Parameters("use"); p["effective-kind"] = kind.ToString(); AddConversionParameters(p, conversion);
            choices.Add(new(new($"fixed-slash.{frame.Id}.typed.{kind}.{conversion.SkillId}.{conversion.BindingId}.{card.Id}"),
                $"将牌当【{CardCatalog.Get(kind).DisplayName}】使用", [card.Id], [target.Seat], p));
        }
        foreach (var pair in GetZhangbaSlashPairs(actor))
        {
            if (!Legal(pair[0], CardKind.Slash, noRank: true, rank: ZhangbaSpecificSlashRank(actor, pair), materials:pair.Select(c=>c.Id).ToArray())) continue;
            var p = Parameters("use"); p["effective-kind"] = CardKind.Slash.ToString(); p["equipment"] = CardKind.ZhangbaSerpentSpear.ToString();
            var ids = pair.Select(card => card.Id).ToArray();
            choices.Add(new(new($"fixed-slash.{frame.Id}.zhangba.{string.Join('-', ids)}"), "使用丈八蛇矛：将两张手牌当【杀】使用", ids, [target.Seat], p));
        }
        foreach (var kind in SlashKinds)
        foreach (var selection in GetProgramMultiCardViewAsSelections(actor, kind, false))
        {
            if (!PaymentRange(selection.Cards) || !Legal(selection.Cards[0], kind, selection.Source, selection.Cards.Count > 1, materials:selection.Cards.Select(c=>c.Id).ToArray())) continue;
            var p = Parameters("use"); p["effective-kind"] = kind.ToString(); AddConversionParameters(p, selection.Source);
            var ids = selection.Cards.Select(card => card.Id).ToArray();
            choices.Add(new(new($"fixed-slash.{frame.Id}.multi.{selection.Source.SkillId}.{selection.Source.BindingId}.{string.Join('-', ids)}"),
                "将所选牌当【杀】使用", ids, [target.Seat], p));
        }
        if (!d.Issued && GetFactionResponsePolicy(actor, CardKind.Slash) is { } policy &&
            SlashKinds.Any(kind => IsFixedTargetFactionSlashLegal(frame, kind)))
        {
            var p = Parameters("faction"); p["skill"] = policy.SkillId;
            choices.Add(new(new($"fixed-slash.{frame.Id}.faction"), "发动技能请求其他角色提供【杀】", [], [target.Seat], p));
        }
        choices.Add(new(new($"fixed-slash.{frame.Id}.decline"), "不使用【杀】", [], [], Parameters("decline")));
        return choices.Where(choice => choice.Parameters["request-option"] != "use" ||
            !IsTurnPhysicalUseForbidden(actor.Seat, choice.Cards)).ToArray();
    }

    private void ResolveFixedTargetSlashChoice(ProgramSkillFrame frame, PromptChoice selected)
    {
        var draft = frame.FixedTargetSlash!;
        if (draft.Issued || selected.Parameters.GetValueOrDefault("request-option") == "decline")
        { ReplaceRuntimeTop(frame with { FixedTargetSlash = null }); AdvanceRuntimeProgram(frame.Id); return; }
        var actor = _players[frame.OwnerSeat];
        ReplaceRuntimeTop(frame = frame with { FixedTargetSlash = draft with { Issued = true } });
        if (selected.Parameters["request-option"] == "faction")
        { BeginFixedTargetFactionSlash(frame); return; }
        var kind = Enum.Parse<CardKind>(selected.Parameters["effective-kind"]);
        IReadOnlyList<Card> cards;
        CardConversionSource? conversion;
        if (selected.Cards.Count > 1)
        {
            if (selected.Parameters.GetValueOrDefault("equipment") == CardKind.ZhangbaSerpentSpear.ToString())
            { cards = GetZhangbaSlashPairs(actor).Single(pair => pair.Select(card => card.Id).SequenceEqual(selected.Cards)); conversion = null; }
            else
            {
                if (!TryReadConversionSource(selected.Parameters, out conversion) || conversion is null)
                    throw new InvalidOperationException("A fixed Slash lost its multi-card conversion.");
                cards = FindProgramMultiCardViewAsSelection(actor, selected.Cards, kind, false, conversion)?.Cards ??
                    throw new InvalidOperationException("A fixed Slash lost its physical payment.");
            }
        }
        else
        {
            var card = GetPlayableCards(actor).Concat(GetEquipment(actor)).Single(card => card.Id == selected.Cards.Single());
            TryReadConversionSource(selected.Parameters, out _selectedUseConversion); _hasSelectedUseConversionChoice = true;
            conversion = GetSelectedUseConversion(actor, actor, card, kind);
            if (conversion is null) { _selectedUseConversion = null; _hasSelectedUseConversionChoice = true; }
            cards = [card];
        }
        if (!CanUseSlashTarget(actor, _players[draft.TargetSeat], cards[0], conversion, kind, ignoreDistance: draft.IgnoreDistance,
            noEffectiveRank: cards.Count > 1, specificEffectiveRank: selected.Parameters.GetValueOrDefault("equipment") == CardKind.ZhangbaSerpentSpear.ToString() ? ZhangbaSpecificSlashRank(actor, cards) : null, physicalCardIds:cards.Select(c=>c.Id).ToArray()))
            throw new InvalidOperationException("The fixed Slash target became illegal.");
        ResolveSlashCore(actor, _players[draft.TargetSeat], cards[0], kind, actor.Seat, physicalCards: cards,
            countsTowardSlashLimit: false, usesZhuqueFan: kind == CardKind.FireSlash && cards[0].Kind == CardKind.Slash && HasZhuqueFan(actor),
            conversionSource: conversion, programSkillCardUseFrameId: frame.Id);
    }

    private bool IsFixedTargetFactionSlashLegal(ProgramSkillFrame frame, CardKind kind, int? effectiveRank = null, bool allowPotentialRank = true)
    {
        if (frame.FixedTargetSlash is not { } draft || !IsValidPlayerSeat(draft.TargetSeat)) return false;
        var owner = _players[frame.OwnerSeat]; var target = _players[draft.TargetSeat];
        return owner.IsAlive && target.IsAlive && owner.Seat != target.Seat &&
            !IsCardUseForbidden(owner.Seat, kind, CardActionType.Use) &&
            (draft.IgnoreDistance || IsWithinSpecificSlashRange(owner, target, kind, effectiveRank) ||
                allowPotentialRank && effectiveRank is null && HasPotentialRankSlashRange(owner, target, kind)) &&
            !IsDirectedCardTargetProhibited(owner.Seat, target.Seat, kind) &&
            CanSpendSlashUse(owner, target, ignoresCount: true, kind) && !IsSlashProhibited(target);
    }

    private void BeginFixedTargetFactionSlash(ProgramSkillFrame frame)
    {
        if (ActiveFactionCardRequest is not null || frame.FixedTargetSlash is not { Issued: true } draft ||
            frame.ChoiceBindings.Any(binding => binding.Name == FixedTargetFactionResultBind))
            throw new InvalidOperationException("A fixed faction Slash lost its one-shot parent.");
        var policy = GetFactionResponsePolicy(_players[frame.OwnerSeat], CardKind.Slash) ??
            throw new InvalidOperationException("The fixed Slash lost its faction policy.");
        var candidates = GetFactionProviderSeats(frame.OwnerSeat, policy.FactionId);
        ActiveFactionCardRequest = new FactionCardRequestHandle(this, frame.Id, FactionCardRequestPurpose.AssistedProgramUse,
            frame.OwnerSeat, candidates, policy.FactionId, targetSeat: draft.TargetSeat, programSkillFrameId: frame.Id,
            policySource: policy, assistedResultBind: FixedTargetFactionResultBind);
        _status = EngineStatus.Running;
        AdvanceEventRulesAndQueueFact(new FactionSlashRequestedEvent(frame.Id, frame.OwnerSeat, candidates, policy.SkillId, IsActiveUse: true, draft.TargetSeat));
        AdvanceFactionSlashCandidate();
    }

    private bool ValidateFixedTargetFactionSlashParent(FactionCardRequestHandle pending, ProgramSkillFrame frame)
    {
        if (frame.FixedTargetSlash is not { Issued: true } draft) return false;
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        var answer = frame.ChoiceBindings.SingleOrDefault(binding => binding.Name == FixedTargetFactionResultBind);
        if (effect.Op != SkillProgramEffectOp.UseOwnerSlashAgainstTurnOwner || pending.AssistedResultBind != FixedTargetFactionResultBind ||
            pending.OwnerSeat != frame.OwnerSeat || pending.TargetSeat != draft.TargetSeat || pending.PolicySource is null ||
            frame.WindowContext?.Window != SkillProgramTriggerWindow.TurnEnding || frame.WindowContext.TargetSeat != draft.TargetSeat ||
            (pending.ActiveAttack is null ? answer is not null : answer?.OptionId != "used-slash"))
            throw new InvalidOperationException("A fixed faction Slash changed its owner, target or issued instruction.");
        return true;
    }

    private SkillProgramStepOutcome BeginDeclaredDeckCriterion(ProgramSkillFrame frame)
    {
        if (frame.DeckCriterion is not null || frame.TriggerId is not null || frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats.Count != 0)
            throw new InvalidOperationException("Declared deck search requires a zero-input activation.");
        ReplaceRuntimeTop(frame = frame with { DeckCriterion = new("declare") });
        PublishFixedSlashAndDeclaredDeckPrompt(frame, DeclaredDeckCriterionChoices(frame), "声明一种牌的类别或颜色。", []);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> DeclaredDeckCriterionChoices(ProgramSkillFrame frame) => DeckCriteria.Select(criterion =>
    {
        var p = FixedSlashAndDeclaredDeckParameters(frame, "declared-deck-criterion"); p["criterion"] = criterion;
        var label = criterion switch { "basic" => "基本牌", "trick" => "锦囊牌", "equipment" => "装备牌", "red" => "红色牌", _ => "黑色牌" };
        return new PromptChoice(new($"declared-deck.{frame.Id}.{criterion}"), $"声明{label}", [], [], p);
    }).ToArray();

    private static bool MatchesDeclaredDeckCriterion(Card card, string criterion) => criterion switch
    {
        "basic" => GetProgramCardCategory(card.Kind) == SkillProgramCardCategory.Basic,
        "trick" => GetProgramCardCategory(card.Kind) == SkillProgramCardCategory.Trick,
        "equipment" => GetProgramCardCategory(card.Kind) == SkillProgramCardCategory.Equipment,
        "red" => card.Suit is Suit.Heart or Suit.Diamond,
        "black" => card.Suit is Suit.Spade or Suit.Club,
        _ => throw new InvalidOperationException("Unknown declared deck criterion.")
    };

    private IReadOnlyList<PromptChoice> DeclaredDeckRecipientChoices(ProgramSkillFrame frame) =>
        _players.Where(player => player.IsAlive && player.Gender == GeneralGender.Male).Select(player =>
        {
            var p = FixedSlashAndDeclaredDeckParameters(frame, "declared-deck-recipient");
            return new PromptChoice(new($"declared-deck.{frame.Id}.recipient-{player.Seat}"), $"将展示的牌交给 {player.Name}", [], [player.Seat], p);
        }).ToArray();

    private void ResumeDeclaredDeckCriterion(ProgramSkillFrame frame)
    {
        var d = frame.DeckCriterion!;
        if (d.Stage == "gift") { FinishDeclaredDeckCriterion(frame); return; }
        if (!DeckProgramSourceValid(frame) || _winner != Winner.None)
        { CancelProgramBindingAndCleanup(frame, "荐言的拥有者或技能实例已失效。"); return; }
        if (d.Stage == "declare")
        { PublishFixedSlashAndDeclaredDeckPrompt(frame, DeclaredDeckCriterionChoices(frame), "声明一种牌的类别或颜色。", []); return; }
        if (d.Stage == "recipient")
        {
            if (d.MatchedCardId is not { } match || _cardZones.GetLocation(match) != CardLocation.Processing)
            { FinishDeclaredDeckCriterion(frame); return; }
            var choices = DeclaredDeckRecipientChoices(frame);
            if (choices.Count == 0) { FinishDeclaredDeckCriterion(frame); return; }
            PublishFixedSlashAndDeclaredDeckPrompt(frame, choices, "选择一名男性角色获得展示的牌。", choices.SelectMany(c => c.Targets).ToArray());
            return;
        }
        if (d.Stage != "search" || d.Criterion is null) throw new InvalidOperationException("Declared deck search lost its stage.");
        if (d.Remaining == 0 || _cardZones.Count(CardLocation.DrawPile) == 0)
        { FinishDeclaredDeckCriterion(frame); return; }
        var card = _cardZones.CardsAt(CardLocation.DrawPile)[^1];
        var matched = MatchesDeclaredDeckCriterion(card, d.Criterion);
        var next = d with { Remaining = d.Remaining - 1, Stage = matched ? "recipient" : "search", MatchedCardId = matched ? card.Id : null };
        ReplaceRuntimeTop(frame = frame with { DeckCriterion = next });
        if (matched) SetProgramCardSet(frame.Id, DeclaredDeckCardBind, [card.Id], SkillProgramCardSetVisibility.Public, [CardLocation.DrawPile]);
        AdvanceEventRulesAndQueueFact(new ProgramDeckCriterionRevealedEvent(frame.Id, frame.OwnerSeat, d.Criterion, card.Id, matched));
        AdvanceEventRulesAndQueueFact(new ProgramCardsRevealedEvent(frame.Id, frame.SkillId, GetProgramBindingId(frame),
            frame.OwnerSeat, DeclaredDeckCardBind, Array.AsReadOnly(new[] { ToSnapshot(card) })));
        MoveCard(card, CardLocation.DrawPile, matched ? CardLocation.Processing : CardLocation.DiscardPile,
            new(matched ? "program.declared-deck.match" : "program.declared-deck.unmatched"));
        AwaitDeckProgramMovement(frame.Id);
    }

    private void FinishDeclaredDeckCriterion(ProgramSkillFrame frame)
    {
        // A reveal can lose its source or have no surviving male recipient. Bound
        // processing entities still follow the ordinary program cleanup path.
        CleanupProgramBoundCards(frame, completed: true);
        var current = GetActiveProgramFrame(frame.Id);
        ReplaceRuntimeTop(current with { DeckCriterion = null });
        AdvanceRuntimeProgram(frame.Id);
    }

    private bool TryResumeFixedSlashAndDeclaredDeck(long frameId)
    {
        var frame = GetActiveProgramFrame(frameId);
        if (frame.FixedTargetSlash is { } slash)
        {
            if (slash.Issued)
            {
                if (ActiveFactionCardRequest is { IsAssistedProgramUse: true } request && request.ProgramSkillFrameId == frameId) return true;
                ReplaceRuntimeTop(frame with { FixedTargetSlash = null }); AdvanceRuntimeProgram(frameId);
            }
            else if (!DeckProgramSourceValid(frame) || !_players[slash.TargetSeat].IsAlive || _winner != Winner.None)
                CancelProgramBindingAndCleanup(frame, "诛害的参与者或技能实例已失效。");
            else PublishFixedSlashAndDeclaredDeckPrompt(frame, FixedTargetSlashChoices(frame), "选择一张【杀】对本回合角色使用。", [slash.TargetSeat]);
            return true;
        }
        if (frame.DeckCriterion is null) return false;
        if (frame.PendingMovementContinuation is not null) return true;
        ResumeDeclaredDeckCriterion(frame);
        return true;
    }

    private void PublishFixedSlashAndDeclaredDeckPrompt(ProgramSkillFrame frame, IReadOnlyList<PromptChoice> choices, string text, IReadOnlyList<int> targets)
    {
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat, text, choices.SelectMany(c => c.Cards).Distinct().ToArray(), targets, frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = frame.OwnerSeat, Choices = Array.AsReadOnly(choices.ToArray()),
            SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveFixedSlashAndDeclaredDeckChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Fixed Slash or deck choice lost its frame.");
        var action = selected.Parameters.GetValueOrDefault("program-action");
        var choices = action switch
        {
            "fixed-target-slash" when frame.FixedTargetSlash is { Issued: false } => FixedTargetSlashChoices(frame),
            "declared-deck-criterion" when frame.DeckCriterion is { Stage: "declare" } => DeclaredDeckCriterionChoices(frame),
            "declared-deck-recipient" when frame.DeckCriterion is { Stage: "recipient" } => DeclaredDeckRecipientChoices(frame),
            _ => throw new InvalidOperationException("Fixed Slash or declared deck choice changed its stage.")
        };
        var canonical = choices.SingleOrDefault(choice => choice.Id == selected.Id);
        if (canonical is null || !AssistedChoicesEqual([selected], [canonical]) || _pendingDecision?.PlayerSeat != frame.OwnerSeat)
            throw new InvalidOperationException("Fixed Slash or deck choice changed its physical payment or recipient.");
        ClearPendingDecision();
        if (!DeckProgramSourceValid(frame) || _winner != Winner.None)
        { CancelProgramBindingAndCleanup(frame, "技能拥有者或技能实例已失效。"); return; }
        if (action == "fixed-target-slash") { ResolveFixedTargetSlashChoice(frame, canonical); return; }
        var d = frame.DeckCriterion!;
        if (action == "declared-deck-criterion")
        {
            // Reshuffle only before the finite search starts. Failed reveals do
            // not refill the pile, so an absent criterion cannot loop forever.
            EnsureDrawPile();
            ReplaceRuntimeTop(frame = GetActiveProgramFrame(frame.Id) with
            { DeckCriterion = d with { Stage = "search", Criterion = canonical.Parameters["criterion"], Remaining = _cardZones.Count(CardLocation.DrawPile) } });
            AwaitDeckProgramMovement(frame.Id);
            return;
        }
        var recipient = canonical.Targets.Single();
        if (d.MatchedCardId is not { } cardId || _cardZones.GetLocation(cardId) != CardLocation.Processing)
        { FinishDeclaredDeckCriterion(frame); return; }
        var card = _cardZones.CardsAt(CardLocation.Processing).Single(c => c.Id == cardId);
        ReplaceRuntimeTop(frame with { DeckCriterion = d with { Stage = "gift", RecipientSeat = recipient } });
        MoveCard(card, CardLocation.Processing, CardLocation.Hand(recipient), new("program.declared-deck.give"));
        AwaitDeckProgramMovement(frame.Id);
    }

    private PromptChoice SelectAiFixedSlashAndDeclaredDeck(PendingDecision decision, ProgramSkillFrame frame)
    {
        if (frame.DeckCriterion is { Stage: "recipient" })
            return decision.Choices.FirstOrDefault(choice => choice.Targets.Contains(frame.OwnerSeat)) ?? decision.Choices[0];
        if (frame.DeckCriterion is { Stage: "declare" })
            return decision.Choices.Single(choice => choice.Parameters.GetValueOrDefault("criterion") == "trick");
        return decision.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("request-option") == "use") ?? decision.Choices[0];
    }

    private void AssertFixedSlashAndDeclaredDeckDrafts(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (frame.FixedTargetSlash is { } slash && (paused.Op != SkillProgramEffectOp.UseOwnerSlashAgainstTurnOwner ||
            frame.WindowContext?.Window != SkillProgramTriggerWindow.TurnEnding || frame.WindowContext.TargetSeat != slash.TargetSeat ||
            slash.TargetSeat == frame.OwnerSeat || !IsValidPlayerSeat(slash.TargetSeat) || slash.IgnoreDistance != paused.BooleanValue))
            throw new InvalidOperationException("The fixed Slash draft lost its frozen target or instruction.");
        if (frame.DeckCriterion is { } d && (paused.Op != SkillProgramEffectOp.DeclareDeckCriterionAndGiveMatchingCard ||
            frame.TriggerId is not null || frame.SelectedCardIds.Count != 0 || frame.SelectedTargetSeats.Count != 0 ||
            d.Stage is not ("declare" or "search" or "recipient" or "gift") || d.Remaining < 0 ||
            (d.Stage == "declare" ? d.Criterion is not null : d.Criterion is null || !DeckCriteria.Contains(d.Criterion)) ||
            (d.Stage is "recipient" or "gift") != (d.MatchedCardId is not null) ||
            (d.Stage == "gift") != (d.RecipientSeat is not null)))
            throw new InvalidOperationException("The declared deck draft lost its finite search or committed recipient.");
        if (!ReferenceEquals(frame, _resolutionStack.LastOrDefault()) || frame.PendingMovementContinuation is not null) return;
        IReadOnlyList<PromptChoice>? expected = frame.FixedTargetSlash is { Issued: false } ? FixedTargetSlashChoices(frame) :
            frame.DeckCriterion is { Stage: "declare" } ? DeclaredDeckCriterionChoices(frame) :
            frame.DeckCriterion is { Stage: "recipient" } ? DeclaredDeckRecipientChoices(frame) : null;
        if (expected is not null && (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p ||
            p.PlayerSeat != frame.OwnerSeat || !AssistedChoicesEqual(p.Choices, expected)))
            throw new InvalidOperationException("Fixed Slash or declared deck lost its canonical private prompt.");
    }
}
