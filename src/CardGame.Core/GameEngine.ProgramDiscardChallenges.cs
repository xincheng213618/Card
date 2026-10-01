namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void ValidateProgramDiscardChallengeState(ProgramSkillFrame frame)
    {
        if (frame.DiscardChallenge is not { } draft) return;
        var paused = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        if (draft.Mode == "category" && paused.Op != SkillProgramEffectOp.ChooseCategoryAlternativeDiscard ||
            draft.Mode == "escalating" && paused.Op != SkillProgramEffectOp.EscalatingDiscardOrDamage ||
            draft.Mode is not ("category" or "escalating") || draft.ChooserSeat < 0 || draft.ChooserSeat >= _playerCount ||
            draft.Cursor < 0 || draft.Cursor > _playerCount - 1 || draft.PreviousCount < 0 || draft.Remaining < -1 ||
            draft.SelectedIds.Count != draft.SelectedIds.Distinct().Count() ||
            draft.Mode == "category" && draft.SelectedIds.Count != 0 ||
            draft.Mode == "escalating" && draft.SelectedIds.Any(id =>
                _cardZones.GetLocation(id).OwnerSeat != draft.ChooserSeat ||
                !paused.Zones.Contains(_cardZones.GetLocation(id).Zone)) ||
            draft.Mode == "category" && (draft.Remaining > paused.MinimumValue ||
                draft.Remaining >= 0 && !draft.ComplementOnly && draft.Remaining != 0) ||
            frame.PendingMovementContinuation is { } movement &&
                (movement.SubjectSeat != draft.ChooserSeat || movement.CoverageResultBind is not null ||
                 !frame.ReexecuteParticipantInstruction || draft.SelectedIds.Count != 0))
            throw new InvalidOperationException("Invalid serialized discard challenge state.");
        if (ReferenceEquals(frame, _resolutionStack.LastOrDefault()) && frame.PendingMovementContinuation is null &&
            (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } decision ||
             decision.PlayerSeat != draft.ChooserSeat || decision.Choices.Any(choice =>
                 choice.Parameters.GetValueOrDefault("program-action") != "discard-challenge")))
            throw new InvalidOperationException("A discard challenge must retain its chooser's private prompt.");
    }

    private PromptChoice SelectAiProgramDiscardChallenge(PendingDecision decision, ProgramSkillFrame frame)
    {
        var draft = frame.DiscardChallenge!;
        var finish = decision.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "finish");
        if (finish is not null) return finish;
        var cards = decision.Choices.Where(c => c.Parameters.GetValueOrDefault("branch") == "card").ToArray();
        if (draft.Mode == "escalating" && cards.Length + draft.SelectedIds.Count <= draft.PreviousCount)
            return decision.Choices.Single(c => c.Parameters.GetValueOrDefault("branch") == "damage");
        if (draft.Mode == "category" && !draft.ComplementOnly)
        {
            var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
                .GetPausedInstruction(frame.InstructionIndex).Effect;
            var primary = cards.FirstOrDefault(c => effect.CardCategories.Contains(GetProgramCardCategory(
                _cardZones.CardsAt(_cardZones.GetLocation(c.Cards[0])).Single(card => card.Id == c.Cards[0]).Kind)));
            if (primary is not null) return primary;
        }
        return cards.FirstOrDefault() ?? decision.Choices.First();
    }

    private sealed partial class ProgramSkillHost : IDiscardChallengeProgramHost
    {
        public SkillProgramStepOutcome ExecuteDiscardChallenge(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat) =>
            engine.ExecuteDiscardChallenge(effect, frame, targetSeat);
    }

    private SkillProgramStepOutcome ExecuteDiscardChallenge(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat)
    {
        var escalating = effect.Op == SkillProgramEffectOp.EscalatingDiscardOrDamage;
        var draft = frame.DiscardChallenge ?? new ProgramDiscardChallengeDraft(
            escalating ? "escalating" : "category", targetSeat, 0, 0, -1, false, []);
        if (escalating)
        {
            var cursor = draft.Cursor;
            while (cursor < _playerCount - 1 && !_players[(frame.OwnerSeat + cursor + 1) % _playerCount].IsAlive) cursor++;
            if (cursor >= _playerCount - 1)
            { ReplaceRuntimeTop(frame with { DiscardChallenge = null }); return SkillProgramStepOutcome.Continue; }
            draft = draft with { Cursor = cursor, ChooserSeat = (frame.OwnerSeat + cursor + 1) % _playerCount };
        }
        else if (draft.Remaining == 0 || !_players[draft.ChooserSeat].IsAlive)
        { ReplaceRuntimeTop(frame with { DiscardChallenge = null }); return SkillProgramStepOutcome.Continue; }
        ReplaceRuntimeTop(frame with { DiscardChallenge = draft });
        PublishDiscardChallenge(GetActiveProgramFrame(frame.Id), effect);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void PublishDiscardChallenge(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var draft = frame.DiscardChallenge!;
        var choices = new List<PromptChoice>();
        foreach (var zone in effect.Zones)
        foreach (var card in _cardZones.CardsAt(new CardLocation(zone, draft.ChooserSeat)))
        {
            if (draft.SelectedIds.Contains(card.Id) || draft.Mode == "category" && draft.ComplementOnly &&
                effect.CardCategories.Contains(GetProgramCardCategory(card.Kind))) continue;
            choices.Add(new(new ChoiceId($"discard-challenge.card-{card.Id}"), $"{(draft.Mode == "escalating" ? "选择" : "弃置")}【{card.DisplayName}】", [card.Id], [draft.ChooserSeat],
                new Dictionary<string, string> { ["program-action"] = "discard-challenge", ["branch"] = "card" }));
        }
        if (draft.Mode == "escalating")
        {
            if (draft.SelectedIds.Count > draft.PreviousCount)
                choices.Add(new(new ChoiceId("discard-challenge.finish"), $"弃置所选的{draft.SelectedIds.Count}张牌", [], [],
                    new Dictionary<string, string> { ["program-action"] = "discard-challenge", ["branch"] = "finish" }));
            choices.Add(new(new ChoiceId("discard-challenge.damage"), $"受到{effect.Amount}点{(effect.DamageNature == DamageNature.Fire ? "火焰" : effect.DamageNature == DamageNature.Thunder ? "雷电" : "")}伤害", [], [],
                new Dictionary<string, string> { ["program-action"] = "discard-challenge", ["branch"] = "damage" }));
        }
        else if (choices.Count == 0)
        {
            ReplaceRuntimeTop(frame with { DiscardChallenge = null });
            AdvanceRuntimeProgram(frame.Id);
            return;
        }
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, draft.ChooserSeat,
            draft.Mode == "escalating" ? $"【{skill.Name}】弃置至少{draft.PreviousCount + 1}张牌，或受到伤害。" : $"【{skill.Name}】弃置一张锦囊，或依次弃置两张非锦囊。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), [draft.ChooserSeat], frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices.ToArray(), SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[draft.ChooserSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveProgramDiscardChallengeChoice(PromptChoice choice)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Missing discard challenge frame.");
        var draft = frame.DiscardChallenge ?? throw new InvalidOperationException("Missing discard challenge state.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        var branch = choice.Parameters.GetValueOrDefault("branch");
        if (choice.Parameters.GetValueOrDefault("program-action") != "discard-challenge" ||
            effect.Op is not (SkillProgramEffectOp.ChooseCategoryAlternativeDiscard or SkillProgramEffectOp.EscalatingDiscardOrDamage) ||
            _pendingDecision?.PlayerSeat != draft.ChooserSeat) throw new InvalidOperationException("Mismatched discard challenge choice.");
        if (!_players[frame.OwnerSeat].IsAlive || !_players[draft.ChooserSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId))
        { ClearPendingDecision(); CancelProgramBindingAndCleanup(frame, "弃牌参与者或技能实例失效。"); return; }
        if (branch == "card")
        {
            if (choice.Cards.Count != 1 || choice.Targets.Count != 1 || choice.Targets[0] != draft.ChooserSeat || draft.SelectedIds.Contains(choice.Cards[0]))
                throw new InvalidOperationException("Invalid discard challenge card.");
            var location = _cardZones.GetLocation(choice.Cards[0]);
            if (location.OwnerSeat != draft.ChooserSeat || !effect.Zones.Contains(location.Zone)) throw new InvalidOperationException("Discard card left its owner.");
            var card = _cardZones.CardsAt(location).Single(c => c.Id == choice.Cards[0]);
            var primary = effect.CardCategories.Contains(GetProgramCardCategory(card.Kind));
            if (draft.Mode == "category" && draft.ComplementOnly && primary) throw new InvalidOperationException("Wrong discard category.");
            ClearPendingDecision();
            if (draft.Mode == "escalating")
            { ReplaceRuntimeTop(frame with { DiscardChallenge = draft with { SelectedIds = draft.SelectedIds.Append(card.Id).ToArray() } }); PublishDiscardChallenge(GetActiveProgramFrame(frame.Id), effect); return; }
            var remaining = draft.Remaining < 0 ? (primary ? 0 : effect.MinimumValue - 1) : draft.Remaining - 1;
            ReplaceRuntimeTop(frame with { DiscardChallenge = draft with { Remaining = remaining, ComplementOnly = !primary },
                ReexecuteParticipantInstruction = true, PendingMovementContinuation = new(draft.ChooserSeat, 0, null) });
            MoveCard(card, location, CardLocation.DiscardPile, new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"));
            if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(frame.Id);
            return;
        }
        if (draft.Mode != "escalating" || choice.Cards.Count != 0 || choice.Targets.Count != 0 ||
            branch is not ("finish" or "damage") || branch == "finish" && draft.SelectedIds.Count <= draft.PreviousCount)
            throw new InvalidOperationException("Invalid discard challenge branch.");
        var physical = draft.SelectedIds.Select(id => (Id: id, Location: _cardZones.GetLocation(id))).ToArray();
        if (branch == "finish" && physical.Any(c => c.Location.OwnerSeat != draft.ChooserSeat || !effect.Zones.Contains(c.Location.Zone)))
            throw new InvalidOperationException("Staged discard card moved.");
        ClearPendingDecision();
        ReplaceRuntimeTop(frame with { DiscardChallenge = draft with { Cursor = draft.Cursor + 1,
            PreviousCount = branch == "finish" ? draft.SelectedIds.Count : 0, SelectedIds = [] }, ReexecuteParticipantInstruction = true,
            PendingMovementContinuation = branch == "finish" ? new(draft.ChooserSeat, 0, null) : null });
        if (branch == "damage")
        {
            if (BeginProgramSkillDamage(GetActiveProgramFrame(frame.Id), draft.ChooserSeat, effect.Amount, nature: effect.DamageNature) == SkillProgramStepOutcome.Continue)
                AdvanceRuntimeProgram(frame.Id);
            return;
        }
        MoveProgramCardsFromMultipleSources(physical.Select(item => item.Id).ToArray(), CardLocation.DiscardPile,
            new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"));
        if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(frame.Id);
    }
}
