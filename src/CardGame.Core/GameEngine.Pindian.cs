namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void BeginSharedPindian(long parentId, SkillPromptPresentation presentation, int sourceSeat,
        int? opponentSeat = null, int? sourceCardId = null, SkillKind? legacySkill = null)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault()?.Id != parentId ||
            _resolutionStack.Any(item => item is PindianFrame) || !_players[sourceSeat].IsAlive ||
            GetHand(_players[sourceSeat]).Count == 0)
            throw new InvalidOperationException("Pindian requires one suspended parent and a living source with hand cards.");
        var frame = new PindianFrame(++_resolutionSequence, parentId, presentation.SkillId, presentation,
            sourceSeat, opponentSeat, sourceCardId, legacySkill,
            opponentSeat is null ? PindianStep.ChooseParticipants : PindianStep.ChooseOpponentCard);
        _resolutionStack.Add(frame);
        PublishPindianSelection(frame);
    }

    private static (DecisionKind Kind, string Action) PindianCompatibility(SkillKind? legacy) => legacy switch
    {
        SkillKind.Quhu => (DecisionKind.QuhuPindian, "quhu-pindian"),
        SkillKind.Tianyi => (DecisionKind.TianyiPindian, "tianyi-pindian"),
        SkillKind.Xianzhen => (DecisionKind.XianzhenPindian, "xianzhen-pindian"),
        _ => (DecisionKind.SkillModule, "pindian-card")
    };

    private void PublishPindianSelection(PindianFrame frame)
    {
        var selectingSource = frame.PindianStep == PindianStep.ChooseParticipants;
        var chooser = _players[selectingSource ? frame.SourceSeat : frame.OpponentSeat!.Value];
        var hand = GetHand(chooser);
        var targets = selectingSource ? _players.Where(player => player.IsAlive &&
            player.Seat != chooser.Seat && GetHand(player).Count > 0).Select(player => player.Seat).ToArray() : [];
        if (!chooser.IsAlive || hand.Count == 0 || selectingSource && targets.Length == 0 ||
            !selectingSource && (chooser.Seat == frame.SourceSeat ||
                !GetHand(_players[frame.SourceSeat]).Any(card => card.Id == frame.SourceCardId)))
            throw new InvalidOperationException("Pindian participants no longer have legal hand cards.");
        var compatibility = PindianCompatibility(frame.LegacySkill);
        var choices = new List<PromptChoice>();
        foreach (var card in hand)
        {
            if (selectingSource)
                foreach (var target in targets)
                    choices.Add(new(new ChoiceId($"pindian.{frame.Id}.card-{card.Id}.target-{target}"),
                        $"以【{card.DisplayName}】（{card.Rank}）与 {_players[target].Name} 拼点", [card.Id], [target],
                        new Dictionary<string, string> { ["action"] = "pindian-start" }));
            else
                choices.Add(new(new ChoiceId($"pindian.{frame.Id}.card-{card.Id}"),
                    $"以【{card.DisplayName}】（{card.Rank}）参与拼点", [card.Id], [],
                    new Dictionary<string, string> { ["action"] = compatibility.Action }));
        }
        var prompt = selectingSource ? $"【{frame.Presentation.Name}】：选择自己的拼点牌及一名其他角色。"
            : $"{_players[frame.SourceSeat].Name} 对你发动【{frame.Presentation.Name}】，请选择一张手牌作为拼点牌。";
        SetPindianPrompt(frame, chooser.Seat, selectingSource ? DecisionKind.SkillModule : compatibility.Kind,
            prompt, choices, hand.Select(card => card.Id).ToArray(), targets,
            frame.Presentation with { Title = $"{frame.Presentation.Name} · 拼点", Instructions = prompt });
    }

    private void SetPindianPrompt(PindianFrame frame, int playerSeat, DecisionKind kind, string prompt,
        IReadOnlyList<PromptChoice> choices, IReadOnlyList<int> cards, IReadOnlyList<int> targets,
        SkillPromptPresentation presentation)
    {
        _pendingDecision = new PendingDecision(kind, playerSeat, prompt, cards, targets, SourceSeat: frame.SourceSeat)
        {
            PromptId = CreatePromptId(), TargetSeat = playerSeat, IsPrivate = true,
            SkillPrompt = presentation, Choices = Array.AsReadOnly(choices.ToArray())
        };
        _status = _players[playerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolvePindianChoice(PromptChoice selected)
    {
        AssertPindianInvariant();
        var frame = (PindianFrame)_resolutionStack[^1];
        if (_pendingDecision!.Choices.All(choice => choice.Id != selected.Id))
            throw new InvalidOperationException("The Pindian answer was not published.");
        ClearPendingDecision();
        switch (frame.PindianStep)
        {
            case PindianStep.ChooseParticipants:
                frame = frame with { OpponentSeat = selected.Targets.Single(), SourceCardId = selected.Cards.Single(),
                    PindianStep = PindianStep.ChooseOpponentCard };
                _resolutionStack[^1] = frame;
                PublishPindianSelection(frame);
                return;
            case PindianStep.ChooseOpponentCard:
                RevealPindian(frame, selected.Cards.Single());
                return;
            case PindianStep.AfterResult:
                var candidate = frame.Candidates![frame.CandidateIndex];
                var plan = frame.ClaimPlan!;
                var used = selected.Parameters.GetValueOrDefault("action") == "pindian-claim";
                // Commit the cursor before moving a card; the same claim cannot execute twice.
                frame = frame with { CandidateIndex = frame.CandidateIndex + 1, ClaimPlan = null };
                _resolutionStack[^1] = frame;
                if (used)
                {
                    var card = _cardZones.CardsAt(CardLocation.Processing).Single(card => card.Id == plan.CardId);
                    MoveCard(card, CardLocation.Processing, CardLocation.Hand(candidate.OwnerSeat),
                        new CardMoveReason("skill.pindian.claim"));
                    QueueGameEvent(new PindianCardClaimedEvent(frame.Id, candidate.SkillId, candidate.OwnerSeat, card.Id));
                }
                QueueGameEvent(new SkillModuleResolvedEvent(frame.Id, candidate.SkillId, candidate.OwnerSeat, used));
                ContinuePindianResults(frame);
                return;
            default: throw new InvalidOperationException("Unknown Pindian step.");
        }
    }

    private void RevealPindian(PindianFrame frame, int opponentCardId)
    {
        var source = _players[frame.SourceSeat];
        var opponent = _players[frame.OpponentSeat!.Value];
        var sourceCard = GetHand(source).Single(card => card.Id == frame.SourceCardId);
        var opponentCard = GetHand(opponent).Single(card => card.Id == opponentCardId);
        MoveCard(sourceCard, CardLocation.Hand(source.Seat), CardLocation.Processing, CardMoveReasons.PindianReveal);
        MoveCard(opponentCard, CardLocation.Hand(opponent.Seat), CardLocation.Processing, CardMoveReasons.PindianReveal);
        var result = new PindianResult(source.Seat, opponent.Seat, sourceCard.Id, opponentCard.Id, sourceCard.Rank, opponentCard.Rank);
        QueueGameEvent(new PindianResultDeterminedEvent(frame.Id, frame.SkillId, result));
        if (frame.LegacySkill is { } legacy)
            QueueGameEvent(new PindianResolvedEvent(frame.ParentFrameId, legacy, source.Seat, opponent.Seat,
                sourceCard.Id, opponentCard.Id, sourceCard.Rank, opponentCard.Rank, result.SourceWon));
        AddLog("Pindian", $"{source.Name} 以 {sourceCard.Rank} 点与 {opponent.Name} 的 {opponentCard.Rank} 点拼点，" +
            (result.SourceWon ? "发起者获胜。" : "发起者未赢。"), source.Seat, opponent.Seat);
        var candidates = _players.Where(player => player.Seat == source.Seat || player.Seat == opponent.Seat)
            .OrderBy(player => (player.Seat - _currentSeat + _playerCount) % _playerCount)
            .SelectMany(player => EnabledContentSkillIds(player).OrderBy(id => id, StringComparer.Ordinal)
                .Where(id => _contentRegistry!.Skills[id].PindianResultSkill is not null)
                .Select(id => new PindianTriggerCandidate(player.Seat, id))).ToArray();
        frame = frame with { PindianStep = PindianStep.AfterResult, Result = result,
            Candidates = Array.AsReadOnly(candidates) };
        _resolutionStack[^1] = frame;
        ContinuePindianResults(frame);
    }

    private int[] AvailablePindianCards(PindianResult result) => new[] { result.SourceCardId, result.OpponentCardId }
        .Where(id => _cardZones.GetLocation(id) == CardLocation.Processing).ToArray();

    private void ContinuePindianResults(PindianFrame frame)
    {
        while (frame.CandidateIndex < frame.Candidates!.Count)
        {
            var candidate = frame.Candidates[frame.CandidateIndex];
            var owner = _players[candidate.OwnerSeat];
            var available = AvailablePindianCards(frame.Result!);
            if (owner.IsAlive && HasRuntimeSkill(owner, candidate.SkillId) &&
                _contentRegistry!.Skills[candidate.SkillId].PindianResultSkill!.CreatePlan(new(
                    owner.Seat, frame.SkillId, frame.Result!, Array.AsReadOnly(available))) is { } plan)
            {
                if (plan.Presentation.SkillId != candidate.SkillId || string.IsNullOrWhiteSpace(plan.Presentation.Name) ||
                    string.IsNullOrWhiteSpace(plan.Prompt) || !available.Contains(plan.CardId))
                    throw new InvalidOperationException("A Pindian result module may only claim an available contest card.");
                frame = frame with { ClaimPlan = plan };
                _resolutionStack[^1] = frame;
                PromptChoice Choice(string action, string text) => new(
                    new ChoiceId($"pindian.{frame.Id}.result-{frame.CandidateIndex}.{action}"), text, [], [],
                    new Dictionary<string, string> { ["action"] = action });
                SetPindianPrompt(frame, owner.Seat, DecisionKind.SkillModule, plan.Prompt,
                    [Choice("pindian-claim", $"发动【{plan.Presentation.Name}】，获得拼点牌"),
                     Choice("pindian-skip", $"不发动【{plan.Presentation.Name}】")], [], [], plan.Presentation);
                return;
            }
            frame = frame with { CandidateIndex = frame.CandidateIndex + 1 };
            _resolutionStack[^1] = frame;
        }
        foreach (var id in AvailablePindianCards(frame.Result!))
        {
            var card = _cardZones.CardsAt(CardLocation.Processing).Single(card => card.Id == id);
            MoveCard(card, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.PindianFinish);
        }
        PopResolutionFrame(frame.Id, ResolutionFrameKind.Pindian);
        if (_resolutionStack[^1] is PhaseSkillFrame phase)
        {
            phase = phase with { Pindian = frame.Result };
            _resolutionStack[^1] = phase;
            ContinuePhaseSkill(phase);
        }
        else if (_resolutionStack[^1] is ActiveSkillFrame active)
        {
            // Compatibility adapters own only their result effects, never selection or card cleanup.
            switch (active.Skill)
            {
                case SkillKind.Tianyi: CompleteTianyiPindian(active.Id, frame.Result!); break;
                case SkillKind.Xianzhen: CompleteXianzhenPindian(active.Id, frame.Result!); break;
                case SkillKind.Quhu: CompleteQuhuPindian(frame.Result!); break;
                default: throw new InvalidOperationException("Unsupported legacy Pindian parent.");
            }
        }
        else throw new InvalidOperationException("Pindian lost its parent continuation.");
    }

    private void ResolvePendingAiPindian()
    {
        var frame = (PindianFrame)_resolutionStack[^1];
        var decision = _pendingDecision!;
        var selected = frame.PindianStep == PindianStep.AfterResult
            ? decision.Choices[frame.ClaimPlan!.AiPrefersActivation ? 0 : 1]
            : decision.Choices.OrderByDescending(choice => GetHand(_players[decision.PlayerSeat])
                    .Single(card => card.Id == choice.Cards.Single()).Rank)
                .ThenBy(choice => choice.Cards.Single()).ThenBy(choice => choice.Targets.FirstOrDefault()).First();
        ResolvePindianChoice(selected);
        PublishState();
    }

    private bool HasPindianChild(long parentId) => _resolutionStack.LastOrDefault() is PindianFrame frame &&
        frame.ParentFrameId == parentId;

    private void AssertPindianInvariant()
    {
        var frames = _resolutionStack.OfType<PindianFrame>().ToArray();
        if (frames.Length == 0) return;
        if (frames.Length != 1 || _resolutionStack.Count != 2 || _resolutionStack[^1] != frames[0] ||
            _resolutionStack[0].Id != frames[0].ParentFrameId ||
            _resolutionStack[0] is not (PhaseSkillFrame or ActiveSkillFrame) ||
            _pendingDecision is not { IsPrivate: true, SkillPrompt: not null } decision || decision.Choices.Count == 0)
            throw new InvalidOperationException("Pindian must retain one parent and one private prompt.");
        var frame = frames[0];
        if (!_players[frame.SourceSeat].IsAlive || frame.OpponentSeat is { } opponent &&
            (opponent == frame.SourceSeat || !_players[opponent].IsAlive) ||
            _status != (_players[decision.PlayerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running))
            throw new InvalidOperationException("Pindian prompt owner or participants are inconsistent.");
        if (frame.PindianStep == PindianStep.AfterResult)
        {
            if (frame.Result is not { } result || frame.Candidates is null ||
                frame.CandidateIndex >= frame.Candidates.Count || frame.ClaimPlan is not { } plan ||
                decision.PlayerSeat != frame.Candidates[frame.CandidateIndex].OwnerSeat ||
                decision.SkillPrompt.SkillId != frame.Candidates[frame.CandidateIndex].SkillId ||
                !AvailablePindianCards(result).Contains(plan.CardId) || decision.Choices.Count != 2 ||
                _cardZones.CardsAt(CardLocation.Processing).Any(card =>
                    card.Id != result.SourceCardId && card.Id != result.OpponentCardId))
                throw new InvalidOperationException("Pindian result prompt lost its candidate or physical card.");
        }
        else
        {
            var owner = frame.PindianStep == PindianStep.ChooseParticipants ? frame.SourceSeat : frame.OpponentSeat!.Value;
            if (decision.PlayerSeat != owner || _cardZones.Count(CardLocation.Processing) != 0 ||
                decision.SkillPrompt.SkillId != frame.SkillId ||
                decision.Choices.Any(choice => choice.Cards.Count != 1 ||
                    !GetHand(_players[owner]).Any(card => card.Id == choice.Cards[0])) ||
                frame.PindianStep == PindianStep.ChooseOpponentCard &&
                !GetHand(_players[frame.SourceSeat]).Any(card => card.Id == frame.SourceCardId))
                throw new InvalidOperationException("Pindian selection leaked or lost a private hand card.");
        }
    }
}
