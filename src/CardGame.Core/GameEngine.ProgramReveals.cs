namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome RevealProgramTargetHandCard(
        ProgramSkillFrame frame,
        ProgramParticipantReference chooserRef,
        ProgramParticipantReference cardOwnerRef,
        string resultBind,
        SkillProgramRevealMode mode,
        IReadOnlyList<Suit> eligibleSuits,
        bool allowDecline)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.RevealCardSelection is not null ||
            active.CardSetBindings.Any(binding => binding.Name == resultBind))
            throw new InvalidOperationException("A hand reveal cannot overwrite an existing draft or binding.");
        if (active.SelectedTargetSeats.Count != 1)
            throw new InvalidOperationException("A hand reveal requires exactly one selected target.");
        var chooserSeat = ResolveProgramParticipant(active, chooserRef);
        var holderSeat = ResolveProgramParticipant(active, cardOwnerRef);
        if (chooserSeat != frame.OwnerSeat || holderSeat == frame.OwnerSeat)
            throw new InvalidOperationException(
                "A hand reveal must target another character's hand and be chosen by the skill owner.");
        var candidates = GetHand(_players[holderSeat]).Select(card => card.Id).ToArray();
        if (candidates.Length == 0)
        {
            CancelProgramBindingAndCleanup(active, "目标没有手牌，技能结算已取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var handCards = _cardZones.CardsAt(CardLocation.Hand(holderSeat)).ToDictionary(card => card.Id);
        var eligible = eligibleSuits.Count == 0 ? candidates : candidates
            .Where(cardId => eligibleSuits.Contains(handCards[cardId].Suit))
            .ToArray();
        if (eligible.Length == 0 && !allowDecline)
        {
            CancelProgramBindingAndCleanup(active, "目标没有符合花色条件的手牌，技能结算已取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        if (mode == SkillProgramRevealMode.Random)
        {
            if (eligible.Length == 0)
            {
                // Nothing eligible to blind-reveal: an allowed decline commits an
                // empty private binding so downstream suit-gated branches stay dormant.
                SetProgramCardSet(frame.Id, resultBind, [], SkillProgramCardSetVisibility.Private, []);
                return SkillProgramStepOutcome.Continue;
            }
            CommitProgramHandReveal(frame.Id, holderSeat, resultBind,
                [eligible[_random.Next(eligible.Length)]]);
            return SkillProgramStepOutcome.Continue;
        }
        active = active with
        {
            RevealCardSelection = new(holderSeat, chooserSeat, resultBind,
                Array.AsReadOnly(candidates), Array.AsReadOnly(eligible), allowDecline)
        };
        _resolutionStack[^1] = active;
        PublishProgramRevealCardSelection(active);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void PublishProgramRevealCardSelection(ProgramSkillFrame frame)
    {
        var draft = frame.RevealCardSelection ?? throw new InvalidOperationException("Missing reveal draft.");
        var skill = _contentRegistry!.GetSkill(frame.SkillId);
        var choices = draft.EligibleCardIds.Select(cardId =>
        {
            var card = _cardZones.CardsAt(CardLocation.Hand(draft.HolderSeat))
                .Single(item => item.Id == cardId);
            return new PromptChoice(
                new ChoiceId($"program-reveal.frame-{frame.Id}.card-{cardId}"),
                $"观看并展示【{card.DisplayName}】（{card.RankText}）。",
                [cardId], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "reveal-target-hand-card",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["result-bind"] = draft.ResultBind
                });
        }).ToList();
        if (draft.AllowDecline)
            choices.Add(new PromptChoice(
                new ChoiceId($"program-reveal.frame-{frame.Id}.decline"),
                "不展示任何牌。", [], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "reveal-target-hand-card-decline",
                    ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["result-bind"] = draft.ResultBind
                }));
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, draft.ChooserSeat,
            $"请选择要展示的 {_players[draft.HolderSeat].Name} 的一张手牌。",
            draft.CandidateCardIds.ToArray(), [], draft.ChooserSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = draft.HolderSeat,
            SkillPrompt = new(frame.SkillId, skill.Name, $"{skill.Name} · 观看手牌", skill.Description),
            Choices = choices.AsReadOnly()
        };
        _status = _players[draft.ChooserSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveProgramRevealCardSelection(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The reveal choice lost its program frame.");
        var effect = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        var draft = frame.RevealCardSelection ??
            throw new InvalidOperationException("The reveal choice lost its suspended draft.");
        var declining =
            selected.Parameters.GetValueOrDefault("program-action") == "reveal-target-hand-card-decline";
        if (effect.Op != SkillProgramEffectOp.RevealTargetHandCard ||
            effect.ResultBind != draft.ResultBind ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("result-bind") != draft.ResultBind ||
            declining && (!draft.AllowDecline || selected.Cards.Count != 0) ||
            !declining && (selected.Cards.Count != 1 ||
                !draft.EligibleCardIds.Contains(selected.Cards[0])))
            throw new InvalidOperationException("The reveal choice does not match its suspended instruction.");
        if (!_players[frame.OwnerSeat].IsAlive || !_players[draft.HolderSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) ||
            draft.CandidateCardIds.Any(cardId =>
                _cardZones.GetLocation(cardId) != CardLocation.Hand(draft.HolderSeat)))
        {
            ClearPendingDecision();
            CancelProgramBindingAndCleanup(frame, "观看参与者或技能实例已失效，技能结算已取消。");
            return;
        }
        ClearPendingDecision();
        _resolutionStack[^1] = frame with { RevealCardSelection = null };
        if (declining)
        {
            // A decline commits an empty private binding: the chain continues with
            // nothing shown, so suit-gated branches stay dormant and no reveal event fires.
            SetProgramCardSet(frame.Id, draft.ResultBind, [], SkillProgramCardSetVisibility.Private, []);
        }
        else
        {
            CommitProgramHandReveal(frame.Id, draft.HolderSeat, draft.ResultBind, [selected.Cards[0]]);
        }
        ContinueProgramSkill(frame.Id);
    }

    private void CommitProgramHandReveal(long frameId, int holderSeat, string resultBind,
        IReadOnlyList<int> cardIds)
    {
        SetProgramCardSet(frameId, resultBind, cardIds, SkillProgramCardSetVisibility.Private,
            cardIds.Select(_ => CardLocation.Hand(holderSeat)).ToArray());
        var frame = GetActiveProgramFrame(frameId);
        RevealProgramBoundCards(frame, resultBind);
        AddLog("SkillEffect",
            $"{_players[frame.OwnerSeat].Name} 展示了 {_players[holderSeat].Name} 的一张手牌。",
            frame.OwnerSeat, holderSeat);
    }

    private void AssertProgramRevealCardSelection(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (paused.Op != SkillProgramEffectOp.RevealTargetHandCard)
        {
            if (frame.RevealCardSelection is not null)
                throw new InvalidOperationException("A reveal draft outlived its suspended instruction.");
            return;
        }
        if (frame.RevealCardSelection is not { } draft)
            throw new InvalidOperationException("A suspended reveal lost its private draft.");
        if (draft.ResultBind != paused.ResultBind || draft.ChooserSeat != frame.OwnerSeat ||
            draft.HolderSeat == frame.OwnerSeat ||
            draft.CandidateCardIds.Count == 0 ||
            draft.CandidateCardIds.Distinct().Count() != draft.CandidateCardIds.Count ||
            draft.CandidateCardIds.Any(cardId =>
                _cardZones.GetLocation(cardId) != CardLocation.Hand(draft.HolderSeat)) ||
            draft.EligibleCardIds.Any(cardId => !draft.CandidateCardIds.Contains(cardId)) ||
            frame.CardSetBindings.Any(binding => binding.Name == draft.ResultBind) ||
            !ReferenceEquals(frame, _resolutionStack.LastOrDefault()) ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } decision ||
            decision.PlayerSeat != draft.ChooserSeat ||
            decision.Choices.Count !=
                draft.EligibleCardIds.Count + (draft.AllowDecline ? 1 : 0) ||
            decision.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("result-bind") != draft.ResultBind ||
                choice.Parameters.GetValueOrDefault("program-action") switch
                {
                    "reveal-target-hand-card" => choice.Cards.Count != 1 ||
                        !draft.EligibleCardIds.Contains(choice.Cards[0]),
                    "reveal-target-hand-card-decline" => !draft.AllowDecline || choice.Cards.Count != 0,
                    _ => true
                }))
            throw new InvalidOperationException("A private reveal draft lost its exact instruction or prompt.");
    }
}
