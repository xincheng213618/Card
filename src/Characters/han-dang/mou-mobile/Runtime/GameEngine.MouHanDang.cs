using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MouHanDangGongqi = "mou-mobile:gongqi", MouHanDangJiefan = "mou-mobile:jiefan";
    private const string MouHanDangDiscardReason = "skill-program.mou-mobile:han-dang.discard", MouHanDangDrawReason = "skill-program.mou-mobile:jiefan.Draw";
    private bool HasMouHanDangCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouHanDangGongqi) ||
        _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveMouHanDangJiefan);
    private static CardConversionSource MouHanDangSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private Card[] MouHanDangDiscardCards(int seat) => GetHand(_players[seat]).Concat(GetEquipment(_players[seat])).Where(c =>
        !IsSelfHandCategoryDiscardForbidden(seat, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard)).OrderBy(c => c.Id).ToArray();
    private SkillProgramStepOutcome StartMouHanDang(ProgramSkillFrame supplied, bool gongqi)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MouHanDang is not null || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) || CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase ||
            phase.ActorSeat != f.OwnerSeat || !IsRecordedActualDiscardRecoveryPhase(phase) ||
            (gongqi ? f.SkillId != MouHanDangGongqi || !MouHanDangPlayStartParent(f) || f.SelectedTargetSeats.Count != 0 || MouHanDangDiscardCards(f.OwnerSeat).Length == 0 :
                f.SkillId != MouHanDangJiefan || f.ActivationId != "aid-by-range" || f.TriggerId is not null || f.WindowContext is not null ||
                f.SelectedTargetSeats is not [var target] || !IsValidPlayerSeat(target) || !_players[target].IsAlive || !CanStartMouHanDangJiefan(_players[f.OwnerSeat])))
            throw new InvalidOperationException("Mou Han Dang requires its exact own native Play entry and live source.");
        var r = new MouHanDangReceipt(MouHanDangSource(f), f.GameplayHash, phase, gongqi)
        { Stage = gongqi ? MouHanDangStage.CostChoice : MouHanDangStage.ModeChoice, TargetSeat = gongqi ? f.OwnerSeat : f.SelectedTargetSeats.Single(), Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { MouHanDang = r });
        AdvanceEventRulesAndQueueFact(new MouHanDangStartedEvent(f.Id, r.Source, r.GameplayHash, phase, gongqi, r.TargetSeat));
        PublishMouHanDang(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private PromptChoice MouHanDangChoice(ProgramSkillFrame f, string branch, string label, int? card = null) => new(
        new($"mou-han-dang.{f.Id}.{f.MouHanDang!.Index}.{branch}.{card ?? 0}"), label, card is { } id ? [id] : [], [],
        new Dictionary<string, string> { ["program-action"] = "mou-han-dang", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });
    private IReadOnlyList<PromptChoice> MouHanDangChoices(ProgramSkillFrame f)
    {
        var r = f.MouHanDang!;
        if (r.Stage == MouHanDangStage.ModeChoice) return Array.AsReadOnly(new[] {
            MouHanDangChoice(f, "discard", "令攻击范围包含你的其他角色依次弃置一张牌"),
            MouHanDangChoice(f, "draw", "摸此时攻击范围包含你的其他角色数的牌"),
            MouHanDangChoice(f, "backwater", "背水：令解烦失效直至发动者击杀角色，然后执行两项") });
        var chooser = r.Gongqi ? f.OwnerSeat : r.Participants[r.Index];
        return Array.AsReadOnly(MouHanDangDiscardCards(chooser).Select(c => MouHanDangChoice(f, "discard-card", $"弃置【{c.DisplayName}】", c.Id)).ToArray());
    }
    private void PublishMouHanDang(ProgramSkillFrame f)
    {
        var r = f.MouHanDang!; var chooser = r.Stage == MouHanDangStage.ModeChoice ? r.TargetSeat : r.Gongqi ? f.OwnerSeat : r.Participants[r.Index];
        var choices = MouHanDangChoices(f); if (choices.Count == 0) throw new InvalidOperationException("Mou Han Dang lost its currently payable choice.");
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, r.Stage == MouHanDangStage.ModeChoice ? "解烦：选择一项，或背水执行两项。" : "弃置一张自己的手牌或装备牌。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = chooser, Choices = Array.AsReadOnly(choices.Select(PromptChoiceProjection.Freeze).ToArray()), SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("mou-han-dang")]
    private void AnswerMouHanDang(PromptChoice selected)
    {
        var f = GetActiveProgramFrame(_resolutionStack[^1].Id); var r = f.MouHanDang;
        if (r is null || !ValidMouHanDang(f) || !AssistedChoicesEqual([selected], MouHanDangChoices(f).Where(c => c.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("Mou Han Dang accepts only its current published choice.");
        ClearPendingDecision();
        if (r.Stage == MouHanDangStage.ModeChoice)
        {
            var branch = selected.Parameters["branch"];
            ReplaceRuntimeTop(f = f with { MouHanDang = r = r with { Branch = branch, Stage = MouHanDangStage.DiscardChildren } });
            AdvanceEventRulesAndQueueFact(new MouHanDangBranchEvent(f.Id, r.TargetSeat, branch));
            if (branch == "backwater")
            {
                AdvanceEventRulesAndQueueFact(new MouHanDangJiefanDisabledEvent(f.Id, f.OwnerSeat));
                AddLog("SkillTriggered", $"{_players[f.OwnerSeat].Name} 的【解烦】因背水失效，直至其击杀一名角色。", f.OwnerSeat);
            }
            if (branch != "draw")
            {
                var seats = MouHanDangRangeSeats(r.TargetSeat, f.OwnerSeat);
                ReplaceRuntimeTop(f = f with { MouHanDang = r = r with { Participants = seats } });
                for (var i = 0; i < seats.Count; i++) AdvanceEventRulesAndQueueFact(new MouHanDangParticipantEvent(f.Id, i, seats[i]));
            }
            ContinueMouHanDang(f.Id); return;
        }
        var seat = r.Gongqi ? f.OwnerSeat : r.Participants[r.Index]; var card = GetAdvancedCard(selected.Cards.Single()); var from = _cardZones.GetLocation(card.Id);
        var suit = EffectiveSuit(_players[seat], card); var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { MouHanDang = r with { Stage = MouHanDangStage.Paying, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveLogicalDiscard(f.OwnerSeat, seat, card, from, new(MouHanDangDiscardReason));
        f = GetActiveProgramFrame(f.Id); r = f.MouHanDang!;
        var m = _cardMovements.Single(m => m.Sequence > before && m.CardId == card.Id && m.From == from && m.Reason.Value == MouHanDangDiscardReason);
        ReplaceRuntimeTop(f = f with { MouHanDang = r = r with { Stage = MouHanDangStage.DiscardChildren, PaidCardId = card.Id, PaidSuit = suit, PaymentSequence = m.Sequence, After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new MouHanDangDiscardedEvent(f.Id, r.Index, seat, card.Id, from, m.To, m.Sequence, suit));
        if (r.Gongqi)
        {
            AdvanceEventRulesAndQueueFact(new MouHanDangGongqiGrantedEvent(f.Id, r.Source, r.GameplayHash, r.Phase, card.Id, suit, m.Sequence));
            AddLog("SkillTriggered", $"{_players[f.OwnerSeat].Name} 发动【弓骑】：本阶段其用牌只能以虚拟牌或{(IsRedSuit(suit) ? "红色" : "黑色")}手牌响应。", f.OwnerSeat);
        }
        SealMouHanDangMovements(f.Id, before); ContinueMouHanDang(f.Id);
    }
    private void ContinueMouHanDang(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouHanDang: { } r } f || f.Id != id) return;
        if (!ValidMouHanDang(f)) throw new InvalidOperationException("Mou Han Dang lost its accepted source, phase or paid native child.");
        if (r.Stage is MouHanDangStage.CostChoice or MouHanDangStage.Paying or MouHanDangStage.ModeChoice or MouHanDangStage.ParticipantChoice or MouHanDangStage.Drawing) return;
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return;
        f = GetActiveProgramFrame(id); r = f.MouHanDang!;
        if (r.Gongqi || r.Stage == MouHanDangStage.DrawChildren || _winner != Winner.None) { FinishMouHanDang(f); return; }
        var index = r.Index + (r.PaidCardId is null ? 0 : 1);
        while (index < r.Participants.Count && (!_players[r.Participants[index]].IsAlive || MouHanDangDiscardCards(r.Participants[index]).Length == 0)) index++;
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, MouHanDang = r = r with { Index = index, PaidCardId = null, PaymentSequence = 0, PaidSuit = Suit.None } });
        if (index < r.Participants.Count)
        {
            ReplaceRuntimeTop(f = f with { MouHanDang = r with { Stage = MouHanDangStage.ParticipantChoice } }); PublishMouHanDang(f); return;
        }
        if (r.Branch == "discard" || !_players[r.TargetSeat].IsAlive) { FinishMouHanDang(f); return; }
        var count = MouHanDangRangeSeats(r.TargetSeat, f.OwnerSeat).Count;
        ReplaceRuntimeTop(f = f with { MouHanDang = r with { Stage = MouHanDangStage.Drawing, RequestedDraw = count } });
        BeginOperationProgramDraw(f, r.TargetSeat, count, new(MouHanDangDrawReason));
    }
    private void FinishMouHanDang(ProgramSkillFrame f)
    {
        AdvanceEventRulesAndQueueFact(new MouHanDangCompletedEvent(f.Id, f.MouHanDang!.Gongqi));
        ReplaceRuntimeTop(f = f with { MouHanDang = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouHanDangGongqi, SkillProgramEffectOp.ResolveMouHanDangJiefan)]
    private PromptChoice AiMouHanDang(PendingDecision p, ProgramSkillFrame f) => p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "draw") ?? p.Choices.First();
    [ProgramCapability] private sealed class MouHanDangCapability(GameEngine g) : IMouHanDangHost
    { public SkillProgramStepOutcome Gongqi(ProgramSkillFrame f) => g.StartMouHanDang(f, true); public SkillProgramStepOutcome Jiefan(ProgramSkillFrame f) => g.StartMouHanDang(f, false); }
}
