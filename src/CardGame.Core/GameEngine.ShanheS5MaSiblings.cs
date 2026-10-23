namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShanheS5TieqiDiscardReason = "skill-program.shanhe-tieqi.discard";

    [ProgramCapability]
    private sealed class ShanheS5TieqiCapability(GameEngine engine) : IShanheS5TieqiProgramHost
    {
        public SkillProgramStepOutcome SuppressAndJudgeAllColor(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.BeginProgramSlashSuitDiscard(frame, effect);
    }

    private bool IsShanheS5Tieqi(ProgramSkillFrame frame) => frame.TriggerId is not null &&
        ProgramInstructionResolver.Default.Features(GetProgramTrigger(frame))
            .HasOperation(SkillProgramEffectOp.SuppressCurrentSlashTargetAndJudgeColorAllDiscard);

    private Card[] ShanheS5TieqiColorCards(ProgramSkillFrame frame)
    {
        var draft = frame.SlashSuitDiscard!;
        if (draft.FinalSuit is null or Suit.None) return [];
        return GetHand(_players[draft.TargetSeat]).Concat(GetEquipment(_players[draft.TargetSeat]))
            .Where(card => EffectiveSuit(_players[draft.TargetSeat], card) is var suit && suit != Suit.None &&
                IsRedSuit(suit) == IsRedSuit(draft.FinalSuit.Value)).ToArray();
    }

    private bool CanPayShanheS5Tieqi(ProgramSkillFrame frame, Card[] cards) => cards.Length > 0 &&
        cards.All(card => !IsForeignEquipmentDiscardPrevented(frame.SlashSuitDiscard!.TargetSeat,
            card, _cardZones.GetLocation(card.Id), OwnedCardMoveIntent.Discard));

    private IReadOnlyList<PromptChoice> ShanheS5TieqiChoices(ProgramSkillFrame frame)
    {
        var cards = ShanheS5TieqiColorCards(frame); var choices = new List<PromptChoice>();
        if (CanPayShanheS5Tieqi(frame, cards)) choices.Add(new(new($"shanhe-tieqi.{frame.Id}.pay"),
            $"弃置全部{(IsRedSuit(frame.SlashSuitDiscard!.FinalSuit!.Value) ? "红" : "黑")}色手牌及装备牌（{cards.Length}张），使此杀可被抵消。",
            cards.Select(card => card.Id).ToArray(), [], new Dictionary<string, string> { ["program-action"] = "shanhe-tieqi-color" }));
        choices.Add(new(new($"shanhe-tieqi.{frame.Id}.decline"), "不弃置，不能抵消此杀。", [], [],
            new Dictionary<string, string> { ["program-action"] = "shanhe-tieqi-skip" }));
        return Array.AsReadOnly(choices.ToArray());
    }

    private void PublishShanheS5Tieqi(ProgramSkillFrame frame)
    {
        var draft = frame.SlashSuitDiscard!; var choices = ShanheS5TieqiChoices(frame); var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, draft.TargetSeat, $"【{skill.Name}】是否弃置全部与判定颜色相同的手牌及装备牌？",
            choices.SelectMany(choice => choice.Cards).ToArray(), [], frame.OwnerSeat)
        {
            PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = draft.TargetSeat, Choices = choices,
            SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description)
        };
        _status = _players[draft.TargetSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private bool ResumeShanheS5Tieqi(ProgramSkillFrame frame)
    {
        var draft = frame.SlashSuitDiscard!;
        if (frame.PendingMovementContinuation is not null) return true;
        _ = SlashSuitDiscardAttack(frame);
        if (draft.Stage is ProgramSlashSuitDiscardStage.Judging or ProgramSlashSuitDiscardStage.Choosing) return true;
        if (draft.Stage == ProgramSlashSuitDiscardStage.PaymentMovement)
        {
            if (!ValidShanheS5TieqiPayment(frame)) throw new InvalidOperationException("The whole-color Tieqi cost lost its original real discard cohort.");
            FinishShanheS5Tieqi(frame, true); return false;
        }
        if (_winner != Winner.None || !_players[draft.TargetSeat].IsAlive || draft.FinalSuit is null or Suit.None ||
            !CanPayShanheS5Tieqi(frame, ShanheS5TieqiColorCards(frame)))
        { FinishShanheS5Tieqi(frame, false); return false; }
        ReplaceRuntimeTop(frame = frame with { SlashSuitDiscard = draft with { Stage = ProgramSlashSuitDiscardStage.Choosing } });
        PublishShanheS5Tieqi(frame); return true;
    }

    [ProgramAnswer("shanhe-tieqi-color", "shanhe-tieqi-skip")]
    private void ResolveShanheS5TieqiChoice(PromptChoice choice)
    {
        var frame = GetActiveProgramFrame(_resolutionStack[^1].Id);
        if (!IsShanheS5Tieqi(frame) || frame.SlashSuitDiscard is not { Stage: ProgramSlashSuitDiscardStage.Choosing } draft ||
            _pendingDecision?.PlayerSeat != draft.TargetSeat ||
            !AssistedChoicesEqual([choice], ShanheS5TieqiChoices(frame).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Whole-color Tieqi requires its exact published target-private cohort.");
        _ = SlashSuitDiscardAttack(frame); ClearPendingDecision();
        if (choice.Parameters.GetValueOrDefault("program-action") == "shanhe-tieqi-skip" || !_players[draft.TargetSeat].IsAlive || _winner != Winner.None)
        { FinishShanheS5Tieqi(frame, false); AdvanceRuntimeProgram(frame.Id); return; }
        var cards = ShanheS5TieqiColorCards(frame);
        if (!CanPayShanheS5Tieqi(frame, cards) || !cards.Select(card => card.Id).SequenceEqual(choice.Cards))
            throw new InvalidOperationException("Whole-color Tieqi cannot partially pay a changed or protected cohort.");
        var before = _movementSequence;
        ReplaceRuntimeTop(frame = frame with
        {
            SlashSuitDiscard = draft with { Stage = ProgramSlashSuitDiscardStage.PaymentMovement },
            ShanheS5Tieqi = new(before, before, 0, cards.Select(card => card.Id).ToArray(), cards.Select(card => _cardZones.GetLocation(card.Id)).ToArray())
        });
        MoveProgramLogicalDiscard(frame, draft.TargetSeat, choice.Cards, new(ShanheS5TieqiDiscardReason), (batch, moves) =>
        {
            var owner = GetActiveProgramFrame(frame.Id);
            ReplaceRuntimeTop(owner with { ShanheS5Tieqi = owner.ShanheS5Tieqi! with { BatchId = batch, After = _movementSequence } });
        });
        frame = GetActiveProgramFrame(frame.Id);
        ReplaceRuntimeTop(frame = frame with { ShanheS5Tieqi = frame.ShanheS5Tieqi! with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new ShanheS5TieqiPaidEvent(frame.Id, draft.CardUseFrameId, draft.TargetSeat,
            choice.Cards.Count, before, _movementSequence));
        if (AwaitProgramBoundCardMovements(frame.Id, frame.OwnerSeat) == SkillProgramStepOutcome.Continue) AdvanceRuntimeProgram(frame.Id);
    }

    private bool ValidShanheS5TieqiPayment(ProgramSkillFrame frame)
    {
        if (frame.ShanheS5Tieqi is not { } paid || frame.SlashSuitDiscard is not { Stage: ProgramSlashSuitDiscardStage.PaymentMovement } draft ||
            paid.CardIds.Count == 0 || paid.CardIds.Count != paid.From.Count || paid.CardIds.Distinct().Count() != paid.CardIds.Count ||
            paid.BatchId <= 0 || paid.Before >= paid.After || paid.After > _movementSequence ||
            paid.From.Any(from => from.OwnerSeat != draft.TargetSeat || from.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment))) return false;
        var logical = ProgramEventHistory<NativeLogicalDiscardInvocationRecordedEvent>().Where(e => e.ParentFrameId == frame.Id &&
            e.InitiatorSeat == frame.OwnerSeat && e.ExecutorSeat == draft.TargetSeat && e.SubjectSeat == draft.TargetSeat &&
            e.ActualCount == paid.CardIds.Count && e.SequenceBefore == paid.Before && e.SequenceAfter == paid.After &&
            e.Reason.Value == ShanheS5TieqiDiscardReason).ToArray();
        if (logical.Length != 1 || paid.CardIds.Select((id, index) =>
                ProgramEventHistory<NativeLogicalDiscardMaterialCompletedEvent>().Count(e => e.InvocationId == logical[0].InvocationId &&
                    e.CardId == id && e.From == paid.From[index] && e.CompletionSequence > paid.Before && e.CompletionSequence <= paid.After) == 1)
            .Any(valid => !valid)) return false;
        return paid.CardIds.Select((id, index) => _cardMovements.Count(move => move.CardId == id && move.Sequence > paid.Before &&
            move.Sequence <= paid.After && move.Reason.Value == ShanheS5TieqiDiscardReason &&
            (GetProgramDiscardSource(move) == paid.From[index] || move.From == paid.From[index] && move.From.Zone == CardZoneKind.Equipment &&
             move.To == CardLocation.OutsideGame && (GetAttackCard(id).IsGeneralWeapon || IsEphemeralCardDestroyedMaterial(move)))) == 1).All(value => value) &&
            ProgramEventHistory<ShanheS5TieqiPaidEvent>().Count(e => e.FrameId == frame.Id && e.CardUseFrameId == draft.CardUseFrameId &&
                e.TargetSeat == draft.TargetSeat && e.Count == paid.CardIds.Count && e.Before == paid.Before && e.After == paid.After) == 1;
    }

    private void FinishShanheS5Tieqi(ProgramSkillFrame frame, bool paid)
    {
        ReplaceRuntimeTop(frame = frame with { ShanheS5Tieqi = null });
        FinishProgramSlashSuitDiscard(frame, paid);
    }

    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 5760)]
    private bool IsShanheS5TieqiMovement(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect?.Op == SkillProgramEffectOp.SuppressCurrentSlashTargetAndJudgeColorAllDiscard && frame.SlashSuitDiscard is
            { Stage: ProgramSlashSuitDiscardStage.JudgmentMovement or ProgramSlashSuitDiscardStage.PaymentMovement } &&
        pending.SubjectSeat == frame.OwnerSeat && pending.CoverageResultBind is null &&
        (frame.ShanheS5Tieqi is null || ValidShanheS5TieqiPayment(frame));

    private void AssertShanheS5Tieqi(ProgramSkillFrame frame)
    {
        if (frame.SlashSuitDiscard is not { } draft) return;
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.SuppressCurrentSlashTargetAndJudgeColorAllDiscard ||
            effect.JudgmentReason != draft.JudgmentReason || effect.ResultBind != draft.ResultBind)
            throw new InvalidOperationException("Whole-color Tieqi lost its original instruction and judgment identity.");
        _ = SlashSuitDiscardAttack(frame, requireActive: false);
        if (draft.Stage == ProgramSlashSuitDiscardStage.PaymentMovement ? !ValidShanheS5TieqiPayment(frame) : frame.ShanheS5Tieqi is not null)
            throw new InvalidOperationException("Whole-color Tieqi lost its owning judgment or once-paid material proof.");
        if (ReferenceEquals(frame, _resolutionStack.LastOrDefault()) && draft.Stage == ProgramSlashSuitDiscardStage.Choosing &&
            (_pendingDecision is not { IsPrivate: true } prompt || prompt.PlayerSeat != draft.TargetSeat ||
             !AssistedChoicesEqual(prompt.Choices, ShanheS5TieqiChoices(frame))))
            throw new InvalidOperationException("Whole-color Tieqi lost its complete target-private payment choices.");
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 5760)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 5760)]
    private bool CanContinuePaidShanheS5Tieqi(ProgramSkillFrame frame) => frame.ShanheS5Tieqi is not null &&
        IsShanheS5Tieqi(frame) && ValidShanheS5TieqiPayment(frame);

    [ProgramAiChoice(SkillProgramEffectOp.SuppressCurrentSlashTargetAndJudgeColorAllDiscard)]
    private PromptChoice SelectAiShanheS5Tieqi(PendingDecision decision, ProgramSkillFrame frame)
    {
        var cards = ShanheS5TieqiColorCards(frame);
        var player = _players[frame.SlashSuitDiscard!.TargetSeat];
        var pay = decision.Choices.FirstOrDefault(choice => choice.Cards.Count > 0);
        // Only the payer's own cards enter this estimate; hidden deck order is irrelevant.
        var hasRemainingDodge = GetHand(player).Any(card => card.Kind == CardKind.Dodge && !cards.Any(cost => cost.Id == card.Id));
        return pay is not null && hasRemainingDodge && cards.Sum(card => GetKeepValue(card, player)) <= 8d
            ? pay : decision.Choices.Last();
    }
}
