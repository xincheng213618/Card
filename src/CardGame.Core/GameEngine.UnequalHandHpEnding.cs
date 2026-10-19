using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramCapability]
    private sealed class TargetedTrickAndHandHpCapability(GameEngine engine) : ITargetedTrickAndHandHpHost
    {
        public SkillProgramStepOutcome ResolveUnequalHandHpEnding(ProgramSkillFrame frame) => engine.BeginUnequalHandHpEnding(frame);
        public SkillProgramStepOutcome AdjustTargetedOrdinaryTrick(ProgramSkillFrame frame) => engine.BeginTargetedOrdinaryTrick(frame);
    }
    private CardConversionSource TargetedTrickHandHpSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private int[] UnequalHandHpTargets(int owner) => _players.Where(p => p.IsAlive && GetHand(p).Count != p.Hp).Select(p => p.Seat).ToArray();
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeCondition, 100)]
    private bool CanOfferUnequalHandHp(SkillProgramTrigger trigger, ProgramSkillWindowContext context, int owner) =>
        !trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveUnequalHandHpEnding) ||
        context.Window == SkillProgramTriggerWindow.PlayEnding && owner == _currentSeat && _players[owner].IsAlive && UnequalHandHpTargets(owner).Length > 0;
    private static string UnequalHandHpReason(ProgramSkillFrame f, string step) => $"skill-program.{f.SkillId}.unequal-hand-hp.{step}";
    private SkillProgramStepOutcome BeginUnequalHandHpEnding(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!IsExactOwnPlayEndingProgram(f) || f.InstructionIndex != 1 || f.UnequalHandHp is not null)
            throw new InvalidOperationException("An unequal hand/HP choice requires its exact own Play ending.");
        var r = new UnequalHandHpReceipt { WindowFrameId = f.WindowContext!.ParentFrameId, Source = TargetedTrickHandHpSource(f),
            GameplayHash = f.GameplayHash, InstructionIndex = f.InstructionIndex, Stage = UnequalHandHpStage.Target, Targets = UnequalHandHpTargets(f.OwnerSeat) };
        ReplaceRuntimeTop(f = f with { UnequalHandHp = r });
        AdvanceEventRulesAndQueueFact(new UnequalHandHpIssuedEvent(f.Id, r.WindowFrameId, r.Source, r.GameplayHash, r.InstructionIndex));
        PublishUnequalHandHpChoice(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> UnequalHandHpChoices(ProgramSkillFrame f)
    {
        var r = f.UnequalHandHp!;
        Dictionary<string, string> Parameters(string branch) => new() { ["program-action"] = "unequal-hand-hp-ending", ["branch"] = branch,
            ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) };
        PromptChoice Choice(string branch, string text, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) =>
            new(new($"unequal-hand-hp.{f.Id}.{r.Stage}.{branch}.{string.Join('.', cards ?? [])}.{string.Join('.', targets ?? [])}"), text, cards ?? [], targets ?? [], Parameters(branch));
        var choices = new List<PromptChoice>();
        switch (r.Stage)
        {
            case UnequalHandHpStage.Target:
                choices.AddRange(r.Targets.Where(s => _players[s].IsAlive && GetHand(_players[s]).Count != _players[s].Hp)
                    .Select(s => Choice("target", _players[s].Name, targets: [s]))); break;
            case UnequalHandHpStage.Action:
                if (GetHand(_players[r.TargetSeat]).Count > 0) choices.Add(Choice("discard", "令其弃置一张手牌"));
                choices.Add(Choice("draw", "令其摸一张牌")); break;
            case UnequalHandHpStage.Discard:
                choices.AddRange(GetHand(_players[r.TargetSeat]).Select(c => Choice("discard-card", $"弃置【{c.DisplayName}】", [c.Id]))); break;
            case UnequalHandHpStage.Gift:
                if (r.TargetSeat != f.OwnerSeat && _players[r.TargetSeat].IsAlive)
                    choices.AddRange(GetHand(_players[f.OwnerSeat]).Concat(GetEquipment(f.OwnerSeat))
                        .Where(c => !IsActiveProgramSourceEquipmentCard(f.OwnerSeat, f.SkillId, f.SkillInstanceId, c))
                        .Select(c => Choice("give-card", $"交给 {_players[r.TargetSeat].Name}【{c.DisplayName}】", [c.Id], [r.TargetSeat])));
                break;
        }
        if (r.Stage is UnequalHandHpStage.Target or UnequalHandHpStage.Gift) choices.Add(Choice("decline", "不执行"));
        return Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray());
    }
    private void PublishUnequalHandHpChoice(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var r = f.UnequalHandHp!;
        var choices = UnequalHandHpChoices(f); var chooser = r.Stage == UnequalHandHpStage.Discard ? r.TargetSeat : f.OwnerSeat;
        var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { UnequalHandHp = r with { Choices = choices } });
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, $"【{skill.Name}】请选择。", choices.SelectMany(c => c.Cards).Distinct().ToArray(),
            choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = r.Stage is UnequalHandHpStage.Discard or UnequalHandHpStage.Gift,
            Choices = choices, TargetSeat = r.TargetSeat >= 0 ? r.TargetSeat : null, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("unequal-hand-hp-ending")]
    private void ResolveUnequalHandHpChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("No owning unequal hand/HP frame.");
        RequireUnequalHandHpReceipt(f);
        var r = f.UnequalHandHp!;
        if (!AssistedChoicesEqual([choice], r.Choices.Where(c => c.Id == choice.Id).ToArray()) ||
            !AssistedChoicesEqual([choice], UnequalHandHpChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("The exact hand/HP participant or payment changed.");
        var branch = choice.Parameters["branch"]; ClearPendingDecision();
        if (branch == "decline") { CompleteUnequalHandHp(f); return; }
        if (r.Stage == UnequalHandHpStage.Target)
        {
            var target = choice.Targets.Single();
            r = r with { TargetSeat = target, FrozenHandCount = GetHand(_players[target]).Count, FrozenHp = _players[target].Hp,
                Stage = UnequalHandHpStage.Action, Choices = [] };
            ReplaceRuntimeTop(f = f with { UnequalHandHp = r });
            AdvanceEventRulesAndQueueFact(new UnequalHandHpTargetSelectedEvent(f.Id, target, r.FrozenHandCount, r.FrozenHp));
            PublishUnequalHandHpChoice(f); return;
        }
        if (r.Stage == UnequalHandHpStage.Action)
        {
            var draw = branch == "draw";
            ReplaceRuntimeTop(f = f with { UnequalHandHp = r with { Draw = draw, Choices = [] } });
            AdvanceEventRulesAndQueueFact(new UnequalHandHpActionSelectedEvent(f.Id, draw));
            if (draw) IssueUnequalHandHpMovement(f, "adjust-draw", r.TargetSeat, null, CardLocation.Hand(r.TargetSeat), UnequalHandHpStage.AdjustmentChildren);
            else { ReplaceRuntimeTop(f = f with { UnequalHandHp = f.UnequalHandHp! with { Stage = UnequalHandHpStage.Discard } }); PublishUnequalHandHpChoice(f); }
            return;
        }
        if (r.Stage == UnequalHandHpStage.Discard)
        { IssueUnequalHandHpMovement(f, "adjust-discard", r.TargetSeat, choice.Cards.Single(), CardLocation.DiscardPile, UnequalHandHpStage.AdjustmentChildren); return; }
        if (r.Stage == UnequalHandHpStage.Gift)
        { IssueUnequalHandHpMovement(f, "gift", r.TargetSeat, choice.Cards.Single(), CardLocation.Hand(r.TargetSeat), UnequalHandHpStage.GiftChildren); return; }
        throw new InvalidOperationException("A hand/HP choice appeared after its movement was already issued.");
    }
    private void IssueUnequalHandHpMovement(ProgramSkillFrame supplied, string step, int subject, int? cardId,
        CardLocation to, UnequalHandHpStage stage)
    {
        var f = GetActiveProgramFrame(supplied.Id); var before = _movementSequence;
        var from = cardId is { } id ? _cardZones.GetLocation(id) : (CardLocation?)null;
        ReplaceRuntimeTop(f = f with { UnequalHandHp = f.UnequalHandHp! with { Stage = stage, Choices = [] }, PendingMovementContinuation = new(subject, 0, null) });
        var reason = new CardMoveReason(UnequalHandHpReason(f, step));
        var logicalDiscard = cardId is { } cost && to == CardLocation.DiscardPile
            ? CaptureNativeLogicalDiscardInvocation(f.OwnerSeat, f.OwnerSeat, [cost], reason, f.Id) : null;
        var count = cardId is null ? DrawCards(_players[subject], 1, true, reason).Count : 1;
        if (cardId is { } paid) MoveProgramCardsFromMultipleSources([paid], to, reason);
        CompleteNativeLogicalDiscardInvocation(logicalDiscard);
        var payment = new UnequalHandHpPayment(step, subject, before, _movementSequence, count, cardId, from, to);
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f = f with { UnequalHandHp = f.UnequalHandHp! with { Payments = f.UnequalHandHp.Payments.Append(payment).ToArray() } });
        AdvanceEventRulesAndQueueFact(new UnequalHandHpMovementIssuedEvent(f.Id, payment));
        if (!DrainUnequalHandHpChildren(f)) ReturnUnequalHandHpMovement(f);
    }
    private bool DrainUnequalHandHpChildren(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void CompleteUnequalHandHp(ProgramSkillFrame f)
    {
        AdvanceEventRulesAndQueueFact(new UnequalHandHpCompletedEvent(f.Id, f.UnequalHandHp!.TargetSeat));
        ReplaceRuntimeTop(f = f with { UnequalHandHp = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 20400)]
    private bool ReturnUnequalHandHpMovement(ProgramSkillFrame f)
    {
        if (f.UnequalHandHp is null) return false;
        RequireUnequalHandHpReceipt(f);
        if (f.PendingMovementContinuation is null) throw new InvalidOperationException("An issued hand/HP move lost its typed return.");
        if (DrainUnequalHandHpChildren(f)) return true;
        var r = f.UnequalHandHp!; ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive) { CompleteUnequalHandHp(f); return true; }
        if (r.Stage == UnequalHandHpStage.AdjustmentChildren)
        {
            var target = _players[r.TargetSeat]; var hand = GetHand(target).Count;
            var qualified = target.IsAlive && hand == target.Hp;
            ReplaceRuntimeTop(f = f with { UnequalHandHp = r with { RewardQualified = qualified } });
            AdvanceEventRulesAndQueueFact(new UnequalHandHpRewardQualifiedEvent(f.Id, target.Seat, hand, target.Hp, qualified));
            if (!qualified) { CompleteUnequalHandHp(f); return true; }
            IssueUnequalHandHpMovement(f, "reward", f.OwnerSeat, null, CardLocation.Hand(f.OwnerSeat), UnequalHandHpStage.RewardChildren); return true;
        }
        if (r.Stage == UnequalHandHpStage.RewardChildren)
        {
            if (!_players[r.TargetSeat].IsAlive || r.TargetSeat == f.OwnerSeat) { CompleteUnequalHandHp(f); return true; }
            ReplaceRuntimeTop(f = f with { UnequalHandHp = r with { Stage = UnequalHandHpStage.Gift } }); PublishUnequalHandHpChoice(f); return true;
        }
        if (r.Stage != UnequalHandHpStage.GiftChildren) throw new InvalidOperationException("The hand/HP movement returned to an unissued stage.");
        CompleteUnequalHandHp(f); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 20400)]
    private bool ResumeUnequalHandHp(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { UnequalHandHp: not null } f || f.Id != id) return false;
        RequireUnequalHandHpReceipt(f);
        if (f.PendingMovementContinuation is not null && !DrainUnequalHandHpChildren(f)) ReturnUnequalHandHpMovement(f);
        return true;
    }
    private void RequireUnequalHandHpReceipt(ProgramSkillFrame f)
    {
        if (f.UnequalHandHp is not { } r || f.TriggerId is null || r.Source != TargetedTrickHandHpSource(f) || r.GameplayHash != f.GameplayHash ||
            r.InstructionIndex != 1 || f.InstructionIndex != 1 || f.WindowContext is not { Window: SkillProgramTriggerWindow.PlayEnding } context ||
            r.WindowFrameId != context.ParentFrameId || !IsExactOwnPlayEndingProgram(f) ||
            _contentRegistry.GetSkill(f.SkillId).Program is not { } program || program.GameplayHash != f.GameplayHash ||
            GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.ResolveUnequalHandHpEnding ||
            CompleteProgramEventHistory().OfType<UnequalHandHpIssuedEvent>().Count(e => e.ProgramFrameId == f.Id && e.WindowFrameId == r.WindowFrameId &&
                e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.InstructionIndex == 1) != 1 || r.Payments.Count > 3 ||
            r.Stage != UnequalHandHpStage.Target && (!IsValidPlayerSeat(r.TargetSeat) || r.FrozenHandCount == r.FrozenHp))
            throw new InvalidOperationException("The hand/HP receipt lost its exact source, preparation parent or instruction.");
        var expectedCount = r.Stage switch
        {
            UnequalHandHpStage.Target or UnequalHandHpStage.Action or UnequalHandHpStage.Discard => 0,
            UnequalHandHpStage.AdjustmentChildren => 1,
            UnequalHandHpStage.RewardChildren or UnequalHandHpStage.Gift => 2,
            UnequalHandHpStage.GiftChildren => 3,
            _ => -1
        };
        var targets = CompleteProgramEventHistory().OfType<UnequalHandHpTargetSelectedEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray();
        var actions = CompleteProgramEventHistory().OfType<UnequalHandHpActionSelectedEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray();
        var rewards = CompleteProgramEventHistory().OfType<UnequalHandHpRewardQualifiedEvent>().Where(e => e.ProgramFrameId == f.Id).ToArray();
        if (r.Payments.Count != expectedCount || r.Targets.Count != r.Targets.Distinct().Count() || r.Targets.Any(s => !IsValidPlayerSeat(s)) ||
            (r.Stage == UnequalHandHpStage.Target
                ? r.TargetSeat != -1 || targets.Length != 0
                : targets is not [var selected] || selected.TargetSeat != r.TargetSeat || selected.HandCount != r.FrozenHandCount ||
                  selected.Hp != r.FrozenHp || !r.Targets.Contains(r.TargetSeat)) ||
            (r.Stage is UnequalHandHpStage.Target or UnequalHandHpStage.Action
                ? r.Draw is not null || actions.Length != 0
                : r.Draw is not { } draw || actions is not [var action] || action.Draw != draw || r.Stage == UnequalHandHpStage.Discard && draw) ||
            (expectedCount < 2 ? r.RewardQualified is not null || rewards.Length != 0
                : r.RewardQualified != true || rewards is not [var reward] || !reward.Qualified || reward.TargetSeat != r.TargetSeat || reward.HandCount != reward.Hp))
            throw new InvalidOperationException("The hand/HP receipt lost its exact participant, choice or paid stage prefix.");
        for (var index = 0; index < r.Payments.Count; index++)
        {
            var p = r.Payments[index];
            var expectedStep = index == 0 ? r.Draw == true ? "adjust-draw" : "adjust-discard" : index == 1 ? "reward" : "gift";
            var expectedSubject = index == 1 ? f.OwnerSeat : r.TargetSeat;
            var expectedTo = expectedStep == "adjust-discard" ? CardLocation.DiscardPile : CardLocation.Hand(expectedSubject);
            var actual = _cardMovements.Where(m => m.Sequence > p.Before && m.Sequence <= p.After && m.Reason.Value == UnequalHandHpReason(f, p.Step)).ToArray();
            var fact = CompleteProgramEventHistory().OfType<UnequalHandHpMovementIssuedEvent>().Where(e => e.ProgramFrameId == f.Id && e.Payment.Step == p.Step).ToArray();
            if (p.Step != expectedStep || p.SubjectSeat != expectedSubject || p.To != expectedTo || p.Before < 0 || p.After < p.Before || p.After > _movementSequence ||
                index > 0 && p.Before < r.Payments[index - 1].After ||
                (expectedStep is "reward" or "adjust-draw" ? p.CardId is not null || p.From is not null
                    : p.CardId is null || expectedStep == "adjust-discard" && p.From != CardLocation.Hand(r.TargetSeat) ||
                      expectedStep == "gift" && p.From != CardLocation.Hand(f.OwnerSeat) && p.From != CardLocation.Equipment(f.OwnerSeat)) ||
                fact is not [var issued] || issued.Payment != p || p.Count is < 0 or > 1 || actual.Length != p.Count ||
                (p.CardId is { } paid ? p.Count != 1 || p.From is null || actual[0].CardId != paid || actual[0].From != p.From ||
                    actual[0].To != p.To && !(actual[0].To == CardLocation.OutsideGame && (GetAdvancedCard(paid).IsGeneralWeapon || IsDiouCarrierDestroyedMaterial(actual[0])))
                    : actual.Any(m => m.From != CardLocation.DrawPile || m.To != CardLocation.Hand(p.SubjectSeat))))
                throw new InvalidOperationException("The hand/HP receipt lost its exact once-only actual movement producer.");
        }
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 50)]
    private void AssertRegisteredUnequalHandHpReceipt(ProgramSkillFrame frame)
    {
        if (frame.UnequalHandHp is not null) RequireUnequalHandHpReceipt(frame);
    }
    private bool IsUnequalHandHpMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        f.UnequalHandHp is { Payments.Count: > 0 } r && effect?.Op == SkillProgramEffectOp.ResolveUnequalHandHpEnding &&
        pending.SubjectSeat == r.Payments[^1].SubjectSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null;
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 470)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 480)]
    private bool CanContinueUnequalHandHp(ProgramSkillFrame f)
    {
        if (f.UnequalHandHp is not { Payments.Count: > 0 }) return false;
        RequireUnequalHandHpReceipt(f); return true;
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveUnequalHandHpEnding)]
    private PromptChoice SelectAiUnequalHandHp(PendingDecision prompt, ProgramSkillFrame f)
    {
        var r = f.UnequalHandHp!;
        if (r.Stage == UnequalHandHpStage.Target) return prompt.Choices.OrderByDescending(c => c.Targets.Count == 0 ? 0d :
            Math.Max(UnequalHandHpPublicChoiceScore(f.OwnerSeat, c.Targets[0], true), UnequalHandHpPublicChoiceScore(f.OwnerSeat, c.Targets[0], false))).First();
        if (r.Stage == UnequalHandHpStage.Action)
        {
            var desired = UnequalHandHpPublicChoiceScore(f.OwnerSeat, r.TargetSeat, true) >= UnequalHandHpPublicChoiceScore(f.OwnerSeat, r.TargetSeat, false) ? "draw" : "discard";
            return prompt.Choices.FirstOrDefault(c => c.Parameters["branch"] == desired) ?? prompt.Choices.First();
        }
        if (r.Stage == UnequalHandHpStage.Gift && _aiBrains[f.OwnerSeat].ScoreCardEffectIntervention(CreateSnapshot(f.OwnerSeat), CardKind.DrawTwo, r.TargetSeat) >= 0d)
            return prompt.Choices.Single(c => c.Parameters["branch"] == "decline");
        return prompt.Choices.First();
    }
}
