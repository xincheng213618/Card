namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void AssertAssistedPhysicalCardDrafts(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (ReferenceEquals(frame, _resolutionStack.LastOrDefault()) &&
            ((paused.Op == SkillProgramEffectOp.RequestSlashAgainstChosenTarget && frame.AssistedSlashRequest is null && !frame.ChoiceBindings.Any(binding => binding.Name == paused.ResultBind) && !(ActiveFactionCardRequest is { IsAssistedProgramUse: true } faction && faction.ProgramSkillFrameId == frame.Id)) ||
             (paused.Op == SkillProgramEffectOp.TakeSelectedTargetCards && frame.OtherCardSelection is null && frame.PendingMovementContinuation is null)))
            throw new InvalidOperationException("A suspended assisted-card operation lost its draft.");
        if (frame.AssistedSlashRequest is { } request)
        {
            if (paused.Op != SkillProgramEffectOp.RequestSlashAgainstChosenTarget || frame.SelectedTargetSeats is not [var actor] || actor != request.ActorSeat || actor == frame.OwnerSeat ||
                request.TargetSeat is { } target && !AssistedPhysicalSlashTargets(actor).Contains(target) || frame.ChoiceBindings.Any(binding => binding.Name == paused.ResultBind) || request.ActorChoosesTarget != (paused.ChooserRef?.Kind == ProgramParticipantRef.SelectedTarget))
                throw new InvalidOperationException("An assisted physical Slash draft lost its frozen instruction or participants.");
            if (ReferenceEquals(frame, _resolutionStack.LastOrDefault()) && (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } prompt ||
                prompt.PlayerSeat != (request.TargetSeat is null && !request.ActorChoosesTarget ? frame.OwnerSeat : actor) || !AssistedChoicesEqual(RequestedDeckBasicNativeDecision(prompt).Choices, AssistedPhysicalSlashChoices(frame))))
                throw new InvalidOperationException("An assisted physical Slash prompt changed while suspended.");
        }
        if (frame.OtherCardSelection is { } draft)
        {
            if (paused.Op != SkillProgramEffectOp.TakeSelectedTargetCards || frame.SelectedTargetSeats is not [var source] || source != draft.SourceSeat || source == frame.OwnerSeat ||
                draft.RequiredCount is < 1 or > 2 || draft.RequiredCount != Math.Min(paused.Amount, GetHand(_players[source]).Count + GetEquipment(_players[source]).Count) || draft.SelectedSlots.Count >= draft.RequiredCount || draft.SelectedSlots.Distinct().Count() != draft.SelectedSlots.Count ||
                draft.SelectedSlots.Any(slot => slot.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) || slot.SlotIndex < 0 || slot.SlotIndex >= _cardZones.Count(new CardLocation(slot.Zone, source))))
                throw new InvalidOperationException("An other-owned card draft lost its source or selection bounds.");
            if (ReferenceEquals(frame, _resolutionStack.LastOrDefault()) && (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } prompt || prompt.PlayerSeat != frame.OwnerSeat ||
                !AssistedChoicesEqual(prompt.Choices, OtherCardSelectionChoices(frame))))
                throw new InvalidOperationException("An other-owned card prompt changed while suspended.");
        }
    }

    private static bool AssistedChoicesEqual(IReadOnlyList<PromptChoice> actual, IReadOnlyList<PromptChoice> expected) =>
        actual.Count == expected.Count && actual.Zip(expected).All(pair =>
            pair.First.Id == pair.Second.Id && pair.First.Description == pair.Second.Description &&
            pair.First.Cards.SequenceEqual(pair.Second.Cards) && pair.First.Targets.SequenceEqual(pair.Second.Targets) &&
            pair.First.ContentIds.SequenceEqual(pair.Second.ContentIds) &&
            pair.First.Parameters.OrderBy(item => item.Key).SequenceEqual(pair.Second.Parameters.OrderBy(item => item.Key)));

    private int[] AssistedPhysicalSlashTargets(int actorSeat) => _players.Where(target => _players[actorSeat].IsAlive && target.IsAlive && target.Seat != actorSeat &&
        (HasPotentialProvenanceSlash(_players[actorSeat]) || IsWithinAttackRange(actorSeat, target.Seat) || HasPotentialRankSlashRange(_players[actorSeat],target,CardKind.Slash))).Select(target => target.Seat).ToArray();

    private SkillProgramStepOutcome RequestProgramSlashAgainstChosenTarget(ProgramSkillFrame frame, int actorSeat, string bind, bool actorChoosesTarget)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.AssistedSlashRequest is not null || active.SelectedTargetSeats.Count != 1 || active.SelectedTargetSeats[0] != actorSeat || actorSeat == active.OwnerSeat)
            throw new InvalidOperationException("An assisted Slash requires one distinct selected actor.");
        if (!_players[actorSeat].IsAlive || AssistedPhysicalSlashTargets(actorSeat).Length == 0)
        {
            CommitProgramChoiceResult(active.Id, bind, "declined", actorSeat, "没有合法目标。");
            return SkillProgramStepOutcome.Continue;
        }
        active = active with { AssistedSlashRequest = new(actorSeat, ActorChoosesTarget: actorChoosesTarget) };
        ReplaceRuntimeTop(active);
        PublishAssistedPhysicalSlashPrompt(active);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> AssistedPhysicalSlashChoices(ProgramSkillFrame frame)
    {
        var draft = frame.AssistedSlashRequest ?? throw new InvalidOperationException("Missing assisted Slash draft.");
        var actor = _players[draft.ActorSeat];
        Dictionary<string, string> Parameters(string option) => new()
        {
            ["program-action"] = "assisted-physical-slash", ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), ["request-option"] = option
        };
        if (draft.TargetSeat is null)
            return AssistedPhysicalSlashTargets(actor.Seat).Select(seat => new PromptChoice(new ChoiceId($"assisted-slash.{frame.Id}.target-{seat}"), $"指定 {_players[seat].Name} 为【杀】的目标", [], [seat], Parameters("target"))).ToArray();
        var targetSeat = draft.TargetSeat.Value;
        var choices = new List<PromptChoice>();
        foreach (var card in GetSlashUseCards(actor))
        {
            var identity = GetProgramCardIdentityMatches(actor, card).FirstOrDefault(item => IsSlashCard(item.Identity.OutputKind));
            var baseKind = identity?.Identity.OutputKind ?? (IsSlashCard(card.Kind) ? card.Kind : CardKind.Slash);
            foreach (var kind in GetSlashUseKinds(actor, baseKind))
            {
                if (!AssistedSlashPaymentHasRange(actor, targetSeat, [card]) || !CanUseSlashTarget(actor, _players[targetSeat], card, effectiveKind: kind)) continue;
                var parameters = Parameters("use");
                parameters["effective-kind"] = kind.ToString();
                choices.AddRange(CreateConversionChoiceVariants(actor, card, kind, false, $"assisted-slash.{frame.Id}.card-{card.Id}.{kind}", $"使用【{CardCatalog.Get(kind).DisplayName}】", [card.Id], [targetSeat], parameters));
            }
        }
        foreach (var card in GetPlayableCards(actor).Concat(GetEquipment(actor)).DistinctBy(card => card.Id))
        foreach (var kind in SlashKinds.Where(kind => kind != CardKind.Slash))
        foreach (var conversion in GetProgramViewAsConversions(actor, card, kind, forResponse: false))
        {
            if (IsTurnHandCardRestricted(actor, card) || !AssistedSlashPaymentHasRange(actor, targetSeat, [card]) || !CanUseSlashTarget(actor, _players[targetSeat], card, conversion, kind)) continue;
            var parameters = Parameters("use");
            parameters["effective-kind"] = kind.ToString();
            AddConversionParameters(parameters, conversion);
            choices.Add(new PromptChoice(new ChoiceId($"assisted-slash.{frame.Id}.typed.{kind}.{conversion.SkillId}.{conversion.BindingId}.{card.Id}"), $"将牌当【{CardCatalog.Get(kind).DisplayName}】使用", [card.Id], [targetSeat], parameters));
        }
        foreach (var pair in GetZhangbaSlashPairs(actor))
        {
            if (!CanUseSlashTarget(actor, _players[targetSeat], pair[0], noEffectiveRank:true, specificEffectiveRank:ZhangbaSpecificSlashRank(actor,pair), physicalCardIds:pair.Select(c=>c.Id).ToArray())) continue;
            var parameters = Parameters("use");
            parameters["effective-kind"] = CardKind.Slash.ToString();
            parameters["equipment"] = CardKind.ZhangbaSerpentSpear.ToString();
            var ids = pair.Select(card => card.Id).ToArray();
            choices.Add(new PromptChoice(new ChoiceId($"assisted-slash.{frame.Id}.zhangba.{string.Join('-', ids)}"), "使用丈八蛇矛：将两张手牌当【杀】使用", ids, [targetSeat], parameters));
        }
        foreach (var kind in SlashKinds)
        foreach (var selection in GetProgramMultiCardViewAsSelections(actor, kind, false))
        {
            if (!AssistedSlashPaymentHasRange(actor, targetSeat, selection.Cards) || !CanUseSlashTarget(actor, _players[targetSeat], selection.Cards[0], selection.Source, kind, noEffectiveRank: selection.Cards.Count > 1, physicalCardIds:selection.Cards.Select(c=>c.Id).ToArray())) continue;
            var parameters = Parameters("use");
            parameters["effective-kind"] = kind.ToString();
            AddConversionParameters(parameters, selection.Source);
            var ids = selection.Cards.Select(card => card.Id).ToArray();
            choices.Add(new PromptChoice(new ChoiceId($"assisted-slash.{frame.Id}.multi.{selection.Source.SkillId}.{selection.Source.BindingId}.{string.Join('-', ids)}"), "将所选牌当【杀】使用", ids, [targetSeat], parameters));
        }
        var factionPolicy = GetFactionResponsePolicy(actor, CardKind.Slash);
        if (factionPolicy is not null && CanRequestAssistedProgramFactionSlash(actor.Seat, targetSeat))
        {
            var parameters = Parameters("faction");
            parameters["skill"] = factionPolicy.SkillId;
            choices.Add(new PromptChoice(new ChoiceId($"assisted-slash.{frame.Id}.faction"), "发动技能请求其他角色提供【杀】", [], [targetSeat], parameters));
        }
        choices.Add(new PromptChoice(new ChoiceId($"assisted-slash.{frame.Id}.decline"), "不使用【杀】", [], [], Parameters("decline")));
        return choices.Where(c => c.Parameters.GetValueOrDefault("request-option") != "use" || !IsTurnPhysicalUseForbidden(actor.Seat,c.Cards)).ToArray();
    }

    private bool AssistedSlashPaymentHasRange(CharacterState actor, int targetSeat, IReadOnlyList<Card> cards) =>
        (HasProvenanceUseDistance(actor, cards.Select(c=>c.Id).ToArray()) || HasGrantedPhaseEntityDistance(actor, cards.Select(c=>c.Id).ToArray())) || cards.Count == 1 && HasRankSlashRange(actor, CardKind.Slash) && cards[0].Rank > 0 ||
        !cards.Any(card => _cardZones.GetLocation(card.Id) == CardLocation.Equipment(actor.Seat) && EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Weapon) ||
        GetCombatDistance(actor.Seat, targetSeat) <= 1;

    private void PublishAssistedPhysicalSlashPrompt(ProgramSkillFrame frame)
    {
        var draft = frame.AssistedSlashRequest!;
        var chooserSeat = draft.TargetSeat is null && !draft.ActorChoosesTarget ? frame.OwnerSeat : draft.ActorSeat;
        var choices = AssistedPhysicalSlashChoices(frame);
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, chooserSeat, draft.TargetSeat is null ? "请选择受令角色攻击范围内的目标。" : "使用一张【杀】，或允许技能拥有者获得你的牌。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), draft.TargetSeat is null ? AssistedPhysicalSlashTargets(draft.ActorSeat) : [draft.TargetSeat.Value], frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = chooserSeat, Choices = Array.AsReadOnly(choices.ToArray()), SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooserSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveAssistedPhysicalSlashChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Assisted Slash frame disappeared.");
        selected = AssistedPhysicalSlashChoices(frame).SingleOrDefault(choice => choice.Id == selected.Id) ?? throw new InvalidOperationException("The assisted Slash option is no longer available.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        var draft = frame.AssistedSlashRequest ?? throw new InvalidOperationException("Assisted Slash draft disappeared.");
        if (effect.Op != SkillProgramEffectOp.RequestSlashAgainstChosenTarget || selected.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            _pendingDecision?.PlayerSeat != (draft.TargetSeat is null && !draft.ActorChoosesTarget ? frame.OwnerSeat : draft.ActorSeat) || !AssistedPhysicalSlashChoices(frame).Any(choice => choice.Id == selected.Id))
            throw new InvalidOperationException("Assisted Slash choice changed its frozen instruction or participants.");
        ClearPendingDecision();
        if (!_players[frame.OwnerSeat].IsAlive || !_players[draft.ActorSeat].IsAlive || !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
        { CancelProgramBindingAndCleanup(frame, "受令角色或技能实例已失效。"); return; }
        if (draft.TargetSeat is null)
        {
            ReplaceRuntimeTop(frame with { AssistedSlashRequest = draft with { TargetSeat = selected.Targets.Single() } });
            PublishAssistedPhysicalSlashPrompt((ProgramSkillFrame)_resolutionStack[^1]);
            return;
        }
        ReplaceRuntimeTop(frame with { AssistedSlashRequest = null });
        if (selected.Parameters.GetValueOrDefault("request-option") == "decline")
        {
            CommitProgramChoiceResult(frame.Id, effect.ResultBind!, "declined", draft.ActorSeat, "不使用【杀】。");
            AdvanceRuntimeProgram(frame.Id);
            return;
        }
        if (selected.Parameters.GetValueOrDefault("request-option") == "faction")
        {
            BeginAssistedProgramFactionSlashRequest(frame, draft.ActorSeat, draft.TargetSeat.Value, effect.ResultBind!);
            return;
        }
        var actor = _players[draft.ActorSeat];
        var (cards, kind, conversion) = ReadAssistedSlashPayment(actor, selected);
        if (!AssistedPhysicalSlashTargets(actor.Seat).Contains(draft.TargetSeat.Value) || !AssistedSlashPaymentHasRange(actor, draft.TargetSeat.Value, cards) || !CanUseSlashTarget(actor, _players[draft.TargetSeat.Value], cards[0], conversion, kind, noEffectiveRank:cards.Count > 1, specificEffectiveRank:selected.Parameters.GetValueOrDefault("equipment") == CardKind.ZhangbaSerpentSpear.ToString() ? ZhangbaSpecificSlashRank(actor,cards) : null, physicalCardIds:cards.Select(c=>c.Id).ToArray()))
            throw new InvalidOperationException("The assisted Slash target became illegal.");
        CommitProgramChoiceResult(frame.Id, effect.ResultBind!, "used-slash", actor.Seat, "使用【杀】。");
        ResolveSlashCore(actor, _players[draft.TargetSeat.Value], cards[0], kind, actor.Seat, physicalCards: cards, countsTowardSlashLimit: false,
            usesZhuqueFan: kind == CardKind.FireSlash && cards[0].Kind == CardKind.Slash && HasZhuqueFan(actor), conversionSource: conversion, programSkillCardUseFrameId: frame.Id);
    }

    private SkillProgramStepOutcome TakeProgramSelectedTargetCards(ProgramSkillFrame frame, int sourceSeat, int count)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (sourceSeat == active.OwnerSeat || active.SelectedTargetSeats.Single() != sourceSeat || active.OtherCardSelection is not null || count is < 1 or > 2)
            throw new InvalidOperationException("Other-owned card selection requires a distinct selected source.");
        var available = GetHand(_players[sourceSeat]).Count + GetEquipment(_players[sourceSeat]).Count;
        if (!_players[sourceSeat].IsAlive || available == 0) return SkillProgramStepOutcome.Continue;
        active = active with { OtherCardSelection = new(sourceSeat, Math.Min(count, available), []) };
        ReplaceRuntimeTop(active);
        PublishOtherCardSelection(active);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> OtherCardSelectionChoices(ProgramSkillFrame frame)
    {
        var draft = frame.OtherCardSelection!;
        return BuildOwnedCardPaymentChoices(frame.Id, frame.OwnerSeat, draft.SourceSeat, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Obtain)
            .Where(choice => !draft.SelectedSlots.Contains(new(Enum.Parse<CardZoneKind>(choice.Parameters["source-zone"]), int.Parse(choice.Parameters["slot-index"]))))
            .Select(choice => choice with { Id = new ChoiceId($"take-other.{frame.Id}.{draft.SelectedSlots.Count}.{choice.Id.Value}"), Parameters = new Dictionary<string, string>(choice.Parameters) { ["program-action"] = "take-other-card", ["selection-index"] = draft.SelectedSlots.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) } }).ToArray();
    }

    private void PublishOtherCardSelection(ProgramSkillFrame frame)
    {
        var choices = OtherCardSelectionChoices(frame);
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, frame.OwnerSeat, "请选择要获得的手牌或装备牌，选齐后同时移动。", choices.SelectMany(choice => choice.Cards).Distinct().ToArray(), [], frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = frame.SelectedTargetSeats.Single(), Choices = Array.AsReadOnly(choices.ToArray()), SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveOtherCardSelectionChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Other-card selection lost its frame.");
        selected = OtherCardSelectionChoices(frame).SingleOrDefault(choice => choice.Id == selected.Id) ?? throw new InvalidOperationException("The other-owned card slot is no longer available.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        var draft = frame.OtherCardSelection ?? throw new InvalidOperationException("Other-card selection lost its draft.");
        if (effect.Op != SkillProgramEffectOp.TakeSelectedTargetCards || _pendingDecision?.PlayerSeat != frame.OwnerSeat || !OtherCardSelectionChoices(frame).Any(choice => choice.Id == selected.Id)) throw new InvalidOperationException("Other-card selection changed its frozen source or slot.");
        ClearPendingDecision();
        if (!_players[frame.OwnerSeat].IsAlive || !_players[draft.SourceSeat].IsAlive || !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
        { CancelProgramBindingAndCleanup(frame, "获得牌的参与者或技能实例已失效。"); return; }
        var slots = draft.SelectedSlots.Append(new ProgramOtherCardSlot(Enum.Parse<CardZoneKind>(selected.Parameters["source-zone"]), int.Parse(selected.Parameters["slot-index"]))).ToArray();
        if (slots.Length < draft.RequiredCount)
        {
            ReplaceRuntimeTop(frame with { OtherCardSelection = draft with { SelectedSlots = slots } });
            PublishOtherCardSelection((ProgramSkillFrame)_resolutionStack[^1]);
            return;
        }
        var moves = slots.Select(slot =>
        {
            var from = new CardLocation(slot.Zone, draft.SourceSeat);
            var card = _cardZones.CardsAt(from)[slot.SlotIndex];
            return (Card: card, From: from, To: card.IsGeneralWeapon && slot.Zone == CardZoneKind.Equipment ? CardLocation.OutsideGame : CardLocation.Hand(frame.OwnerSeat));
        }).ToArray();
        ReplaceRuntimeTop(frame with { OtherCardSelection = null, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        var batch = BeginCardMovementBatch(moves.Select(move => move.From), moves.Select(move => move.To));
        var records = new List<CardMovementRecord>();
        var committed = false;
        var reason = new CardMoveReason($"skill-program.{frame.SkillId}.take-selected-target-cards");
        try
        {
            foreach (var move in moves) _cardZones.Move(move.Card.Id, move.From, move.To);
            foreach (var move in moves)
            {
                records.Add(RecordMovement(move.Card, move.From, move.To, reason));
                ResolveEquipmentSkillGrant(move.Card, move.From, move.To);
                ClearJudgmentEffectiveKindAfterMove(move.Card, move.From, move.To);
                ResolveSilverLionRemoval(move.Card, move.From, reason);
                ResolveWoodenOxMove(move.Card, move.From, move.To);
            }
            committed = true;
        }
        finally { CompleteCardMovementBatch(batch, records, committed); }
        if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(frame.Id);
    }

    private sealed partial class ProgramSkillHost
    {
        public SkillProgramStepOutcome RequestSlashAgainstChosenTarget(ProgramSkillFrame frame, int actorSeat, string resultBind, bool actorChoosesTarget = false) => engine.RequestProgramSlashAgainstChosenTarget(frame, actorSeat, resultBind, actorChoosesTarget);
        public SkillProgramStepOutcome TakeSelectedTargetCards(ProgramSkillFrame frame, int sourceSeat, int count) => engine.TakeProgramSelectedTargetCards(frame, sourceSeat, count);
    }
}
