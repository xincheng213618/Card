using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasMouLuXun => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.RecordMouLuXunQianxun);
    private static CardConversionSource MouLuXunSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private CardKind[] MouLuXunRecordedNames(int owner)
    {
        var result = new HashSet<CardKind>();
        foreach (var fact in CompleteProgramEventHistory())
        {
            if (fact is MouLuXunNameRecordedEvent recorded && recorded.OwnerSeat == owner) result.Add(recorded.Kind);
            if (fact is MouLuXunNameRemovedEvent removed && removed.OwnerSeat == owner) result.Remove(removed.Kind);
        }
        return result.Order().ToArray();
    }
    private int MouLuXunLostCount(int owner, int turn) => _cardMovements.Count(m => m.TurnNumber == turn &&
        m.From.OwnerSeat == owner && m.From != m.To && m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment);

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 32800,
        SkillProgramEffectOp.RecordMouLuXunQianxun, SkillProgramEffectOp.UseMouLuXunRecordedTrick, SkillProgramEffectOp.DistributeMouLuXunLianying)]
    private bool CanOfferMouLuXun(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (!_players[candidate.OwnerSeat].IsAlive || _winner != Winner.None) return false;
        if (trigger.Id == "record-and-hold")
            return context.Window == SkillProgramTriggerWindow.CardEffectBeforeApply && context.CardUse is { } card &&
                CardCatalog.Get(card.EffectiveKind).CategoryName == "锦囊牌" &&
                _resolutionStack.OfType<CardEffectBeforeApplyFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is { } window &&
                !MouLuXunRecordedNames(candidate.OwnerSeat).Contains(card.EffectiveKind) &&
                MouLuXunEffectUser(window) != candidate.OwnerSeat;
        if (trigger.Id == "use-recorded-trick")
            return context.Window == SkillProgramTriggerWindow.PlayPhaseStarting && context.SourceSeat == candidate.OwnerSeat &&
                MouLuXunRecordedNames(candidate.OwnerSeat).Length > 0;
        return context.Window == SkillProgramTriggerWindow.AfterTurnEnded && context.SourceSeat != candidate.OwnerSeat &&
            MouLuXunLostCount(candidate.OwnerSeat, _turnNumber) > 0;
    }
    private int? MouLuXunEffectUser(CardEffectBeforeApplyFrame window)
    {
        if (window.DelayedReturn is not { } delayed) return window.Action.ActorSeat;
        var declared = ProgramEventHistory<CardUseDeclaredEvent>().LastOrDefault(e => e.CardId == delayed.CardId);
        if (declared is null) return null; // Native placement without a Use has no invented actor.
        return ProgramEventHistory<ProgramCardUseActorReplacedEvent>().LastOrDefault(e => e.CardUseFrameId == declared.ResolutionId)?.ActorSeat ?? declared.SourceSeat;
    }
    private bool MouLuXunParentMatches(ProgramSkillFrame f)
    {
        var index = _resolutionStack.FindIndex(frame => frame.Id == f.Id);
        if (index < 1 || f.WindowContext is not { } context) return false;
        var parent = _resolutionStack[index - 1];
        if (parent.Id != context.ParentFrameId) return false;
        if (parent is CardEffectBeforeApplyFrame effect)
            return effect.CandidateIndex < effect.Candidates.Count && effect.Candidates[effect.CandidateIndex] is { } c &&
                c.FrozenContext == context && MountObserverCandidateMatches(f, ToSharedCandidate(c));
        if (parent is PlayPhaseStartingBoundaryFrame starting)
            return starting.OwnerSeat == f.OwnerSeat && starting.ItemIndex < starting.Items.Count &&
                starting.Items[starting.ItemIndex].Candidate is { } c && MountObserverCandidateMatches(f, c);
        return parent is DeferredTurnEndFrame ending && MatchesAfterTurnEndedChild(ending, f, context);
    }
    private bool ValidMouLuXun(ProgramSkillFrame f) => f.MouLuXun is { } r && !string.IsNullOrWhiteSpace(r.GrantSourceId) && r.Source == MouLuXunSource(f) &&
        r.GameplayHash == f.GameplayHash && _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash == f.GameplayHash &&
        f.WindowContext?.ParentFrameId == r.WindowId && r.ActualTurn == _turnNumber && r.TurnOwner == _currentSeat &&
        MouLuXunParentMatches(f) && f.TriggerId == f.ActivationId && f.InstructionIndex == 1 && f.SelectedCardIds.Count == 0 && f.SelectedTargetSeats.Count == 0 &&
        ProgramEventHistory<MouLuXunStartedEvent>().Count(e => e == new MouLuXunStartedEvent(f.Id, r.Source, r.GameplayHash,
            r.Operation, r.WindowId, r.ActualTurn, r.TurnOwner, r.Limit, r.GrantSourceId)) == 1;

    private SkillProgramStepOutcome BeginMouLuXun(ProgramSkillFrame input, SkillProgramEffectOp operation)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.MouLuXun is not null || f.InstructionIndex != 1 || f.TriggerId is null || f.WindowContext is not { } c ||
            !CanOfferMouLuXun(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, 0), GetProgramTrigger(f), c))
            throw new InvalidOperationException("Mobile Lu Xun requires its actual unpaid effect, Play-start or other-turn ending candidate.");
        var hold = operation == SkillProgramEffectOp.RecordMouLuXunQianxun;
        var distribute = operation == SkillProgramEffectOp.DistributeMouLuXunLianying;
        var limit = hold ? Math.Min(5, MouLuXunRecordedNames(f.OwnerSeat).Length + 1) : distribute ? Math.Min(5, MouLuXunLostCount(f.OwnerSeat, _turnNumber)) : 0;
        var r = new MouLuXunReceipt(MouLuXunSource(f), f.GameplayHash, operation, c.ParentFrameId, _turnNumber, _currentSeat,
            hold ? MouLuXunStage.HoldChoice : distribute ? MouLuXunStage.ViewChildren : MouLuXunStage.NameChoice, limit)
        {
            GrantSourceId = _players[f.OwnerSeat].SkillGrants.Grants.Single(g => g.SkillId == f.SkillId && g.SkillInstanceId == f.SkillInstanceId).SourceId
        };
        ReplaceRuntimeTop(f = f with { MouLuXun = r });
        AdvanceEventRulesAndQueueFact(new MouLuXunStartedEvent(f.Id, r.Source, r.GameplayHash, operation, r.WindowId, r.ActualTurn, r.TurnOwner, limit, r.GrantSourceId));
        if (hold)
        {
            var kind = c.CardUse!.EffectiveKind;
            AdvanceEventRulesAndQueueFact(new MouLuXunNameRecordedEvent(f.Id, f.OwnerSeat, kind, c.ParentFrameId, c.CardUse.CardActionId, _turnNumber, MouLuXunRecordedNames(f.OwnerSeat).Length + 1));
            ReplaceRuntimeTop(f = f with { MouLuXun = r with { Paid = true, Kind = kind } });
        }
        if (distribute) { BeginMouLuXunView(f); return SkillProgramStepOutcome.AwaitChild; }
        PublishMouLuXun(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> MouLuXunChoices(ProgramSkillFrame f)
    {
        var r = f.MouLuXun!; var choices = new List<PromptChoice>();
        void Add(string verb, string text, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null, string? value = null) =>
            choices.Add(new(new($"mou-lu-xun.{f.Id}.{r.Stage}.{r.Cursor}.{verb}.{value}.{string.Join('.', cards ?? [])}.{string.Join('.', targets ?? [])}"), text, cards ?? [], targets ?? [],
                new Dictionary<string, string> { ["program-action"] = "mou-lu-xun", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["verb"] = verb, ["value"] = value ?? "" }));
        if (r.Stage == MouLuXunStage.HoldChoice)
        {
            foreach (var card in GetHand(_players[f.OwnerSeat]).Concat(GetEquipment(_players[f.OwnerSeat])))
                if (r.Selected.Contains(card.Id) || r.Selected.Count < r.Limit)
                    Add("toggle", (r.Selected.Contains(card.Id) ? "取消" : "暂存") + card.DisplayName, [card.Id]);
            Add("confirm", r.Selected.Count == 0 ? "不暂存牌" : "确认暂存");
        }
        else if (r.Stage == MouLuXunStage.NameChoice)
            foreach (var kind in MouLuXunRecordedNames(f.OwnerSeat)) Add("name", "移去记录【" + CardCatalog.Get(kind).DisplayName + "】", value: kind.ToString());
        else if (r.Stage == MouLuXunStage.UseChoice)
        {
            var options = MouLuXunUseOptions(f);
            for (var i = 0; i < options.Count; i++) Add("use", options[i].Description, targets: options[i].TargetSeats, value: i.ToString(CultureInfo.InvariantCulture));
            Add("decline", "不使用此牌");
        }
        else if (r.Stage == MouLuXunStage.Distribution && r.Cursor < r.Viewed.Count)
        {
            var cardId = r.Viewed[r.Cursor];
            foreach (var player in _players.Where(p => p.IsAlive)) Add("give", "将此牌交给" + player.Name, [cardId], [player.Seat]);
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishMouLuXun(ProgramSkillFrame f)
    {
        var choices = MouLuXunChoices(f);
        if (choices.Count == 0) { FinishMouLuXun(f); return; }
        PublishParticipantHandChoice(f, f.OwnerSeat, choices, "请选择【" + _contentRegistry.GetSkill(f.SkillId).Name + "】的合法选项。", f.OwnerSeat);
    }
    [ProgramAnswer("mou-lu-xun")]
    private void AnswerMouLuXun(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouLuXun: { } r } f || !ValidMouLuXun(f) ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual([choice], MouLuXunChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Lu Xun accepts only its exact current physical entities, record and ordered native target plan.");
        ClearPendingDecision(); var verb = choice.Parameters["verb"];
        if (r.Stage == MouLuXunStage.HoldChoice)
        {
            if (verb == "toggle")
            {
                var id = choice.Cards.Single();
                ReplaceRuntimeTop(f = f with { MouLuXun = r with { Selected = r.Selected.Contains(id) ? r.Selected.Where(c => c != id).ToArray() : r.Selected.Append(id).ToArray() } });
                PublishMouLuXun(f); return;
            }
            if (r.Selected.Count == 0) { FinishMouLuXun(f); return; }
            PayMouLuXunHold(f); return;
        }
        if (r.Stage == MouLuXunStage.NameChoice)
        {
            var kind = Enum.Parse<CardKind>(choice.Parameters["value"]);
            AdvanceEventRulesAndQueueFact(new MouLuXunNameRemovedEvent(f.Id, f.OwnerSeat, kind, r.ActualTurn));
            ReplaceRuntimeTop(f = f with { MouLuXun = r with { Kind = kind, Paid = true, Stage = MouLuXunStage.UseChoice } });
            if (!IsOrdinaryTrick(kind) || MouLuXunUseOptions(f).Count == 0) { FinishMouLuXun(f); return; }
            PublishMouLuXun(f); return;
        }
        if (r.Stage == MouLuXunStage.UseChoice)
        {
            if (verb == "decline") { FinishMouLuXun(f); return; }
            IssueMouLuXunUse(f, int.Parse(choice.Parameters["value"], CultureInfo.InvariantCulture)); return;
        }
        if (r.Stage == MouLuXunStage.Distribution) { PayMouLuXunGift(f, choice.Targets.Single()); return; }
        throw new InvalidOperationException("Lu Xun's native child cannot accept another selection.");
    }
    private bool DrainMouLuXun(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void FinishMouLuXun(ProgramSkillFrame f)
    {
        AdvanceEventRulesAndQueueFact(new MouLuXunCompletedEvent(f.Id));
        ReplaceRuntimeTop(f = f with { MouLuXun = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramCapability]
    private sealed class MouLuXunCapability(GameEngine g) : IMouLuXunHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp operation) => g.BeginMouLuXun(f, operation); }
}
