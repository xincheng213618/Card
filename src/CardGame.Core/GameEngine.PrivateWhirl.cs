using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    // Capability latch only: replayed commands rebuild it, it contains no use ID,
    // entity, pending cost or continuation. Ordinary matches never scan its history.
    private bool _hasIssuedPrivateWhirl;
    private bool HasPrivateWhirlCapability => _hasIssuedPrivateWhirl;
    private static CardConversionSource WhirlSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_PrivateWhirl.WhirlSource(f);
    private static CardLocation WhirlLocation(CardConversionSource source) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_PrivateWhirl.WhirlLocation(source);
    private PrivateWhirlDeposit[] LiveWhirl(int owner, string? skill = null, string? instance = null)
    {
        if (!HasPrivateWhirlCapability || !_cardZones.HasPrivatePersistentCards(owner)) return [];
        var history = CompleteProgramEventHistory().ToArray(); var removed = history.OfType<PrivateWhirlRemovedEvent>().Select(e => (e.Deposit.DepositId, e.Deposit.Source, e.Deposit.CardId)).ToHashSet();
        return history.OfType<PrivateWhirlDepositedEvent>().Select(e => e.Deposit).Where(d => d.Source.OwnerSeat == owner && (skill is null || d.Source.SkillId == skill) && (instance is null || d.Source.SkillInstanceId == instance) && !removed.Contains((d.DepositId, d.Source, d.CardId)) && _cardZones.GetLocation(d.CardId) == d.Location).ToArray();
    }
    private bool WhirlSourceCurrent(ProgramSkillFrame f) => _players[f.OwnerSeat].IsAlive && HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) && _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash == f.GameplayHash;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 340)]
    private bool CanOfferPrivateWhirl(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (trigger.Effects is not [var effect]) return true;
        var deposits = LiveWhirl(candidate.OwnerSeat, candidate.SkillId, candidate.SkillInstanceId);
        if (IsPrivateWhirlDeposit(effect.Op)) return LiveWhirl(candidate.OwnerSeat).Length < PrivateWhirlMaximum(candidate.SkillId) && GetHand(_players[candidate.OwnerSeat]).Count > 0;
        if (effect.Op == SkillProgramEffectOp.ClearPrivateWhirlAtNextPlayEnding) return deposits.Any(d => _cardUseDebitPhaseInstanceId > d.CreatedPlayInstance);
        if (effect.Op != SkillProgramEffectOp.ConsumePrivateWhirlForActualUse) return true;
        return deposits.Length > 0 && context.CardUse is { } card && LifecycleCardUse(card.ParentCardUseFrameId) is { Action: { Type: CardActionType.Use } action } &&
            action.ActorSeat == candidate.OwnerSeat && action.ActionId == card.CardActionId && !CompleteProgramEventHistory().OfType<PrivateWhirlSegmentIssuedEvent>().Any(e => e.Source.OwnerSeat == candidate.OwnerSeat && e.Source.SkillId == candidate.SkillId && e.Source.SkillInstanceId == candidate.SkillInstanceId && e.ActionId == action.ActionId);
    }
    private SkillProgramStepOutcome BeginPrivateWhirl(ProgramSkillFrame supplied, SkillProgramEffect effect)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.PrivateWhirl is not null || f.WindowContext is not { } context || !WhirlSourceCurrent(f) || !CanOfferPrivateWhirl(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority), GetProgramTrigger(f), context)) throw new InvalidOperationException("Whirl requires an unpaid current source and exact offered boundary.");
        _hasIssuedPrivateWhirl = true;
        var r = new PrivateWhirlReceipt { InstructionIndex = f.InstructionIndex, Source = WhirlSource(f), GameplayHash = f.GameplayHash, WindowId = context.ParentFrameId, CreatedPlayInstance = _cardUseDebitPhaseInstanceId };
        if (IsPrivateWhirlDeposit(effect.Op)) r = r with { Stage = PrivateWhirlStage.SelectDeposit };
        else if (effect.Op == SkillProgramEffectOp.ConsumePrivateWhirlForActualUse)
        { var use = LifecycleCardUse(context.CardUse!.ParentCardUseFrameId)!; r = r with { Stage = PrivateWhirlStage.SelectRemove, OriginalAction = use.Action, UseId = use.Id, Deposits = LiveWhirl(f.OwnerSeat, f.SkillId, f.SkillInstanceId) }; }
        else r = r with { Stage = PrivateWhirlStage.ExpiryChildren, Deposits = LiveWhirl(f.OwnerSeat, f.SkillId, f.SkillInstanceId).Where(d => _cardUseDebitPhaseInstanceId > d.CreatedPlayInstance).ToArray() };
        ReplaceRuntimeTop(f = f with { PrivateWhirl = r });
        if (r.Stage == PrivateWhirlStage.ExpiryChildren) { PayWhirlRemoval(f, r.Deposits, "play-expired"); return SkillProgramStepOutcome.AwaitChild; }
        PublishWhirlChoice(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private PromptChoice WhirlChoice(ProgramSkillFrame f, string branch, string label, IReadOnlyList<int>? cards = null) => new(new($"private-whirl.{f.Id}.{f.PrivateWhirl!.Stage}.{branch}"), label, cards ?? [], [], new Dictionary<string, string> { ["program-action"] = "private-whirl", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });
    private IReadOnlyList<PromptChoice> WhirlChoices(ProgramSkillFrame f)
    {
        var r = f.PrivateWhirl!; var choices = new List<PromptChoice>();
        if (r.Stage == PrivateWhirlStage.SelectDeposit)
        {
            if (r.Selected.Count + LiveWhirl(f.OwnerSeat).Length < PrivateWhirlMaximum(f.SkillId)) choices.AddRange(GetHand(_players[f.OwnerSeat]).Where(c => !r.Selected.Contains(c.Id)).Select(c => WhirlChoice(f, $"add-{c.Id}", $"暗置{c.DisplayName}（{c.Suit} {c.RankText}）", [c.Id])));
            choices.Add(WhirlChoice(f, "confirm", $"确认暗置{r.Selected.Count}张手牌", r.Selected));
        }
        else if (r.Stage == PrivateWhirlStage.SelectRemove) choices.AddRange(r.Deposits.Where(d => _cardZones.GetLocation(d.CardId) == d.Location).Select(d => WhirlChoice(f, $"remove-{d.CardId}", $"移去旋中的{GetAttackCard(d.CardId).DisplayName}", [d.CardId])));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishWhirlChoice(ProgramSkillFrame f)
    {
        var choices = WhirlChoices(f); if (choices.Count == 0) { FinishWhirl(f); return; }
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse }); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "请选择暗置或移去的旋。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) }; _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("private-whirl")]
    private void ResolvePrivateWhirlChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Whirl lost its owning frame."); RequirePrivateWhirl(f); var r = f.PrivateWhirl!;
        if (_pendingDecision?.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual([choice], WhirlChoices(f).Where(c => c.Id == choice.Id).ToArray())) throw new InvalidOperationException("Whirl selection changed while suspended.");
        ClearPendingDecision(); if (!WhirlSourceCurrent(f)) { FinishWhirl(f); return; }
        if (r.Stage == PrivateWhirlStage.SelectRemove) { PayWhirlRemoval(f, [r.Deposits.Single(d => d.CardId == choice.Cards.Single())], "used"); return; }
        if (choice.Parameters["branch"] != "confirm") { ReplaceRuntimeTop(f = f with { PrivateWhirl = r with { Selected = r.Selected.Append(choice.Cards.Single()).ToArray() } }); PublishWhirlChoice(f); return; }
        if (r.Selected.Count == 0) { FinishWhirl(f); return; }
        var location = WhirlLocation(r.Source); _cardZones.EnsurePrivatePersistentPile(location); var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { PrivateWhirl = r with { Stage = PrivateWhirlStage.DepositChildren, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        var deposits = new List<PrivateWhirlDeposit>();
        foreach (var id in r.Selected)
        {
            var sequence = _movementSequence; MoveCard(GetHand(_players[f.OwnerSeat]).Single(c => c.Id == id), CardLocation.Hand(f.OwnerSeat), location, new("skill-program.private-whirl.deposit"));
            var deposit = new PrivateWhirlDeposit(f.Id, r.Source, r.GameplayHash, id, location, r.CreatedPlayInstance, sequence, _movementSequence); deposits.Add(deposit); AdvanceEventRulesAndQueueFact(new PrivateWhirlDepositedEvent(deposit));
        }
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { PrivateWhirl = f.PrivateWhirl! with { Deposits = deposits, After = _movementSequence } }); IssueWhirlSegment(f, r.Selected.Count); ContinueWhirl(f);
    }
    private void PayWhirlRemoval(ProgramSkillFrame f, IReadOnlyList<PrivateWhirlDeposit> deposits, string reason)
    {
        var r = f.PrivateWhirl!; var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { PrivateWhirl = r with { Deposits = deposits, Stage = reason == "used" ? PrivateWhirlStage.RemovalChildren : PrivateWhirlStage.ExpiryChildren, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null), Step = ResolutionFrameStep.ResolvingEffect });
        foreach (var d in deposits)
        {
            var sequence = _movementSequence; MoveCard(GetAttackCard(d.CardId), d.Location, CardLocation.DiscardPile, new($"skill-program.private-whirl.{reason}")); AdvanceEventRulesAndQueueFact(new PrivateWhirlRemovedEvent(f.Id, d, r.OriginalAction?.ActionId, sequence, _movementSequence, reason));
        }
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { PrivateWhirl = f.PrivateWhirl! with { After = _movementSequence } }); IssueWhirlSegment(f, deposits.Count); ContinueWhirl(f);
    }
    private void IssueWhirlSegment(ProgramSkillFrame f, int count) { var r = f.PrivateWhirl!; AdvanceEventRulesAndQueueFact(new PrivateWhirlSegmentIssuedEvent(f.Id, r.Source, r.Stage, r.OriginalAction?.ActionId, r.Before, r.After, count, r.RemainingCount, r.NotUniqueMaximum)); }
    private bool DrainWhirl(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueWhirl(ProgramSkillFrame f)
    {
        if (DrainWhirl(f)) return; f = GetActiveProgramFrame(f.Id); var r = f.PrivateWhirl!;
        if (r.Stage == PrivateWhirlStage.RemovalChildren) { DrawWhirlSegment(f, 1, PrivateWhirlStage.FirstDrawChildren); return; }
        if (r.Stage == PrivateWhirlStage.FirstDrawChildren)
        {
            var hand = GetHand(_players[f.OwnerSeat]).Count; var notUnique = _players.Any(p => p.IsAlive && p.Seat != f.OwnerSeat && GetHand(p).Count >= hand); var x = LiveWhirl(f.OwnerSeat, f.SkillId, f.SkillInstanceId).Length;
            ReplaceRuntimeTop(f = f with { PrivateWhirl = r with { RemainingCount = x, NotUniqueMaximum = notUnique } });
            if (notUnique && x > 0 && _players[f.OwnerSeat].IsAlive) { DrawWhirlSegment(f, x, PrivateWhirlStage.ExtraDrawChildren); return; }
        }
        FinishWhirl(f);
    }
    private void DrawWhirlSegment(ProgramSkillFrame f, int amount, PrivateWhirlStage stage)
    {
        var before = _movementSequence; ReplaceRuntimeTop(f = f with { PrivateWhirl = f.PrivateWhirl! with { Stage = stage, Before = before, After = before, DrawCount = 0 } });
        var drawn = _players[f.OwnerSeat].IsAlive ? DrawCards(_players[f.OwnerSeat], amount, true, new("skill-program.private-whirl.draw")).Count : 0;
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { PrivateWhirl = f.PrivateWhirl! with { After = _movementSequence, DrawCount = drawn } }); IssueWhirlSegment(f, drawn); ContinueWhirl(f);
    }
    private void FinishWhirl(ProgramSkillFrame f) { ReplaceRuntimeTop(f = f with { PrivateWhirl = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 22100)]
    private bool ResumePrivateWhirl(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { PrivateWhirl: { } r } f || f.Id != id || r.Stage is PrivateWhirlStage.SelectDeposit or PrivateWhirlStage.SelectRemove) return false; RequirePrivateWhirl(f); ContinueWhirl(f); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 22100)]
    private bool ReturnPrivateWhirlMovement(ProgramSkillFrame f) => f.PrivateWhirl is not null && ResumePrivateWhirl(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 60)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 60)]
    private bool CanContinuePrivateWhirl(ProgramSkillFrame f) { if (f.PrivateWhirl is not { } r || r.Stage is PrivateWhirlStage.SelectDeposit or PrivateWhirlStage.SelectRemove) return false; RequirePrivateWhirl(f); return true; }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 80)]
    private bool IsPrivateWhirlMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) => CanContinuePrivateWhirl(f) && (effect?.Op is SkillProgramEffectOp.DepositPrivateWhirlUntilNextPlayEnding or SkillProgramEffectOp.DepositPersistentPrivateWhirl or SkillProgramEffectOp.ConsumePrivateWhirlForActualUse or SkillProgramEffectOp.ClearPrivateWhirlAtNextPlayEnding) && pending.SubjectSeat == f.OwnerSeat && pending.CoverageResultBind is null;
    private void RequirePrivateWhirl(ProgramSkillFrame f)
    {
        if (f.PrivateWhirl is not { } r || r.Source != WhirlSource(f) || r.GameplayHash != f.GameplayHash || r.InstructionIndex != f.InstructionIndex || r.WindowId != f.WindowContext?.ParentFrameId || r.Selected.Distinct().Count() != r.Selected.Count || r.Selected.Count > PrivateWhirlMaximum(f.SkillId) || r.Before > r.After || r.After > _movementSequence || r.DrawCount < 0 || r.DrawCount > PrivateWhirlMaximum(f.SkillId)) throw new InvalidOperationException("Whirl lost its immutable owning source, instruction, selection or paid movement.");
        if (r.OriginalAction is { } original && (original.Type != CardActionType.Use || original.ActorSeat != f.OwnerSeat || original.ActionId != f.WindowContext?.CardUse?.CardActionId || r.UseId != f.WindowContext?.CardUse?.ParentCardUseFrameId || LifecycleCardUse(r.UseId!.Value)?.Action is not { } actual || !SameOverflowAction(original, actual))) throw new InvalidOperationException("Whirl lost its exact committed actual use.");
        if (r.Stage is PrivateWhirlStage.SelectDeposit or PrivateWhirlStage.SelectRemove) return;
        var facts = CompleteProgramEventHistory().OfType<PrivateWhirlSegmentIssuedEvent>().Where(e => e.ProgramFrameId == f.Id && e.Source == r.Source && e.Stage == r.Stage && e.Before == r.Before && e.After == r.After).ToArray();
        if (facts is not [var fact] || fact.ActionId != r.OriginalAction?.ActionId || f.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } p || p.SubjectSeat != f.OwnerSeat) throw new InvalidOperationException("Whirl cannot resume without its exact once-issued segment.");
        if (r.Stage is PrivateWhirlStage.FirstDrawChildren or PrivateWhirlStage.ExtraDrawChildren)
        { if (fact.Count != r.DrawCount || _cardMovements.Count(m => m.Sequence > r.Before && m.Sequence <= r.After && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == "skill-program.private-whirl.draw") != r.DrawCount) throw new InvalidOperationException("Whirl draw lost its once-paid native movement."); }
        else if (fact.Count != r.Deposits.Count || r.Deposits.Any(d => _cardMovements.Count(m => m.CardId == d.CardId && m.Sequence > r.Before && m.Sequence <= r.After && m.From == (r.Stage == PrivateWhirlStage.DepositChildren ? CardLocation.Hand(f.OwnerSeat) : d.Location) && m.To == (r.Stage == PrivateWhirlStage.DepositChildren ? d.Location : CardLocation.DiscardPile)) != 1)) throw new InvalidOperationException("Whirl deposit/removal lost its physical invoice.");
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 120)]
    private void AssertRegisteredPrivateWhirl(ProgramSkillFrame frame)
    {
        if (frame.PrivateWhirl is not null) RequirePrivateWhirl(frame);
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 80)]
    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 90)]
    private void CleanupLostPrivateWhirlPiles()
    {
        if (!HasPrivateWhirlCapability || !_cardZones.HasPrivatePersistentCards()) return;
        foreach (var d in _players.SelectMany(p => LiveWhirl(p.Seat)).Where(d => !_players[d.Source.OwnerSeat].IsAlive || !_players[d.Source.OwnerSeat].SkillGrants.Grants.Any(g => g.SkillId == d.Source.SkillId && g.SkillInstanceId == d.Source.SkillInstanceId)).ToArray())
        {
            if (_cardZones.GetLocation(d.CardId) != d.Location) continue; var before = _movementSequence; MoveCard(GetAttackCard(d.CardId), d.Location, CardLocation.DiscardPile, new("skill-program.private-whirl.source-lost")); AdvanceEventRulesAndQueueFact(new PrivateWhirlRemovedEvent(0, d, null, before, _movementSequence, "source-lost"));
        }
    }
    private IReadOnlyList<PrivateWhirlPileSnapshot>? ProjectPrivateWhirlPiles(int owner, int viewer, bool revealAll)
    {
        var deposits = LiveWhirl(owner); if (deposits.Length == 0) return null;
        return Array.AsReadOnly(deposits.GroupBy(d => (d.Source.SkillId, d.Source.SkillInstanceId)).Select(g => new PrivateWhirlPileSnapshot { SkillId = g.Key.SkillId, Count = g.Count(), Cards = owner == viewer || revealAll ? g.Select(d => ToSnapshot(GetAttackCard(d.CardId))).ToArray() : null }).ToArray());
    }
    [ProgramCapability]
    private sealed class PrivateWhirlCapability(GameEngine engine) : IPrivateWhirlHost
    { public SkillProgramStepOutcome BeginPrivateWhirl(ProgramSkillFrame f, SkillProgramEffect effect) => engine.BeginPrivateWhirl(f, effect); }
}
