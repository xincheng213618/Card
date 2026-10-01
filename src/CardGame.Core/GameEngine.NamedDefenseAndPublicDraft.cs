namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : INamedDefenseAndPublicDraftHost
    {
        public SkillProgramStepOutcome BeginNamedTargetDefense(ProgramSkillFrame frame, string ledgerId, IReadOnlyList<CardKind> names) => engine.BeginNamedTargetDefense(frame, ledgerId, names);
        public SkillProgramStepOutcome BeginLowHandPublicDraft(ProgramSkillFrame frame) => engine.BeginLowHandPublicDraft(frame);
    }

    private static string DefenseNameUsage(string ledger, CardKind kind) => $"declared-name:{ledger}:{ProgramBasicCardName(kind)}";
    private CardUseFrame NamedDefenseUse(ProgramSkillFrame frame)
    {
        var context = frame.WindowContext;
        if (context is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } use } ||
            !IsSlashCard(use.EffectiveKind) || use.EventTargetSeat != frame.OwnerSeat ||
            _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(parent => parent.Id == use.ParentCardUseFrameId) is not { Action: { } action } parent ||
            action.ActionId != use.CardActionId || action.ActorSeat != use.ActorSeat || action.EffectiveKind != use.EffectiveKind || !parent.TargetSeats.Contains(frame.OwnerSeat))
            throw new InvalidOperationException("A named defense requires its target's frozen actual Slash.");
        return parent;
    }

    private SkillProgramStepOutcome BeginNamedTargetDefense(ProgramSkillFrame frame, string ledgerId, IReadOnlyList<CardKind> names)
    {
        _ = NamedDefenseUse(frame);
        var available = names.Where(kind => _skillRuntimeState.GetUsage(frame.OwnerSeat, frame.SkillId,
            DefenseNameUsage(ledgerId, kind), SkillUsageScope.Game) == 0).ToArray();
        if (available.Length == 0) return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(frame with { NamedTargetDefense = new(ledgerId) });
        PublishNamedDraftPrompt(frame, frame.OwnerSeat, "声明一种尚未声明的非装备牌名。", available.Select(kind =>
            NamedDraftChoice(frame, "named-defense-name", CardCatalog.Get(kind).DisplayName, name: kind)).ToArray(), true);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void PublishNamedDefensePayment(ProgramSkillFrame frame)
    {
        var defense = frame.NamedTargetDefense!; var parent = NamedDefenseUse(frame); var actor = parent.Action!.ActorSeat;
        if (!_players[actor].IsAlive) { FinishNamedDefense(frame, true); return; }
        var cards = GetHand(_players[actor]).Concat(GetEquipment(_players[actor]))
            .Where(card => ProgramBasicCardName(card.Kind) == defense.Name).OrderBy(card => card.Id).ToArray();
        var choices = cards.Select(card => NamedDraftChoice(frame, "named-defense-pay", $"弃置【{card.DisplayName}】并获得对方一张牌", card.Id)).ToList();
        choices.Add(NamedDraftChoice(frame, "named-defense-decline", "令此杀对目标无效"));
        PublishNamedDraftPrompt(frame, actor, $"弃置一张【{CardCatalog.Get(defense.Name!.Value).DisplayName}】，或令此杀对目标无效。", choices, true);
    }

    private void PublishNamedDefenseTake(ProgramSkillFrame frame)
    {
        var parent = NamedDefenseUse(frame); var actor = parent.Action!.ActorSeat;
        if (!_players[actor].IsAlive || !_players[frame.OwnerSeat].IsAlive) { FinishNamedDefense(frame, false); return; }
        var choices = GetHand(_players[frame.OwnerSeat]).Select((card, slot) => NamedDraftChoice(frame,
            "named-defense-take-hand", $"获得第 {slot + 1} 张手牌", slot: slot)).ToList();
        choices.AddRange(GetEquipment(_players[frame.OwnerSeat]).Select(card => NamedDraftChoice(frame,
            "named-defense-take-equipment", $"获得【{card.DisplayName}】", card.Id)));
        if (choices.Count == 0) { FinishNamedDefense(frame, false); return; }
        PublishNamedDraftPrompt(frame, actor, "获得目标一张手牌或装备牌。", choices, true);
    }

    private void FinishNamedDefense(ProgramSkillFrame frame, bool nullify)
    {
        if (nullify)
        {
            var parent = NamedDefenseUse(frame);
            MarkCardEffectIneffective(parent.Id, frame.OwnerSeat);
            AdvanceEventRulesAndQueueFact(new CardEffectSkippedEvent(parent.Id, parent.Action!.ActorSeat, frame.OwnerSeat,
                parent.CardKind, CardEffectSkipReason.SkillNullified));
        }
        ReplaceRuntimeTop(frame with { NamedTargetDefense = null });
        AdvanceRuntimeProgram(frame.Id);
    }

    private SkillProgramStepOutcome BeginLowHandPublicDraft(ProgramSkillFrame frame)
    {
        var seats = frame.WindowContext?.Facts?.LowHandPopulationSeats ??
            throw new InvalidOperationException("A population draft requires its frozen end-phase population.");
        if (seats.Count == 0) return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(frame with { PublicHandDraft = new(seats, []) });
        DrawCards(_players[frame.OwnerSeat], seats.Count, log: true, new CardMoveReason($"skill-program.{frame.SkillId}.population-draw"));
        if (AwaitProgramBoundCardMovements(frame.Id, frame.OwnerSeat) == SkillProgramStepOutcome.Continue)
            AdvanceRuntimeProgram(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private bool TryResumeNamedDefenseAndPublicDraft(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != frameId || frame.PendingMovementContinuation is not null) return false;
        if (frame.NamedTargetDefense is { Stage: 2 }) { PublishNamedDefenseTake(frame); return true; }
        if (frame.NamedTargetDefense is { Stage: 3 }) { FinishNamedDefense(frame, false); return true; }
        if (frame.PublicHandDraft is { } draft)
        {
            if (draft.Stage == 0)
            {
                ReplaceRuntimeTop(frame = frame with { PublicHandDraft = draft with { Stage = 1 } });
                PublishPublicDraftSelection(frame); return true;
            }
            if (draft.Stage == 3) { ContinuePublicHandDraft(frame); return true; }
        }
        return false;
    }

    private void PublishPublicDraftSelection(ProgramSkillFrame frame)
    {
        var draft = frame.PublicHandDraft!;
        var cards = GetHand(_players[frame.OwnerSeat]).Where(card => !draft.CardIds.Contains(card.Id)).ToArray();
        if (cards.Length == 0 && draft.CardIds.Count < draft.RecipientSeats.Count)
        { ReplaceRuntimeTop(frame with { PublicHandDraft = null }); AdvanceRuntimeProgram(frame.Id); return; }
        PublishNamedDraftPrompt(frame, frame.OwnerSeat, $"选择展示的手牌（{draft.CardIds.Count}/{draft.RecipientSeats.Count}）。",
            cards.Select(card => NamedDraftChoice(frame, "public-draft-show", $"展示【{card.DisplayName}】（{GetSuitDisplayName(card.Suit)} {card.RankText}）", card.Id)).ToArray(), true);
    }

    private void ContinuePublicHandDraft(ProgramSkillFrame frame)
    {
        var draft = frame.PublicHandDraft!;
        var cards = draft.CardIds.Where(id => _cardZones.GetLocation(id) == CardLocation.Hand(frame.OwnerSeat)).ToArray();
        var cursor = draft.Cursor;
        while (cursor < draft.RecipientSeats.Count && !_players[draft.RecipientSeats[cursor]].IsAlive) cursor++;
        if (cursor >= draft.RecipientSeats.Count || cards.Length == 0 || !_players[frame.OwnerSeat].IsAlive)
        { ReplaceRuntimeTop(frame with { PublicHandDraft = null }); AdvanceRuntimeProgram(frame.Id); return; }
        ReplaceRuntimeTop(frame = frame with { PublicHandDraft = draft with { CardIds = cards, Cursor = cursor } });
        var recipient = draft.RecipientSeats[cursor];
        PublishNamedDraftPrompt(frame, recipient, "从展示的牌中选择获得一张。", cards.Select(id =>
        {
            var card = GetHand(_players[frame.OwnerSeat]).Single(card => card.Id == id);
            return NamedDraftChoice(frame, "public-draft-gain", $"获得【{card.DisplayName}】（{GetSuitDisplayName(card.Suit)} {card.RankText}）", id);
        }).ToArray(), false);
    }

    private static PromptChoice NamedDraftChoice(ProgramSkillFrame frame, string action, string label, int? card = null, int? slot = null, CardKind? name = null, int? seat = null) =>
        new(new ChoiceId($"named-draft.{frame.Id}.{action}.{card?.ToString() ?? slot?.ToString() ?? name?.ToString() ?? seat?.ToString() ?? "none"}"), label,
            card is { } id ? [id] : [], seat is { } target ? [target] : [], new Dictionary<string, string>
            { ["program-action"] = action, ["frame-id"] = frame.Id.ToString(), ["slot-index"] = slot?.ToString() ?? "", ["card-name"] = name?.ToString() ?? "" });

    private void PublishNamedDraftPrompt(ProgramSkillFrame frame, int chooser, string text, IReadOnlyList<PromptChoice> choices, bool privacy)
    {
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, text, choices.SelectMany(choice => choice.Cards).ToArray(),
            choices.SelectMany(choice => choice.Targets).ToArray(), SourceSeat: frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = privacy, TargetSeat = chooser, Choices = choices,
            SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveNamedDefenseAndDraftChoice(PromptChoice choice)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("The named/draft choice lost its program.");
        if (choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString()) throw new InvalidOperationException("The named/draft choice belongs to another frame.");
        var action = choice.Parameters["program-action"];
        if (frame.NamedTargetDefense is { } defense)
        {
            var parent = NamedDefenseUse(frame); var actor = parent.Action!.ActorSeat;
            if (defense.Stage == 0 && action == "named-defense-name")
            {
                var name = Enum.Parse<CardKind>(choice.Parameters["card-name"]);
                if (!_skillRuntimeState.TryConsumeUsage(frame.OwnerSeat, frame.SkillId, DefenseNameUsage(defense.LedgerId, name), SkillUsageScope.Game, 1))
                    throw new InvalidOperationException("The declared card name was already used.");
                ClearPendingDecision();
                AdvanceEventRulesAndQueueFact(new ProgramDefenseNameDeclaredEvent(frame.Id, frame.SkillId, frame.OwnerSeat, name));
                ReplaceRuntimeTop(frame = frame with { NamedTargetDefense = defense with { Name = name, Stage = 1 } });
                PublishNamedDefensePayment(frame); return;
            }
            if (defense.Stage == 1 && action == "named-defense-decline")
            { ClearPendingDecision(); FinishNamedDefense(frame, true); return; }
            if (defense.Stage == 1 && action == "named-defense-pay")
            {
                var id = choice.Cards.Single(); var from = _cardZones.GetLocation(id);
                if (from.OwnerSeat != actor || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) throw new InvalidOperationException("The named cost is not the actor's owned card.");
                var card = _cardZones.CardsAt(from).Single(card => card.Id == id);
                if (ProgramBasicCardName(card.Kind) != defense.Name) throw new InvalidOperationException("The real cost has another name.");
                ClearPendingDecision(); ReplaceRuntimeTop(frame = frame with { NamedTargetDefense = defense with { Stage = 2 } });
                MoveCard(card, from, CardLocation.DiscardPile, new CardMoveReason($"skill-program.{frame.SkillId}.named-cost"));
                if (AwaitProgramBoundCardMovements(frame.Id, frame.OwnerSeat) == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(frame.Id);
                return;
            }
            if (defense.Stage == 2 && action is "named-defense-take-hand" or "named-defense-take-equipment")
            {
                Card card; CardLocation from;
                if (action == "named-defense-take-hand")
                { from = CardLocation.Hand(frame.OwnerSeat); card = _cardZones.CardsAt(from)[int.Parse(choice.Parameters["slot-index"])]; }
                else { var id = choice.Cards.Single(); from = _cardZones.GetLocation(id); if (from != CardLocation.Equipment(frame.OwnerSeat)) throw new InvalidOperationException("The selected equipment moved."); card = _cardZones.CardsAt(from).Single(c => c.Id == id); }
                ClearPendingDecision(); ReplaceRuntimeTop(frame = frame with { NamedTargetDefense = defense with { Stage = 3 } });
                MoveCard(card, from, CardLocation.Hand(actor), new CardMoveReason($"skill-program.{frame.SkillId}.named-take"));
                if (AwaitProgramBoundCardMovements(frame.Id, frame.OwnerSeat) == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(frame.Id);
                return;
            }
            throw new InvalidOperationException("The named defense has an invalid stage/choice.");
        }
        if (frame.PublicHandDraft is not { } draft) throw new InvalidOperationException("The public hand draft lost its state.");
        if (draft.Stage == 1 && action == "public-draft-show")
        {
            var id = choice.Cards.Single();
            if (draft.CardIds.Contains(id) || _cardZones.GetLocation(id) != CardLocation.Hand(frame.OwnerSeat)) throw new InvalidOperationException("The reveal must select another real owner hand card.");
            ClearPendingDecision(); var selected = draft.CardIds.Append(id).ToArray();
            ReplaceRuntimeTop(frame = frame with { PublicHandDraft = draft with { CardIds = selected } });
            if (selected.Length < draft.RecipientSeats.Count) { PublishPublicDraftSelection(frame); return; }
            ReplaceRuntimeTop(frame = frame with { PublicHandDraft = frame.PublicHandDraft! with { Stage = 2 } });
            AdvanceEventRulesAndQueueFact(new ProgramHandDraftRevealedEvent(frame.Id, frame.SkillId, frame.OwnerSeat, draft.RecipientSeats, selected));
            PublishNamedDraftPrompt(frame, frame.OwnerSeat, "选择顺时针分配的起始角色。", _players.Where(player => player.IsAlive)
                .Select(player => NamedDraftChoice(frame, "public-draft-start", player.Name, seat: player.Seat)).ToArray(), false); return;
        }
        if (draft.Stage == 2 && action == "public-draft-start")
        {
            var start = choice.Targets.Single();
            if (!_players[start].IsAlive) throw new InvalidOperationException("The chosen starting character is no longer alive.");
            ClearPendingDecision(); ReplaceRuntimeTop(frame = frame with { PublicHandDraft = draft with
            { Stage = 3, RecipientSeats = draft.RecipientSeats.OrderBy(seat => (seat - start + _playerCount) % _playerCount).ToArray() } });
            ContinuePublicHandDraft(frame); return;
        }
        if (draft.Stage == 3 && action == "public-draft-gain")
        {
            var id = choice.Cards.Single(); var recipient = draft.RecipientSeats[draft.Cursor];
            if (!draft.CardIds.Contains(id) || !_players[recipient].IsAlive || _cardZones.GetLocation(id) != CardLocation.Hand(frame.OwnerSeat)) throw new InvalidOperationException("The draft card or current recipient is unavailable.");
            var card = GetHand(_players[frame.OwnerSeat]).Single(card => card.Id == id);
            ClearPendingDecision(); ReplaceRuntimeTop(frame = frame with { PublicHandDraft = draft with { CardIds = draft.CardIds.Where(cardId => cardId != id).ToArray(), Cursor = draft.Cursor + 1 } });
            AdvanceEventRulesAndQueueFact(new ProgramHandDraftCardObtainedEvent(frame.Id, frame.SkillId, frame.OwnerSeat, recipient, id));
            if (recipient != frame.OwnerSeat) MoveCard(card, CardLocation.Hand(frame.OwnerSeat), CardLocation.Hand(recipient), new CardMoveReason($"skill-program.{frame.SkillId}.public-draft"));
            if (AwaitProgramBoundCardMovements(frame.Id, frame.OwnerSeat) == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(frame.Id);
            return;
        }
        throw new InvalidOperationException("The public hand draft has an invalid stage/choice.");
    }

    private IEnumerable<int> GetPublicHandDraftCardIds() => _resolutionStack.OfType<ProgramSkillFrame>()
        .Where(frame => frame.PublicHandDraft is { Stage: >= 2 }).SelectMany(frame => frame.PublicHandDraft!.CardIds
            .Where(id => _cardZones.GetLocation(id) == CardLocation.Hand(frame.OwnerSeat)));

    private void AssertNamedDefenseAndPublicDraft(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (frame.NamedTargetDefense is { } defense)
        {
            if (paused.Op != SkillProgramEffectOp.DeclareNameForTargetDefense || defense.LedgerId != paused.StateId ||
                defense.Stage is < 0 or > 3 || (defense.Stage == 0) != (defense.Name is null) ||
                defense.Name is { } name && (!paused.CardKinds.Contains(name) || _skillRuntimeState.GetUsage(frame.OwnerSeat, frame.SkillId, DefenseNameUsage(defense.LedgerId, name), SkillUsageScope.Game) != 1))
                throw new InvalidOperationException("A named defense lost its declared canonical name or paid instruction.");
            _ = NamedDefenseUse(frame);
        }
        if (frame.PublicHandDraft is { } draft && (paused.Op != SkillProgramEffectOp.DrawAndDraftLowHandPopulation ||
            draft.Stage is < 0 or > 3 || draft.RecipientSeats.Distinct().Count() != draft.RecipientSeats.Count ||
            draft.CardIds.Distinct().Count() != draft.CardIds.Count || draft.CardIds.Count > draft.RecipientSeats.Count ||
            draft.Cursor < 0 || draft.Cursor > draft.RecipientSeats.Count ||
            frame.WindowContext?.Facts?.LowHandPopulationSeats is not { } frozen || !draft.RecipientSeats.Order().SequenceEqual(frozen.Order())))
            throw new InvalidOperationException("A public draft lost its frozen participants, real card set or cursor.");
        if (frame.NamedTargetDefense is null && frame.PublicHandDraft is null ||
            !ReferenceEquals(frame, _resolutionStack.LastOrDefault()) || frame.PendingMovementContinuation is not null) return;
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } prompt || prompt.Choices.Count == 0 ||
            prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString()))
            throw new InvalidOperationException("The named/draft operation lost its published decision.");
        var expectedChooser = frame.NamedTargetDefense is { } named ? named.Stage == 0 ? frame.OwnerSeat : NamedDefenseUse(frame).Action!.ActorSeat :
            frame.PublicHandDraft!.Stage == 3 ? frame.PublicHandDraft.RecipientSeats[frame.PublicHandDraft.Cursor] : frame.OwnerSeat;
        if (prompt.PlayerSeat != expectedChooser || prompt.IsPrivate != (frame.NamedTargetDefense is not null || frame.PublicHandDraft!.Stage < 2))
            throw new InvalidOperationException("The named/draft decision changed its actor or privacy boundary.");
    }
}
