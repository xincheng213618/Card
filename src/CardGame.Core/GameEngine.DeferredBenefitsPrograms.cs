namespace CardGame.Core;

public sealed partial class GameEngine
{
    private readonly List<ProgramNextTurnRuleModifier> _programNextTurnRuleModifiers = [];
    private readonly List<ProgramFactionRecoveryDebt> _programFactionRecoveryDebts = [];
    private bool TryBeginFactionRecoveryDebts(DyingCompletionReceipt dying, bool survived)
    {
        var debts = _programFactionRecoveryDebts.Where(item => item.DyingFrameId == dying.FrameId).ToArray();
        if (debts.Length == 0) return false;
        _programFactionRecoveryDebts.RemoveAll(item => item.DyingFrameId == dying.FrameId);
        var first = debts[0];
        var program = _contentRegistry.GetSkill(first.SkillId).Program!;
        var trigger = ProgramInstructionResolver.Default.Resolve(program,
            ProgramInstructionSourceKind.Trigger, first.BindingId).Trigger!;
        var id = ++_resolutionSequence;
        var debtReturn = new FactionRecoveryDebtReturn(dying.FrameId, dying.ParentFrameId,
            dying.VictimSeat, dying.Continuation, survived, CurrentDamageAttempt?.ResolutionId,
            ActiveDamageTrigger?.Id);
        PushRuntimeFrame(new ProgramSkillFrame(id, first.OwnerSeat, first.SkillId, first.BindingId,
            program.GameplayHash, trigger.Effects.Count, [], [])
        {
            TriggerId = first.BindingId, SkillInstanceId = first.SkillInstanceId,
            FactionRecoveryDebtReturn = debtReturn,
            WindowContext = new(trigger.Window, dying.FrameId, first.OwnerSeat),
            FactionRecoveryDraft = new(dying.FrameId, debts.Select(item => item.ResponderSeat).ToArray(), 0,
                debts.Select(item => item.ResponderSeat).ToArray(), SettlingDebts: true)
        });
        TryContinueFactionRecoveryDebts(id);
        return true;
    }

    private bool TryContinueFactionRecoveryDebts(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { FactionRecoveryDraft.SettlingDebts: true } frame ||
            frame.Id != frameId) return false;
        var continuation = frame.FactionRecoveryDebtReturn ??
            throw new InvalidOperationException("Faction damage debts lost their original dying continuation.");
        var draft = frame.FactionRecoveryDraft!;
        var cursor = draft.ResponderIndex;
        while (cursor < draft.ResponderSeats.Count && !_players[draft.ResponderSeats[cursor]].IsAlive) cursor++;
        if (cursor >= draft.ResponderSeats.Count)
        {
            PopResolutionFrame(frameId, ResolutionFrameKind.ProgramSkill);
            if (frame.AttackAttempt is not null || ActiveDying is not null)
                throw new InvalidOperationException("Settled faction damage retained an unfinished child resolution.");
            if (CurrentDamageAttempt?.ResolutionId != continuation.ParentAttackOwnerFrameId ||
                ActiveDamageTrigger?.Id != continuation.ParentDamageWindowFrameId)
                throw new InvalidOperationException("Settled faction damage lost its outer damage frame.");
            ContinueDyingAfterFactionRecoveryDebts(new DyingCompletionReceipt(continuation.DyingFrameId,
                continuation.DyingParentFrameId, continuation.DyingVictimSeat, continuation.DyingContinuation),
                continuation.Survived);
            return true;
        }
        var seat = draft.ResponderSeats[cursor];
        ReplaceRuntimeTop(frame with
        {
            FactionRecoveryDraft = draft with { ResponderIndex = cursor + 1 },
            WindowContext = frame.WindowContext! with { TargetSeat = seat }
        });
        // The internal seat anchors resolution; attribution is explicitly absent.
        BeginProgramSkillDamage(GetActiveProgramFrame(frame.Id), seat, 1,
            new ProgramParticipantReference(ProgramParticipantRef.EventTarget), sourceLess: true);
        return true;
    }

    private sealed partial class ProgramSkillHost : IDeferredBenefitsProgramHost
    {
        public SkillProgramStepOutcome RequestFactionRecovery(ProgramSkillFrame frame, string factionId) =>
            engine.RequestProgramFactionRecovery(frame, factionId);
        public void SetNextTurnRuleModifier(ProgramSkillFrame frame, int targetSeat, SkillRuleQuery query, int amount) =>
            engine.QueueProgramNextTurnRuleModifier(frame, targetSeat, query, amount);
        public SkillProgramStepOutcome WeaponDiscardOrDamageBonus(ProgramSkillFrame frame, int amount) =>
            engine.BeginProgramWeaponDamageChoice(frame, amount);
        public void ResolveJudgmentColorBenefit(ProgramSkillFrame frame, int targetSeat, string sourceBind, int recovery) =>
            engine.ResolveProgramJudgmentColorBenefit(frame, targetSeat, sourceBind, recovery);
    }

    private int CountPlayPhaseDamageTakenByAny() => _phase != TurnPhase.Play ? 0 :
        EventsSinceLastBoundary(item => item is TurnStartedEvent or PhaseChangedEvent { Phase: TurnPhase.Play })
            .OfType<DamageAppliedEvent>().Count();

    private void QueueProgramNextTurnRuleModifier(ProgramSkillFrame frame, int targetSeat, SkillRuleQuery query, int amount)
    {
        if (!IsValidPlayerSeat(targetSeat) || targetSeat == frame.OwnerSeat || !_players[targetSeat].IsAlive ||
            query is not (SkillRuleQuery.HandLimit or SkillRuleQuery.SlashLimit or SkillRuleQuery.DrawCount) || amount is < 1 or > 20)
            throw new InvalidOperationException("A next-turn grant needs a living other target and a bounded hand or Slash allowance.");
        var grant = new ProgramNextTurnRuleModifier(frame.Id, frame.InstructionIndex - 1, targetSeat,
            CreateProgramTurnEffectSource(frame), query, amount);
        if (_programNextTurnRuleModifiers.Any(item => item.ParentFrameId == grant.ParentFrameId && item.EffectIndex == grant.EffectIndex))
            throw new InvalidOperationException("A next-turn grant cannot be scheduled twice.");
        _programNextTurnRuleModifiers.Add(grant);
        AdvanceEventRulesAndQueueFact(new ProgramNextTurnRuleModifierQueuedEvent(grant));
    }

    private void ApplyQueuedNextTurnRuleModifiers(int targetSeat)
    {
        foreach (var scheduled in _programNextTurnRuleModifiers.Where(item => item.TargetSeat == targetSeat).ToArray())
        {
            _programNextTurnRuleModifiers.Remove(scheduled);
            if (!_players[targetSeat].IsAlive) continue;
            var granted = _turnCardUseEffects.GrantRuleModifier(_turnNumber, _currentSeat,
                scheduled.ParentFrameId, scheduled.EffectIndex, scheduled.Source, scheduled.Query,
                SkillRuleOperation.Add, scheduled.Amount, affectedSeat: targetSeat);
            AdvanceEventRulesAndQueueFact(new TurnRuleModifierGrantedEvent(granted));
        }
    }

    private void ResolveProgramJudgmentColorBenefit(ProgramSkillFrame frame, int targetSeat, string sourceBind, int recovery)
    {
        var binding = GetProgramCardSet(GetActiveProgramFrame(frame.Id), sourceBind);
        var suit = binding.FrozenRevealedSuit ?? throw new InvalidOperationException("A judgment benefit requires its final frozen suit.");
        if (suit is Suit.Heart or Suit.Diamond)
            new ProgramSkillHost(this).Recover(frame.Id, frame.OwnerSeat, targetSeat, recovery, null, null);
        else if (suit is Suit.Spade or Suit.Club)
            DrawProgramCards(frame.Id, targetSeat, frame.WindowContext?.Amount ?? 0, null, null,
                SkillProgramCardSetVisibility.Private, new CardMoveReason("skill-program.judgment-color.draw"));
    }

    private SkillProgramStepOutcome RequestProgramFactionRecovery(ProgramSkillFrame frame, string factionId)
    {
        var dying = ActiveDying ?? throw new InvalidOperationException("Faction recovery has no dying occurrence.");
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.DyingEntering } context ||
            context.ParentFrameId != _resolutionStack[^2].Id || dying.VictimSeat != frame.OwnerSeat)
            throw new InvalidOperationException("Faction recovery lost its entering dying owner.");
        var responders = Enumerable.Range(1, _playerCount - 1).Select(offset => (frame.OwnerSeat + offset) % _playerCount)
            .Where(seat => _players[seat].IsAlive && GetEffectiveFactionId(_players[seat]) == factionId).ToArray();
        if (responders.Length == 0) return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(frame with { FactionRecoveryDraft = new(dying.FrameId, responders, 0, []) });
        PublishFactionRecoveryPrompt(GetActiveProgramFrame(frame.Id));
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void PublishFactionRecoveryPrompt(ProgramSkillFrame frame)
    {
        var draft = frame.FactionRecoveryDraft!;
        var chooser = draft.ResponderSeats[draft.ResponderIndex];
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        var choices = new[]
        {
            new PromptChoice(new ChoiceId("faction-recovery.accept"), "令其回复1点体力，濒死结算结束后你受到1点伤害", [], [frame.OwnerSeat],
                new Dictionary<string,string> { ["program-action"] = "faction-recovery", ["accept"] = "true" }),
            new PromptChoice(new ChoiceId("faction-recovery.decline"), "不响应", [], [],
                new Dictionary<string,string> { ["program-action"] = "faction-recovery", ["accept"] = "false" })
        };
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, $"【{skill.Name}】是否令 {_players[frame.OwnerSeat].Name} 回复1点体力？", [], [frame.OwnerSeat], frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveFactionRecoveryChoice(PromptChoice choice)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Faction recovery lost its frame.");
        var draft = frame.FactionRecoveryDraft ?? throw new InvalidOperationException("Faction recovery lost its draft.");
        var chooser = draft.ResponderSeats[draft.ResponderIndex];
        var accepted = choice.Parameters.GetValueOrDefault("accept") == "true";
        if (choice.Parameters.GetValueOrDefault("program-action") != "faction-recovery" ||
            choice.Parameters.GetValueOrDefault("accept") is not ("true" or "false") ||
            choice.Cards.Count != 0 || _pendingDecision?.PlayerSeat != chooser ||
            accepted && !choice.Targets.SequenceEqual([frame.OwnerSeat]) || !accepted && choice.Targets.Count != 0)
            throw new InvalidOperationException("Invalid faction recovery choice.");
        ClearPendingDecision();
        if (accepted && _players[chooser].IsAlive && _players[frame.OwnerSeat].IsAlive)
        {
            new ProgramSkillHost(this).Recover(frame.Id, chooser, frame.OwnerSeat, 1, null, null);
            _programFactionRecoveryDebts.Add(new(draft.DyingFrameId, frame.OwnerSeat, chooser,
                frame.SkillId, GetProgramBindingId(frame), frame.SkillInstanceId));
        }
        AdvanceEventRulesAndQueueFact(new ProgramFactionRecoveryChoiceEvent(frame.Id, frame.SkillId, frame.OwnerSeat, chooser, accepted, draft.DyingFrameId));
        var next = draft.ResponderIndex + 1;
        while (next < draft.ResponderSeats.Count && !_players[draft.ResponderSeats[next]].IsAlive) next++;
        var updated = draft with { ResponderIndex = next,
            AcceptedSeats = accepted ? draft.AcceptedSeats.Append(chooser).ToArray() : draft.AcceptedSeats };
        ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with { FactionRecoveryDraft = next < draft.ResponderSeats.Count ? updated : null });
        if (next < draft.ResponderSeats.Count) PublishFactionRecoveryPrompt(GetActiveProgramFrame(frame.Id));
        else AdvanceRuntimeProgram(frame.Id);
    }

    private PromptChoice SelectAiFactionRecovery(PendingDecision decision, ProgramSkillFrame frame)
    {
        var view = CreateSnapshot(decision.PlayerSeat);
        var self = view.Players.Single(player => player.Seat == decision.PlayerSeat);
        // Public relationship alone determines aid; private hands do not enter the decision.
        var accept = self.Role == Role.Loyalist || self.Role == Role.Renegade && view.Players.Count(player => player.IsAlive) > 2;
        return decision.Choices.Single(choice => choice.Parameters["accept"] == (accept ? "true" : "false"));
    }

    private void ValidateFactionRecoveryDraft(ProgramSkillFrame frame)
    {
        if (frame.FactionRecoveryDraft is not { } draft) return;
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex).Effect;
        if (draft.SettlingDebts)
        {
            if (frame.FactionRecoveryDebtReturn is not { } continuation ||
                continuation.DyingFrameId != draft.DyingFrameId || effect.Op != SkillProgramEffectOp.RequestFactionRecovery ||
                draft.ResponderSeats.Count == 0 || draft.ResponderSeats.Distinct().Count() != draft.ResponderSeats.Count ||
                draft.ResponderSeats.Any(seat => !IsValidPlayerSeat(seat) || seat == frame.OwnerSeat) ||
                draft.ResponderIndex < 0 || draft.ResponderIndex > draft.ResponderSeats.Count ||
                !draft.AcceptedSeats.SequenceEqual(draft.ResponderSeats))
                throw new InvalidOperationException("Invalid faction damage debt continuation.");
            return;
        }
        if (effect.Op != SkillProgramEffectOp.RequestFactionRecovery || draft.DyingFrameId != ActiveDying?.FrameId ||
            ActiveDying?.VictimSeat != frame.OwnerSeat || draft.ResponderSeats.Count == 0 ||
            draft.ResponderSeats.Distinct().Count() != draft.ResponderSeats.Count ||
            draft.ResponderSeats.Any(seat => !IsValidPlayerSeat(seat) || seat == frame.OwnerSeat || GetEffectiveFactionId(_players[seat]) != effect.ProviderFactionId) ||
            draft.ResponderIndex < 0 || draft.ResponderIndex >= draft.ResponderSeats.Count ||
            draft.AcceptedSeats.Distinct().Count() != draft.AcceptedSeats.Count ||
            draft.AcceptedSeats.Any(seat => !draft.ResponderSeats.Take(draft.ResponderIndex).Contains(seat)))
            throw new InvalidOperationException("Invalid faction recovery draft.");
        if (ReferenceEquals(frame, _resolutionStack.LastOrDefault()) &&
            (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } decision ||
             decision.PlayerSeat != draft.ResponderSeats[draft.ResponderIndex] ||
             decision.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") != "faction-recovery")))
            throw new InvalidOperationException("Faction recovery must preserve its private responder prompt.");
    }

    private SkillProgramStepOutcome BeginProgramWeaponDamageChoice(ProgramSkillFrame frame, int amount)
    {
        var context = frame.WindowContext ?? throw new InvalidOperationException("Weapon damage choice has no damage context.");
        if (context.Window != SkillProgramTriggerWindow.BeforeDamageApplied || context.SourceSeat != frame.OwnerSeat ||
            context.TargetSeat is not { } target || ActiveCardAttack is not { IsChainPropagation: false } attack ||
            attack.EffectiveCardKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash))
            throw new InvalidOperationException("Weapon damage choice requires the owner's direct Slash damage.");
        var weapon = GetEquipment(_players[frame.OwnerSeat]).Where(card => EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Weapon)
            .OrderByDescending(card => GetWeaponAttackRange(_players[frame.OwnerSeat], card)).ThenBy(card => card.Id).FirstOrDefault();
        if (weapon is null) return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(frame with { WeaponDamageDraft = new(target, weapon.Id, Math.Max(0, GetWeaponAttackRange(_players[frame.OwnerSeat], weapon)), []) });
        PublishWeaponDamagePrompt(GetActiveProgramFrame(frame.Id), amount);
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void PublishWeaponDamagePrompt(ProgramSkillFrame frame, int amount)
    {
        var draft = frame.WeaponDamageDraft!;
        var choices = new List<PromptChoice>();
        if (GetHand(_players[draft.TargetSeat]).Count >= draft.RequiredCount)
        {
            if (draft.SelectedIds.Count < draft.RequiredCount)
                choices.AddRange(GetHand(_players[draft.TargetSeat]).Where(card => !draft.SelectedIds.Contains(card.Id)).Select(card =>
                    new PromptChoice(new ChoiceId("weapon-damage.card-" + card.Id), "选择弃置【" + card.DisplayName + "】", [card.Id], [],
                        new Dictionary<string,string> { ["program-action"] = "weapon-damage", ["branch"] = "card" })));
            else choices.Add(new(new ChoiceId("weapon-damage.finish"), $"弃置所选{draft.RequiredCount}张手牌及其武器", [], [],
                new Dictionary<string,string> { ["program-action"] = "weapon-damage", ["branch"] = "finish" }));
        }
        choices.Add(new(new ChoiceId("weapon-damage.bonus"), $"令此杀伤害+{amount}", [], [],
            new Dictionary<string,string> { ["program-action"] = "weapon-damage", ["branch"] = "bonus" }));
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, draft.TargetSeat,
            $"【{skill.Name}】弃置{draft.RequiredCount}张手牌并弃置其武器，或令此杀伤害+{amount}。", choices.SelectMany(choice => choice.Cards).ToArray(), [], frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices.ToArray(), SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[draft.TargetSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveWeaponDamageChoice(PromptChoice choice)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Weapon choice lost its frame.");
        var draft = frame.WeaponDamageDraft ?? throw new InvalidOperationException("Weapon choice lost its draft.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        var branch = choice.Parameters.GetValueOrDefault("branch");
        if (choice.Parameters.GetValueOrDefault("program-action") != "weapon-damage" ||
            _pendingDecision?.PlayerSeat != draft.TargetSeat || effect.Op != SkillProgramEffectOp.WeaponDiscardOrDamageBonus ||
            choice.Targets.Count != 0 || branch is not ("card" or "finish" or "bonus"))
            throw new InvalidOperationException("Invalid weapon damage choice.");
        if (branch == "card")
        {
            if (choice.Cards.Count != 1 || draft.SelectedIds.Contains(choice.Cards[0]) || draft.SelectedIds.Count >= draft.RequiredCount ||
                _cardZones.GetLocation(choice.Cards[0]) != CardLocation.Hand(draft.TargetSeat))
                throw new InvalidOperationException("Illegal staged weapon discard.");
            ClearPendingDecision();
            ReplaceRuntimeTop(frame with { WeaponDamageDraft = draft with { SelectedIds = draft.SelectedIds.Append(choice.Cards[0]).ToArray() } });
            PublishWeaponDamagePrompt(GetActiveProgramFrame(frame.Id), effect.Amount);
            return;
        }
        if (choice.Cards.Count != 0 || branch == "finish" &&
            (draft.SelectedIds.Count != draft.RequiredCount || draft.SelectedIds.Any(id => _cardZones.GetLocation(id) != CardLocation.Hand(draft.TargetSeat)) ||
             _cardZones.GetLocation(draft.WeaponCardId) != CardLocation.Equipment(frame.OwnerSeat)))
            throw new InvalidOperationException("Weapon discard costs changed before commitment.");
        var context = frame.WindowContext!;
        if (_resolutionStack[^2] is not BeforeDamageProgramWindowFrame window || window.Id != context.ParentFrameId ||
            ActiveCardAttack is not { } attack || attack.SourceSeat != frame.OwnerSeat || attack.TargetSeat != draft.TargetSeat)
            throw new InvalidOperationException("Weapon choice lost its active damage.");
        ClearPendingDecision();
        ReplaceRuntimeTop(frame with { WeaponDamageDraft = null });
        var after = window.Amount;
        if (branch == "bonus")
        {
            attack.IncreaseFinalizedDamageAmount(effect.Amount);
            after = checked(window.Amount + effect.Amount);
            ReplaceRuntimeFrame(_resolutionStack[^2].Id, window with { Amount = after });
        }
        else MoveProgramCardsFromMultipleSources(draft.SelectedIds.Append(draft.WeaponCardId).ToArray(), CardLocation.DiscardPile,
            new CardMoveReason("skill-program.weapon-discard-cost"));
        AdvanceEventRulesAndQueueFact(new ProgramWeaponDamageChoiceEvent(frame.Id, frame.SkillId, frame.OwnerSeat, draft.TargetSeat,
            draft.WeaponCardId, branch == "finish", branch == "finish" ? draft.SelectedIds : [], window.Amount, after));
        if (!TryBeginCardsMovedProgramWindow(frame.Id)) AdvanceRuntimeProgram(frame.Id);
    }

    private PromptChoice SelectAiWeaponDamage(PendingDecision decision, ProgramSkillFrame frame) =>
        decision.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("branch") == "finish") ??
        decision.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("branch") == "card") ?? decision.Choices.Single();

    private void ValidateWeaponDamageDraft(ProgramSkillFrame frame)
    {
        if (frame.WeaponDamageDraft is not { } draft) return;
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.WeaponDiscardOrDamageBonus || !IsValidPlayerSeat(draft.TargetSeat) || draft.RequiredCount < 0 ||
            draft.SelectedIds.Count > draft.RequiredCount || draft.SelectedIds.Distinct().Count() != draft.SelectedIds.Count ||
            draft.SelectedIds.Any(id => _cardZones.GetLocation(id) != CardLocation.Hand(draft.TargetSeat)) ||
            _cardZones.GetLocation(draft.WeaponCardId) != CardLocation.Equipment(frame.OwnerSeat))
            throw new InvalidOperationException("Invalid weapon damage draft.");
        if (ReferenceEquals(frame, _resolutionStack.LastOrDefault()) &&
            (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } decision || decision.PlayerSeat != draft.TargetSeat ||
             decision.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") != "weapon-damage")))
            throw new InvalidOperationException("Weapon damage must preserve its private victim prompt.");
    }
}
