namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void BeginSharedPindian(long parentId, SkillPromptPresentation presentation, int sourceSeat,
        int? opponentSeat = null, int? sourceCardId = null,
        string? programResultBind = null,
        SkillProgramCardSetVisibility programResultVisibility = SkillProgramCardSetVisibility.Public)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault()?.Id != parentId ||
            _resolutionStack.Any(item => item is PindianFrame) || !_players[sourceSeat].IsAlive ||
            GetHand(_players[sourceSeat]).Count == 0 || opponentSeat is { } opponent && !CanBePindianTarget(sourceSeat, opponent))
            throw new InvalidOperationException("Pindian requires one suspended parent and a living source with hand cards.");
        var frame = new PindianFrame(++_resolutionSequence, parentId, presentation.SkillId, presentation,
            sourceSeat, opponentSeat, sourceCardId,
            opponentSeat is null ? PindianStep.ChooseParticipants :
            sourceCardId is null ? PindianStep.ChooseSourceCard : PindianStep.ChooseOpponentCard,
            ProgramResultBind: programResultBind,
            ProgramResultVisibility: programResultVisibility,
            ParentProcessingCardIds: Array.AsReadOnly(_cardZones.CardsAt(CardLocation.Processing)
                .Select(card => card.Id).Order().ToArray()));
        PushRuntimeFrame(frame);
        PublishPindianSelection(frame);
    }

    private void PublishPindianSelection(PindianFrame frame)
    {
        if (TryContinuePindianRandomSelection(frame)) return;
        frame = (PindianFrame)_resolutionStack[^1];
        var selectingSource = frame.PindianStep is PindianStep.ChooseParticipants or PindianStep.ChooseSourceCard;
        var chooser = _players[selectingSource ? frame.SourceSeat : frame.OpponentSeat!.Value];
        var hand = GetHand(chooser);
        var targets = frame.PindianStep == PindianStep.ChooseParticipants ? _players.Where(player => player.IsAlive &&
            player.Seat != chooser.Seat && GetHand(player).Count > 0 && CanBePindianTarget(frame.SourceSeat, player.Seat)).Select(player => player.Seat).ToArray() : [];
        if (!chooser.IsAlive || hand.Count == 0 || frame.PindianStep == PindianStep.ChooseParticipants && targets.Length == 0 ||
            !selectingSource && (chooser.Seat == frame.SourceSeat || !CanBePindianTarget(frame.SourceSeat, chooser.Seat) ||
                !HasCurrentPindianSourceCard(frame)))
            throw new InvalidOperationException("Pindian participants no longer have legal hand cards.");
        var choices = new List<PromptChoice>();
        foreach (var card in hand)
        {
            if (frame.PindianStep == PindianStep.ChooseParticipants)
                foreach (var target in targets)
                    choices.Add(new(new ChoiceId($"pindian.{frame.Id}.card-{card.Id}.target-{target}"),
                        $"以【{card.DisplayName}】（{card.Rank}）与 {_players[target].Name} 拼点", [card.Id], [target],
                        new Dictionary<string, string> { ["action"] = "pindian-start" }));
            else if (frame.PindianStep == PindianStep.ChooseSourceCard)
                choices.Add(new(new ChoiceId($"pindian.{frame.Id}.source-card-{card.Id}"),
                    $"以【{card.DisplayName}】（{card.Rank}）发起拼点", [card.Id], [],
                    new Dictionary<string, string> { ["action"] = "pindian-source-card" }));
            else
                choices.Add(new(new ChoiceId($"pindian.{frame.Id}.card-{card.Id}"),
                    $"以【{card.DisplayName}】（{card.Rank}）参与拼点", [card.Id], [],
                    new Dictionary<string, string> { ["action"] = "pindian-card" }));
        }
        if(CanOfferPindianTopChoice(chooser))
        {
            var topTargets=frame.PindianStep==PindianStep.ChooseParticipants?targets.Select(t=>(int?)t):new int?[]{null};
            foreach(var target in topTargets) choices.Add(new(new ChoiceId($"pindian.{frame.Id}.top.target-{target}"),"使用牌堆顶的牌拼点",[],target is { } seat?[seat]:[],new Dictionary<string,string>{["action"]="pindian-top"}));
        }
        var prompt = frame.PindianStep == PindianStep.ChooseParticipants
            ? $"【{frame.Presentation.Name}】：选择自己的拼点牌及一名其他角色。"
            : frame.PindianStep == PindianStep.ChooseSourceCard
                ? $"【{frame.Presentation.Name}】：请选择自己的拼点牌。"
            : $"{_players[frame.SourceSeat].Name} 对你发动【{frame.Presentation.Name}】，请选择一张手牌作为拼点牌。";
        SetPindianPrompt(frame, chooser.Seat, DecisionKind.SkillModule,
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
        if (TryResolvePindianRandomChoice(frame, selected)) return;
        if (frame.PindianStep == PindianStep.ClaimResult)
        {
            ResolvePindianClaimChoice(selected);
            return;
        }
        ClearPendingDecision();
        var top=selected.Parameters.GetValueOrDefault("action")=="pindian-top";
        var selectingSource=frame.PindianStep is PindianStep.ChooseParticipants or PindianStep.ChooseSourceCard;
        var chooser=_players[selectingSource?frame.SourceSeat:frame.OpponentSeat!.Value];
        var selectedId=top?ReserveOrReadPindianTop(chooser,selectingSource):selected.Cards.Single();
        switch (frame.PindianStep)
        {
            case PindianStep.ChooseParticipants:
                frame = frame with { OpponentSeat = selected.Targets.Single(), SourceCardId = selectedId, SourceUsesDrawPileTop = top,
                    PindianStep = PindianStep.ChooseOpponentCard };
                ReplaceRuntimeTop(frame);
                PublishPindianSelection(frame);
                return;
            case PindianStep.ChooseSourceCard:
                frame = frame with { SourceCardId = selectedId, SourceUsesDrawPileTop = top, PindianStep = PindianStep.ChooseOpponentCard };
                ReplaceRuntimeTop(frame);
                PublishPindianSelection(frame);
                return;
            case PindianStep.ChooseOpponentCard:
                RevealPindian(frame, selectedId, top);
                return;
            default: throw new InvalidOperationException("Unknown Pindian step.");
        }
    }

    private void RevealPindian(PindianFrame frame, int opponentCardId, bool opponentTop = false)
    {
        var source = _players[frame.SourceSeat];
        var opponent = _players[frame.OpponentSeat!.Value];
        var sourceCard = _cardZones.CardsAt(frame.SourceUsesDrawPileTop?CardLocation.Processing:CardLocation.Hand(source.Seat)).Single(card => card.Id == frame.SourceCardId);
        var opponentCard = _cardZones.CardsAt(opponentTop?CardLocation.DrawPile:CardLocation.Hand(opponent.Seat)).Single(card => card.Id == opponentCardId);
        var sourceIdentityRank = CaptureAlcoholPindianRank(frame, source, sourceCard, true, frame.SourceUsesDrawPileTop);
        var opponentIdentityRank = CaptureAlcoholPindianRank(frame, opponent, opponentCard, false, opponentTop);
        if(!frame.SourceUsesDrawPileTop) MoveCard(sourceCard, CardLocation.Hand(source.Seat), CardLocation.Processing, CardMoveReasons.PindianReveal);
        MoveCard(opponentCard, opponentTop?CardLocation.DrawPile:CardLocation.Hand(opponent.Seat), CardLocation.Processing, CardMoveReasons.PindianReveal);
        var result = new PindianResult(source.Seat, opponent.Seat, sourceCard.Id, opponentCard.Id, sourceIdentityRank ?? EffectivePindianRank(source,sourceCard), opponentIdentityRank ?? EffectivePindianRank(opponent,opponentCard));
        AdvanceEventRulesAndQueueFact(new PindianResultDeterminedEvent(frame.Id, frame.SkillId, result));
        AddLog("Pindian", $"{source.Name} 以 {result.SourceRank} 点与 {opponent.Name} 的 {result.OpponentRank} 点拼点，" +
            (result.SourceWon ? "发起者获胜。" : "发起者未赢。"), source.Seat, opponent.Seat);
        frame = frame with { Result = result };
        ReplaceRuntimeTop(frame);
        if (!BeginPindianClaims(frame)) CompletePindian((PindianFrame)_resolutionStack[^1]);
    }

    private int[] AvailablePindianCards(PindianResult result) => new[] { result.SourceCardId, result.OpponentCardId }
        .Where(id => _cardZones.GetLocation(id) == CardLocation.Processing).ToArray();

    private void CompletePindian(PindianFrame frame)
    {
        foreach (var id in AvailablePindianCards(frame.Result!))
        {
            var card = _cardZones.CardsAt(CardLocation.Processing).Single(card => card.Id == id);
            MoveCard(card, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.PindianFinish);
        }
        PopResolutionFrame(frame.Id, ResolutionFrameKind.Pindian);
        if (_resolutionStack[^1] is ProgramSkillFrame program)
        {
            if (frame.ProgramResultBind is not { } bind ||
                program.PindianResultBindings.Any(item => item.Name == bind))
                throw new InvalidOperationException("Program Pindian lost its unique result binding.");
            var result = frame.Result!;
            ReplaceRuntimeTop(program with
            {
                PindianResultBindings = Array.AsReadOnly(program.PindianResultBindings.Append(
                    new ProgramPindianResultBinding(
                        bind, result.SourceSeat, result.OpponentSeat,
                        result.SourceRank, result.OpponentRank, result.SourceWon,
                        frame.ProgramResultVisibility)).ToArray())
            });
            AdvanceRuntimeProgram(program.Id);
        }
        else throw new InvalidOperationException("Pindian lost its parent continuation.");
    }

    private void ResolvePendingAiPindian()
    {
        var frame = (PindianFrame)_resolutionStack[^1];
        var decision = _pendingDecision!;
        if (IsPindianRandomPrompt(frame))
        {
            ResolvePindianChoice(decision.Choices.First(choice => choice.Parameters.GetValueOrDefault("take") == "true"));
            AdvanceRulesAndPublishState();
            return;
        }
        if (frame.PindianStep == PindianStep.ClaimResult)
        {
            ResolvePindianChoice(decision.Choices.First(choice => choice.Parameters.GetValueOrDefault("take") == "true"));
            AdvanceRulesAndPublishState();
            return;
        }
        var selected = decision.Choices.OrderByDescending(choice => choice.Parameters.GetValueOrDefault("action")=="pindian-top"?9:
                EffectivePindianRank(_players[decision.PlayerSeat],GetHand(_players[decision.PlayerSeat]).Single(card => card.Id == choice.Cards.Single())))
                .ThenBy(choice => choice.Cards.FirstOrDefault()).ThenBy(choice => choice.Targets.FirstOrDefault()).First();
        ResolvePindianChoice(selected);
        AdvanceRulesAndPublishState();
    }

    private bool HasPindianChild(long parentId) => _resolutionStack.LastOrDefault() is PindianFrame frame &&
        frame.ParentFrameId == parentId;

    private void AssertPindianInvariant()
    {
        var frames = _resolutionStack.OfType<PindianFrame>().ToArray();
        if (frames.Length == 0) return;
        if (frames.Length != 1 || _resolutionStack.Count < 2 || _resolutionStack[^1] != frames[0] ||
            _resolutionStack[^2].Id != frames[0].ParentFrameId ||
            _resolutionStack[^2] is not ProgramSkillFrame ||
            _pendingDecision is not { IsPrivate: true, SkillPrompt: not null } decision || decision.Choices.Count == 0)
            throw new InvalidOperationException("Pindian must retain one parent and one private prompt.");
        var frame = frames[0];
        var parentCards = frame.ParentProcessingCardIds ?? [];
        var contestCards = CurrentPindianProcessingIds(frame);
        if (_cardZones.CardsAt(CardLocation.Processing).Select(card => card.Id).Order()
                .SequenceEqual(parentCards.Concat(contestCards).Order()) == false ||
            parentCards.Any(id => _cardZones.GetLocation(id) != CardLocation.Processing))
            throw new InvalidOperationException("Pindian lost its parent card or acquired an unrelated processing card.");
        if (!_players[frame.SourceSeat].IsAlive || frame.OpponentSeat is { } opponent &&
            (opponent == frame.SourceSeat || !_players[opponent].IsAlive) ||
            _status != (_players[decision.PlayerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running))
            throw new InvalidOperationException("Pindian prompt owner or participants are inconsistent.");
        if (AssertPindianRandomSelection(frame, decision)) return;
        if (frame.PindianStep == PindianStep.ClaimResult)
        {
            AssertPindianPolicyClaims(frame);
            if (frame.Result is null || frame.ClaimSeats is null || frame.ClaimIndex < 0 || frame.ClaimIndex >= frame.ClaimSeats.Count ||
                decision.PlayerSeat != frame.ClaimSeats[frame.ClaimIndex] ||
                decision.Choices.Any(choice => choice.Parameters.GetValueOrDefault("action") != "pindian-claim" ||
                    choice.Cards.Any(id => !contestCards.Contains(id))))
                throw new InvalidOperationException("Pindian claim lost its public card or claimant.");
            return;
        }
            var owner = frame.PindianStep is PindianStep.ChooseParticipants or PindianStep.ChooseSourceCard
                ? frame.SourceSeat : frame.OpponentSeat!.Value;
            if (decision.PlayerSeat != owner ||
                decision.SkillPrompt.SkillId != frame.SkillId ||
                decision.Choices.Any(choice => choice.Parameters.GetValueOrDefault("action")=="pindian-top"
                    ? choice.Cards.Count!=0 || !CanOfferPindianTopChoice(_players[owner])
                    : choice.Cards.Count != 1 || !GetHand(_players[owner]).Any(card => card.Id == choice.Cards[0])) ||
                frame.PindianStep == PindianStep.ChooseOpponentCard &&
                !HasCurrentPindianSourceCard(frame))
                throw new InvalidOperationException("Pindian selection leaked or lost a private hand card.");
    }

}
