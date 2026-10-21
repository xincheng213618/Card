using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MouChenGongGiftReason = "skill-program.mou-mobile:mingce.give", MouChenGongDrawReason = "skill-program.mou-mobile:mingce.Draw";
    private bool HasMouChenGongCapability => _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveMouChenGongMingceGift) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouChenGongMingceRelease) || _contentRegistry.Skills.ContainsKey(MouChenGongComposition.Zhichi);
    private static CardConversionSource MouChenGongSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_chen_gong_mou_mobile_MouChenGongMingce.MouChenGongSource(f);
    private bool MouChenGongPhaseSpent(int owner, ActualDiscardRecoveryPhaseKey phase) => ProgramEventHistory<MouChenGongMingceStartedEvent>()
        .Any(e => e.Source.OwnerSeat == owner && e.Source.SkillId == MouChenGongComposition.Mingce && e.Phase == phase);
    [ProgramPhaseUsedProjection(30)]
    private int MouChenGongProjectedPhaseUses(int owner, string skill, int fallback) => skill == MouChenGongComposition.Mingce &&
        CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase && IsRecordedActualDiscardRecoveryPhase(phase)
        ? ProgramEventHistory<MouChenGongMingceStartedEvent>().Count(e => e.Source.OwnerSeat == owner && e.Source.SkillId == skill && e.Phase == phase) : fallback;
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart, 29600, SkillProgramEffectOp.ResolveMouChenGongMingceGift)]
    private bool CanStartMouChenGongGift(CharacterState owner) => CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase &&
        phase.ActorSeat == owner.Seat && IsRecordedActualDiscardRecoveryPhase(phase) && !MouChenGongPhaseSpent(owner.Seat, phase) &&
        GetHand(owner).Concat(GetEquipment(owner)).Any() && _players.Any(p => p.IsAlive && p.Seat != owner.Seat);
    private bool MouChenGongGiftRoot(ProgramSkillFrame f) => f.MouChenGongGift is { } r && f.SkillId == MouChenGongComposition.Mingce &&
        f.TriggerId is null && f.ActivationId == "gift-and-plan" && f.InstructionIndex == 1 && f.WindowContext is null && f.SelectedCardIds.Count == 0 &&
        f.SelectedTargetSeats.SequenceEqual([r.RecipientSeat]) && r.Source == MouChenGongSource(f) && r.GameplayHash == f.GameplayHash &&
        IsValidPlayerSeat(r.RecipientSeat) && r.RecipientSeat != f.OwnerSeat && r.Phase.ActorSeat == f.OwnerSeat && r.Phase.Kind == ActualDiscardRecoveryPhaseKind.Play &&
        IsRecordedActualDiscardRecoveryPhase(r.Phase) && _contentRegistry.GetSkill(f.SkillId).Program is { } p && p.GameplayHash == f.GameplayHash &&
        ProgramEventHistory<MouChenGongMingceStartedEvent>().Count(e => e == new MouChenGongMingceStartedEvent(f.Id, r.Source, r.GameplayHash, r.Phase, r.RecipientSeat)) == 1;
    private SkillProgramStepOutcome BeginMouChenGongGift(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MouChenGongGift is not null || f.SkillId != MouChenGongComposition.Mingce || f.ActivationId != "gift-and-plan" || f.TriggerId is not null ||
            f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats is not [var recipient] || recipient == f.OwnerSeat ||
            !_players[f.OwnerSeat].IsAlive || !_players[recipient].IsAlive || CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase ||
            phase.ActorSeat != f.OwnerSeat || !IsRecordedActualDiscardRecoveryPhase(phase) || MouChenGongPhaseSpent(f.OwnerSeat, phase))
            throw new InvalidOperationException("Mingce requires one fresh exact actual own Play and one living other recipient.");
        var r = new MouChenGongGiftReceipt(MouChenGongSource(f), f.GameplayHash, phase, recipient);
        ReplaceRuntimeTop(f = f with { MouChenGongGift = r });
        AdvanceEventRulesAndQueueFact(new MouChenGongMingceStartedEvent(f.Id, r.Source, r.GameplayHash, phase, recipient));
        PublishMouChenGong(f, f.OwnerSeat, MouChenGongGiftChoices(f), "明策：将一张手牌或装备牌交给目标。");
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private void PublishMouChenGong(ProgramSkillFrame f, int chooser, IReadOnlyList<PromptChoice> choices, string text)
    {
        if (choices.Count == 0) throw new InvalidOperationException("Mingce lost every canonical private choice.");
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, text, choices.SelectMany(c => c.Cards).Distinct().ToArray(),
            choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = chooser, Choices = Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray()),
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    private IReadOnlyList<PromptChoice> MouChenGongGiftChoices(ProgramSkillFrame f) => Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, f.OwnerSeat,
        [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Transfer, destination: SkillProgramCardDestination.SelectedTargetHand,
        destinationSeat: f.MouChenGongGift!.RecipientSeat).Select(c => c with
        { Parameters = new Dictionary<string, string>(c.Parameters) { ["program-action"] = "mou-chen-gong-gift" } }).ToArray());
    [ProgramAnswer("mou-chen-gong-gift")]
    private void AnswerMouChenGongGift(PromptChoice selected)
    {
        var f = GetActiveProgramFrame(_resolutionStack[^1].Id); var r = f.MouChenGongGift!;
        if (!MouChenGongGiftRoot(f) || r.Stage != MouChenGongGiftStage.GiftChoice || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([selected], MouChenGongGiftChoices(f).Where(c => c.Id == selected.Id).ToArray())) throw new InvalidOperationException("Mingce requires the owner's exact current HE payment slot.");
        var from = new CardLocation(Enum.Parse<CardZoneKind>(selected.Parameters["source-zone"]), f.OwnerSeat);
        var card = _cardZones.CardsAt(from)[int.Parse(selected.Parameters["slot-index"], CultureInfo.InvariantCulture)];
        ClearPendingDecision(); var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { MouChenGongGift = r with { Stage = MouChenGongGiftStage.GiftChildren, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveCard(card, from, CardLocation.Hand(r.RecipientSeat), new(MouChenGongGiftReason), NativeGiveCompletion(f.Id,f.OwnerSeat,r.RecipientSeat,[card.Id],_movementSequence,m =>
        {
            var current = GetActiveProgramFrame(f.Id);
            if (!MouChenGongGiftRoot(current) || current.MouChenGongGift!.Gift is not null || m.CardId != card.Id || m.From != from)
                throw new InvalidOperationException("Mingce can pay only its one original actual gift.");
            var paid = new MouChenGongGiftPayment(m.CardId, m.CardKind, m.From, m.To, m.Sequence);
            ReplaceRuntimeTop(current with { MouChenGongGift = current.MouChenGongGift with { Gift = paid, After = m.Sequence } });
            AdvanceEventRulesAndQueueFact(new MouChenGongGiftPaidEvent(f.Id, r.RecipientSeat, m.CardId, m.CardKind, m.From, m.To, m.Sequence));
        }), disclosure: from.Zone == CardZoneKind.Hand ? CardMovementDisclosure.FaceDown : CardMovementDisclosure.FaceUp);
        SealMouChenGongMovements(f.Id, before); ContinueMouChenGongGift(f.Id);
    }
    private IReadOnlyList<PromptChoice> MouChenGongBranchChoices(ProgramSkillFrame f) => Array.AsReadOnly(new[] { ("lose-hp", "失去1点体力，令明策发动者摸两张牌并获得一个策"), ("draw", "摸一张牌") }
        .Select(p => new PromptChoice(new($"mou-chen-gong.{f.Id}.{p.Item1}"), p.Item2, [], [], new Dictionary<string, string>
        { ["program-action"] = "mou-chen-gong-mingce", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = p.Item1 })).ToArray());
    [ProgramAnswer("mou-chen-gong-mingce")]
    private void AnswerMouChenGongBranch(PromptChoice selected)
    {
        var f = GetActiveProgramFrame(_resolutionStack[^1].Id); var r = f.MouChenGongGift!;
        if (!MouChenGongGiftRoot(f) || r.Stage != MouChenGongGiftStage.RecipientChoice || _pendingDecision?.PlayerSeat != r.RecipientSeat ||
            !AssistedChoicesEqual([selected], MouChenGongBranchChoices(f).Where(c => c.Id == selected.Id).ToArray())) throw new InvalidOperationException("Mingce accepts only its actual recipient's current private branch.");
        ClearPendingDecision(); var branch = selected.Parameters["branch"];
        ReplaceRuntimeTop(f = f with { MouChenGongGift = r with { Branch = branch, Stage = branch == "lose-hp" ? MouChenGongGiftStage.LosingHp : MouChenGongGiftStage.Drawing },
            PendingMovementContinuation = branch == "lose-hp" ? new(f.OwnerSeat, 0, null) : null });
        AdvanceEventRulesAndQueueFact(new MouChenGongBranchAcceptedEvent(f.Id, r.RecipientSeat, branch));
        if (branch == "draw") { BeginOperationProgramDraw(f, r.RecipientSeat, 1, new(MouChenGongDrawReason)); return; }
        AdvanceEventRulesAndQueueFact(new MouChenGongLossIssuedEvent(f.Id, r.RecipientSeat, 1));
        var result = new ProgramSkillHost(this).LoseHp(f.Id, f.SkillId, r.RecipientSeat, 1);
        if (result == SkillProgramStepOutcome.Continue && _resolutionStack.LastOrDefault()?.Id == f.Id) ContinueMouChenGongGift(f.Id);
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 29600)]
    private void CaptureMouChenGongLoss(IGameEvent fact)
    {
        if (fact is not ProgramSkillHpLostEvent lost || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouChenGongGift: { Stage: MouChenGongGiftStage.LosingHp } r } f || lost.FrameId != f.Id) return;
        var change = _pendingHpChanges.SingleOrDefault(c => c.ParentFrameId == f.Id && c.Kind == HpChangeKind.Loss && c.TargetSeat == r.RecipientSeat);
        if (!MouChenGongGiftRoot(f) || lost.SkillId != f.SkillId || lost.TargetSeat != r.RecipientSeat || change is null || change.SourceSeat is not null ||
            change.Amount != Math.Min(change.HpBefore, 1) || lost.Amount != change.Amount || lost.RemainingHp != change.HpAfter || r.Loss is not null)
            throw new InvalidOperationException("Mingce's recipient loss must be its one actual native HP deduction.");
        var paid = new MouChenGongLossPayment(change.Id, change.HpBefore, change.HpAfter, change.Amount);
        ReplaceRuntimeTop(f with { MouChenGongGift = r with { Stage = MouChenGongGiftStage.LossChildren, Loss = paid } });
        AdvanceEventRulesAndQueueFact(new MouChenGongLossReturnedEvent(f.Id, r.RecipientSeat, paid.ChangeId, paid.HpBefore, paid.HpAfter, paid.ActualAmount, false));
    }
    private void ContinueMouChenGongGift(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouChenGongGift: { } r } f || f.Id != id) return;
        AssertMouChenGong(f);
        if (r.Stage is MouChenGongGiftStage.GiftChoice or MouChenGongGiftStage.RecipientChoice or MouChenGongGiftStage.Drawing) return;
        if (DrainMouChenGongChildren(f)) return;
        f = GetActiveProgramFrame(id); r = f.MouChenGongGift!;
        if (_winner != Winner.None) { FinishMouChenGongGift(f); return; }
        if (r.Stage == MouChenGongGiftStage.GiftChildren)
        {
            if (!_players[r.RecipientSeat].IsAlive) { FinishMouChenGongGift(f); return; }
            ReplaceRuntimeTop(f = f with { MouChenGongGift = r with { Stage = MouChenGongGiftStage.RecipientChoice }, PendingMovementContinuation = null });
            PublishMouChenGong(f, r.RecipientSeat, MouChenGongBranchChoices(f), "明策：选择失去体力以助发动者，或自己摸一张牌。"); return;
        }
        if (r.Stage == MouChenGongGiftStage.LosingHp)
        {
            var appeared = ProgramEventHistory<IdentityGeneralAppearedEvent>().SingleOrDefault(e => e.Receipt.Entry == IdentityAppearanceEntry.ProgramHpLoss &&
                e.Receipt.ParentFrameId == id && e.Receipt.OwnerSeat == r.RecipientSeat && e.Receipt.RequestedDeduction == 1 && e.Receipt.OriginalInstructionIndex == f.InstructionIndex);
            if (appeared is null || !ProgramEventHistory<IdentityAppearanceCompletedEvent>().Any(e => e.AppearanceFrameId == appeared.AppearanceFrameId))
                throw new InvalidOperationException("Mingce cannot resume an unreturned native HP deduction.");
            var paid = new MouChenGongLossPayment(0, appeared.Receipt.RestoredHp, appeared.Receipt.RestoredHp, 0, true);
            ReplaceRuntimeTop(f = f with { MouChenGongGift = r with { Stage = MouChenGongGiftStage.LossChildren, Loss = paid } });
            AdvanceEventRulesAndQueueFact(new MouChenGongLossReturnedEvent(f.Id, r.RecipientSeat, 0, paid.HpBefore, paid.HpAfter, 0, true)); r = f.MouChenGongGift!;
        }
        if (r.Stage == MouChenGongGiftStage.LossChildren)
        {
            if (!_players[f.OwnerSeat].IsAlive) { FinishMouChenGongGift(f); return; }
            ReplaceRuntimeTop(f = f with { MouChenGongGift = r with { Stage = MouChenGongGiftStage.Drawing }, PendingMovementContinuation = null });
            BeginOperationProgramDraw(f, f.OwnerSeat, 2, new(MouChenGongDrawReason)); return;
        }
        if (r.Stage != MouChenGongGiftStage.DrawChildren) throw new InvalidOperationException("Mingce cannot replay its accepted branch.");
        if (r.Branch == "lose-hp" && _players[f.OwnerSeat].IsAlive && !r.CeGained)
        {
            ReplaceRuntimeTop(f = f with { MouChenGongGift = r with { CeGained = true } });
            ChangeMouChenGongCe(f, 1);
        }
        FinishMouChenGongGift(GetActiveProgramFrame(id));
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouChenGongMingceGift)]
    private void ReturnMouChenGongDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        var r = f.MouChenGongGift!; var seat = r.Branch == "lose-hp" ? f.OwnerSeat : r.RecipientSeat; var count = r.Branch == "lose-hp" ? 2 : 1;
        if (!MouChenGongGiftRoot(f) || r.Stage != MouChenGongGiftStage.Drawing || r.Draw is not null || outcome.RecipientSeat != seat || outcome.RequestedCount != count)
            throw new InvalidOperationException("Mingce cannot replace or repeat its exact accepted native draw.");
        ReplaceRuntimeTop(f = f with { MouChenGongGift = r with { Stage = MouChenGongGiftStage.DrawChildren, Draw = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MouChenGongDrawReturnedEvent(f.Id, seat, count, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId));
        SealMouChenGongMovements(f.Id, outcome.SequenceBefore); ContinueMouChenGongGift(f.Id);
    }
    private void FinishMouChenGongGift(ProgramSkillFrame f)
    {
        var r = f.MouChenGongGift!;
        AdvanceEventRulesAndQueueFact(new MouChenGongMingceCompletedEvent(f.Id, r.RecipientSeat, r.Branch, r.CeGained));
        ReplaceRuntimeTop(f = f with { MouChenGongGift = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouChenGongMingceGift)]
    private PromptChoice AiMouChenGongGift(PendingDecision pending, ProgramSkillFrame f)
    {
        if (f.MouChenGongGift!.Stage == MouChenGongGiftStage.GiftChoice)
            return pending.Choices.OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[f.OwnerSeat])).ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
        var view = CreateSnapshot(pending.PlayerSeat);
        var friendly = _aiBrains[pending.PlayerSeat].ScoreProgramTarget(view, f.OwnerSeat, new(OwnerDraw: 0, OwnerRecovery: 0, OwnerHpLoss: 0,
            TargetDraw: 1, TargetRecovery: 0, TargetHpLoss: 0, GivesSelected: false, DiscardsSelected: false)) > 0;
        var branch = _players[f.OwnerSeat].IsAlive && view.Players[pending.PlayerSeat].Hp >= 4 && friendly ? "lose-hp" : "draw";
        return pending.Choices.Single(c => c.Parameters["branch"] == branch);
    }
    [ProgramCapability] private sealed class MouChenGongCapability(GameEngine engine) : IMouChenGongHost
    {
        public SkillProgramStepOutcome Gift(ProgramSkillFrame frame) => engine.BeginMouChenGongGift(frame);
        public SkillProgramStepOutcome Release(ProgramSkillFrame frame) => engine.BeginMouChenGongRelease(frame);
    }
}
