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
        if (chooserSeat != frame.OwnerSeat || holderSeat == frame.OwnerSeat &&
            mode != SkillProgramRevealMode.SelfChoiceOtherwiseRandom)
            throw new InvalidOperationException(
                "A hand reveal must target another character's hand and be chosen by the skill owner.");
        var candidates = GetHand(_players[holderSeat]).Select(card => card.Id).ToArray();
        if (candidates.Length == 0)
        {
            CancelProgramBindingAndCleanup(active, "目标没有手牌，技能结算已取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        // The chooser always views the whole hand; the suit filter only limits
        // which cards may actually be revealed.
        var eligible = eligibleSuits.Count == 0 ? candidates
            : candidates.Where(cardId => eligibleSuits.Contains(EffectiveSuit(
                _players[holderSeat],
                _cardZones.CardsAt(CardLocation.Hand(holderSeat)).Single(card => card.Id == cardId))))
                .ToArray();
        if ((mode is SkillProgramRevealMode.Random or SkillProgramRevealMode.SelfChoiceOtherwiseRandom) && eligible.Length == 0 ||
            mode == SkillProgramRevealMode.Chooser && eligible.Length == 0 && !allowDecline)
        {
            CancelProgramBindingAndCleanup(active, "目标没有符合条件的牌，技能结算已取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        if (mode == SkillProgramRevealMode.Random ||
            mode == SkillProgramRevealMode.SelfChoiceOtherwiseRandom && holderSeat != chooserSeat)
        {
            CommitProgramHandReveal(frame.Id, holderSeat, resultBind,
                [eligible[_random.Next(eligible.Length)]]);
            return SkillProgramStepOutcome.Continue;
        }
        active = active with
        {
            RevealCardSelection = new(holderSeat, chooserSeat, resultBind,
                Array.AsReadOnly(candidates), Array.AsReadOnly(eligible), allowDecline)
        };
        ReplaceRuntimeTop(active);
        PublishProgramRevealCardSelection(active);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void PublishProgramRevealCardSelection(ProgramSkillFrame frame)
    {
        var draft = frame.RevealCardSelection ?? throw new InvalidOperationException("Missing reveal draft.");
        var skill = _contentRegistry!.GetSkill(frame.SkillId);
        var hand = _cardZones.CardsAt(CardLocation.Hand(draft.HolderSeat));
        var choices = draft.EligibleCardIds.Select(cardId =>
        {
            var card = hand.Single(item => item.Id == cardId);
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
                "不展示其手牌。",
                [], [],
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
            PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = draft.HolderSeat,
            SkillPrompt = new(frame.SkillId, skill.Name, $"{skill.Name} · 观看手牌", skill.Description),
            Choices = Array.AsReadOnly(choices.ToArray())
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
        var decline = selected.Parameters.GetValueOrDefault("program-action") == "reveal-target-hand-card-decline";
        if (effect.Op != SkillProgramEffectOp.RevealTargetHandCard ||
            effect.ResultBind != draft.ResultBind ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("result-bind") != draft.ResultBind ||
            decline && (!draft.AllowDecline || selected.Cards.Count != 0) ||
            !decline && (selected.Cards.Count != 1 || !draft.EligibleCardIds.Contains(selected.Cards[0])))
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
        ReplaceRuntimeTop(frame with { RevealCardSelection = null });
        CommitProgramHandReveal(frame.Id, draft.HolderSeat, draft.ResultBind,
            decline ? [] : [selected.Cards[0]]);
        AdvanceRuntimeProgram(frame.Id);
    }

    private void CommitProgramHandReveal(long frameId, int holderSeat, string resultBind,
        IReadOnlyList<int> cardIds)
    {
        var frame = GetActiveProgramFrame(frameId);
        if (cardIds.Count == 0)
        {
            // 攻心-style decline: the viewing happened, but nothing is revealed,
            // so downstream suit-gated effects see an empty private binding.
            SetProgramCardSet(frameId, resultBind, [], SkillProgramCardSetVisibility.Private, []);
            AddLog("SkillEffect",
                $"{_players[frame.OwnerSeat].Name} 观看了 {_players[holderSeat].Name} 的手牌，但没有展示任何牌。",
                frame.OwnerSeat, holderSeat);
            return;
        }
        SetProgramCardSet(frameId, resultBind, cardIds, SkillProgramCardSetVisibility.Private,
            cardIds.Select(_ => CardLocation.Hand(holderSeat)).ToArray());
        frame = GetActiveProgramFrame(frameId);
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
        var expectedChoices = draft.EligibleCardIds.Count + (draft.AllowDecline ? 1 : 0);
        if (draft.ResultBind != paused.ResultBind || draft.ChooserSeat != frame.OwnerSeat ||
            (draft.HolderSeat == frame.OwnerSeat && paused.RevealMode != SkillProgramRevealMode.SelfChoiceOtherwiseRandom) ||
            draft.AllowDecline != paused.AllowDecline ||
            draft.CandidateCardIds.Count == 0 ||
            draft.CandidateCardIds.Distinct().Count() != draft.CandidateCardIds.Count ||
            draft.EligibleCardIds.Count == 0 && !draft.AllowDecline ||
            draft.EligibleCardIds.Any(cardId => !draft.CandidateCardIds.Contains(cardId)) ||
            draft.EligibleCardIds.Distinct().Count() != draft.EligibleCardIds.Count ||
            draft.CandidateCardIds.Any(cardId =>
                _cardZones.GetLocation(cardId) != CardLocation.Hand(draft.HolderSeat)) ||
            frame.CardSetBindings.Any(binding => binding.Name == draft.ResultBind) ||
            !ReferenceEquals(frame, _resolutionStack.LastOrDefault()) ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } decision ||
            decision.PlayerSeat != draft.ChooserSeat ||
            decision.Choices.Count != expectedChoices ||
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
