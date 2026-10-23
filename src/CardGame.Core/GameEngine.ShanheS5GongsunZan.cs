using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShanheS5QiaomengReason = "skill-program.shanhe-qiaomeng.discard";
    private static readonly CardZoneKind[] ShanheS5QiaomengZones = [CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment];

    [ProgramCapability]
    private sealed class ShanheS5GongsunCapability(GameEngine engine) : IShanheS5GongsunProgramHost
    {
        public SkillProgramStepOutcome DiscardAttackRangeCardsFromSlashTarget(ProgramSkillFrame f) => engine.BeginShanheS5Qiaomeng(f);
    }

    private Card[] ShanheS5QiaomengCards(int owner, int target) => ShanheS5QiaomengZones
        .SelectMany(zone => _cardZones.CardsAt(new(zone, target)).Where(card =>
            !IsForeignEquipmentDiscardPrevented(owner, card, new(zone, target), OwnedCardMoveIntent.Discard))).ToArray();

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 5740)]
    private bool CanOfferShanheS5Gongsun(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.DiscardAttackRangeCardsFromSlashTarget))
            return context is { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, TargetSeat: { } target, CardUse: { } identity } &&
                IsValidPlayerSeat(target) && _players[target].IsAlive &&
                LifecycleCardUse(identity.ParentCardUseFrameId) is { Action: { } action } use &&
                action.ActionId == identity.CardActionId && action.ActorSeat == candidate.OwnerSeat &&
                IsSlashCard(use.CardKind) && action.EffectiveDesignatedTargetSeats.Contains(target) &&
                ShanheS5QiaomengCards(candidate.OwnerSeat, target).Length > 0;
        if (candidate.SkillId == "shanhe:s5-jiuzhu-505")
            return context is { Window: SkillProgramTriggerWindow.DyingResponse, TargetSeat: { } victim } &&
                IsValidPlayerSeat(victim) && _players[candidate.OwnerSeat].HasHpValue && _players[candidate.OwnerSeat].Hp > 1 &&
                OtherRevealedPublicTeamAlly(_players[candidate.OwnerSeat], _players[victim]) && _players[victim].Hp <= 0 &&
                GetHand(_players[candidate.OwnerSeat]).Count + GetEquipment(_players[candidate.OwnerSeat]).Count > 0;
        return true;
    }

    private SkillProgramStepOutcome BeginShanheS5Qiaomeng(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id); var use = CurrentFinalTargetSlashOwner(f);
        var target = f.WindowContext!.TargetSeat!.Value;
        if (f.ShanheS5Qiaomeng is not null || f.InstructionIndex != 1)
            throw new InvalidOperationException("The range discard must begin once on its exact designated-target instruction.");
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !_players[target].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) return SkillProgramStepOutcome.Continue;
        var range = GetAttackRange(f.OwnerSeat);
        var count = Math.Min(range, ShanheS5QiaomengCards(f.OwnerSeat, target).Length);
        if (count == 0) return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(f = f with { ShanheS5Qiaomeng = new(use.Id, use.Action!.ActionId, target, range, count) });
        AdvanceEventRulesAndQueueFact(new ShanheS5QiaomengStartedEvent(f.Id, use.Id, use.Action.ActionId, f.OwnerSeat, target, range, count));
        PublishShanheS5Qiaomeng(f); return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> ShanheS5QiaomengChoices(ProgramSkillFrame f)
    {
        var d = f.ShanheS5Qiaomeng!;
        return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, d.TargetSeat, ShanheS5QiaomengZones,
            OwnedCardMoveIntent.Discard, canSelect: (_, card) => !d.SelectedIds.Contains(card.Id)).Select(choice => choice with
            {
                Targets = Array.AsReadOnly(new[] { d.TargetSeat }),
                Parameters = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>(choice.Parameters)
                    { ["program-action"] = "shanhe-qiaomeng-card" })
            }).ToArray());
    }

    private void PublishShanheS5Qiaomeng(ProgramSkillFrame f)
    {
        var d = f.ShanheS5Qiaomeng!; var choices = ShanheS5QiaomengChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat,
            $"【{skill.Name}】选择 {_players[d.TargetSeat].Name} 的区域牌（{d.SelectedIds.Count}/{d.RequiredCount}）。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), [d.TargetSeat], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = d.TargetSeat, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    [ProgramAnswer("shanhe-qiaomeng-card")]
    private void ResolveShanheS5QiaomengChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("The range discard lost its owner.");
        var d = f.ShanheS5Qiaomeng ?? throw new InvalidOperationException("The range discard lost its original selection.");
        AssertShanheS5Qiaomeng(f);
        if (d.Paid || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([choice], ShanheS5QiaomengChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("The range discard no longer matches a published private zone slot.");
        ClearPendingDecision();
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !_players[d.TargetSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
        { CancelProgramBindingAndCleanup(f, "趫猛的未支付来源或目标失效。"); return; }
        var zone = Enum.Parse<CardZoneKind>(choice.Parameters["source-zone"]);
        var slot = int.Parse(choice.Parameters["slot-index"], CultureInfo.InvariantCulture);
        var from = new CardLocation(zone, d.TargetSeat); var card = _cardZones.CardsAt(from)[slot];
        var ids = d.SelectedIds.Append(card.Id).ToArray(); var locations = d.OriginalLocations.Append(from).ToArray();
        ReplaceRuntimeTop(f = f with { ShanheS5Qiaomeng = d with { SelectedIds = ids, OriginalLocations = locations } });
        if (ids.Length < d.RequiredCount) { PublishShanheS5Qiaomeng(f); return; }
        if (ids.Length != d.RequiredCount || ids.Select((id, index) => _cardZones.GetLocation(id) != locations[index] ||
                !ShanheS5QiaomengCards(f.OwnerSeat, d.TargetSeat).Any(c => c.Id == id)).Any(invalid => invalid))
            throw new InvalidOperationException("The complete unspent range-discard selection changed its original zone or legality.");
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { ShanheS5Qiaomeng = f.ShanheS5Qiaomeng! with { Paid = true, Before = before, After = before },
            PendingMovementContinuation = new(d.TargetSeat, 0, null) });
        MoveProgramLogicalDiscard(f, f.OwnerSeat, ids, new(ShanheS5QiaomengReason), (batch, records) =>
        {
            foreach (var move in records) RecordBianFuRenForeignDiscardProducer(move, f.OwnerSeat, f.Id);
            var root = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(root with { ShanheS5Qiaomeng = root.ShanheS5Qiaomeng! with { BatchId = batch, After = _movementSequence } });
        });
        f = GetActiveProgramFrame(f.Id); var after = _movementSequence;
        ReplaceRuntimeTop(f with { ShanheS5Qiaomeng = f.ShanheS5Qiaomeng! with { After = after } });
        AdvanceEventRulesAndQueueFact(new ShanheS5QiaomengPaidEvent(f.Id, d.UseFrameId, d.TargetSeat, ids.Length, before, after));
        AdvanceRuntimeProgram(f.Id);
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 5740)]
    private bool ResumeShanheS5Qiaomeng(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.ShanheS5Qiaomeng is not { Paid: true }) return false;
        AssertShanheS5Qiaomeng(f);
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return true;
        ReplaceRuntimeTop(f with { ShanheS5Qiaomeng = null, PendingMovementContinuation = null });
        AdvanceRuntimeProgram(id); return true;
    }

    [ProgramContinuation(ProgramContinuationKind.Movement, 5740)]
    private bool ReturnShanheS5QiaomengMovement(ProgramSkillFrame f)
    {
        if (f.ShanheS5Qiaomeng is not { Paid: true }) return false;
        AdvanceRuntimeProgram(f.Id); return true;
    }

    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 5740)]
    private bool IsShanheS5QiaomengMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect?.Op == SkillProgramEffectOp.DiscardAttackRangeCardsFromSlashTarget && f.ShanheS5Qiaomeng is { Paid: true } d &&
        pending.SubjectSeat == d.TargetSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null &&
        ValidShanheS5QiaomengPayment(f);

    private bool ValidShanheS5QiaomengPayment(ProgramSkillFrame f)
    {
        if (f.ShanheS5Qiaomeng is not { Paid: true } d || d.SelectedIds.Count != d.RequiredCount ||
            d.BatchId is null || d.After <= d.Before || d.After > _movementSequence) return false;
        return d.SelectedIds.Select((id, index) => _cardMovements.Count(m => m.CardId == id && m.Sequence > d.Before && m.Sequence <= d.After &&
            m.From == d.OriginalLocations[index] && m.Reason.Value == ShanheS5QiaomengReason &&
            (GetProgramDiscardSource(m) == d.OriginalLocations[index] ||
             m.From.Zone == CardZoneKind.Equipment && m.To == CardLocation.OutsideGame &&
             (GetAttackCard(m.CardId).IsGeneralWeapon || IsEphemeralCardDestroyedMaterial(m)))) == 1).All(valid => valid) &&
            ProgramEventHistory<ShanheS5QiaomengPaidEvent>().Count(e => e.FrameId == f.Id && e.UseFrameId == d.UseFrameId &&
                e.TargetSeat == d.TargetSeat && e.Count == d.RequiredCount && e.Before == d.Before && e.After == d.After) == 1;
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 5740)]
    private void AssertShanheS5Qiaomeng(ProgramSkillFrame f)
    {
        if (f.ShanheS5Qiaomeng is not { } d) return;
        var use = CurrentFinalTargetSlashOwner(f);
        if (f.InstructionIndex != 1 || use.Id != d.UseFrameId || use.Action!.ActionId != d.ActionId ||
            f.WindowContext!.TargetSeat != d.TargetSeat || d.FrozenRange < 1 || d.RequiredCount < 1 || d.RequiredCount > d.FrozenRange ||
            d.SelectedIds.Count > d.RequiredCount || d.SelectedIds.Distinct().Count() != d.SelectedIds.Count ||
            d.OriginalLocations.Count != d.SelectedIds.Count || d.OriginalLocations.Any(from => from.OwnerSeat != d.TargetSeat || !ShanheS5QiaomengZones.Contains(from.Zone)) ||
            ProgramEventHistory<ShanheS5QiaomengStartedEvent>().Count(e => e.FrameId == f.Id && e.UseFrameId == d.UseFrameId &&
                e.ActionId == d.ActionId && e.OwnerSeat == f.OwnerSeat && e.TargetSeat == d.TargetSeat && e.FrozenRange == d.FrozenRange && e.RequiredCount == d.RequiredCount) != 1 ||
            (d.Paid ? !ValidShanheS5QiaomengPayment(f) : f.PendingMovementContinuation is not null ||
                d.SelectedIds.Select((id, index) => _cardZones.GetLocation(id) != d.OriginalLocations[index]).Any(invalid => invalid)))
            throw new InvalidOperationException("The range discard lost its exact native use, target, frozen range, private selection or one real paid batch.");
    }

    [ProgramAiChoice(SkillProgramEffectOp.DiscardAttackRangeCardsFromSlashTarget)]
    private PromptChoice SelectAiShanheS5Qiaomeng(PendingDecision decision, ProgramSkillFrame f)
    {
        var (choice, thought) = _aiBrains[decision.PlayerSeat].ChooseOtherOwnedCardDiscard(CreateSnapshot(decision.PlayerSeat),
            decision.Choices, _contentRegistry.GetSkill(f.SkillId).Name, ++_thoughtSequence);
        AddThought(thought); return choice;
    }
}
