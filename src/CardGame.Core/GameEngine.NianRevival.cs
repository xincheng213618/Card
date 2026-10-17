using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum NianRevivalStage { DiscardChildren, StateChildren, DrawChildren, DrawAttempt }
public sealed record NianRevivalReceipt(int InstructionIndex, int TargetSeat, int UsedRevives,
    int MaximumHp, NianRevivalStage Stage, long Before, long After, int Count);

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NianRevivalReceipt? NianRevival { get; init; }
}

// Scalar facts also serve as the shared, replayed payment ledger. A suspended
// operation owns its continuation on its ProgramSkillFrame, never on the team.
public sealed record NianRevivalPaidEvent(long FrameId, int ActorSeat, int TargetSeat, int UsedRevives,
    int MaximumHp) : IGameEvent;
public sealed record NianRevivalSegmentEvent(long FrameId, int TargetSeat, NianRevivalStage Stage,
    long Before, long After, int Count) : IGameEvent;
public sealed record NianAllyRevivedEvent(long FrameId, int ActorSeat, int TargetSeat, int UsedRevives,
    int Hp) : IGameEvent;
public sealed record NianRevivalCompletedEvent(long FrameId, int TargetSeat, int DrawnCards) : IGameEvent;
public sealed record NianRevivalCancelledEvent(long FrameId, int TargetSeat, string Reason) : IGameEvent;

internal interface INianRevivalHost
{
    SkillProgramStepOutcome ReviveNianAlly(ProgramSkillFrame frame);
}
internal interface INianRevivalSelectionQuery : IProgramSelectionQuery
{
    IReadOnlyList<int> DeadAllies(int ownerSeat);
}

internal sealed class ReviveNianAllyDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReviveNianAlly;
    public override ISkillProgramEffectHandler Handler { get; } = new ReviveNianAllyHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Recover,
        static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new ReadSelectedTarget()];
}
internal sealed class ReviveNianAllyHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ReviveNianAlly;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat,
        ISkillProgramEffectHost host) => host.GetCapability<INianRevivalHost>().ReviveNianAlly(frame);
}

internal static class NianRevivalComposition
{
    internal static void Validate(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow? window, bool selectedTarget, int selectedCards, int targetSetMaximum,
        SkillProgramTargetKind? targetKind)
    {
        if (!effects.Any(e => e.Op == SkillProgramEffectOp.ReviveNianAlly) &&
            targetKind != SkillProgramTargetKind.NianDeadAlly) return;
        if (window is not null || !selectedTarget || selectedCards != 0 || targetSetMaximum != 0 ||
            targetKind != SkillProgramTargetKind.NianDeadAlly || effects is not
            [{ Op: SkillProgramEffectOp.ReviveNianAlly, Target: SkillProgramEffectTarget.Owner,
                Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException($"{path}: Nian revival is one zero-card, one-dead-ally activation.");
    }
}

[ProgramTargetResolver(SkillProgramTargetKind.NianDeadAlly)]
internal sealed class NianDeadAllyResolver : IProgramTargetResolver
{
    public IReadOnlyList<int> Resolve(ProgramTargetResolverContext context, IProgramSelectionQueries queries) =>
        queries.Get<INianRevivalSelectionQuery>().DeadAllies(context.OwnerSeat);
}

// A specialized source provider owns the exceptional dead target domain. The
// ordinary activation selector keeps its universal living-target restriction.
[ProgramSelectionModule(ProgramSelectionStage.Sources, 27930, SkillProgramEffectOp.ReviveNianAlly)]
internal sealed class NianRevivalSelectionSource : IProgramSelectionSourceProvider
{
    public bool TryBuild(ProgramSelectionContext context, ProgramSelectionCandidates candidates,
        IProgramSelectionQueries queries)
    {
        candidates.Cards = [];
        return true;
    }
    public void BuildTargets(ProgramSelectionContext context, ProgramSelectionCandidates candidates,
        IProgramSelectionQueries queries) =>
        candidates.Targets = queries.Get<INianRevivalSelectionQuery>().DeadAllies(context.OwnerSeat).ToArray();
}

public sealed partial class GameEngine
{
    private const string NianReviveSkill = "nian:revive";
    private static readonly CardMoveReason NianRevivalDiscard = new("activity.nian.revival-discard");
    private static readonly CardMoveReason NianRevivalDraw = new("activity.nian.revival-draw");

    [ProgramCapability]
    private sealed class NianRevivalCapability(GameEngine engine) : INianRevivalHost, INianRevivalSelectionQuery
    {
        public SkillProgramStepOutcome ReviveNianAlly(ProgramSkillFrame frame) => engine.BeginNianRevival(frame);
        public IReadOnlyList<int> DeadAllies(int ownerSeat) => engine.NianDeadAllies(ownerSeat);
    }

    private int NianUsedRevives => CompleteProgramEventHistory().OfType<NianRevivalPaidEvent>().Count();
    private bool IsNianAlliedSeat(int seat) => _modeDefinition.NianActivity is not null &&
        _modeDefinition.BossActivity is { } activity && IsValidPlayerSeat(seat) &&
        seat != BossActivitySeat && _players[seat].TeamId == activity.PlayerTeamId;

    private IReadOnlyList<int> NianDeadAllies(int ownerSeat)
    {
        if (!IsNianAlliedSeat(ownerSeat) || !_players[ownerSeat].IsAlive ||
            NianUsedRevives >= _modeDefinition.NianActivity!.SharedRevives) return [];
        var died = CompleteProgramEventHistory().OfType<PlayerDiedEvent>().Select(e => e.VictimSeat).ToHashSet();
        return Array.AsReadOnly(_players.Where(p => p.Seat != ownerSeat && IsNianAlliedSeat(p.Seat) &&
                !p.IsAlive && p.GeneralSelected && p.MaxHp > 0 && p.HasHpValue && died.Contains(p.Seat))
            .Select(p => p.Seat).Order().ToArray());
    }

    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeOwnerFacts, 27930, SkillProgramEffectOp.ReviveNianAlly)]
    private bool AllowsNianRevival(ProgramActivationLegalityContext gate) => gate.SkillId == NianReviveSkill &&
        gate.Activation is { Id: "revive", MinCards: 0, MaxCards: 0, MinTargets: 1, MaxTargets: 1,
            TargetKind: SkillProgramTargetKind.NianDeadAlly } && NianDeadAllies(gate.OwnerSeat).Count != 0;

    private SkillProgramStepOutcome BeginNianRevival(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.SkillId != NianReviveSkill || f.ActivationId != "revive" || f.TriggerId is not null ||
            f.NianRevival is not null || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats is not [var targetSeat] ||
            _phase != TurnPhase.Play || _currentSeat != f.OwnerSeat || !NianDeadAllies(f.OwnerSeat).Contains(targetSeat))
            throw new InvalidOperationException("Nian revival lost its living actor, dead ally or remaining shared payment.");
        var target = _players[targetSeat];
        var ordinal = NianUsedRevives + 1;
        var cards = _cardZones.CreateDiagnostics().Where(c => c.Location.OwnerSeat == targetSeat)
            .GroupBy(c => c.Location).Select(g => (Location: g.Key, Cards: g.Select(c => c.CardId).ToArray())).ToArray();
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { NianRevival = new(f.InstructionIndex, targetSeat, ordinal, target.MaxHp,
            NianRevivalStage.DiscardChildren, before, before, 0) });
        AdvanceEventRulesAndQueueFact(new NianRevivalPaidEvent(f.Id, f.OwnerSeat, targetSeat, ordinal, target.MaxHp));
        foreach (var group in cards)
            MoveCards(group.Cards.Select(id => GetAttackCard(id)).ToArray(), group.Location,
                CardLocation.DiscardPile, NianRevivalDiscard);
        CompleteNianSegment(f.Id, cards.Sum(g => g.Cards.Length));
        ContinueNianRevival(GetActiveProgramFrame(f.Id));
        return SkillProgramStepOutcome.AwaitChild;
    }

    private void CompleteNianSegment(long id, int count)
    {
        var f = GetActiveProgramFrame(id);
        var r = f.NianRevival! with { After = _movementSequence, Count = count };
        ReplaceRuntimeTop(f = f with { NianRevival = r });
        AdvanceEventRulesAndQueueFact(new NianRevivalSegmentEvent(id, r.TargetSeat, r.Stage, r.Before, r.After, count));
    }

    private bool DrainNianRevival(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);

    private bool IsNianRevivalMovementBatch(ProgramSkillFrame f, CardMovementBatchContext batch)
    {
        if (f.NianRevival is not { } r || batch.ParentFrameId != f.Id ||
            batch.AwaitingProgramFrameId is not null || batch.OriginOwnerSeat != f.OwnerSeat ||
            batch.OriginSkillId != f.SkillId || batch.OriginSkillInstanceId != f.SkillInstanceId ||
            batch.Movements.Count == 0 || r.Stage == NianRevivalStage.StateChildren) return false;
        RequireNianRevival(f);
        return batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After &&
            (r.Stage == NianRevivalStage.DiscardChildren
                ? m.From.OwnerSeat == r.TargetSeat && m.To == CardLocation.DiscardPile && m.Reason == NianRevivalDiscard
                : m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(r.TargetSeat) && m.Reason == NianRevivalDraw));
    }

    private void ContinueNianRevival(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        RequireNianRevival(f);
        if (DrainNianRevival(f)) return;
        f = GetActiveProgramFrame(f.Id);
        var r = f.NianRevival!;
        var target = _players[r.TargetSeat];
        if (_winner != Winner.None || _status == EngineStatus.Completed)
        {
            AdvanceEventRulesAndQueueFact(new NianRevivalCancelledEvent(f.Id, r.TargetSeat, "game-ended"));
            ReplaceRuntimeTop(f = f with { NianRevival = null });
            FinishProgramSkill(f, false);
            return;
        }
        if (r.Stage == NianRevivalStage.DiscardChildren)
        {
            if (target.IsAlive || target.MaxHp != r.MaximumHp || !target.HasHpValue)
                throw new InvalidOperationException("A paid Nian revival lost its original dead character.");
            var wasFaceDown = target.IsFaceDown;
            target.IsAlive = true;
            target.Hp = Math.Min(2, target.MaxHp);
            target.IsFaceDown = false;
            target.AlcoholDamageBonus = 0;
            target.UsedPlayPhaseAlcoholThisTurn = false;
            if (target.IsChained)
            {
                target.IsChained = false;
                AdvanceEventRulesAndQueueFact(new IronChainStateChangedEvent(f.Id, f.OwnerSeat, target.Seat, false));
            }
            ReplaceRuntimeTop(f = f with { NianRevival = r with { Stage = NianRevivalStage.StateChildren,
                Before = _movementSequence, After = _movementSequence, Count = 0 } });
            AdvanceEventRulesAndQueueFact(new NianAllyRevivedEvent(f.Id, f.OwnerSeat, target.Seat, r.UsedRevives, target.Hp));
            // Direct life restoration intentionally issues no Recovery/HpRecovered
            // event. Actual face-state observers still receive their real change.
            if (wasFaceDown) RecordCharacterStateChange(target.Seat, SkillProgramTriggerWindow.CharacterTurnedFaceUp);
            RecordCharacterTurnedOver(target.Seat, wasFaceDown);
            CompleteNianSegment(f.Id, 0);
            ContinueNianRevival(GetActiveProgramFrame(f.Id));
            return;
        }
        if (r.Stage == NianRevivalStage.StateChildren)
        {
            ReplaceRuntimeTop(f = f with { NianRevival = r with { Stage = NianRevivalStage.DrawAttempt,
                Before = _movementSequence, After = _movementSequence, Count = 0 } });
            CompleteNianSegment(f.Id, 0);
            BeginOperationProgramDraw(GetActiveProgramFrame(f.Id), target.Seat, 2, NianRevivalDraw);
            return;
        }
        if (r.Stage == NianRevivalStage.DrawAttempt)
            throw new InvalidOperationException("A Nian revival cannot bypass its exact native draw return.");
        AdvanceEventRulesAndQueueFact(new NianRevivalCompletedEvent(f.Id, r.TargetSeat, r.Count));
        ReplaceRuntimeTop(f = f with { NianRevival = null });
        FinishProgramSkill(f, true);
    }

    [ProgramNativeDrawReturn(SkillProgramEffectOp.ReviveNianAlly)]
    private void ReturnNianRevivalDraw(ProgramSkillFrame input, NativeDrawOutcome outcome)
    {
        var frame = GetActiveProgramFrame(input.Id);
        RequireNianRevival(frame);
        if (frame.NativeDraw is not null || frame.NianRevival is not { Stage: NianRevivalStage.DrawAttempt } receipt ||
            outcome.RecipientSeat != receipt.TargetSeat || outcome.RequestedCount != 2 ||
            outcome.SequenceBefore < receipt.Before || outcome.SequenceAfter < outcome.SequenceBefore ||
            outcome.SequenceAfter != _movementSequence || outcome.ActualDrawIds.Count > 2 ||
            outcome.ActualDrawIds.Distinct().Count() != outcome.ActualDrawIds.Count ||
            outcome.Replacement is not null && outcome.ActualDrawIds.Count != 0 ||
            !_cardMovements.Where(m => m.Sequence > outcome.SequenceBefore && m.Sequence <= outcome.SequenceAfter &&
                m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(receipt.TargetSeat) && m.Reason == NianRevivalDraw)
                .OrderBy(m => m.Sequence).Select(m => m.CardId).SequenceEqual(outcome.ActualDrawIds))
            throw new InvalidOperationException("A Nian revival lost its exact actual draw or replacement return.");
        ReplaceRuntimeTop(frame = frame with { NianRevival = receipt with { Stage = NianRevivalStage.DrawChildren,
            Before = outcome.SequenceBefore, After = outcome.SequenceAfter, Count = outcome.ActualDrawIds.Count } });
        AdvanceEventRulesAndQueueFact(new NianRevivalSegmentEvent(frame.Id, receipt.TargetSeat,
            NianRevivalStage.DrawChildren, outcome.SequenceBefore, outcome.SequenceAfter, outcome.ActualDrawIds.Count));
        ContinueNianRevival(GetActiveProgramFrame(frame.Id));
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 27930)]
    private bool ResumeNianRevival(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { NianRevival: not null } f || f.Id != id) return false;
        ContinueNianRevival(f);
        return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 27930)]
    private bool ReturnNianRevivalMovement(ProgramSkillFrame f) => f.NianRevival is not null && ResumeNianRevival(f.Id);

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 27930)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 27930)]
    private bool CanCompleteNianRevival(ProgramSkillFrame f)
    {
        if (f.NianRevival is null) return false;
        RequireNianRevival(f);
        return true;
    }

    private void RequireNianRevival(ProgramSkillFrame f)
    {
        if (f.NianRevival is not { } r || f.SkillId != NianReviveSkill || f.ActivationId != "revive" ||
            f.TriggerId is not null || r.InstructionIndex != f.InstructionIndex ||
            f.SelectedCardIds.Count != 0 || !f.SelectedTargetSeats.SequenceEqual([r.TargetSeat]) ||
            !IsNianAlliedSeat(r.TargetSeat) || r.MaximumHp <= 0 || r.Before > r.After || r.After > _movementSequence ||
            r.Count < 0 || !Enum.IsDefined(r.Stage) || r.UsedRevives <= 0 ||
            r.UsedRevives > _modeDefinition.NianActivity!.SharedRevives)
            throw new InvalidOperationException("Nian revival lost its exact owning instruction and original participant.");
        var paid = CompleteProgramEventHistory().OfType<NianRevivalPaidEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (paid is not [var payment] || payment.ActorSeat != f.OwnerSeat || payment.TargetSeat != r.TargetSeat ||
            payment.UsedRevives != r.UsedRevives || payment.MaximumHp != r.MaximumHp ||
            NianUsedRevives < r.UsedRevives)
            throw new InvalidOperationException("Nian revival cannot resume without its once-consumed team payment.");
        var segment = CompleteProgramEventHistory().OfType<NianRevivalSegmentEvent>()
            .Where(e => e.FrameId == f.Id && e.Stage == r.Stage).ToArray();
        // Only the owning operation can be observed between payment and issuing
        // its scalar movement receipt; event observers do not execute children.
        if (segment.Length == 0 && r.Before == r.After && r.Count == 0) return;
        if (segment is not [var issued] || issued.TargetSeat != r.TargetSeat || issued.Before != r.Before ||
            issued.After != r.After || issued.Count != r.Count)
            throw new InvalidOperationException("Nian revival lost its paid native movement segment.");
        var movements = _cardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= r.After).ToArray();
        if (r.Stage == NianRevivalStage.DiscardChildren &&
            movements.Count(m => m.From.OwnerSeat == r.TargetSeat && m.To == CardLocation.DiscardPile && m.Reason == NianRevivalDiscard) != r.Count ||
            r.Stage == NianRevivalStage.DrawChildren &&
            movements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(r.TargetSeat) && m.Reason == NianRevivalDraw) != r.Count)
            throw new InvalidOperationException("Nian revival's physical discard/draw invoice changed while suspended.");
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 27930)]
    private void AssertNianRevival(ProgramSkillFrame f)
    {
        if (f.NianRevival is not null) RequireNianRevival(f);
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.BeforeDeclaration, 27930)]
    private bool TryAdvanceNianRevivalAi()
    {
        if (_modeDefinition.NianActivity is null || !_setupComplete || _winner != Winner.None ||
            _pendingDecision is not null || _resolutionStack.Count != 0 || _phase != TurnPhase.Play ||
            !IsNianAlliedSeat(_currentSeat) || _players[_currentSeat].IsHuman || !_players[_currentSeat].IsAlive) return false;
        var action = BuildLegalActions(_players[_currentSeat]).SingleOrDefault(a =>
            a.Kind == LegalActionKind.UseProgramSkill && a.ProgramSkillId == NianReviveSkill && a.ProgramActivationId == "revive");
        if (action is null) return false;
        var target = NianDeadAllies(_currentSeat).OrderByDescending(s => _players[s].IsHuman).ThenBy(s => s).First();
        ExecuteProgramSkill(_players[_currentSeat], action, [], [target]);
        AdvanceRulesAndPublishState();
        return true;
    }
}
