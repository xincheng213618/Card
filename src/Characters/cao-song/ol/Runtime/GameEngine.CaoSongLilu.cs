using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string LiluDrawReason = "skill-program.caosong.lilu.draw";
    private const string LiluGiftReason = "skill-program.caosong.lilu.gift";
    private CardConversionSource CaoSongSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);

    [ProgramCapability]
    private sealed class CaoSongProgramCapability(GameEngine engine) : ICaoSongProgramHost
    {
        public SkillProgramStepOutcome ReplaceDrawWithLiluGift(ProgramSkillFrame frame) => engine.BeginCaoSongLilu(frame);
        public void GrantYizhengSupport(ProgramSkillFrame frame) => engine.IssueCaoSongYizheng(frame);
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 26030)]
    private bool CanOfferCaoSongLilu(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context) =>
        !trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ReplaceDrawWithLiluGift) ||
        context.Window == SkillProgramTriggerWindow.DrawPhaseStarting && context.OwnerSeat == _currentSeat &&
        _players[candidate.OwnerSeat] is { IsAlive: true, HasHpValue: true } &&
        _players.Any(p => p.IsAlive && p.Seat != candidate.OwnerSeat);

    private SkillProgramStepOutcome BeginCaoSongLilu(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.CaoSongLilu is not null || frame.InstructionIndex != 1 || frame.TriggerId is null ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.DrawPhaseStarting } context ||
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(p => p.Id == context.ParentFrameId) is not { } parent)
            throw new InvalidOperationException("Lilu requires its exact unpaid native draw-phase replacement.");
        var normal = parent.FrozenBaseDrawCount ?? FreezeAndCaptureCaoXiNativeDrawBase(_players[frame.OwnerSeat]);
        ReplaceRuntimeFrame(parent.Id, parent with { FrozenBaseDrawCount = normal, NormalDrawAdjustment = -normal });
        var limit = Math.Min(5, _players[frame.OwnerSeat].MaxHp);
        var before = _movementSequence;
        ReplaceRuntimeTop(frame = frame with { CaoSongLilu = new(CaoSongSource(frame), frame.GameplayHash,
            frame.InstructionIndex, parent.Id, limit, CaoSongLiluStage.Drawing, SequenceBefore: before, SequenceAfter: before) });
        DrawCards(_players[frame.OwnerSeat], Math.Max(0, limit - GetHand(_players[frame.OwnerSeat]).Count), true, new(LiluDrawReason));
        frame = GetActiveProgramFrame(frame.Id);
        ReplaceRuntimeTop(frame with { CaoSongLilu = frame.CaoSongLilu! with { SequenceAfter = _movementSequence } });
        ContinueCaoSongLilu(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private bool DrainCaoSongLiluChildren(long id) =>
        TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);

    private void ContinueCaoSongLilu(long id)
    {
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame { CaoSongLilu: { } receipt } frame && frame.Id == id)
        {
            AssertCaoSongLilu(frame);
            if (DrainCaoSongLiluChildren(id)) return;
            frame = GetActiveProgramFrame(id); receipt = frame.CaoSongLilu!;
            if (!_players[frame.OwnerSeat].IsAlive || _winner != Winner.None)
            { CompleteCaoSongLilu(frame, receipt.GiftCount > 0); return; }
            if (receipt.GiftCount == 0 && !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
            { CompleteCaoSongLilu(frame, false); return; }
            switch (receipt.Stage)
            {
                case CaoSongLiluStage.Drawing:
                    if (GetHand(_players[frame.OwnerSeat]).Count == 0 || !_players.Any(p => p.IsAlive && p.Seat != frame.OwnerSeat))
                    { CompleteCaoSongLilu(frame, true); return; }
                    ReplaceRuntimeTop(frame = frame with { CaoSongLilu = receipt with { Stage = CaoSongLiluStage.Recipient } });
                    PublishCaoSongLiluChoice(frame); return;
                case CaoSongLiluStage.Recipient:
                case CaoSongLiluStage.Cards:
                    PublishCaoSongLiluChoice(frame); return;
                case CaoSongLiluStage.GiftChildren:
                    if (!receipt.Grows) { CompleteCaoSongLilu(frame, true); return; }
                    ReplaceRuntimeTop(frame = frame with { CaoSongLilu = receipt with { Stage = CaoSongLiluStage.MaximumChildren } });
                    ChangeProgramMaximumHp(frame, 1);
                    continue;
                case CaoSongLiluStage.MaximumChildren:
                    ReplaceRuntimeTop(frame = frame with { CaoSongLilu = receipt with { Stage = CaoSongLiluStage.RecoveryChildren } });
                    RecoverProgramTargetTo(frame.Id, frame.OwnerSeat, frame.OwnerSeat, SkillProgramNumberExpression.IntegerConstant,
                        checked(_players[frame.OwnerSeat].Hp + 1), true);
                    continue;
                case CaoSongLiluStage.RecoveryChildren:
                    CompleteCaoSongLilu(frame, true); return;
                default: throw new InvalidOperationException("Unknown Lilu stage.");
            }
        }
    }

    private IReadOnlyList<PromptChoice> CaoSongLiluChoices(ProgramSkillFrame frame)
    {
        var receipt = frame.CaoSongLilu!;
        PromptChoice Choice(string action, string label, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) =>
            new(new($"caosong-lilu.{frame.Id}.{receipt.Stage}.{action}.{string.Join('.', cards ?? [])}.{string.Join('.', targets ?? [])}"),
                label, cards ?? [], targets ?? [], new Dictionary<string, string>
                { ["program-action"] = "caosong-lilu", ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture), ["option"] = action });
        if (receipt.Stage == CaoSongLiluStage.Recipient)
            return Array.AsReadOnly(_players.Where(p => p.IsAlive && p.Seat != frame.OwnerSeat)
                .Select(p => Choice("recipient", $"交给 {p.Name} 手牌", targets: [p.Seat])).ToArray());
        if (receipt.Stage != CaoSongLiluStage.Cards || receipt.RecipientSeat is not { } recipient || !_players[recipient].IsAlive) return [];
        var choices = GetHand(_players[frame.OwnerSeat]).Select(card => Choice("card",
            $"{(receipt.SelectedCardIds.Contains(card.Id) ? "取消选择" : "选择")}【{card.DisplayName}】 {GetSuitDisplayName(card.Suit)} {card.RankText}", [card.Id])).ToList();
        if (receipt.SelectedCardIds.Count > 0)
            choices.Add(Choice("give", $"交出已选 {receipt.SelectedCardIds.Count} 张手牌", receipt.SelectedCardIds, [recipient]));
        return Array.AsReadOnly(choices.ToArray());
    }

    private void PublishCaoSongLiluChoice(ProgramSkillFrame frame)
    {
        var choices = CaoSongLiluChoices(frame);
        if (choices.Count == 0) { CompleteCaoSongLilu(frame, true); return; }
        ReplaceRuntimeTop(frame = frame with { Step = ResolutionFrameStep.AwaitingResponse });
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat,
            frame.CaoSongLilu!.Stage == CaoSongLiluStage.Recipient ? "礼赂：选择其他角色" : "礼赂：选择至少一张手牌，然后确认交出",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices,
            SkillPrompt = new(skill.Id, skill.Name, skill.Name, skill.Description) };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    [ProgramAnswer("caosong-lilu")]
    private void AnswerCaoSongLilu(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { CaoSongLilu: { } receipt } frame ||
            _pendingDecision?.PlayerSeat != frame.OwnerSeat ||
            !AssistedChoicesEqual([choice], CaoSongLiluChoices(frame).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Lilu lost its actual private chooser or current hand selection.");
        ClearPendingDecision();
        if (choice.Parameters["option"] == "recipient")
            ReplaceRuntimeTop(frame with { CaoSongLilu = receipt with { Stage = CaoSongLiluStage.Cards, RecipientSeat = choice.Targets.Single() }, Step = ResolutionFrameStep.ResolvingEffect });
        else if (choice.Parameters["option"] == "card")
        {
            var card = choice.Cards.Single();
            var selected = receipt.SelectedCardIds.Contains(card) ? receipt.SelectedCardIds.Where(id => id != card).ToArray() : receipt.SelectedCardIds.Append(card).ToArray();
            ReplaceRuntimeTop(frame with { CaoSongLilu = receipt with { SelectedCardIds = selected }, Step = ResolutionFrameStep.ResolvingEffect });
        }
        else if (choice.Parameters["option"] == "give")
        {
            var ids = receipt.SelectedCardIds;
            var recipient = receipt.RecipientSeat!.Value;
            if (ids.Count == 0 || ids.Any(id => _cardZones.GetLocation(id) != CardLocation.Hand(frame.OwnerSeat)) || !_players[recipient].IsAlive)
                throw new InvalidOperationException("Lilu must commit at least one current physical hand card to the selected other recipient.");
            var previous = ProgramEventHistory<CaoSongLiluGiftCommittedEvent>().LastOrDefault(e => e.Source.OwnerSeat == frame.OwnerSeat)?.GiftCount;
            var grows = previous is null || ids.Count > previous;
            var before = _movementSequence;
            ReplaceRuntimeTop(frame = frame with { CaoSongLilu = receipt with { Stage = CaoSongLiluStage.GiftChildren,
                GiftCount = ids.Count, PreviousGiftCount = previous, Grows = grows, SequenceBefore = before, SequenceAfter = before }, Step = ResolutionFrameStep.ResolvingEffect });
            MoveProgramCardsFromMultipleSources(ids, CardLocation.Hand(recipient), new(LiluGiftReason));
            frame = GetActiveProgramFrame(frame.Id); receipt = frame.CaoSongLilu! with { SequenceAfter = _movementSequence };
            ReplaceRuntimeTop(frame with { CaoSongLilu = receipt });
            AdvanceEventRulesAndQueueFact(new CaoSongLiluGiftCommittedEvent(frame.Id, receipt.Source, recipient, ids.Count,
                previous, grows, before, receipt.SequenceAfter));
        }
        else throw new InvalidOperationException("Unknown Lilu choice.");
        ContinueCaoSongLilu(frame.Id);
    }

    [ProgramAiChoice(SkillProgramEffectOp.ReplaceDrawWithLiluGift)]
    private PromptChoice ChooseAiCaoSongLilu(PendingDecision decision, ProgramSkillFrame frame)
    {
        var receipt = frame.CaoSongLilu!;
        if (receipt.Stage == CaoSongLiluStage.Recipient) return decision.Choices.First();
        var previous = ProgramEventHistory<CaoSongLiluGiftCommittedEvent>().LastOrDefault(e => e.Source.OwnerSeat == frame.OwnerSeat)?.GiftCount ?? 0;
        var hand = GetHand(_players[frame.OwnerSeat]);
        var desired = previous < hand.Count ? previous + 1 : 1;
        if (receipt.SelectedCardIds.Count >= desired) return decision.Choices.Single(c => c.Parameters["option"] == "give");
        return decision.Choices.Where(c => c.Parameters["option"] == "card" && !receipt.SelectedCardIds.Contains(c.Cards.Single()))
            .OrderBy(c => GetKeepValue(GetAttackCard(c.Cards.Single()), _players[frame.OwnerSeat])).ThenBy(c => c.Cards.Single()).First();
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 26030)]
    private bool ResumeCaoSongLilu(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { CaoSongLilu: not null } frame || frame.Id != id) return false;
        ContinueCaoSongLilu(id); return true;
    }
    private void CompleteCaoSongLilu(ProgramSkillFrame frame, bool completed)
    { ReplaceRuntimeTop(frame = frame with { CaoSongLilu = null }); FinishProgramSkill(frame, completed); }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 26030)]
    private void AssertCaoSongLilu(ProgramSkillFrame frame)
    {
        if (frame.CaoSongLilu is not { } receipt) return;
        if (frame.SkillId != "ol:lilu" || frame.TriggerId is null || receipt.Source != CaoSongSource(frame) ||
            receipt.GameplayHash != frame.GameplayHash || receipt.InstructionIndex != frame.InstructionIndex ||
            frame.WindowContext?.ParentFrameId != receipt.DrawWindowId || receipt.FrozenHandLimit is < 1 or > 5 ||
            receipt.SelectedCardIds.Distinct().Count() != receipt.SelectedCardIds.Count ||
            receipt.SequenceBefore < 0 || receipt.SequenceAfter < receipt.SequenceBefore || receipt.SequenceAfter > _movementSequence)
            throw new InvalidOperationException("Lilu lost its exact owning draw frame, source or frozen physical payment.");
        if (receipt.GiftCount > 0 && !ProgramEventHistory<CaoSongLiluGiftCommittedEvent>().Any(e => e.ProgramFrameId == frame.Id &&
            e.Source == receipt.Source && e.RecipientSeat == receipt.RecipientSeat && e.GiftCount == receipt.GiftCount && e.PreviousGiftCount == receipt.PreviousGiftCount && e.Grows == receipt.Grows))
            throw new InvalidOperationException("A paid Lilu gift lost its immutable previous-count invoice.");
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 26030)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 26030)]
    private bool CanContinuePaidCaoSongLilu(ProgramSkillFrame frame) => frame.CaoSongLilu is { GiftCount: > 0 } &&
        ProgramEventHistory<CaoSongLiluGiftCommittedEvent>().Any(e => e.ProgramFrameId == frame.Id && e.Source == frame.CaoSongLilu.Source);
}
