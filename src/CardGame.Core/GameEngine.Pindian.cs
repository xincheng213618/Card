namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void BeginTianyiPindian(CharacterState source, int sourceCardId, int opponentSeat, long frameId)
    {
        var opponent = _players[opponentSeat];
        if (!UsesFormalTaishiCi || !HasRuntimeSkill(source, SkillKind.Tianyi) ||
            !GetHand(source).Any(card => card.Id == sourceCardId) ||
            !opponent.IsAlive || opponent.Seat == source.Seat || GetHand(opponent).Count == 0 ||
            source.UsedActiveSkillKinds.Contains(SkillKind.Tianyi))
            throw new InvalidOperationException("The selected Tianyi Pindian is no longer legal.");
        source.UsedActiveSkillKinds.Add(SkillKind.Tianyi);
        SetActiveSkillFrameStep(frameId, ResolutionFrameStep.AwaitingResponse);
        BeginSharedPindian(frameId, new("classic:tianyi", "天义", "天义", "选择拼点牌"),
            source.Seat, opponentSeat, sourceCardId, SkillKind.Tianyi);
    }

    private void CompleteTianyiPindian(long frameId, PindianResult result)
    {
        var source = _players[result.SourceSeat];
        source.TianyiWonThisTurn = result.SourceWon;
        source.TianyiLostThisTurn = !result.SourceWon;
        SetActiveSkillFrameStep(frameId, ResolutionFrameStep.Completed);
        QueueGameEvent(new ActiveSkillResolvedEvent(frameId, source.Seat, SkillKind.Tianyi,
            ActiveSkillEffectKind.PindianForSlashBonus));
        PopResolutionFrame(frameId, ResolutionFrameKind.ActiveSkill);
    }

    private void BeginSharedPindian(long parentId, SkillPromptPresentation presentation, int sourceSeat,
        int? opponentSeat = null, int? sourceCardId = null, SkillKind? legacySkill = null,
        string? programResultBind = null,
        SkillProgramCardSetVisibility programResultVisibility = SkillProgramCardSetVisibility.Public)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault()?.Id != parentId ||
            _resolutionStack.Any(item => item is PindianFrame) || !_players[sourceSeat].IsAlive ||
            GetHand(_players[sourceSeat]).Count == 0)
            throw new InvalidOperationException("Pindian requires one suspended parent and a living source with hand cards.");
        var frame = new PindianFrame(++_resolutionSequence, parentId, presentation.SkillId, presentation,
            sourceSeat, opponentSeat, sourceCardId, legacySkill,
            opponentSeat is null ? PindianStep.ChooseParticipants :
            sourceCardId is null ? PindianStep.ChooseSourceCard : PindianStep.ChooseOpponentCard,
            ProgramResultBind: programResultBind,
            ProgramResultVisibility: programResultVisibility);
        _resolutionStack.Add(frame);
        PublishPindianSelection(frame);
    }

    private static (DecisionKind Kind, string Action) PindianCompatibility(SkillKind? legacy) => legacy switch
    {
        SkillKind.Quhu => (DecisionKind.QuhuPindian, "quhu-pindian"),
        SkillKind.Tianyi => (DecisionKind.TianyiPindian, "tianyi-pindian"),
        _ => (DecisionKind.SkillModule, "pindian-card")
    };

    private void PublishPindianSelection(PindianFrame frame)
    {
        var selectingSource = frame.PindianStep is PindianStep.ChooseParticipants or PindianStep.ChooseSourceCard;
        var chooser = _players[selectingSource ? frame.SourceSeat : frame.OpponentSeat!.Value];
        var hand = GetHand(chooser);
        var targets = frame.PindianStep == PindianStep.ChooseParticipants ? _players.Where(player => player.IsAlive &&
            player.Seat != chooser.Seat && GetHand(player).Count > 0).Select(player => player.Seat).ToArray() : [];
        if (!chooser.IsAlive || hand.Count == 0 || frame.PindianStep == PindianStep.ChooseParticipants && targets.Length == 0 ||
            !selectingSource && (chooser.Seat == frame.SourceSeat ||
                !GetHand(_players[frame.SourceSeat]).Any(card => card.Id == frame.SourceCardId)))
            throw new InvalidOperationException("Pindian participants no longer have legal hand cards.");
        var compatibility = PindianCompatibility(frame.LegacySkill);
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
                    new Dictionary<string, string> { ["action"] = compatibility.Action }));
        }
        var prompt = frame.PindianStep == PindianStep.ChooseParticipants
            ? $"【{frame.Presentation.Name}】：选择自己的拼点牌及一名其他角色。"
            : frame.PindianStep == PindianStep.ChooseSourceCard
                ? $"【{frame.Presentation.Name}】：请选择自己的拼点牌。"
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
            case PindianStep.ChooseSourceCard:
                frame = frame with { SourceCardId = selected.Cards.Single(), PindianStep = PindianStep.ChooseOpponentCard };
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
                case SkillKind.Quhu: CompleteQuhuPindian(frame.Result!); break;
                default: throw new InvalidOperationException("Unsupported legacy Pindian parent.");
            }
        }
        else if (_resolutionStack[^1] is ProgramSkillFrame program)
        {
            if (frame.ProgramResultBind is not { } bind ||
                program.PindianResultBindings.Any(item => item.Name == bind))
                throw new InvalidOperationException("Program Pindian lost its unique result binding.");
            var result = frame.Result!;
            _resolutionStack[^1] = program with
            {
                PindianResultBindings = Array.AsReadOnly(program.PindianResultBindings.Append(
                    new ProgramPindianResultBinding(
                        bind, result.SourceSeat, result.OpponentSeat,
                        result.SourceRank, result.OpponentRank, result.SourceWon,
                        frame.ProgramResultVisibility)).ToArray())
            };
            ContinueProgramSkill(program.Id);
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
            _resolutionStack[0] is not (PhaseSkillFrame or ActiveSkillFrame or ProgramSkillFrame) ||
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
            var owner = frame.PindianStep is PindianStep.ChooseParticipants or PindianStep.ChooseSourceCard
                ? frame.SourceSeat : frame.OpponentSeat!.Value;
            if (decision.PlayerSeat != owner || _cardZones.Count(CardLocation.Processing) != 0 ||
                decision.SkillPrompt.SkillId != frame.SkillId ||
                decision.Choices.Any(choice => choice.Cards.Count != 1 ||
                    !GetHand(_players[owner]).Any(card => card.Id == choice.Cards[0])) ||
                frame.PindianStep == PindianStep.ChooseOpponentCard &&
                !GetHand(_players[frame.SourceSeat]).Any(card => card.Id == frame.SourceCardId))
                throw new InvalidOperationException("Pindian selection leaked or lost a private hand card.");
        }
    }

    // Lieren is still a legacy staged adapter, but all of its private choices,
    // reveal and cleanup belong to the Pindian subsystem. Keeping it here makes
    // the remaining migration seam explicit instead of exposing another
    // character-named engine partial.

    private enum LierenStage { Offer, OpponentPindian, Gain }

    private sealed class LierenResolution(AttackResolution attack)
    {
        public AttackResolution Attack { get; } = attack;
        public LierenStage Stage { get; set; }
        public int? OwnerCardId { get; set; }
        public int? TargetCardId { get; set; }
    }

    private bool TryBeginLierenChoice(AttackResolution attack)
    {
        if (!UsesFormalZhuRong || attack.LierenAttempted || !attack.DamageWasApplied ||
            attack.IsChainPropagation || attack.SourceSkill is not null ||
            attack.EffectiveCardKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash))
            return false;
        attack.MarkLierenAttempted();
        var owner = _players[attack.SourceSeat];
        var target = _players[attack.TargetSeat];
        if (!owner.IsAlive || !target.IsAlive || !HasRuntimeSkill(owner, SkillKind.Lieren) ||
            GetHand(owner).Count == 0 || GetHand(target).Count == 0)
            return false;
        _pendingLieren = new LierenResolution(attack) { Stage = LierenStage.Offer };
        PublishLierenOffer(_pendingLieren);
        return true;
    }

    private void PublishLierenOffer(LierenResolution pending)
    {
        var owner = _players[pending.Attack.SourceSeat];
        var target = _players[pending.Attack.TargetSeat];
        var choices = GetHand(owner).Select(card => new PromptChoice(
                new ChoiceId($"lieren.use.{card.Id}"), $"以【{card.DisplayName}】（{card.Rank}）发动【烈刃】。",
                [card.Id], [target.Seat], new Dictionary<string, string> { ["action"] = "lieren-use" }))
            .Append(new PromptChoice(new ChoiceId("lieren.skip"), "不发动【烈刃】。", [], [],
                new Dictionary<string, string> { ["action"] = "lieren-skip" })).ToArray();
        _pendingDecision = new PendingDecision(DecisionKind.Lieren, owner.Seat,
            $"你对 {target.Name} 使用【杀】造成了伤害，是否发动【烈刃】？",
            GetHand(owner).Select(card => card.Id).ToArray(), [target.Seat])
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private CommandResult SubmitLierenPromptAnswer(PromptChoice selected)
    {
        if (_pendingLieren is null || _pendingDecision is not { Kind: DecisionKind.Lieren })
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的烈刃窗口。");
        return Accept(() =>
        {
            ResolveLierenChoice(selected);
            PublishState();
            return _options.AdvanceAfterHumanCommands ? AdvanceToHumanBoundary() : BuildResult();
        });
    }

    private void ResolveLierenChoice(PromptChoice selected)
    {
        var pending = _pendingLieren ?? throw new InvalidOperationException("There is no Lieren resolution.");
        var action = selected.Parameters.GetValueOrDefault("action");
        if (pending.Stage == LierenStage.Offer)
        {
            if (action == "lieren-skip" && selected.Cards.Count == 0)
            {
                FinishLieren(pending, used: false, won: false, gainedCardId: null);
                return;
            }
            if (action != "lieren-use" || selected.Cards.Count != 1)
                throw new InvalidOperationException("The Lieren offer choice is malformed.");
            var owner = _players[pending.Attack.SourceSeat];
            pending.OwnerCardId = GetHand(owner).Single(card => card.Id == selected.Cards[0]).Id;
            pending.Stage = LierenStage.OpponentPindian;
            PublishLierenOpponentChoice(pending);
            return;
        }
        if (pending.Stage == LierenStage.OpponentPindian)
        {
            if (action != "lieren-pindian" || selected.Cards.Count != 1)
                throw new InvalidOperationException("The Lieren Pindian choice is malformed.");
            ResolveLierenPindian(pending, selected.Cards[0]);
            return;
        }
        if (pending.Stage != LierenStage.Gain || action != "lieren-gain")
            throw new InvalidOperationException("The Lieren gain choice is malformed.");
        ResolveLierenGain(pending, selected);
    }

    private void PublishLierenOpponentChoice(LierenResolution pending)
    {
        var owner = _players[pending.Attack.SourceSeat];
        var target = _players[pending.Attack.TargetSeat];
        var hand = GetHand(target);
        ClearPendingDecision();
        _pendingDecision = new PendingDecision(DecisionKind.Lieren, target.Seat,
            $"{owner.Name} 对你发动【烈刃】，请选择一张手牌拼点。", hand.Select(c => c.Id).ToArray(), [], owner.Seat)
        {
            PromptId = target.IsHuman ? CreatePromptId() : default, IsPrivate = true,
            Choices = hand.Select(card => new PromptChoice(new ChoiceId($"lieren.pindian.{card.Id}"),
                $"以【{card.DisplayName}】（{card.Rank}）拼点", [card.Id], [],
                new Dictionary<string, string> { ["action"] = "lieren-pindian" })).ToArray()
        };
        _status = target.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveLierenPindian(LierenResolution pending, int targetCardId)
    {
        var owner = _players[pending.Attack.SourceSeat];
        var target = _players[pending.Attack.TargetSeat];
        var ownerCard = GetHand(owner).Single(card => card.Id == pending.OwnerCardId);
        var targetCard = GetHand(target).Single(card => card.Id == targetCardId);
        pending.TargetCardId = targetCard.Id;
        ClearPendingDecision();
        MoveCard(ownerCard, CardLocation.Hand(owner.Seat), CardLocation.Processing, CardMoveReasons.PindianReveal);
        MoveCard(targetCard, CardLocation.Hand(target.Seat), CardLocation.Processing, CardMoveReasons.PindianReveal);
        var won = ownerCard.Rank > targetCard.Rank;
        QueueGameEvent(new PindianResolvedEvent(pending.Attack.ResolutionId, SkillKind.Lieren, owner.Seat,
            target.Seat, ownerCard.Id, targetCard.Id, ownerCard.Rank, targetCard.Rank, won));
        MoveCard(ownerCard, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.PindianFinish);
        MoveCard(targetCard, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.PindianFinish);
        AddLog("Pindian", $"{owner.Name} 以 {ownerCard.Rank} 点与 {target.Name} 的 {targetCard.Rank} 点拼点，烈刃{(won ? "获胜" : "未赢")}。", owner.Seat, target.Seat);
        if (!won || GetHand(target).Count + GetEquipment(target).Count == 0)
        {
            FinishLieren(pending, used: true, won, gainedCardId: null);
            return;
        }
        pending.Stage = LierenStage.Gain;
        PublishLierenGainChoice(pending);
    }

    private void PublishLierenGainChoice(LierenResolution pending)
    {
        var owner = _players[pending.Attack.SourceSeat];
        var target = _players[pending.Attack.TargetSeat];
        var choices = new List<PromptChoice>();
        for (var slot = 0; slot < GetHand(target).Count; slot++)
            choices.Add(new PromptChoice(new ChoiceId($"lieren.gain.hand.{slot}"), $"获得 {target.Name} 的第 {slot + 1} 张暗置手牌。", [], [target.Seat],
                new Dictionary<string, string> { ["action"] = "lieren-gain", ["zone"] = "hand", ["slot"] = slot.ToString(System.Globalization.CultureInfo.InvariantCulture) }));
        foreach (var card in GetEquipment(target))
            choices.Add(new PromptChoice(new ChoiceId($"lieren.gain.equipment.{card.Id}"), $"获得 {target.Name} 的【{card.DisplayName}】。", [card.Id], [target.Seat],
                new Dictionary<string, string> { ["action"] = "lieren-gain", ["zone"] = "equipment" }));
        _pendingDecision = new PendingDecision(DecisionKind.Lieren, owner.Seat,
            $"【烈刃】拼点获胜，获得 {target.Name} 的一张牌。", [], [target.Seat])
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveLierenGain(LierenResolution pending, PromptChoice selected)
    {
        var owner = _players[pending.Attack.SourceSeat];
        var target = _players[pending.Attack.TargetSeat];
        Card card;
        CardLocation from;
        if (selected.Parameters.GetValueOrDefault("zone") == "hand" &&
            int.TryParse(selected.Parameters.GetValueOrDefault("slot"), out var slot))
        {
            card = GetHand(target)[slot];
            from = CardLocation.Hand(target.Seat);
        }
        else
        {
            card = GetEquipment(target).Single(item => selected.Cards.SequenceEqual([item.Id]));
            from = CardLocation.Equipment(target.Seat);
        }
        ClearPendingDecision();
        MoveCard(card, from, CardLocation.Hand(owner.Seat), CardMoveReasons.LierenGain);
        FinishLieren(pending, used: true, won: true, card.Id);
    }

    private void FinishLieren(LierenResolution pending, bool used, bool won, int? gainedCardId)
    {
        var attack = pending.Attack;
        ClearPendingDecision();
        QueueGameEvent(new LierenResolvedEvent(attack.ResolutionId, attack.SourceSeat, attack.TargetSeat,
            pending.OwnerCardId, pending.TargetCardId, used, won, gainedCardId));
        _pendingLieren = null;
        CompleteAttack(attack);
    }

    private bool IsAiLierenPending() => _pendingLieren is not null &&
        _pendingDecision is { Kind: DecisionKind.Lieren, PlayerSeat: var seat } && !_players[seat].IsHuman;

    private void ResolvePendingAiLieren()
    {
        var pending = _pendingLieren ?? throw new InvalidOperationException("There is no AI Lieren choice.");
        var decision = _pendingDecision ?? throw new InvalidOperationException("Lieren requires a decision.");
        PromptChoice selected;
        if (pending.Stage == LierenStage.Offer)
            selected = decision.Choices.Where(c => c.Parameters.GetValueOrDefault("action") == "lieren-use")
                .OrderByDescending(c => GetHand(_players[decision.PlayerSeat]).Single(card => card.Id == c.Cards[0]).Rank).First();
        else if (pending.Stage == LierenStage.OpponentPindian)
            selected = decision.Choices.OrderByDescending(c => GetHand(_players[decision.PlayerSeat]).Single(card => card.Id == c.Cards[0]).Rank).First();
        else
            selected = decision.Choices.First();
        ResolveLierenChoice(selected);
        PublishState();
    }
}
