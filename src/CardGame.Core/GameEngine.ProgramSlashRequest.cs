namespace CardGame.Core;

public sealed partial class GameEngine
{
    private SkillProgramStepOutcome RequestProgramSlashByTarget(
        ProgramSkillFrame frame,
        int targetSeat,
        string resultBind)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.ChoiceBindings.Any(binding => binding.Name == resultBind))
            throw new InvalidOperationException("A named program choice cannot be answered twice.");
        var owner = _players[frame.OwnerSeat];
        var user = _players[targetSeat];
        if (!owner.IsAlive)
        {
            CancelProgramBindingAndCleanup(active, "技能拥有者已失效，技能剩余结算已取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var slashes = GetHand(user).Where(card => IsSlashCard(card.Kind)).ToArray();
        if (slashes.Length == 0)
        {
            CommitProgramChoiceResult(frame.Id, resultBind,
                RequestSlashByTargetProgramOperationDescriptor.DeclinedOption,
                user.Seat, "没有可用的【杀】。");
            return SkillProgramStepOutcome.Continue;
        }

        var skill = _contentRegistry!.GetSkill(frame.SkillId);
        var choices = slashes.Select(card => new PromptChoice(
            new ChoiceId($"program-request-slash.frame-{active.Id}.card-{card.Id}"),
            $"使用【{card.DisplayName}】攻击 {owner.Name}。",
            [card.Id],
            [frame.OwnerSeat],
            new Dictionary<string, string>
            {
                ["program-action"] = "request-slash",
                ["frame-id"] = active.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["result-bind"] = resultBind,
                ["card-id"] = card.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })).ToList();
        choices.Add(new PromptChoice(
            new ChoiceId($"program-request-slash.frame-{active.Id}.decline"),
            $"不使用【杀】。",
            [],
            [],
            new Dictionary<string, string>
            {
                ["program-action"] = "request-slash-decline",
                ["frame-id"] = active.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["result-bind"] = resultBind
            }));
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger,
            user.Seat,
            $"【{skill.Name}】需对 {owner.Name} 使用一张【杀】，否则其弃置你的一张牌。",
            slashes.Select(card => card.Id).Order().ToArray(),
            [frame.OwnerSeat],
            frame.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = user.Seat,
            SkillPrompt = new SkillPromptPresentation(frame.SkillId, skill.Name,
                $"{skill.Name} · 是否使用【杀】", skill.Description),
            Choices = Array.AsReadOnly(choices.ToArray())
        };
        _status = user.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveProgramRequestSlashChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The slash request lost its program frame.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame,
            _contentRegistry!.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.RequestSlashByTarget ||
            effect.ResultBind is not { } resultBind ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            selected.Parameters.GetValueOrDefault("result-bind") != resultBind)
            throw new InvalidOperationException("The slash request does not match its suspended instruction.");
        var userSeat = frame.SelectedTargetSeats.Single();
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != userSeat)
            throw new InvalidOperationException("The slash request responder changed while suspended.");
        if (!_players[frame.OwnerSeat].IsAlive || !_players[userSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
        {
            ClearPendingDecision();
            CancelProgramBindingAndCleanup(frame, "挑衅参与者或技能实例已失效，技能剩余步骤取消。");
            return;
        }

        var action = selected.Parameters.GetValueOrDefault("program-action");
        ClearPendingDecision();
        if (action == "request-slash-decline")
        {
            if (selected.Cards.Count != 0 || selected.Targets.Count != 0)
                throw new InvalidOperationException("The decline branch must not name a card or target.");
            CommitProgramChoiceResult(frame.Id, resultBind,
                RequestSlashByTargetProgramOperationDescriptor.DeclinedOption,
                userSeat, "不使用【杀】。");
            ContinueProgramSkill(frame.Id);
            return;
        }
        if (action != "request-slash" ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("card-id"),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var cardId) ||
            selected.Cards.Count != 1 || selected.Cards[0] != cardId ||
            selected.Targets.Count != 1 || selected.Targets[0] != frame.OwnerSeat)
            throw new InvalidOperationException("The slash request answer is malformed.");
        var location = _cardZones.GetLocation(cardId);
        var card = location.Zone == CardZoneKind.Hand && location.OwnerSeat == userSeat
            ? _cardZones.CardsAt(location).SingleOrDefault(item => item.Id == cardId)
            : null;
        if (card is null || !IsSlashCard(card.Kind))
        {
            CancelProgramBindingAndCleanup(frame, "所选【杀】已离开用牌者手牌，技能剩余结算已取消。");
            return;
        }
        CommitProgramChoiceResult(frame.Id, resultBind,
            RequestSlashByTargetProgramOperationDescriptor.UsedSlashOption,
            userSeat, $"使用【{card.DisplayName}】。");
        ResolveSlashCore(_players[userSeat], _players[frame.OwnerSeat], card, card.Kind, userSeat,
            physicalCards: [card], countsTowardSlashLimit: false,
            programSkillCardUseFrameId: frame.Id);
    }

    private PromptChoice SelectAiProgramRequestSlash(PendingDecision decision, ProgramSkillFrame frame)
    {
        // A forced Slash is worth more than a guaranteed one-card loss; prefer the
        // first physical Slash in card-id order so the answer stays deterministic.
        return decision.Choices.FirstOrDefault(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "request-slash") ??
            decision.Choices.First();
    }

    private int[] GetNearestLivingCharacterSeats(int seat)
    {
        var candidates = _players
            .Where(player => player.IsAlive && player.Seat != seat)
            .Select(player => (Seat: player.Seat, Distance: GetCombatDistance(seat, player.Seat)))
            .ToArray();
        if (candidates.Length == 0) return [];
        var minimum = candidates.Min(candidate => candidate.Distance);
        return candidates.Where(candidate => candidate.Distance == minimum)
            .Select(candidate => candidate.Seat).Order().ToArray();
    }

    private SkillProgramStepOutcome RequestProgramSlashByNearest(
        ProgramSkillFrame frame, int participantSeat, int hpAmount)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (!_players[frame.OwnerSeat].IsAlive)
        {
            CancelProgramBindingAndCleanup(active, "技能拥有者已失效，技能剩余结算已取消。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var participant = _players[participantSeat];
        var nearest = GetNearestLivingCharacterSeats(participantSeat);
        var slashes = GetHand(participant)
            .Where(card => IsSlashCard(card.Kind))
            .OrderBy(card => card.Id)
            .ToArray();
        if (nearest.Length == 0 || slashes.Length == 0)
            return new ProgramSkillHost(this).LoseHp(active.Id, active.SkillId, participantSeat, hpAmount);

        var skill = _contentRegistry!.GetSkill(frame.SkillId);
        var choices = new List<PromptChoice>();
        foreach (var card in slashes)
            foreach (var seat in nearest)
                choices.Add(new PromptChoice(
                    new ChoiceId($"program-request-slash-nearest.frame-{active.Id}.card-{card.Id}.seat-{seat}"),
                    $"使用【{card.DisplayName}】攻击 {_players[seat].Name}。",
                    [card.Id],
                    [seat],
                    new Dictionary<string, string>
                    {
                        ["program-action"] = "request-slash-nearest",
                        ["frame-id"] = active.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["seat"] = participantSeat.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["card-id"] = card.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["target-seat"] = seat.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    }));
        choices.Add(new PromptChoice(
            new ChoiceId($"program-request-slash-nearest.frame-{active.Id}.decline"),
            $"不使用【杀】，失去{hpAmount}点体力。",
            [],
            [],
            new Dictionary<string, string>
            {
                ["program-action"] = "request-slash-nearest-decline",
                ["frame-id"] = active.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["seat"] = participantSeat.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }));
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger,
            participant.Seat,
            $"【{skill.Name}】需对距离最近的角色使用一张【杀】，否则失去{hpAmount}点体力。",
            slashes.Select(card => card.Id).Order().ToArray(),
            nearest,
            frame.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = participant.Seat,
            SkillPrompt = new SkillPromptPresentation(frame.SkillId, skill.Name,
                $"{skill.Name} · 是否使用【杀】", skill.Description),
            Choices = Array.AsReadOnly(choices.ToArray())
        };
        _status = participant.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveProgramRequestSlashByNearestChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The nearest-character slash request lost its program frame.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame,
            _contentRegistry!.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.RequestSlashByNearest ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("seat"),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var participantSeat) ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The nearest-character slash request does not match its suspended instruction.");
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != participantSeat)
            throw new InvalidOperationException("The nearest-character slash request responder changed while suspended.");
        var action = selected.Parameters.GetValueOrDefault("program-action");
        if (!_players[frame.OwnerSeat].IsAlive || !_players[participantSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
        {
            ClearPendingDecision();
            CancelProgramBindingAndCleanup(frame, "乱武参与者或技能实例已失效，技能剩余步骤取消。");
            return;
        }

        ClearPendingDecision();
        if (action == "request-slash-nearest-decline")
        {
            if (selected.Cards.Count != 0 || selected.Targets.Count != 0)
                throw new InvalidOperationException("The decline branch must not name a card or target.");
            if (new ProgramSkillHost(this).LoseHp(frame.Id, frame.SkillId, participantSeat, effect.Amount) ==
                SkillProgramStepOutcome.Continue)
                ContinueProgramSkill(frame.Id);
            return;
        }
        if (action != "request-slash-nearest" ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("card-id"),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var cardId) ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("target-seat"),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var targetSeat) ||
            selected.Cards.Count != 1 || selected.Cards[0] != cardId ||
            selected.Targets.Count != 1 || selected.Targets[0] != targetSeat)
            throw new InvalidOperationException("The nearest-character slash request answer is malformed.");
        var location = _cardZones.GetLocation(cardId);
        var card = location == CardLocation.Hand(participantSeat)
            ? _cardZones.CardsAt(location).SingleOrDefault(item => item.Id == cardId)
            : null;
        if (card is null || !IsSlashCard(card.Kind) ||
            !GetNearestLivingCharacterSeats(participantSeat).Contains(targetSeat))
        {
            CancelProgramBindingAndCleanup(frame, "所选【杀】或最近角色已失效，技能剩余结算已取消。");
            return;
        }
        ResolveSlashCore(_players[participantSeat], _players[targetSeat], card, card.Kind, participantSeat,
            physicalCards: [card], countsTowardSlashLimit: false,
            programSkillCardUseFrameId: frame.Id);
    }

    private PromptChoice SelectAiProgramRequestSlashByNearest(PendingDecision decision) =>
        decision.Choices.FirstOrDefault(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "request-slash-nearest") ??
        decision.Choices.First();

    private void CommitProgramChoiceResult(long frameId, string bind, string optionId,
        int chooserSeat, string label)
    {
        var frame = GetActiveProgramFrame(frameId);
        _resolutionStack[^1] = frame with
        {
            ChoiceBindings = Array.AsReadOnly(frame.ChoiceBindings.Append(
                new ProgramChoiceResultBinding(bind, optionId, chooserSeat)).ToArray())
        };
        QueueGameEvent(new ProgramOptionChosenEvent(frameId, frame.SkillId, GetProgramBindingId(frame),
            frame.OwnerSeat, bind, optionId, chooserSeat, label));
    }
}
