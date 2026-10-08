using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed record OriginalHandStateKey(int OwnerSeat, string SkillId, string StateId);
    private sealed record OriginalHandEntity(int CardId, long DealSequence, long? FirstLossSequence);
    private sealed record OriginalHandEntityState(long InitializationFrameId, CardConversionSource Source,
        string GameplayHash, IReadOnlyList<OriginalHandEntity> Entities);
    // Durable gameplay state, not pending resolution. A departed physical
    // entity never becomes original again on return or source reacquisition.
    private readonly Dictionary<OriginalHandStateKey, OriginalHandEntityState> _originalHandEntityStates = new();
    private readonly Dictionary<long, OriginalHandPermanentBonus> _originalHandPermanentBonuses = new();
    private bool TracksOriginalHandEntities => _contentRegistry.ProgramDependencies
        .HasTriggerOperation(SkillProgramEffectOp.InitializeOriginalHandEntities);
    private long OriginalHandMovementSequence => _cardMovements.LastOrDefault()?.Sequence ?? 0;
    private int GetOriginalHandHandLimitBonus(int seat) => _originalHandPermanentBonuses.Values.Count(b =>
        b.RecipientSeat == seat && b.Kind == OriginalHandBenefitKind.HandLimit);
    private int GetOriginalHandAttackRangeBonus(int seat) => _originalHandPermanentBonuses.Values.Count(b =>
        b.RecipientSeat == seat && b.Kind == OriginalHandBenefitKind.AttackRange);
    private IReadOnlyList<OriginalHandEntitySnapshot>? ProjectOriginalHandEntities(int ownerSeat, int viewerSeat, bool revealAll)
    {
        if (ownerSeat != viewerSeat && !revealAll && !_players[ownerSeat].GeneralRevealed) return null;
        var items = _originalHandEntityStates.Where(p => p.Key.OwnerSeat == ownerSeat)
            .OrderBy(p => p.Key.SkillId, StringComparer.Ordinal).ThenBy(p => p.Key.StateId, StringComparer.Ordinal)
            .Select(p => new OriginalHandEntitySnapshot(p.Key.SkillId, p.Key.StateId,
                _contentRegistry.GetSkill(p.Key.SkillId).ProgramPresentation?.AuthorityName ?? "原始手牌",
                p.Value.Entities.Count(e => e.FirstLossSequence is null), ownerSeat == viewerSeat || revealAll ?
                    p.Value.Entities.Where(e => e.FirstLossSequence is null && _cardZones.GetLocation(e.CardId) == CardLocation.Hand(ownerSeat))
                        .Select(e => e.CardId).ToArray() : null)).ToArray();
        return items.Length == 0 ? null : Array.AsReadOnly(items);
    }
    private OriginalHandPermanentBonus[] OriginalHandInheritableBonuses(int owner, string skill, string stateId) =>
        _originalHandPermanentBonuses.Values.Where(b => b.RecipientSeat == owner && b.SourceSkillId == skill && b.StateId == stateId)
            .OrderBy(b => b.BonusId).ToArray();
    private static bool IsOriginalHandTrigger(SkillProgramTrigger trigger, SkillProgramEffectOp op) =>
        trigger.Effects is [var effect] && effect.Op == op;
    private OriginalHandLossEligibleEvent? OriginalHandLossEligibility(int owner, string skill, string state, string binding, long sequence) =>
        CompleteProgramEventHistory().OfType<OriginalHandLossEligibleEvent>().SingleOrDefault(e => e.OwnerSeat == owner &&
            e.SkillId == skill && e.StateId == state && e.BindingId == binding && e.MovementSequence == sequence);

    private void ObserveOriginalHandMovement(CardMovementRecord movement)
    {
        if (movement.From is not { Zone: CardZoneKind.Hand, OwnerSeat: { } ownerSeat } || movement.To == movement.From ||
            !_cardMovements.Contains(movement)) return;
        foreach (var pair in _originalHandEntityStates.Where(p => p.Key.OwnerSeat == ownerSeat).ToArray())
        {
            var original = pair.Value.Entities.SingleOrDefault(e => e.CardId == movement.CardId && e.FirstLossSequence is null);
            if (original is null || movement.Sequence <= original.DealSequence) continue;
            var entities = pair.Value.Entities.Select(e => e == original ? e with { FirstLossSequence = movement.Sequence } : e).ToArray();
            _originalHandEntityStates[pair.Key] = pair.Value with { Entities = Array.AsReadOnly(entities) };
            AdvanceEventRulesAndQueueFact(new OriginalHandEntityConsumedEvent(ownerSeat, pair.Key.SkillId, pair.Key.StateId,
                movement.Sequence, entities.Count(e => e.FirstLossSequence is null)));
            var owner = _players[ownerSeat]; if (!owner.IsAlive || _winner != Winner.None) continue;
            var bindings = GetSkillBindingShard(owner).ProgramInstances.Where(i => i.SkillId == pair.Key.SkillId)
                .SelectMany(i => i.Program.Triggers.Where(t => IsOriginalHandTrigger(t, SkillProgramEffectOp.OfferOriginalHandLossBenefit) &&
                    t.Effects[0].StateId == pair.Key.StateId).Select(t => (Instance: i, Trigger: t)))
                .OrderBy(b => b.Instance.SkillInstanceId, StringComparer.Ordinal).ToArray();
            if (bindings.FirstOrDefault() is not { Instance: { } instance, Trigger: { } trigger }) continue;
            AdvanceEventRulesAndQueueFact(new OriginalHandLossEligibleEvent(ownerSeat, pair.Key.SkillId, pair.Key.StateId,
                instance.SkillInstanceId, instance.Program.GameplayHash, trigger.Id, movement.Sequence));
        }
    }

    private bool CanOfferOriginalHandEntityProgram(int ownerSeat, string skillId, SkillProgramTrigger trigger,
        ProgramSkillWindowContext context, string? skillInstanceId = null)
    {
        if (trigger.Effects is not [var effect] || !OriginalHandEntitiesComposition.IsOperation(effect.Op)) return true;
        var key = new OriginalHandStateKey(ownerSeat, effect.SourceBind ?? skillId, effect.StateId!);
        if (effect.Op == SkillProgramEffectOp.InitializeOriginalHandEntities)
            return context.Window == SkillProgramTriggerWindow.GameStarting && !_originalHandEntityStates.ContainsKey(key);
        if (effect.Op == SkillProgramEffectOp.AwakenWhenOriginalHandEmpty)
            return context.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && ownerSeat == _currentSeat &&
                _originalHandEntityStates.TryGetValue(key, out var state) && state.Entities.All(e => e.FirstLossSequence is not null);
        if (effect.Op == SkillProgramEffectOp.InheritOriginalHandBonuses)
            return context.Window == SkillProgramTriggerWindow.OwnerDied && !_players[ownerSeat].IsAlive &&
                OriginalHandInheritableBonuses(ownerSeat, skillId, effect.StateId!).Length > 0 &&
                _players.Any(p => p.IsAlive && p.Seat != ownerSeat);
        if (context.Window != SkillProgramTriggerWindow.CardsMoved || context.MovementBatch is not { } batch ||
            context.MovementIndex is not { } index || index < 0 || index >= batch.Movements.Count) return false;
        var movement = batch.Movements[index];
        var eligible = OriginalHandLossEligibility(ownerSeat, skillId, effect.StateId!, trigger.Id, movement.Sequence);
        return movement.From == CardLocation.Hand(ownerSeat) && movement.To != movement.From && _cardMovements.Contains(movement) &&
            _originalHandEntityStates.TryGetValue(key, out var originalState) &&
            originalState.Entities.Any(e => e.CardId == movement.CardId && e.FirstLossSequence == movement.Sequence) &&
            eligible is not null && (skillInstanceId is null || eligible.SkillInstanceId == skillInstanceId) &&
            HasRuntimeSkillInstance(_players[ownerSeat], skillId, eligible.SkillInstanceId) &&
            !CompleteProgramEventHistory().OfType<OriginalHandBenefitStartedEvent>().Any(e => e.Source.OwnerSeat == ownerSeat &&
                e.Source.SkillId == skillId && e.StateId == effect.StateId && e.MovementSequence == movement.Sequence);
    }

    private SkillProgramStepOutcome InitializeOriginalHandEntities(ProgramSkillFrame supplied, string stateId)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!ExactOriginalHandLifecycleParent(f, SkillProgramTriggerWindow.GameStarting) || f.InstructionIndex != 1 ||
            !IsOriginalHandTrigger(GetProgramTrigger(f), SkillProgramEffectOp.InitializeOriginalHandEntities) ||
            !_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
            throw new InvalidOperationException("Original-hand initialization requires its exact native game-start candidate.");
        var key = new OriginalHandStateKey(f.OwnerSeat, f.SkillId, stateId);
        if (_originalHandEntityStates.ContainsKey(key)) throw new InvalidOperationException("An original-hand state cannot initialize twice.");
        var entities = _cardMovements.Where(m => m.Reason == CardMoveReasons.InitialDeal && m.From == CardLocation.DrawPile &&
                m.To == CardLocation.Hand(f.OwnerSeat)).OrderBy(m => m.Sequence).Select(m => new OriginalHandEntity(m.CardId, m.Sequence,
                _cardMovements.FirstOrDefault(lost => lost.Sequence > m.Sequence && lost.CardId == m.CardId &&
                    lost.From == CardLocation.Hand(f.OwnerSeat) && lost.To != lost.From)?.Sequence)).ToArray();
        if (entities.Select(e => e.CardId).Distinct().Count() != entities.Length)
            throw new InvalidOperationException("The initial deal contains duplicate original physical entities.");
        var source = new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
        _originalHandEntityStates.Add(key, new(f.Id, source, f.GameplayHash, Array.AsReadOnly(entities)));
        AdvanceEventRulesAndQueueFact(new OriginalHandEntitiesInitializedEvent(f.Id, source, f.GameplayHash, stateId,
            entities.Length, entities.Count(e => e.FirstLossSequence is null)));
        return SkillProgramStepOutcome.Continue;
    }

    private SkillProgramStepOutcome BeginOriginalHandBenefit(ProgramSkillFrame supplied, string stateId)
    {
        var f = GetActiveProgramFrame(supplied.Id); var trigger = GetProgramTrigger(f);
        if (f.OriginalHandBenefit is not null || f.InstructionIndex != 1 || !ExactOriginalHandBenefitParent(f) ||
            !CanOfferOriginalHandEntityProgram(f.OwnerSeat, f.SkillId, trigger, f.WindowContext!, f.SkillInstanceId))
            throw new InvalidOperationException("The original-hand benefit lost its exact first physical hand loss.");
        var context = f.WindowContext!; var movement = context.MovementBatch!.Movements[context.MovementIndex!.Value];
        var r = new ProgramOriginalHandBenefitReceipt { InstructionIndex = 1,
            Source = new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId), GameplayHash = f.GameplayHash,
            StateId = stateId, MovementWindowId = context.ParentFrameId, BatchId = context.MovementBatch.Id,
            MovementSequence = movement.Sequence, Stage = OriginalHandBenefitStage.ChoosingTarget,
            CandidateSeats = _players.Where(p => p.IsAlive).Select(p => p.Seat).ToArray() };
        ReplaceRuntimeTop(f = f with { OriginalHandBenefit = r });
        AdvanceEventRulesAndQueueFact(new OriginalHandBenefitStartedEvent(f.Id, r.Source, r.GameplayHash, stateId,
            r.MovementWindowId, r.BatchId, r.MovementSequence));
        PublishOriginalHandEntityChoice(f); return SkillProgramStepOutcome.AwaitChoice;
    }

    private SkillProgramStepOutcome BeginOriginalHandInheritance(ProgramSkillFrame supplied, string stateId)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.OriginalHandInheritance is not null || f.InstructionIndex != 1 || !ExactOriginalHandInheritanceParent(f) ||
            !CanOfferOriginalHandEntityProgram(f.OwnerSeat, f.SkillId, GetProgramTrigger(f), f.WindowContext!, f.SkillInstanceId))
            throw new InvalidOperationException("Original-hand inheritance requires its exact optional native owner-death candidate.");
        var window = (ProgramDeathTriggerWindowFrame)_resolutionStack.Single(w => w.Id == f.WindowContext!.ParentFrameId);
        var r = new ProgramOriginalHandInheritanceReceipt { InstructionIndex = 1,
            Source = new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId), GameplayHash = f.GameplayHash,
            StateId = stateId, DeathWindowId = window.Id, DeathFrameId = window.DeathFrameId,
            Stage = OriginalHandInheritanceStage.ChoosingTarget,
            CandidateSeats = _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat).Select(p => p.Seat).ToArray(),
            Bonuses = OriginalHandInheritableBonuses(f.OwnerSeat, f.SkillId, stateId) };
        ReplaceRuntimeTop(f = f with { OriginalHandInheritance = r });
        AdvanceEventRulesAndQueueFact(new OriginalHandInheritanceStartedEvent(f.Id, r.Source, r.GameplayHash, stateId,
            r.DeathFrameId, r.DeathWindowId, r.Bonuses.Count));
        PublishOriginalHandEntityChoice(f); return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> OriginalHandEntityChoices(ProgramSkillFrame f)
    {
        var binding = f.Id.ToString(CultureInfo.InvariantCulture);
        PromptChoice Target(int seat, string action) => new(new($"original-hand.{f.Id}.{action}.{seat}"), _players[seat].Name, [], [seat],
            new Dictionary<string, string> { ["program-action"] = "original-hand-entity", ["entity-action"] = action, ["frame-id"] = binding });
        if (f.OriginalHandInheritance is { Stage: OriginalHandInheritanceStage.ChoosingTarget } inheritance)
            return Array.AsReadOnly(inheritance.CandidateSeats.Select(seat => Target(seat, "inheritance")).ToArray());
        var r = f.OriginalHandBenefit!;
        if (r.Stage == OriginalHandBenefitStage.ChoosingTarget)
            return Array.AsReadOnly(r.CandidateSeats.Select(seat => Target(seat, "benefit-target")).ToArray());
        return Array.AsReadOnly(new[] { (Id: "hand-limit", Text: "永久增加1点手牌上限"), (Id: "attack-range", Text: "永久增加1点攻击范围"),
            (Id: "draw", Text: "摸一张牌") }.Select(o => new PromptChoice(new($"original-hand.{f.Id}.{o.Id}"), o.Text, [], [],
                new Dictionary<string, string> { ["program-action"] = "original-hand-entity", ["entity-action"] = "benefit-option",
                    ["frame-id"] = binding, ["option"] = o.Id })).ToArray());
    }
    private void PublishOriginalHandEntityChoice(ProgramSkillFrame f)
    {
        var choices = OriginalHandEntityChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "选择获得原始手牌奖励的角色及奖励。", [],
            Array.AsReadOnly(choices.SelectMany(c => c.Targets).Distinct().ToArray()), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private void ResolveOriginalHandEntityChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Original-hand choice lost its frame.");
        AssertOriginalHandEntityProgram(f);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } prompt || prompt.PlayerSeat != f.OwnerSeat ||
            choice.Parameters.GetValueOrDefault("program-action") != "original-hand-entity" ||
            choice.Parameters.GetValueOrDefault("frame-id") != f.Id.ToString(CultureInfo.InvariantCulture) || choice.Cards.Count != 0 ||
            !OriginalHandEntityChoices(f).Any(c => c.Id == choice.Id && c.Targets.SequenceEqual(choice.Targets) &&
                c.Parameters.OrderBy(p => p.Key).SequenceEqual(choice.Parameters.OrderBy(p => p.Key))))
            throw new InvalidOperationException("Original-hand choice changed its exact published owner, target or option.");
        ClearPendingDecision();
        if (f.OriginalHandInheritance is { } inheritance)
        {
            var target = choice.Targets.Single();
            if (!_players[target].IsAlive || target == f.OwnerSeat) throw new InvalidOperationException("Inheritance needs its original living other target.");
            foreach (var bonus in inheritance.Bonuses)
            {
                if (_originalHandPermanentBonuses.GetValueOrDefault(bonus.BonusId) != bonus)
                    throw new InvalidOperationException("Inheritance cannot replace or duplicate a frozen received bonus.");
                _originalHandPermanentBonuses[bonus.BonusId] = bonus with { RecipientSeat = target };
                AdvanceEventRulesAndQueueFact(new OriginalHandBonusTransferredEvent(f.Id, bonus.BonusId, bonus.SourceOwnerSeat,
                    bonus.SourceSkillId, bonus.StateId, f.OwnerSeat, target, bonus.Kind));
            }
            ReplaceRuntimeTop(f = f with { OriginalHandInheritance = inheritance with {
                Stage = OriginalHandInheritanceStage.Complete, TargetSeat = target, Applied = true } });
            AdvanceEventRulesAndQueueFact(new OriginalHandInheritanceCompletedEvent(f.Id, target, inheritance.Bonuses.Count));
            FinishProgramSkill(f, true); return;
        }
        var r = f.OriginalHandBenefit!;
        if (!_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
        { CancelProgramBindingAndCleanup(f, "原始手牌奖励在选定并发行前失去原技能资格。"); return; }
        if (r.Stage == OriginalHandBenefitStage.ChoosingTarget)
        {
            var target = choice.Targets.Single();
            if (!_players[target].IsAlive) throw new InvalidOperationException("The selected original-hand benefit target is no longer living.");
            ReplaceRuntimeTop(f = f with { OriginalHandBenefit = r with { Stage = OriginalHandBenefitStage.ChoosingOption, TargetSeat = target } });
            PublishOriginalHandEntityChoice(f); return;
        }
        var kind = choice.Parameters["option"] switch { "hand-limit" => OriginalHandBenefitKind.HandLimit,
            "attack-range" => OriginalHandBenefitKind.AttackRange, "draw" => OriginalHandBenefitKind.Draw,
            _ => throw new InvalidOperationException("Unknown original-hand benefit option.") };
        var recipient = r.TargetSeat!.Value;
        ReplaceRuntimeTop(f = f with { OriginalHandBenefit = r with { Option = kind } });
        AdvanceEventRulesAndQueueFact(new OriginalHandBenefitChosenEvent(f.Id, recipient, kind));
        if (!_players[recipient].IsAlive || _winner != Winner.None) { CompleteOriginalHandBenefit(f); return; }
        if (kind != OriginalHandBenefitKind.Draw)
        {
            _originalHandPermanentBonuses.Add(f.Id, new(f.Id, f.OwnerSeat, f.SkillId, r.StateId, recipient, kind));
            ReplaceRuntimeTop(f = f with { OriginalHandBenefit = f.OriginalHandBenefit! with { BonusApplied = true } });
            AdvanceEventRulesAndQueueFact(new OriginalHandPermanentBonusGrantedEvent(f.Id, f.OwnerSeat, f.SkillId, r.StateId, recipient, kind));
            CompleteOriginalHandBenefit(f); return;
        }
        var before = OriginalHandMovementSequence;
        ReplaceRuntimeTop(f = f with { OriginalHandBenefit = f.OriginalHandBenefit! with { Stage = OriginalHandBenefitStage.DrawChildren,
            DrawIssued = true, DrawBefore = before, DrawAfter = before }, PendingMovementContinuation = new(recipient, 0, null) });
        var actual = DrawCards(_players[recipient], 1, true, new($"skill-program.{f.SkillId}.original-hand-benefit.draw"), r.Source).Count;
        var current = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(current with { OriginalHandBenefit = current.OriginalHandBenefit! with {
            ActualDrawCount = actual, DrawAfter = OriginalHandMovementSequence } });
        AdvanceEventRulesAndQueueFact(new OriginalHandBenefitDrawIssuedEvent(f.Id, recipient, actual, before, OriginalHandMovementSequence));
        AdvanceRuntimeProgram(f.Id);
    }
    private void CompleteOriginalHandBenefit(ProgramSkillFrame f)
    {
        var r = f.OriginalHandBenefit!;
        ReplaceRuntimeTop(f = f with { OriginalHandBenefit = r with { Stage = OriginalHandBenefitStage.Complete }, PendingMovementContinuation = null });
        AdvanceEventRulesAndQueueFact(new OriginalHandBenefitCompletedEvent(f.Id, r.TargetSeat, r.Option, r.BonusApplied, r.DrawIssued, r.ActualDrawCount));
        FinishProgramSkill(f, true);
    }
    private bool ResumeOriginalHandEntityProgram(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id) return false;
        if (f.OriginalHandAwakening is not null) return ResumeOriginalHandAwakening(id);
        if (f.OriginalHandBenefit is null && f.OriginalHandInheritance is null) return false;
        AssertOriginalHandEntityProgram(f);
        if (f.OriginalHandInheritance is not null || f.OriginalHandBenefit!.Stage is OriginalHandBenefitStage.ChoosingTarget or OriginalHandBenefitStage.ChoosingOption)
        { if (_pendingDecision is null) PublishOriginalHandEntityChoice(f); return true; }
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return true;
        CompleteOriginalHandBenefit(GetActiveProgramFrame(id)); return true;
    }
    private bool ReturnOriginalHandEntityMovement(ProgramSkillFrame f)
    {
        if (f.OriginalHandBenefit is not { Stage: OriginalHandBenefitStage.DrawChildren }) return false;
        AssertOriginalHandEntityProgram(f); AdvanceRuntimeProgram(f.Id); return true;
    }
    private bool IsOriginalHandEntityMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect?.Op == SkillProgramEffectOp.OfferOriginalHandLossBenefit && f.OriginalHandBenefit is { Stage: OriginalHandBenefitStage.DrawChildren } r &&
        f.PendingMovementContinuation == pending && pending.SubjectSeat == r.TargetSeat && pending.BeforeCount == 0 &&
        pending.CoverageResultBind is null && ValidOriginalHandBenefitReceipt(f);
    private bool CanContinueOriginalHandEntityProgram(ProgramSkillFrame f) =>
        f.OriginalHandBenefit is { DrawIssued: true } && ValidOriginalHandBenefitReceipt(f) ||
        f.OriginalHandInheritance is not null && ValidOriginalHandInheritanceReceipt(f) ||
        f.OriginalHandAwakening is not null && ValidOriginalHandAwakeningReceipt(f);
    private PromptChoice SelectAiOriginalHandEntityChoice(PendingDecision decision, ProgramSkillFrame f) =>
        decision.Choices.FirstOrDefault(c => c.Targets.Contains(f.OwnerSeat)) ??
        decision.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("option") == "draw") ?? decision.Choices[0];
    private sealed partial class ProgramSkillHost : IOriginalHandEntitiesProgramHost
    {
        public bool CanContinueOriginalHandEntityProgram(ProgramSkillFrame f) => engine.CanContinueOriginalHandEntityProgram(f);
        public SkillProgramStepOutcome ExecuteOriginalHandEntityOperation(ProgramSkillFrame f, SkillProgramEffect e) => e.Op switch {
            SkillProgramEffectOp.InitializeOriginalHandEntities => engine.InitializeOriginalHandEntities(f, e.StateId!),
            SkillProgramEffectOp.OfferOriginalHandLossBenefit => engine.BeginOriginalHandBenefit(f, e.StateId!),
            SkillProgramEffectOp.InheritOriginalHandBonuses => engine.BeginOriginalHandInheritance(f, e.StateId!),
            SkillProgramEffectOp.AwakenWhenOriginalHandEmpty => engine.BeginOriginalHandAwakening(f, e),
            _ => throw new InvalidOperationException("Unknown original-hand operation.") };
    }
}
