namespace CardGame.Core;
public sealed partial class GameEngine
{
    private static CardConversionSource AnniversaryBlySource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static CardLocation AnniversaryBlyLease(ProgramSkillFrame f) => new(CardZoneKind.PrivatePersistentPile, f.OwnerSeat, $"linghui:{f.Id}");
    private static CardMoveReason AnniversaryBlyReason(string step) => new("skill.anniversary.linghui." + step);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 43600, SkillProgramEffectOp.ResolveAnniversaryBaiLingYunLinghui)]
    private bool CanOfferAnniversaryBly(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w) =>
        c.OwnerSeat == _currentSeat || EventsSinceLastBoundary(e => e is TurnStartedEvent).OfType<PlayerDyingEvent>().Any();
    [ProgramCapability] private sealed class AnniversaryBlyHost(GameEngine engine) : IAnniversaryBaiLingYunHost
    { public SkillProgramStepOutcome Begin(ProgramSkillFrame frame) => engine.BeginAnniversaryBly(frame); }
    private SkillProgramStepOutcome BeginAnniversaryBly(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.AnniversaryBly is not null || f.InstructionIndex != 1 || f.SkillId != AnniversaryBaiLingYunComposition.Linghui || f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } w)
            throw new InvalidOperationException("Linghui requires its accepted original ending candidate.");
        var before = _movementSequence;
        var r = new AnniversaryBlyReceipt(AnniversaryBlySource(f), f.GameplayHash, w.ParentFrameId, _turnNumber, AnniversaryBlyStage.Viewing) { Before = before, After = before, ViewBefore = before, ViewAfter = before };
        ReplaceRuntimeTop(f = f with { AnniversaryBly = r, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        var lease = AnniversaryBlyLease(f); _cardZones.EnsurePrivatePersistentPile(lease);
        var ids = new List<int>();
        for (var i = 0; i < 3 && EnsureDrawPile(); i++)
        {
            var card = _cardZones.CardsAt(CardLocation.DrawPile)[^1]; ids.Add(card.Id);
            MoveCard(card, CardLocation.DrawPile, lease, AnniversaryBlyReason("view"), disclosure: CardMovementDisclosure.FaceDown);
        }
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { AnniversaryBly = r with { Viewed = ids, After = _movementSequence, ViewAfter = _movementSequence } });
        SetProgramCardSet(f.Id, "linghui-private", ids, SkillProgramCardSetVisibility.Private);
        AdvanceEventRulesAndQueueFact(new AnniversaryBlyStartedEvent(f.Id, r.Source, r.Hash, r.ParentId, r.Turn, ids.Count));
        ContinueAnniversaryBly(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private ProgramSkillFrame? AnniversaryBlyQuery(int actor, int? card = null) => _resolutionStack.LastOrDefault() is ProgramSkillFrame { AnniversaryBly: { Stage: AnniversaryBlyStage.Choice or AnniversaryBlyStage.UsePayment } r } f &&
        f.OwnerSeat == actor && (card is null || r.Viewed.Contains(card.Value)) && ValidAnniversaryBly(f) ? f : null;
    private bool AnniversaryBlyQueryableCard(ProgramSkillFrame f, int id) => f.AnniversaryBly!.Viewed.Contains(id) &&
        (_cardZones.GetLocation(id) == AnniversaryBlyLease(f) || f.AnniversaryBly is { Stage: AnniversaryBlyStage.UsePayment, Plan.CardId: var selected } && selected == id && _cardZones.GetLocation(id) == CardLocation.Processing);
    private bool IsAnniversaryBaiLingYunExactCardQuery(CharacterState actor, int? selectedPhysicalCardId) => selectedPhysicalCardId is { } id &&
        AnniversaryBlyQuery(actor.Seat, id) is { } frame && AnniversaryBlyQueryableCard(frame, id);
    private Card? FindAnniversaryBaiLingYunViewedCard(CharacterState actor, int id) => AnniversaryBlyQuery(actor.Seat, id) is { } f && AnniversaryBlyQueryableCard(f, id) ? GetAdvancedCard(id) : null;
    private IReadOnlyList<Card> AppendAnniversaryBaiLingYunPlayableCards(CharacterState actor, IReadOnlyList<Card> cards) => AnniversaryBlyQuery(actor.Seat) is { } f
        ? Array.AsReadOnly(cards.Concat(f.AnniversaryBly!.Viewed.Where(id => AnniversaryBlyQueryableCard(f, id)).Select(GetAdvancedCard)).DistinctBy(c => c.Id).ToArray()) : cards;
    private IReadOnlyList<LegalAction> AnniversaryBlyUseActions(ProgramSkillFrame f) => AnniversaryBlyQuery(f.OwnerSeat) is null || !_players[f.OwnerSeat].IsAlive ? [] :
        Array.AsReadOnly(f.AnniversaryBly!.Viewed.Where(id => AnniversaryBlyQueryableCard(f, id)).SelectMany(id => BuildLegalActions(_players[f.OwnerSeat], selectedPhysicalCardId: id, includeProgramActions: false)
            .Where(a => a.CardId == id && a.ConversionSource is null && a.AdditionalConversionSources is not { Count: > 0 } && a.Kind is not (LegalActionKind.Recast or LegalActionKind.EndPlay or LegalActionKind.UseProgramSkill or LegalActionKind.UseEquipmentEffect) &&
                ((a.PlayedCardKind ?? GetAdvancedCard(id).Kind) == GetAdvancedCard(id).Kind || GetAdvancedCard(id).Kind == CardKind.Slash && a.PlayedCardKind == CardKind.FireSlash && HasZhuqueFan(_players[f.OwnerSeat])))).ToArray());
    private IReadOnlyList<PromptChoice> AnniversaryBlyChoices(ProgramSkillFrame f) => Array.AsReadOnly(AnniversaryBlyUseActions(f).Select((a, i) => new PromptChoice(new($"linghui.{f.Id}.use.{i}"), a.Description,
        [a.CardId!.Value], a.TargetSeats, new Dictionary<string, string> { ["program-action"] = "anniversary-linghui", ["index"] = i.ToString(System.Globalization.CultureInfo.InvariantCulture) }))
        .Append(new(new($"linghui.{f.Id}.skip"), "不使用牌，继续随机获得一张", [], [], new Dictionary<string, string> { ["program-action"] = "anniversary-linghui", ["index"] = "skip" })).ToArray());
    private void PublishAnniversaryBly(ProgramSkillFrame f)
    {
        var choices = AnniversaryBlyChoices(f); ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "灵慧：仅你可见暂存的顶牌，选择真实牌及合法目标，或不使用。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices, SkillPrompt = new(f.SkillId, "灵慧", "灵慧", _contentRegistry.GetSkill(f.SkillId).Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryBaiLingYunLinghui)] private PromptChoice ChooseAnniversaryBly(PendingDecision p) => p.Choices.First();
    [ProgramAnswer("anniversary-linghui")] private void AnswerAnniversaryBly(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryBly.Stage: AnniversaryBlyStage.Choice } f || !AnniversaryBlyChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Linghui rejects a stale private entity or target choice.");
        RequireAnniversaryBly(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (choice.Parameters["index"] == "skip") { GainAndReturnAnniversaryBly(f); return; }
        var a = AnniversaryBlyUseActions(f)[int.Parse(choice.Parameters["index"], System.Globalization.CultureInfo.InvariantCulture)]; var r = f.AnniversaryBly!;
        var plan = new AnniversaryBlyPlan(a.CardId!.Value, a.PlayedCardKind ?? GetAdvancedCard(a.CardId.Value).Kind, a.Kind, a.TargetCardId, a.ProgramActivationId) { Targets = a.TargetSeats };
        ReplaceRuntimeTop(f = f with { AnniversaryBly = r with { Stage = AnniversaryBlyStage.UsePayment, Plan = plan, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveCard(GetAdvancedCard(plan.CardId), AnniversaryBlyLease(f), CardLocation.Processing, AnniversaryBlyReason("use"), disclosure: CardMovementDisclosure.FaceUp);
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { AnniversaryBly = GetActiveProgramFrame(f.Id).AnniversaryBly! with { After = _movementSequence } }); ContinueAnniversaryBly(f.Id);
    }
    private bool DrainAnniversaryBly(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 43600)] private bool ContinueAnniversaryBly(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryBly: { } r } f || f.Id != id) return false;
        RequireAnniversaryBly(f); if (r.Stage is AnniversaryBlyStage.IssuingUse or AnniversaryBlyStage.NativeUse) return true;
        if (DrainAnniversaryBly(f)) return true;
        f = GetActiveProgramFrame(id); r = f.AnniversaryBly!; ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (r.Stage == AnniversaryBlyStage.Viewing)
        { if (!_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) { GainAndReturnAnniversaryBly(f); return true; } ReplaceRuntimeTop(f = f with { AnniversaryBly = r with { Stage = AnniversaryBlyStage.Choice } }); PublishAnniversaryBly(f); return true; }
        if (r.Stage == AnniversaryBlyStage.UsePayment)
        {
            if (_players[f.OwnerSeat].IsAlive && r.Plan is { } p && AnniversaryBlyUseActions(f).Any(a => AnniversaryBlyActionMatches(a, p))) { IssueAnniversaryBlyUse(f); return true; }
            GainAndReturnAnniversaryBly(f); return true;
        }
        if (r.Stage == AnniversaryBlyStage.NativeTail) { GainAndReturnAnniversaryBly(f); return true; }
        if (r.Stage == AnniversaryBlyStage.GainChildren) { FinishProgramSkill(f, true); return true; }
        PublishAnniversaryBly(f); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 43600)] private bool ReturnAnniversaryBlyMovement(ProgramSkillFrame f) => f.AnniversaryBly is not null && ContinueAnniversaryBly(f.Id);
    private static bool AnniversaryBlyActionMatches(LegalAction a, AnniversaryBlyPlan p) => a.CardId == p.CardId && a.Kind == p.ActionKind && a.TargetCardId == p.TargetCardId && a.ProgramActivationId == p.Adjustment && a.TargetSeats.SequenceEqual(p.Targets);
    private void GainAndReturnAnniversaryBly(ProgramSkillFrame f)
    {
        var r = f.AnniversaryBly!; var lease = AnniversaryBlyLease(f); var available = r.Viewed.Where(id => _cardZones.GetLocation(id) == lease).ToArray();
        var gain = _players[f.OwnerSeat].IsAlive && _winner == Winner.None && available.Length > 0 ? available[_random.Next(available.Length)] : (int?)null;
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { AnniversaryBly = r with { Stage = AnniversaryBlyStage.GainChildren, GainId = gain, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (gain is { } chosen) MoveCard(GetAdvancedCard(chosen), lease, CardLocation.Hand(f.OwnerSeat), AnniversaryBlyReason("gain"), disclosure: CardMovementDisclosure.FaceDown);
        // The reserve is an actual private viewing area, so native draws during
        // the chosen use cannot consume the two remaining viewed entities.
        foreach (var id in r.Viewed.Reverse().Where(id => _cardZones.GetLocation(id) == lease)) MoveCard(GetAdvancedCard(id), lease, CardLocation.DrawPile, AnniversaryBlyReason("return"), disclosure: CardMovementDisclosure.FaceDown);
        if (r.Plan is { } unpaid && r.UseId == 0 && _cardZones.GetLocation(unpaid.CardId) == CardLocation.Processing)
            MoveCard(GetAdvancedCard(unpaid.CardId), CardLocation.Processing, CardLocation.DiscardPile, AnniversaryBlyReason("cancelled"), disclosure: CardMovementDisclosure.FaceUp);
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { AnniversaryBly = GetActiveProgramFrame(f.Id).AnniversaryBly! with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new AnniversaryBlyGainCompletedEvent(f.Id, gain is null ? 0 : 1, before, _movementSequence)); ContinueAnniversaryBly(f.Id);
    }
}
