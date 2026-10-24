namespace CardGame.Core;
public sealed partial class GameEngine
{
    private static CardConversionSource ZhangHengSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static CardMoveReason ZhangHengMoveReason => new("skill-program.anniversary:dangzai.move");
    private Card[] ZhangHengMovable(int owner, int donor, IReadOnlyList<int>? selected = null)
    {
        if (!_players[owner].IsAlive || !_players[donor].IsAlive || owner == donor) return [];
        var chosen = selected ?? [];
        var kinds = chosen.Select(id => GetJudgmentEffectiveCardKind(GetAdvancedCard(id))).ToHashSet();
        return GetJudgment(_players[donor]).Where(c => !chosen.Contains(c.Id) && !kinds.Contains(GetJudgmentEffectiveCardKind(c)) &&
            CanMoveProgramCardToCorrespondingZone(c, CardZoneKind.Judgment, owner) &&
            !IsForeignEquipmentDiscardPrevented(owner, c, CardLocation.Judgment(donor), OwnedCardMoveIntent.Transfer)).ToArray();
    }
    private IEnumerable<int> ZhangHengDonors(int owner) => _players.Where(p => p.IsAlive && p.Seat != owner && ZhangHengMovable(owner, p.Seat).Length > 0).Select(p => p.Seat);
    [ProgramCapability] private sealed class AnniversaryZhangHengHost(GameEngine e) : IAnniversaryZhangHengHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame f) => e.BeginZhangHengDangzai(f); }
    private SkillProgramStepOutcome BeginZhangHengDangzai(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.AnniversaryZhangHeng is not null || f.InstructionIndex != 1 || f.SkillId != AnniversaryZhangHengComposition.Dangzai || f.TriggerId != "take-delayed-subset" ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.PlayPhaseStarting } w || !ZhangHengDonors(f.OwnerSeat).Any())
            throw new InvalidOperationException("Dangzai requires the original Play-start boundary and another role's genuinely movable judgment entities.");
        var r = new AnniversaryZhangHengReceipt(ZhangHengSource(f), f.GameplayHash, w.ParentFrameId, _turnNumber, "donor");
        ReplaceRuntimeTop(f = f with { AnniversaryZhangHeng = r });
        AdvanceEventRulesAndQueueFact(new AnniversaryZhangHengStartedEvent(f.Id, r.Source, r.Hash, r.ParentId, r.Turn));
        PublishZhangHeng(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> ZhangHengChoices(ProgramSkillFrame f)
    {
        var r = f.AnniversaryZhangHeng!;
        PromptChoice C(string key, string label, IReadOnlyList<int> cards, IReadOnlyList<int> targets) => new(new($"zhang-heng.{f.Id}.{r.Stage}.{r.Cards.Count}.{key}"), label, cards, targets,
            new Dictionary<string, string> { ["program-action"] = "anniversary-zhang-heng", ["branch"] = key });
        if (r.Stage == "donor") return Array.AsReadOnly(ZhangHengDonors(f.OwnerSeat).Select(seat => C("donor", "移动" + _players[seat].Name + "的判定区牌", [], [seat]) with { Id = new($"zhang-heng.{f.Id}.donor.{seat}") }).ToArray());
        if (r.Stage != "cards" || !_players[r.Donor].IsAlive) return [];
        var choices = ZhangHengMovable(f.OwnerSeat, r.Donor, r.Cards).Select(card => C("card", "选择【" + CardCatalog.Get(GetJudgmentEffectiveCardKind(card)).DisplayName + "】", [card.Id], []) with { Id = new($"zhang-heng.{f.Id}.card.{card.Id}.{r.Cards.Count}") }).ToList();
        choices.Add(C("finish", r.Cards.Count == 0 ? "取消移动" : $"确认移动{r.Cards.Count}张牌", [], []));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishZhangHeng(ProgramSkillFrame f)
    {
        var choices = ZhangHengChoices(f);
        if (!_players[f.OwnerSeat].IsAlive || choices.Count == 0) { FinishZhangHeng(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, f.AnniversaryZhangHeng!.Stage == "donor" ? "挡灾：选择另一名角色的判定区。" : "挡灾：选择任意张可合法移入的判定区牌后确认。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("anniversary-zhang-heng")] private void AnswerZhangHeng(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryZhangHeng: { } r } f || !ValidZhangHeng(f) || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !ZhangHengChoices(f).Any(c => AssistedChoicesEqual([c], [selected]))) throw new InvalidOperationException("Dangzai rejects a changed original Play boundary, donor or physical judgment entity.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (r.Stage == "donor")
        {
            var donor = selected.Targets.Single(); ReplaceRuntimeTop(f = f with { AnniversaryZhangHeng = r with { Stage = "cards", Donor = donor } });
            AdvanceEventRulesAndQueueFact(new AnniversaryZhangHengDonorEvent(f.Id, donor)); PublishZhangHeng(f); return;
        }
        if (selected.Parameters["branch"] == "card")
        { ReplaceRuntimeTop(f = f with { AnniversaryZhangHeng = r with { Cards = r.Cards.Append(selected.Cards.Single()).ToArray() } }); PublishZhangHeng(f); return; }
        if (r.Cards.Count == 0) { FinishZhangHeng(f); return; }
        var cards = r.Cards.Select(GetAdvancedCard).ToArray();
        if (!_players[r.Donor].IsAlive || cards.Any(c => _cardZones.GetLocation(c.Id) != CardLocation.Judgment(r.Donor) ||
                !CanMoveProgramCardToCorrespondingZone(c, CardZoneKind.Judgment, f.OwnerSeat)) || cards.Select(GetJudgmentEffectiveCardKind).Distinct().Count() != cards.Length)
            throw new InvalidOperationException("Dangzai must revalidate the complete actual subset before its one original native transfer.");
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { AnniversaryZhangHeng = r with { Stage = "children", Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveCards(cards, CardLocation.Judgment(r.Donor), CardLocation.Judgment(f.OwnerSeat), ZhangHengMoveReason, disclosure: CardMovementDisclosure.FaceUp);
        var live = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(live with { AnniversaryZhangHeng = live.AnniversaryZhangHeng! with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new AnniversaryZhangHengMovedEvent(f.Id, r.Donor, cards.Length, before, _movementSequence));
        ContinueZhangHeng(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 49600)] private bool ContinueZhangHeng(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryZhangHeng: { } r } f || f.Id != id) return false;
        if (!ValidZhangHeng(f)) throw new InvalidOperationException("Dangzai lost its original selected real judgment transfer or paid observer return.");
        if (r.Stage is "donor" or "cards") { PublishZhangHeng(f); return true; }
        if (TryBeginAdvancedSkillsChanged(f.Id) || TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id)) return true;
        FinishZhangHeng(GetActiveProgramFrame(id)); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 49600)] private bool ReturnZhangHengMovement(ProgramSkillFrame f) => f.AnniversaryZhangHeng is not null && ContinueZhangHeng(f.Id);
    private void FinishZhangHeng(ProgramSkillFrame f)
    { AdvanceEventRulesAndQueueFact(new AnniversaryZhangHengCompletedEvent(f.Id)); ReplaceRuntimeTop(f = f with { AnniversaryZhangHeng = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true); }
    private bool HasAnniversaryZhangHengPrompt() => _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p &&
        _resolutionStack.LastOrDefault() is ProgramSkillFrame { AnniversaryZhangHeng.Stage: "donor" or "cards", Step: ResolutionFrameStep.AwaitingResponse } f &&
        ValidZhangHeng(f) && p.PlayerSeat == f.OwnerSeat && p.TargetSeat == f.OwnerSeat && AssistedChoicesEqual(p.Choices, ZhangHengChoices(f));
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryZhangHengDangzai)] private PromptChoice ChooseZhangHeng(PendingDecision p) => p.Choices.FirstOrDefault(c => c.Cards.Count == 1) ?? p.Choices.First();
}
