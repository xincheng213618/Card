namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ZenrunTakeReason = "skill-program.sun-hong.zenrun.obtain";
    private const string ZenrunCompensationReason = "skill-program.sun-hong.zenrun.compensation";
    private CardConversionSource ZenrunSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool HasPrintedGeneralSkillTargetBan(CardConversionSource source, int target) =>
        HasPrintedGeneralSkillTargetBan(source.OwnerSeat, source.SkillId, target, source.SkillInstanceId);
    private bool HasPrintedGeneralSkillTargetBan(int owner, string skillId, int target, string? skillInstanceId = null)
    {
        if (!IsValidPlayerSeat(owner) || !IsValidPlayerSeat(target) || !SunHongTargetBlocked(owner, target)) return false;
        // Inspect raw source identities, never the HP-conditional binding shard. All
        // native execution/disable eligibility remains with the admitting caller.
        return _players[owner].SkillGrants.Grants.Any(g => g.SkillId == skillId &&
            (skillInstanceId is null || g.SkillInstanceId == skillInstanceId) &&
            g.EquipmentSuitSource is null && !g.SourceId.StartsWith("equipment:", StringComparison.Ordinal));
    }
    private bool ZenrunCanTake(int owner, int source, CardZoneKind zone, Card card) =>
        !(zone == CardZoneKind.Equipment && card.IsGeneralWeapon) &&
        !IsForeignEquipmentDiscardPrevented(owner, card, new(zone, source), OwnedCardMoveIntent.Obtain);
    private bool ZenrunSourceLegal(int owner, int source, int count) => count > 0 && source != owner &&
        IsValidPlayerSeat(source) && _players[source].IsAlive && !HasPrintedGeneralSkillTargetBan(owner, "ol:zenrun", source) &&
        GetHand(_players[source]).Count(c => ZenrunCanTake(owner, source, CardZoneKind.Hand, c)) +
        GetEquipment(_players[source]).Count(c => ZenrunCanTake(owner, source, CardZoneKind.Equipment, c)) >= count;
    private bool ZenrunPhaseAvailable(int owner, ActualDiscardRecoveryPhaseKey? phase) => phase is not null &&
        !ProgramEventHistory<ZenrunPaidEvent>().Any(e => e.Source.OwnerSeat == owner && e.Source.SkillId == "ol:zenrun" && e.Phase == phase);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 27571)]
    private bool QualifyZenrun(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveZenrunNativeDrawReplacement)) return true;
        return candidate.SkillId == "ol:zenrun" && candidate.BindingId == "replace-native-draw" &&
            context.NativeDraw is { } draw && draw.RecipientSeat == candidate.OwnerSeat &&
            ExactNativeDrawAttempt(context) is { Replacement: null } && ZenrunPhaseAvailable(candidate.OwnerSeat, draw.ActualPhase) &&
            _players.Any(p => ZenrunSourceLegal(candidate.OwnerSeat, p.Seat, draw.RequestedCount));
    }
    [ProgramCapability]
    private sealed class SunHongZenrunCapability(GameEngine engine) : ISunHongZenrunHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f) => engine.BeginZenrun(f); }
    private SkillProgramStepOutcome BeginZenrun(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.Zenrun is not null || f.InstructionIndex != 1 || f.SkillId != "ol:zenrun" ||
            GetProgramBindingId(f) != "replace-native-draw" || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            f.WindowContext is not { NativeDraw: { } draw } context ||
            !IsExactNativeDrawCandidate(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash,
                GetProgramTrigger(f).Priority, context.OccurrenceIndex), context) || !ZenrunPhaseAvailable(f.OwnerSeat, draw.ActualPhase))
            throw new InvalidOperationException("Zenrun needs the genuine unconsumed native draw attempt in its actual phase.");
        var r = new ZenrunReceipt(ZenrunSource(f), f.GameplayHash, context.ParentFrameId, draw, ZenrunStage.Target) { SourceSeat = f.OwnerSeat };
        ReplaceRuntimeTop(f = f with { Zenrun = r });
        AdvanceEventRulesAndQueueFact(new ZenrunStartedEvent(f.Id, r.Source, r.GameplayHash, r.WindowId, draw));
        PublishZenrun(f); return SkillProgramStepOutcome.AwaitChild;
    }
    private IReadOnlyList<PromptChoice> ZenrunChoices(ProgramSkillFrame f)
    {
        var r = f.Zenrun!; var result = new List<PromptChoice>();
        PromptChoice C(string branch, string label, IReadOnlyList<int>? cards = null, int? target = null, CardZoneKind? zone = null, int? slot = null)
        {
            var parameters = new Dictionary<string, string> { ["program-action"] = "sun-hong-zenrun", ["branch"] = branch };
            if (zone is { } z) parameters["source-zone"] = z.ToString(); if (slot is { } index) parameters["slot-index"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return new(new($"zenrun.{f.Id}.{r.Stage}.{r.Revision}.{branch}.{target}.{zone}.{slot}"), label, cards ?? [], target is { } seat ? [seat] : [], parameters);
        }
        if (r.Stage == ZenrunStage.Target && _players[f.OwnerSeat].IsAlive && _winner == Winner.None)
            foreach (var player in _players.Where(p => ZenrunSourceLegal(f.OwnerSeat, p.Seat, r.Draw.RequestedCount))) result.Add(C("target", player.Name, target: player.Seat));
        if (r.Stage == ZenrunStage.Cards && _players[f.OwnerSeat].IsAlive && _players[r.SourceSeat].IsAlive && _winner == Winner.None)
        {
            foreach (var choice in BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, r.SourceSeat,
                [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Obtain,
                canSelect: (zone, card) => ZenrunCanTake(f.OwnerSeat, r.SourceSeat, zone, card)))
            {
                var zone = Enum.Parse<CardZoneKind>(choice.Parameters["source-zone"]); var slot = int.Parse(choice.Parameters["slot-index"]);
                var selected = r.Selected.Any(c => c.Zone == zone && c.Index == slot);
                if (selected || r.Selected.Count < r.Draw.RequestedCount)
                    result.Add(C(selected ? "unpick" : "pick", (selected ? "撤回：" : "选择获得：") + choice.Description, choice.Cards, zone: zone, slot: slot));
            }
            if (r.Selected.Count == r.Draw.RequestedCount) result.Add(C("take", $"以这{r.Selected.Count}张原牌替代摸牌"));
        }
        if (r.Stage == ZenrunStage.VictimChoice && _players[r.SourceSeat].IsAlive && _winner == Winner.None)
        {
            result.Add(C("draw", $"摸{r.Draw.RequestedCount}张牌"));
            result.Add(C("block", "令其不能再对你发动武将牌上的技能"));
        }
        return Array.AsReadOnly(result.ToArray());
    }
    private void PublishZenrun(ProgramSkillFrame f)
    {
        var r = f.Zenrun!; var choices = ZenrunChoices(f);
        if (choices.Count == 0) { FinishZenrun(f); return; }
        var chooser = r.Stage == ZenrunStage.VictimChoice ? r.SourceSeat : f.OwnerSeat;
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse }); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, r.Stage == ZenrunStage.VictimChoice ? "谮润：被获得牌者自行选择。" : "谮润：选择其他角色及其原区域牌。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), r.SourceSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("sun-hong-zenrun")]
    private void AnswerZenrun(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Zenrun: { } r } f ||
            _pendingDecision?.PlayerSeat != (r.Stage == ZenrunStage.VictimChoice ? r.SourceSeat : f.OwnerSeat) ||
            !ZenrunChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Zenrun lost the exact opaque chooser or original native choice.");
        AssertZenrun(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect }); var branch = choice.Parameters["branch"];
        if (r.Stage == ZenrunStage.Target)
        {
            var seat = choice.Targets.Single(); if (branch != "target" || !ZenrunSourceLegal(f.OwnerSeat, seat, r.Draw.RequestedCount)) throw new InvalidOperationException("Zenrun's source cannot pay the requested quantity.");
            ReplaceRuntimeTop(f = f with { Zenrun = r with { SourceSeat = seat, Stage = ZenrunStage.Cards } });
            AdvanceEventRulesAndQueueFact(new ZenrunSourceSelectedEvent(f.Id, seat)); PublishZenrun(f); return;
        }
        if (r.Stage == ZenrunStage.Cards)
        {
            if (branch is "pick" or "unpick")
            {
                var zone = Enum.Parse<CardZoneKind>(choice.Parameters["source-zone"]); var index = int.Parse(choice.Parameters["slot-index"]);
                var id = _cardZones.CardsAt(new(zone, r.SourceSeat))[index].Id;
                var selected = branch == "pick" ? r.Selected.Append(new ZenrunSlot(zone, index, id)).ToArray() : r.Selected.Where(s => s.Zone != zone || s.Index != index).ToArray();
                ReplaceRuntimeTop(f = f with { Zenrun = r with { Selected = selected, Revision = r.Revision + 1 } });
                AdvanceEventRulesAndQueueFact(new ZenrunSlotSelectedEvent(f.Id, r.Revision + 1, zone, index, branch == "pick")); PublishZenrun(f); return;
            }
            if (branch != "take" || !ZenrunPhaseAvailable(f.OwnerSeat, r.Draw.ActualPhase) || r.Selected.Count != r.Draw.RequestedCount ||
                !ZenrunSourceLegal(f.OwnerSeat, r.SourceSeat, r.Draw.RequestedCount) || r.Selected.Any(s =>
                    s.Index < 0 || s.Index >= _cardZones.CardsAt(new(s.Zone, r.SourceSeat)).Count ||
                    _cardZones.CardsAt(new(s.Zone, r.SourceSeat))[s.Index].Id != s.CardId ||
                    !ZenrunCanTake(f.OwnerSeat, r.SourceSeat, s.Zone, GetAdvancedCard(s.CardId))))
                throw new InvalidOperationException("Zenrun cannot change or repay its original equal-count HE slots.");
            var materials = r.Selected.Select(s => new ZenrunMaterial(s.CardId, GetAdvancedCard(s.CardId).Kind, new(s.Zone, r.SourceSeat))).ToArray();
            ReplaceRuntimeTop(f = f with { Zenrun = r = r with { Paid = true, Materials = materials, Stage = ZenrunStage.TakeChildren,
                Before = _movementSequence, After = _movementSequence, Step = r.Step + 1 }, PendingMovementContinuation = new(r.SourceSeat, 0, null) });
            AdvanceEventRulesAndQueueFact(new ZenrunPaidEvent(f.Id, r.Source, r.GameplayHash, r.Draw.AttemptFrameId,
                r.Draw.ActualPhase!, r.SourceSeat, r.Draw.RequestedCount));
            for (var i = 0; i < materials.Length; i++) AdvanceEventRulesAndQueueFact(new ZenrunMaterialCommittedEvent(f.Id, i, materials[i].CardId, materials[i].Kind, materials[i].From));
            foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
            var frameId = f.Id; var step = r.Step; var before = r.Before;
            MoveProgramCardsFromMultipleSources(materials.Select(m => m.CardId).ToArray(), CardLocation.Hand(f.OwnerSeat), new(ZenrunTakeReason),
                (batch, moves) => { foreach (var m in moves) AdvanceEventRulesAndQueueFact(new ZenrunEntityMovedEvent(frameId, step, batch, m.CardId, m.CardKind, m.From, m.To, m.Reason, m.Sequence)); });
            f = GetActiveProgramFrame(frameId); ReplaceRuntimeTop(f = f with { Zenrun = f.Zenrun! with { After = _movementSequence } });
            CompleteNativeDrawReplacement(f, r.SourceSeat, materials.Select(m => m.CardId).ToArray(), new(ZenrunTakeReason), before);
            AdvanceEventRulesAndQueueFact(new ZenrunMovementIssuedEvent(frameId, step, ZenrunStage.TakeChildren, before, _movementSequence, materials.Length));
            ContinueZenrun(frameId); return;
        }
        if (r.Stage != ZenrunStage.VictimChoice || branch is not ("draw" or "block")) throw new InvalidOperationException("Zenrun requires the real victim's compensation choice.");
        var option = branch == "draw" ? 1 : 2;
        ReplaceRuntimeTop(f = f with { Zenrun = r = r with { Choice = option } }); AdvanceEventRulesAndQueueFact(new ZenrunVictimChoiceEvent(f.Id, r.SourceSeat, option));
        if (option == 2)
        {
            AdvanceEventRulesAndQueueFact(new ZenrunTargetBlockedEvent(f.Id, r.Source, r.GameplayHash, f.OwnerSeat, r.SourceSeat)); FinishZenrun(f); return;
        }
        ReplaceRuntimeTop(f = f with { Zenrun = r with { Stage = ZenrunStage.CompensationRequest, Before = _movementSequence, After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new ZenrunCompensationRequestedEvent(f.Id, r.SourceSeat, r.Draw.RequestedCount, _movementSequence));
        BeginOperationProgramDraw(f, r.SourceSeat, r.Draw.RequestedCount, new(ZenrunCompensationReason));
    }
    private void ContinueZenrun(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Zenrun: { } r } f || f.Id != id) return;
        AssertZenrun(f);
        if (f.NativeDraw is not null || r.Stage == ZenrunStage.CompensationRequest) return;
        if (r.Stage is ZenrunStage.Target or ZenrunStage.Cards or ZenrunStage.VictimChoice) { if (_pendingDecision is null) PublishZenrun(f); return; }
        if (DrainXianbi(f)) return;
        f = GetActiveProgramFrame(id); r = f.Zenrun!; ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (r.Stage == ZenrunStage.TakeChildren && _players[r.SourceSeat].IsAlive && _winner == Winner.None)
        { ReplaceRuntimeTop(f = f with { Zenrun = r with { Stage = ZenrunStage.VictimChoice } }); PublishZenrun(f); return; }
        FinishZenrun(f);
    }
    private void ReturnZenrunCompensation(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.Zenrun is not { Paid: true, Stage: ZenrunStage.CompensationRequest, Choice: 1 } r || f.NativeDraw is not null ||
            _resolutionStack.LastOrDefault()?.Id != f.Id || outcome.RecipientSeat != r.SourceSeat || outcome.RequestedCount != r.Draw.RequestedCount ||
            outcome.SequenceBefore < r.Before || outcome.SequenceAfter < outcome.SequenceBefore || outcome.SequenceAfter > _movementSequence ||
            outcome.ActualDrawIds.Count > r.Draw.RequestedCount || outcome.ActualDrawIds.Distinct().Count() != outcome.ActualDrawIds.Count ||
            outcome.Replacement is not null && outcome.ActualDrawIds.Count != 0 ||
            ProgramEventHistory<ZenrunCompensationRequestedEvent>().Count(e => e.FrameId == f.Id && e.RecipientSeat == r.SourceSeat &&
                e.RequestedCount == r.Draw.RequestedCount && e.SequenceBefore == r.Before) != 1 ||
            ProgramEventHistory<ZenrunCompensationReturnedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Zenrun compensation lost its exact native draw request return.");
        if (outcome.AttemptFrameId is { } attempt && ProgramEventHistory<NativeDrawAttemptDecidedEvent>().Count(e =>
            e.AttemptFrameId == attempt && e.ParentFrameId == f.Id && e.RecipientSeat == r.SourceSeat &&
            e.RequestedCount == r.Draw.RequestedCount && e.Replaced == (outcome.Replacement is not null)) != 1)
            throw new InvalidOperationException("Zenrun compensation cannot borrow another native draw invoice.");
        ReplaceRuntimeTop(f = f with { Zenrun = r with { Stage = ZenrunStage.CompensationChildren, Step = r.Step + 1,
            Before = checked((int)outcome.SequenceBefore), After = checked((int)outcome.SequenceAfter) }, PendingMovementContinuation = new(r.SourceSeat, 0, null) });
        for (var i = 0; i < outcome.ActualDrawIds.Count; i++) AdvanceEventRulesAndQueueFact(new ZenrunCompensationEntityEvent(f.Id, i, outcome.ActualDrawIds[i]));
        AdvanceEventRulesAndQueueFact(new ZenrunCompensationReturnedEvent(f.Id, outcome.AttemptFrameId, outcome.SequenceBefore, outcome.SequenceAfter,
            outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count));
        AdvanceEventRulesAndQueueFact(new ZenrunMovementIssuedEvent(f.Id, f.Zenrun!.Step, ZenrunStage.CompensationChildren, f.Zenrun.Before, f.Zenrun.After, outcome.ActualDrawIds.Count));
        ContinueZenrun(f.Id);
    }
    private void FinishZenrun(ProgramSkillFrame f)
    {
        var r = f.Zenrun!; AdvanceEventRulesAndQueueFact(new ZenrunCompletedEvent(f.Id, r.Paid, r.Materials.Count, r.Choice));
        ReplaceRuntimeTop(f = f with { Zenrun = null, PendingMovementContinuation = null }); FinishProgramSkill(f, r.Paid);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 27571)]
    private bool ResumeZenrun(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Zenrun: not null, NativeDraw: null } f || f.Id != id) return false; ContinueZenrun(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 27571)]
    private bool ReturnZenrunMovement(ProgramSkillFrame f)
    { if (f.Zenrun is null || f.NativeDraw is not null) return false; ContinueZenrun(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 27571)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 27571)]
    private bool ContinuePaidZenrun(ProgramSkillFrame f) => f.Zenrun is { Paid: true } && ValidZenrun(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveZenrunNativeDrawReplacement)]
    private PromptChoice ChooseZenrunAi(PendingDecision prompt, ProgramSkillFrame f) =>
        prompt.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") is "draw" or "take") ??
        prompt.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "pick") ?? prompt.Choices[0];
}
