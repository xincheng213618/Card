namespace CardGame.Core;

public sealed partial class GameEngine
{
    // 织纴: X is the character count of the used card's printed Chinese name; the
    // trigger gate guarantees the effective kind is the entity's own kind.
    private int ZhirenNameLength(ProgramSkillFrame frame) =>
        frame.WindowContext?.CardUse is { } use
            ? CardCatalog.Get(use.EffectiveKind).DisplayName.Length
            : throw new InvalidOperationException("The zhiren tiers lost their current card use.");

    // 织纴 tier 1: view the top X cards of the draw pile and arrange them back
    // over the top and bottom in any order. The modification granted by 燕尔 may
    // suspend the own-turn gate, so the reorder cannot require the current seat.
    private SkillProgramStepOutcome ZhirenProgramReorderTopByLength(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var length = ZhirenNameLength(active);
        return BeginProgramTopReorder(active, length, null, requireCurrentSeat: false);
    }

    // 织纴 tiers 2-4: field discards are prompted one stage at a time; recovery
    // and the three-card draw settle without prompts.
    private SkillProgramStepOutcome ZhirenProgramResolveFieldTiers(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var length = ZhirenNameLength(active);
        if (length < 2) return ContinueZhirenTiers(active, length, skippedDiscards: true);
        if (ZhirenFieldEquipmentCandidates().Count > 0)
        {
            ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with { PanShuZhirenField = new(false, length) });
            PresentZhirenFieldPrompt(GetActiveProgramFrame(active.Id), delayedTrick: false);
            return SkillProgramStepOutcome.AwaitChoice;
        }
        if (ZhirenFieldDelayedCandidates().Count > 0)
        {
            ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with { PanShuZhirenField = new(true, length) });
            PresentZhirenFieldPrompt(GetActiveProgramFrame(active.Id), delayedTrick: true);
            return SkillProgramStepOutcome.AwaitChoice;
        }
        return ContinueZhirenTiers(active, length, skippedDiscards: true);
    }

    // Resumption path after the first stage: the choice resolver re-enters here
    // through the frame state instead of the paused instruction cursor.
    private void ContinueZhirenFieldAfterEquipment(ProgramSkillFrame frame)
    {
        if (ZhirenFieldDelayedCandidates().Count > 0)
        {
            ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
            {
                PanShuZhirenField = GetActiveProgramFrame(frame.Id).PanShuZhirenField! with { AwaitingDelayedTrick = true }
            });
            PresentZhirenFieldPrompt(GetActiveProgramFrame(frame.Id), delayedTrick: true);
            return;
        }
        var active = GetActiveProgramFrame(frame.Id);
        var length = active.PanShuZhirenField!.NameLength;
        _ = ContinueZhirenTiers(active, length, skippedDiscards: false);
    }

    private SkillProgramStepOutcome ContinueZhirenTiers(ProgramSkillFrame active, int length, bool skippedDiscards)
    {
        active = CompleteFrameNativeLogicalDiscard(GetActiveProgramFrame(active.Id));
        ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with { PanShuZhirenField = null });
        active = GetActiveProgramFrame(active.Id);
        if (length >= 3)
            ApplyProgramSkillRecovery(active.Id, active.OwnerSeat, active.OwnerSeat);
        if (length >= 4)
            DrawProgramCards(active.Id, active.OwnerSeat, 3, null, null,
                SkillProgramCardSetVisibility.Private,
                new($"skill-program.{active.SkillId}.zhiren-tiers"));
        AddLog("SkillTriggered",
            $"{_players[active.OwnerSeat].Name} 的【织纴】按牌名字数 {length} 结算" +
            (skippedDiscards ? "（未弃置场上牌）。" : "。"), active.OwnerSeat);
        AdvanceRuntimeProgram(active.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private IReadOnlyList<Card> ZhirenFieldEquipmentCandidates() => _players
        .Where(player => player.IsAlive)
        .SelectMany(player => GetEquipment(player).Select(card => (Player: player, Card: card)))
        .OrderBy(item => item.Player.Seat)
        .ThenBy(item => item.Card.Id)
        .Select(item => item.Card)
        .ToArray();

    private IReadOnlyList<Card> ZhirenFieldDelayedCandidates() => _players
        .Where(player => player.IsAlive)
        .SelectMany(player => GetJudgment(player).Select(card => (Player: player, Card: card)))
        .OrderBy(item => item.Player.Seat)
        .ThenBy(item => item.Card.Id)
        .Select(item => item.Card)
        .ToArray();

    private void PresentZhirenFieldPrompt(ProgramSkillFrame frame, bool delayedTrick)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var candidates = delayedTrick ? ZhirenFieldDelayedCandidates() : ZhirenFieldEquipmentCandidates();
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = candidates.Select(card =>
        {
            var holder = _cardZones.GetLocation(card.Id);
            var holderName = holder.OwnerSeat is { } seat ? _players[seat].Name : "";
            return new PromptChoice(
                new ChoiceId($"zhiren-field.frame-{frame.Id}.card-{card.Id}"),
                $"弃置 {holderName} 的【{card.DisplayName}】。",
                [card.Id], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "zhiren-field-discard",
                    ["stage"] = delayedTrick ? "delayed" : "equipment",
                    ["card-id"] = card.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
        }).ToList();
        choices.Add(new PromptChoice(
            new ChoiceId($"zhiren-field.frame-{frame.Id}.skip-{(delayedTrick ? "delayed" : "equipment")}"),
            delayedTrick ? "不弃置延时锦囊牌。" : "不弃置装备牌。",
            [], [],
            new Dictionary<string, string>
            {
                ["program-action"] = "zhiren-field-discard",
                ["stage"] = delayedTrick ? "delayed" : "equipment",
                ["action"] = "skip"
            }));
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            delayedTrick
                ? $"【{presentation.Name}】可弃置场上的一张延时锦囊牌。"
                : $"【{presentation.Name}】可弃置场上的一张装备牌。",
            candidates.Select(card => card.Id).ToArray(), [], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · {(delayedTrick ? "延时锦囊" : "装备")}弃置",
                $"此牌牌名字数为 {active.PanShuZhirenField!.NameLength}，可弃置场上的一张{(delayedTrick ? "延时锦囊牌" : "装备牌")}，也可以放弃。"),
            Choices = choices.AsReadOnly()
        };
        _status = _players[active.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    [ProgramAnswer("zhiren-field-discard")]
    private void ResolveZhirenFieldChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The zhiren field choice lost its program frame.");
        var state = frame.PanShuZhirenField ??
            throw new InvalidOperationException("The zhiren field choice lost its stage state.");
        var delayedStage = selected.Parameters.GetValueOrDefault("stage") == "delayed";
        if (delayedStage != state.AwaitingDelayedTrick)
            throw new InvalidOperationException("The zhiren field choice does not match its stage.");
        ClearPendingDecision();
        var active = GetActiveProgramFrame(frame.Id);
        if (selected.Parameters.GetValueOrDefault("action") != "skip")
        {
            if (!int.TryParse(selected.Parameters.GetValueOrDefault("card-id"),
                    System.Globalization.CultureInfo.InvariantCulture, out var cardId))
                throw new InvalidOperationException("The zhiren field choice has no card.");
            var location = _cardZones.GetLocation(cardId);
            var expectedZone = delayedStage ? CardZoneKind.Judgment : CardZoneKind.Equipment;
            if (location.Zone != expectedZone || location.OwnerSeat is null)
                throw new InvalidOperationException("The zhiren field card left its field zone.");
            var card = _cardZones.CardsAt(location).Single(item => item.Id == cardId);
            active = AppendFrameNativeLogicalDiscard(active, active.OwnerSeat,
                [card.Id], new($"skill-program.{active.SkillId}.zhiren-field"));
            MoveCards([card], location, CardLocation.DiscardPile,
                new($"skill-program.{active.SkillId}.zhiren-field"),
                movement => RecordBianFuRenForeignDiscardProducer(movement,active.OwnerSeat,active.Id));
        }
        if (!delayedStage)
        {
            ContinueZhirenFieldAfterEquipment(active);
            return;
        }
        active = CompleteFrameNativeLogicalDiscard(GetActiveProgramFrame(active.Id));
        ReplaceRuntimeTop(active with { PanShuZhirenField = null });
        active = GetActiveProgramFrame(active.Id);
        var length = state.NameLength;
        if (length >= 3)
            ApplyProgramSkillRecovery(active.Id, active.OwnerSeat, active.OwnerSeat);
        if (length >= 4)
            DrawProgramCards(active.Id, active.OwnerSeat, 3, null, null,
                SkillProgramCardSetVisibility.Private,
                new($"skill-program.{active.SkillId}.zhiren-tiers"));
        AddLog("SkillTriggered",
            $"{_players[active.OwnerSeat].Name} 的【织纴】按牌名字数 {length} 结算。", active.OwnerSeat);
        AdvanceRuntimeProgram(active.Id);
    }
}
