using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string WuLuKangDixianDrawReason = "skill-program.wumiao:dixian.draw";
    private const string WuLuKangDixianObtainReason = "skill-program.wumiao:dixian.obtain";
    private bool HasWuLuKangDixianCapability => _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveWuLuKangDixian);
    private static CardConversionSource WuLuKangDixianSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_lu_kang_wumiao_WuLuKangDixian.WuLuKangDixianSource(f);
    private bool HasWuLuKangDixianOriginalPayment(ProgramSkillFrame f)
    {
        if (f.SkillId != WuLuKangComposition.Dixian || f.ActivationId != "declare-rank" || f.TriggerId is not null ||
            f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            _contentRegistry.GetSkill(f.SkillId).Program is not { } p || p.GameplayHash != f.GameplayHash || !WuLuKangComposition.IsDixian(p)) return false;
        var history = CompleteProgramEventHistory().ToArray();
        var starts = history.Select((e, i) => (e, i)).Where(x => x.e is ProgramSkillStartedEvent s && s.FrameId == f.Id &&
            s.OwnerSeat == f.OwnerSeat && s.SkillId == f.SkillId && s.ActivationId == f.ActivationId).ToArray();
        return starts is [var start] && history.Take(start.i).OfType<SkillUsageConsumedEvent>().LastOrDefault(e =>
            e.SkillOwnerSeat == f.OwnerSeat && e.SkillId == f.SkillId && e.UsageId == f.ActivationId && e.Scope == SkillUsageScope.Game) is { Count: 1 };
    }
    private SkillProgramStepOutcome BeginWuLuKangDixian(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (!HasWuLuKangDixianOriginalPayment(f) || f.WuLuKangDixian is not null ||
            CurrentActualDiscardRecoveryPhase() is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != f.OwnerSeat ||
            ProgramEventHistory<WuLuKangDixianStartedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Dixian requires its exact once-paid native Play activation.");
        var r = new WuLuKangDixianReceipt(WuLuKangDixianSource(f), f.GameplayHash, phase, _movementSequence) { Stage = WuLuKangDixianStage.Choosing };
        ReplaceRuntimeTop(f = f with { WuLuKangDixian = r });
        AdvanceEventRulesAndQueueFact(new WuLuKangDixianStartedEvent(f.Id, r.Source, r.GameplayHash, phase, r.SequenceBefore));
        PublishParticipantHandChoice(f, f.OwnerSeat, WuLuKangDixianChoices(f), "声明一个点数（A至K）。", f.OwnerSeat);
        AdvanceRulesAndPublishState(); return SkillProgramStepOutcome.AwaitChild;
    }
    private static IReadOnlyList<PromptChoice> WuLuKangDixianChoices(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_lu_kang_wumiao_WuLuKangDixian.WuLuKangDixianChoices(f);
    [ProgramAnswer("wu-lu-kang-dixian")]
    private void AnswerWuLuKangDixian(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { WuLuKangDixian: { Stage: WuLuKangDixianStage.Choosing } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !HasWuLuKangDixianOriginalPayment(f) || !_players[f.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) ||
            !AssistedChoicesEqual([choice], WuLuKangDixianChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Dixian accepts only its owner's current declared-rank choice.");
        RequireWuLuKangDixian(f);
        var rank = int.Parse(choice.Parameters["rank"], CultureInfo.InvariantCulture);
        var stock = _cardZones.CardsAt(CardLocation.DrawPile).ToArray();
        var allAtLeast = stock.All(c => c.Rank >= rank);
        // Field cards are Equipment/Judgment. Hands and every held/private pile
        // are deliberately outside this frozen, printed-rank collection.
        var locations = _players.SelectMany(p => new[] { CardLocation.Equipment(p.Seat), CardLocation.Judgment(p.Seat) })
            .Concat([CardLocation.DrawPile, CardLocation.DiscardPile]);
        var materials = allAtLeast ? Array.Empty<WuLuKangDixianMaterial>() : locations.SelectMany(loc =>
            _cardZones.CardsAt(loc).Where(c => c.Rank == 13).Select(c => new WuLuKangDixianMaterial(c.Id, c.Kind, c.Rank, loc))).ToArray();
        ClearPendingDecision();
        ReplaceRuntimeTop(f = f with { WuLuKangDixian = r with { Rank = rank, AllAtLeast = allAtLeast, StockCount = stock.Length,
            Materials = materials, Stage = allAtLeast ? WuLuKangDixianStage.Drawing : WuLuKangDixianStage.Collecting } });
        AdvanceEventRulesAndQueueFact(new WuLuKangDixianRankChosenEvent(f.Id, rank, allAtLeast, stock.Length, materials.Length));
        for (var i = 0; i < materials.Length; i++) AdvanceEventRulesAndQueueFact(new WuLuKangDixianMaterialFrozenEvent(f.Id, i, materials[i]));
        if (allAtLeast) BeginOperationProgramDraw(f, f.OwnerSeat, rank, new(WuLuKangDixianDrawReason));
        else ContinueWuLuKangDixian(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveWuLuKangDixian)]
    private PromptChoice AiWuLuKangDixian(PendingDecision p, ProgramSkillFrame f) =>
        p.Choices.Single(c => c.Parameters.GetValueOrDefault("rank") == "13");
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveWuLuKangDixian)]
    private void ReturnWuLuKangDixianDraw(ProgramSkillFrame f, NativeDrawOutcome o)
    {
        if (f.WuLuKangDixian is not { Stage: WuLuKangDixianStage.Drawing, AllAtLeast: true } r || o.RecipientSeat != f.OwnerSeat ||
            o.RequestedCount != r.Rank || ProgramEventHistory<WuLuKangDixianDrawReturnedEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Dixian requires its exact once-returned requested native draw.");
        ReplaceRuntimeTop(f = f with { WuLuKangDixian = r with { DrawOutcome = o, Stage = WuLuKangDixianStage.DrawChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new WuLuKangDixianDrawReturnedEvent(f.Id, o.RequestedCount, o.ActualDrawIds.Count,
            o.ReplacementGainedIds.Count, o.SequenceBefore, o.SequenceAfter, o.AttemptFrameId));
        ContinueWuLuKangDixian(f.Id);
    }
    private void ContinueWuLuKangDixian(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { WuLuKangDixian: { } r } f || f.Id != id) return;
        RequireWuLuKangDixian(f);
        if (r.Stage is WuLuKangDixianStage.Choosing or WuLuKangDixianStage.Drawing)
            throw new InvalidOperationException("Dixian cannot resume before its accepted rank or native draw returns.");
        if (AwaitWuLuKangDixianChildren(id)) return;
        f = GetActiveProgramFrame(id); r = f.WuLuKangDixian!;
        if (r.AllAtLeast || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishWuLuKangDixian(f); return; }
        while (r.MaterialIndex < r.Materials.Count)
        {
            var index = r.MaterialIndex; var material = r.Materials[index];
            if (_cardZones.GetLocation(material.CardId) != material.From)
            {
                ReplaceRuntimeTop(f = f with { WuLuKangDixian = r = r with { MaterialIndex = index + 1, Stage = WuLuKangDixianStage.Collecting } });
                AdvanceEventRulesAndQueueFact(new WuLuKangDixianEntitySkippedEvent(id, index, material.CardId, "left-frozen-source"));
                continue;
            }
            ReplaceRuntimeTop(f = f with { WuLuKangDixian = r with { Stage = WuLuKangDixianStage.MovementChildren }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveCard(GetAdvancedCard(material.CardId), material.From, CardLocation.Hand(f.OwnerSeat), new(WuLuKangDixianObtainReason), move =>
            {
                var current = GetActiveProgramFrame(id); var receipt = current.WuLuKangDixian!;
                if (receipt.MaterialIndex != index || move.CardId != material.CardId || move.From != material.From)
                    throw new InvalidOperationException("Dixian cannot replace or repeat its frozen K entity.");
                ReplaceRuntimeFrame(id, current with { WuLuKangDixian = receipt with { MaterialIndex = index + 1, LastMovementSequence = move.Sequence,
                    ActualGained = receipt.ActualGained + (move.To == CardLocation.Hand(f.OwnerSeat) ? 1 : 0) } });
                AdvanceEventRulesAndQueueFact(new WuLuKangDixianEntityMovedEvent(id, index, move.CardId, move.From, move.To, move.Sequence));
            }, disclosure: material.From == CardLocation.DrawPile ? CardMovementDisclosure.FaceDown : CardMovementDisclosure.FaceUp);
            if (AwaitWuLuKangDixianChildren(id)) return;
            f = GetActiveProgramFrame(id); r = f.WuLuKangDixian!;
            if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishWuLuKangDixian(f); return; }
        }
        FinishWuLuKangDixian(f);
    }
    private bool AwaitWuLuKangDixianChildren(long id)
    {
        var f = GetActiveProgramFrame(id);
        ReplaceRuntimeTop(f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return true;
        ReplaceRuntimeTop(GetActiveProgramFrame(id) with { PendingMovementContinuation = null }); return false;
    }
    private void FinishWuLuKangDixian(ProgramSkillFrame f)
    {
        RequireWuLuKangDixian(f); var r = f.WuLuKangDixian!;
        var grant = r.AllAtLeast && r.DrawOutcome is not null && _players[f.OwnerSeat].IsAlive && _winner == Winner.None;
        if (grant)
        {
            if (WuLuKangDixianGrantFor(f.OwnerSeat) is not null) throw new InvalidOperationException("Dixian cannot issue its permanent privilege twice.");
            AdvanceEventRulesAndQueueFact(new WuLuKangDixianGrantedEvent(new(f.Id, r.Source, r.GameplayHash, r.Rank, r.Phase)));
        }
        AdvanceEventRulesAndQueueFact(new WuLuKangDixianCompletedEvent(f.Id, r.Rank, r.AllAtLeast, r.ActualGained, grant));
        ReplaceRuntimeTop(f = f with { WuLuKangDixian = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 29701)]
    private bool ResumeWuLuKangDixian(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { WuLuKangDixian: not null } f || f.Id != id) return false; ContinueWuLuKangDixian(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 29701)]
    private bool ReturnWuLuKangDixianMovement(ProgramSkillFrame f)
    { if (f.WuLuKangDixian is null || f.PendingMovementContinuation is null) return false; ContinueWuLuKangDixian(f.Id); return true; }
    [ProgramCapability]
    private sealed class WuLuKangDixianCapability(GameEngine engine) : IWuLuKangDixianHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f) => engine.BeginWuLuKangDixian(f); }
}
