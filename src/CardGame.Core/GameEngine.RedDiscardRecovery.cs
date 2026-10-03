namespace CardGame.Core;

/// <summary>
/// Current OL 界孟获 再起: at the owner's own turn ending, up to X other characters
/// (X = red cards put into the discard pile this turn) each choose between drawing
/// one card and recovering the owner by one. The owner-owned draft and the
/// turn-scoped red-discard counter follow the discard-budget / discard-suit-mask
/// precedents: typed state on the owning frame, a public committed fact per
/// selection, and deterministic reconstruction through command replay.
/// </summary>
public sealed partial class GameEngine
{
    private int _turnRedDiscardCount;
    private int _turnRedDiscardTurn = -1;
    private bool UsesRedDiscardRecoveryCounter => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.OfferRedDiscardRecoveryChoice);
    private int CurrentTurnRedDiscardCount => _turnRedDiscardTurn == _turnNumber ? _turnRedDiscardCount : 0;
    internal void CaptureTurnRedDiscardCount(int turn, IReadOnlyList<CardMovementRecord> movements)
    {
        if (!UsesRedDiscardRecoveryCounter || turn <= 0) return;
        if (turn != _turnNumber) throw new InvalidOperationException("A discard batch lost its actual turn.");
        if (_turnRedDiscardTurn != _turnNumber) { _turnRedDiscardTurn = _turnNumber; _turnRedDiscardCount = 0; }
        foreach (var move in movements.Where(m => m.To == CardLocation.DiscardPile && m.From != CardLocation.DiscardPile))
        {
            var suit = _cardZones.CardsAt(move.To).Single(c => c.Id == move.CardId).Suit;
            if (suit is Suit.Heart or Suit.Diamond) _turnRedDiscardCount++;
        }
    }
    private bool RedDiscardRecoverySourceValid(ProgramSkillFrame f) => _players[f.OwnerSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) &&
        EnabledSkillPrograms(_players[f.OwnerSeat]).Any(p => p.Id == f.SkillId);
    private Dictionary<string, string> RedDiscardRecoveryParameters(ProgramSkillFrame f, string mode) => new()
    { ["program-action"] = "red-discard-recovery", ["frame-id"] = f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), ["mode"] = mode };
    internal SkillProgramStepOutcome BeginRedDiscardRecovery(ProgramSkillFrame f)
    {
        if (f.WindowContext?.Window != SkillProgramTriggerWindow.TurnEnding || f.OwnerSeat != _currentSeat)
            throw new InvalidOperationException("Red discard recovery requires its actual own turn ending.");
        var budget = CurrentTurnRedDiscardCount;
        if (budget <= 0 || !_players[f.OwnerSeat].IsAlive) return SkillProgramStepOutcome.Continue;
        var draft = new ProgramRedDiscardRecoveryDraft(budget, []);
        var published = f with { RedDiscardRecoveryDraft = draft };
        ReplaceRuntimeTop(published);
        PublishBudgetGiftPrompt(published, f.OwnerSeat,
            $"本回合置入弃牌堆的红色牌有 {budget} 张，请选择至多 {budget} 名角色。", RedDiscardRecoverySelectionChoices(published));
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> RedDiscardRecoverySelectionChoices(ProgramSkillFrame f)
    {
        var d = f.RedDiscardRecoveryDraft!;
        var choices = new List<PromptChoice>();
        void Add(string key, string label, IReadOnlyList<int> targets) => choices.Add(
            new(new($"red-discard-recovery.{f.Id}.{key}"), label, [], targets, RedDiscardRecoveryParameters(f, key)));
        foreach (var other in _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat && !d.Selected.Contains(p.Seat)))
            Add($"seat:{other.Seat}", $"选择 {other.Name}", [other.Seat]);
        if (d.Selected.Count > 0)
            Add("finish", $"确认，依次结算（已选 {d.Selected.Count}/{d.Budget} 名）", d.Selected);
        Add("skip", "不发动", []);
        return choices;
    }
    private IReadOnlyList<PromptChoice> RedDiscardRecoveryQuestionChoices(ProgramSkillFrame f)
    {
        var owner = _players[f.OwnerSeat];
        var choices = new List<PromptChoice>
        {
            new(new($"red-discard-recovery.{f.Id}.draw"), "摸一张牌", [], [], RedDiscardRecoveryParameters(f, "draw"))
        };
        if (owner.Hp < owner.MaxHp)
            choices.Add(new(new($"red-discard-recovery.{f.Id}.recovery"), $"令 {owner.Name} 回复1点体力", [], [],
                RedDiscardRecoveryParameters(f, "recovery")));
        return choices;
    }
    private void ResolveRedDiscardRecoveryChoice(PromptChoice selected)
    {
        var f = GetActiveProgramFrame(long.Parse(selected.Parameters["frame-id"]));
        var d = f.RedDiscardRecoveryDraft ?? throw new InvalidOperationException("The red discard recovery lost its draft.");
        ClearPendingDecision();
        if (!RedDiscardRecoverySourceValid(f)) { FinishProgramSkill(f, false); return; }
        var mode = selected.Parameters["mode"];
        if (d.AskingSeat is { } askedSeat)
        {
            var legal = RedDiscardRecoveryQuestionChoices(f).Single(choice => choice.Id == selected.Id);
            if (legal.Parameters["mode"] != mode) throw new InvalidOperationException("The red discard recovery answer lost its legal prompt.");
            if (!_players[askedSeat].IsAlive) { FinishProgramSkill(GetActiveProgramFrame(f.Id), false); return; }
            var active = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(active with { RedDiscardRecoveryDraft = active.RedDiscardRecoveryDraft! with { AskingSeat = null } });
            AdvanceEventRulesAndQueueFact(new ProgramRedDiscardRecoveryChosenEvent(f.Id, f.OwnerSeat, askedSeat, mode));
            if (mode == "recovery")
            {
                var owner = _players[f.OwnerSeat];
                var recovered = Math.Min(1, owner.MaxHp - owner.Hp);
                if (recovered > 0)
                {
                    var recoveryId = BeginRecovery(active.Id, active.OwnerSeat, active.OwnerSeat, recovered);
                    owner.Hp += recovered;
                    AdvanceEventRulesAndQueueFact(new RecoveryAppliedEvent(active.OwnerSeat, active.OwnerSeat, recovered, owner.Hp));
                    PopResolutionFrame(recoveryId, ResolutionFrameKind.Recovery);
                }
                AdvanceRuntimeProgram(f.Id);
                return;
            }
            DrawProgramCards(f.Id, askedSeat, 1, null, null, SkillProgramCardSetVisibility.Private,
                new("skill-program.red-discard-recovery.draw"));
            if (AwaitProgramBoundCardMovements(f.Id, f.OwnerSeat) == SkillProgramStepOutcome.Continue)
                AdvanceRuntimeProgram(f.Id);
            return;
        }
        if (mode == "skip")
        {
            ReplaceRuntimeTop(GetActiveProgramFrame(f.Id) with { RedDiscardRecoveryDraft = null });
            AdvanceRuntimeProgram(f.Id);
            return;
        }
        if (mode == "finish")
        {
            var committed = d with { Committed = true, Cursor = 0 };
            var active = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(active with { RedDiscardRecoveryDraft = committed });
            AdvanceEventRulesAndQueueFact(new ProgramRedDiscardRecoveryCommittedEvent(f.Id, f.OwnerSeat, committed.Budget,
                committed.Selected));
            AdvanceRuntimeProgram(f.Id);
            return;
        }
        if (!d.Committed && d.Selected.Count < d.Budget && int.TryParse(mode["seat:".Length..], out var seat) &&
            _players[seat].IsAlive && seat != f.OwnerSeat && !d.Selected.Contains(seat))
        {
            var active = GetActiveProgramFrame(f.Id);
            var grown = active.RedDiscardRecoveryDraft! with { Selected = Array.AsReadOnly(active.RedDiscardRecoveryDraft!.Selected.Append(seat).ToArray()) };
            var published = active with { RedDiscardRecoveryDraft = grown };
            ReplaceRuntimeTop(published);
            PublishBudgetGiftPrompt(published, f.OwnerSeat,
                $"请选择至多 {grown.Budget} 名角色。已选 {grown.Selected.Count} 名。", RedDiscardRecoverySelectionChoices(published));
            return;
        }
        throw new InvalidOperationException("The red discard recovery selection does not match its suspended instruction.");
    }
    private bool ResumeRedDiscardRecovery(long id)
    {
        var f = GetActiveProgramFrame(id);
        if (f.RedDiscardRecoveryDraft is not { Committed: true, AskingSeat: null } d) return false;
        if (_winner != Winner.None) { ReplaceRuntimeTop(f with { RedDiscardRecoveryDraft = null }); return false; }
        var cursor = d.Cursor;
        while (cursor < d.Selected.Count && !_players[d.Selected[cursor]].IsAlive) cursor++;
        if (cursor >= d.Selected.Count || !RedDiscardRecoverySourceValid(f))
        {
            ReplaceRuntimeTop(GetActiveProgramFrame(id) with { RedDiscardRecoveryDraft = null });
            return false;
        }
        var seat = d.Selected[cursor];
        var active = GetActiveProgramFrame(id);
        var asking = active.RedDiscardRecoveryDraft! with { Cursor = cursor + 1, AskingSeat = seat };
        var published = active with { RedDiscardRecoveryDraft = asking };
        ReplaceRuntimeTop(published);
        PublishBudgetGiftPrompt(published, seat, $"请选择【再起】的效果。", RedDiscardRecoveryQuestionChoices(published));
        return true;
    }
    private PromptChoice SelectAiRedDiscardRecovery(PendingDecision decision, ProgramSkillFrame frame)
    {
        if (!decision.Choices.Any(choice => choice.Parameters.GetValueOrDefault("mode") is "draw" or "recovery"))
            return decision.Choices[0];
        // Public HP relationship alone separates the branches; private hands do not enter.
        var owner = CreateSnapshot(decision.PlayerSeat).Players.Single(player => player.Seat == frame.OwnerSeat);
        var heal = decision.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("mode") == "recovery");
        if (heal is not null && owner.MaxHp - owner.Hp >= 2) return heal;
        return decision.Choices.First(choice => choice.Parameters.GetValueOrDefault("mode") == "draw");
    }
    private void AssertRedDiscardRecoveryDraft(ProgramSkillFrame f, SkillProgramEffect paused)
    {
        if (f.RedDiscardRecoveryDraft is not { } d) return;
        if (paused.Op != SkillProgramEffectOp.OfferRedDiscardRecoveryChoice ||
            f.WindowContext?.Window != SkillProgramTriggerWindow.TurnEnding ||
            d.Budget < 0 || d.Selected is null || d.Selected.Distinct().Count() != d.Selected.Count ||
            d.Selected.Any(seat => !IsValidPlayerSeat(seat) || seat == f.OwnerSeat) ||
            d.Selected.Count > d.Budget || d.Cursor < 0 || d.Cursor > d.Selected.Count ||
            (d.AskingSeat is { } asking && (!d.Committed || !IsValidPlayerSeat(asking) ||
                !d.Selected.Contains(asking))) ||
            (!d.Committed && d.AskingSeat is not null))
            throw new InvalidOperationException("Red discard recovery lost its frozen selection or effect cursor.");
        if (d.Committed && !CompleteProgramEventHistory().OfType<ProgramRedDiscardRecoveryCommittedEvent>()
                .Any(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.Budget == d.Budget &&
                    e.Seats.SequenceEqual(d.Selected)))
            throw new InvalidOperationException("Red discard recovery lost its exact public commitment.");
        if (!ReferenceEquals(f, _resolutionStack.LastOrDefault())) return;
        if (d.AskingSeat is { } asked)
        {
            if (_pendingDecision?.PlayerSeat != asked ||
                !AssistedChoicesEqual(_pendingDecision.Choices, RedDiscardRecoveryQuestionChoices(f)))
                throw new InvalidOperationException("Red discard recovery lost its exact beneficiary question.");
        }
        else if (!d.Committed &&
                 (_pendingDecision?.PlayerSeat != f.OwnerSeat ||
                  !AssistedChoicesEqual(_pendingDecision.Choices, RedDiscardRecoverySelectionChoices(f))))
            throw new InvalidOperationException("Red discard recovery lost its legal selection prompt.");
    }
    private sealed partial class ProgramSkillHost : IRedDiscardRecoveryHost
    {
        public SkillProgramStepOutcome OfferRedDiscardRecoveryChoice(ProgramSkillFrame f) => engine.BeginRedDiscardRecovery(f);
    }
}
