using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string RiceDrawReason = "skill-program.rice.draw";
    private const string RiceStoreReason = "skill-program.rice.store";
    private const string RiceGainReason = "skill-program.rice.obtain";
    private bool RiceCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DrawAndStoreTwoPublicRice);
    private CardConversionSource RiceIssuer(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool RiceProducer(PublicPersistentPileSource p) => p.Capacity == 2 &&
        _contentRegistry.GetSkill(p.SkillId).Program?.Triggers.Any(t => t.Effects is [{ Op: SkillProgramEffectOp.DrawAndStoreTwoPublicRice }]) == true;
    private IReadOnlyList<PublicPersistentPileSource> RiceSources(int owner, SkillProgramEffect effect, string instance) =>
        ReferencedPublicPileSources(owner, effect.SkillIds.Single(), instance).Where(p => RiceProducer(p) &&
            _players[owner].SkillGrants.Grants.Any(g => g.SkillId == p.SkillId && g.SkillInstanceId == p.SkillInstanceId)).ToArray();
    private IEnumerable<Card> RiceStorageCards(ProgramSkillFrame f) => GetHand(_players[f.OwnerSeat]).Concat(GetEquipment(_players[f.OwnerSeat]))
        .Where(c => PublicPileStorageCard(f.OwnerSeat, f.SkillId, f.SkillInstanceId, c, _cardZones.GetLocation(c.Id)));
    private PublicPersistentPileSource? ExhaustedRicePile(ProgramTriggerCandidate c, ProgramSkillWindowContext context)
    {
        if (context.MovementBatch is not { } batch) return null;
        var pile = _publicPersistentPiles.GetValueOrDefault((c.OwnerSeat, c.SkillId, c.SkillInstanceId));
        return pile is not null && RiceProducer(pile) && batch.SourceCounts.Any(n => n.Location == pile.Location && n.CountBefore > 0 && n.CountAfter == 0) &&
            batch.Movements.Any(m => m.From == pile.Location && m.To != pile.Location) ? pile : null;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 280)]
    private bool CanOfferRicePile(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context)
    {
        if (t.Effects is not [var e] || !RicePileContract.IsOperation(e.Op)) return true;
        if (e.Op == SkillProgramEffectOp.DrawAndStoreTwoPublicRice)
            return c.OwnerSeat == _currentSeat && context.Window == SkillProgramTriggerWindow.TurnEnding &&
                (_publicPersistentPiles.GetValueOrDefault((c.OwnerSeat, c.SkillId, c.SkillInstanceId)) is not { } existing || PublicPileCards(existing).Count == 0);
        if (e.Op == SkillProgramEffectOp.RecoverAfterLastPublicRice) return ExhaustedRicePile(c, context) is not null;
        if (e.Op == SkillProgramEffectOp.ReplaceJudgmentFromPublicRice) return context.JudgmentReplacement is not null;
        return context is { Window: SkillProgramTriggerWindow.AfterDamageApplied, DamageFrameId: not null, Amount: > 0, TargetSeat: { } target } &&
            _players[target].IsAlive && context.OccurrenceIndex >= 0 && context.OccurrenceIndex < context.Amount &&
            (t.Subject == SkillProgramTriggerSubject.Owner ? target == c.OwnerSeat : context.SourceSeat == c.OwnerSeat && target != c.OwnerSeat) &&
            RiceSources(c.OwnerSeat, e, c.SkillInstanceId).Any(p => PublicPileCards(p).Count > 0);
    }
    private bool ExactRiceParent(ProgramSkillFrame f, SkillProgramEffectOp op)
    {
        if (f.TriggerId is null || f.InstructionIndex != 1 || f.WindowContext is not { } c || c.OwnerSeat != f.OwnerSeat ||
            GetProgramTrigger(f).Effects is not [var e] || e.Op != op) return false;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (index <= 0 || _resolutionStack[index - 1].Id != c.ParentFrameId) return false;
        if (op == SkillProgramEffectOp.DrawAndStoreTwoPublicRice)
            return c.Window == SkillProgramTriggerWindow.TurnEnding && f.OwnerSeat == _currentSeat &&
                _resolutionStack[index - 1] is TurnEndingBoundaryFrame ending && ending.OwnerSeat == f.OwnerSeat &&
                ending.ItemIndex >= 0 && ending.ItemIndex < ending.Items.Count && ending.Items[ending.ItemIndex].Candidate is { } candidate &&
                MountObserverCandidateMatches(f, candidate);
        if (op == SkillProgramEffectOp.OfferRiceAfterDamagePoint)
            return c.Window == SkillProgramTriggerWindow.AfterDamageApplied && c.Amount > 0 && c.OccurrenceIndex >= 0 && c.OccurrenceIndex < c.Amount &&
                _resolutionStack[index - 1] is DamageTriggerWindowFrame w && w.TriggerWindow == c.Window && w.ParentFrameId == c.DamageFrameId &&
                w.CandidateIndex >= 0 && w.CandidateIndex < w.Candidates.Count && MountObserverCandidateMatches(f, w.Candidates[w.CandidateIndex].ToProgramCandidate()) &&
                w.TargetSeat == c.TargetSeat && (GetDamageTriggerAttack(w).IsSourceLess ? c.SourceSeat is null : w.SourceSeat == c.SourceSeat) &&
                _resolutionStack.OfType<DamageFrame>().SingleOrDefault(d => d.Id == w.ParentFrameId) is { } damage && damage.Amount == c.Amount &&
                damage.TargetSeat == c.TargetSeat &&
                (GetProgramTrigger(f).Subject == SkillProgramTriggerSubject.Owner ? c.TargetSeat == f.OwnerSeat : c.SourceSeat == f.OwnerSeat && c.TargetSeat != f.OwnerSeat);
        if (op == SkillProgramEffectOp.RecoverAfterLastPublicRice)
            return c.Window == SkillProgramTriggerWindow.CardsMoved && c.MovementBatch is { } batch &&
                _resolutionStack[index - 1] is CardsMovedTriggerWindowFrame moved && moved.Batch.Id == batch.Id && moved.CandidateIndex >= 0 &&
                moved.CandidateIndex < moved.Candidates.Count && MountObserverCandidateMatches(f, moved.Candidates[moved.CandidateIndex]) &&
                batch.SourceCounts.Any(n => n.Location == f.RicePile?.Pile.Location && n.CountBefore > 0 && n.CountAfter == 0);
        return c.Window == SkillProgramTriggerWindow.JudgmentReplacing && c.JudgmentReplacement is { } replacement &&
            _resolutionStack[index - 1] is JudgmentFrame judged && judged.Id == replacement.JudgmentFrameId && judged.TargetSeat == replacement.SubjectSeat &&
            CurrentJudgmentCandidate(judged) is { } jc && jc.OwnerSeat == f.OwnerSeat && jc.ProgramId == f.SkillId && jc.ProgramTriggerId == f.TriggerId &&
            jc.SkillInstanceId == f.SkillInstanceId && jc.GameplayHash == f.GameplayHash;
    }
    private SkillProgramStepOutcome BeginRicePile(SkillProgramEffect effect, ProgramSkillFrame supplied)
    {
        if (effect.Op == SkillProgramEffectOp.ReplaceJudgmentFromPublicRice) return BeginRiceJudgmentReplacement(supplied, effect);
        var f = GetActiveProgramFrame(supplied.Id); var context = f.WindowContext!;
        PublicPersistentPileSource? pile;
        if (effect.Op == SkillProgramEffectOp.DrawAndStoreTwoPublicRice) pile = EnsurePublicPileSource(f, 2);
        else if (effect.Op == SkillProgramEffectOp.RecoverAfterLastPublicRice)
            pile = ExhaustedRicePile(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority), context);
        else pile = RiceSources(f.OwnerSeat, effect, f.SkillInstanceId).FirstOrDefault(p => PublicPileCards(p).Count > 0);
        if (pile is null) return SkillProgramStepOutcome.Continue;
        var r = new RicePileReceipt { Operation = effect.Op, Source = RiceIssuer(f), GameplayHash = f.GameplayHash,
            ParentId = context.ParentFrameId, DamageId = context.DamageFrameId, OccurrenceIndex = context.OccurrenceIndex, ActualTurn = _turnNumber,
            Pile = pile, BeneficiarySeat = effect.Op == SkillProgramEffectOp.OfferRiceAfterDamagePoint ? context.TargetSeat!.Value : f.OwnerSeat,
            Stage = effect.Op == SkillProgramEffectOp.DrawAndStoreTwoPublicRice ? RicePileStage.DrawChildren :
                effect.Op == SkillProgramEffectOp.RecoverAfterLastPublicRice ? RicePileStage.RecoveryChildren : RicePileStage.StoreSelection };
        ReplaceRuntimeTop(f = f with { RicePile = r });
        if (!ExactRiceParent(f, effect.Op)) throw new InvalidOperationException("Rice requires the exact actual ending, damage point or exhausted source batch.");
        AdvanceEventRulesAndQueueFact(new RicePileStartedEvent(f.Id, r));
        if (effect.Op == SkillProgramEffectOp.DrawAndStoreTwoPublicRice)
        {
            var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { RicePile = r with { DrawBefore = before, DrawAfter = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            var actual = DrawCards(_players[f.OwnerSeat], 2, true, new(RiceDrawReason)).Count;
            ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { RicePile = f.RicePile! with { DrawActual = actual, DrawAfter = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new RicePileDrawIssuedEvent(f.Id, 2, actual, before, _movementSequence));
            if (!DrainRiceChildren(f)) ReturnRiceMovement(f);
            return SkillProgramStepOutcome.AwaitChild;
        }
        if (effect.Op == SkillProgramEffectOp.RecoverAfterLastPublicRice)
        {
            ReplaceRuntimeTop(f = f with { RicePile = r with { RecoveryIssued = true } });
            AdvanceEventRulesAndQueueFact(new RicePileRecoveryIssuedEvent(f.Id, context.MovementBatch!.Id, r.Source, pile));
            new ProgramSkillHost(this).Recover(f.Id, f.OwnerSeat, f.OwnerSeat, 1, null, null);
            if (_resolutionStack.LastOrDefault()?.Id == f.Id) AdvanceRuntimeProgram(f.Id);
            return SkillProgramStepOutcome.AwaitChild;
        }
        PublishRiceChoice(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> RiceChoices(ProgramSkillFrame f)
    {
        var r = f.RicePile!; var result = new List<PromptChoice>();
        void Add(string token, string label, IReadOnlyList<int> cards) => result.Add(new(new($"rice.{f.Id}.{token}"), label, cards, [],
            new Dictionary<string, string> { ["program-action"] = "rice-pile", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["rice-action"] = token }));
        if (r.Operation == SkillProgramEffectOp.DrawAndStoreTwoPublicRice)
        {
            if (r.Selected.Count < r.StoreRequired)
                foreach (var card in RiceStorageCards(f).Where(c => !r.Selected.Contains(c.Id))) Add("store-" + card.Id, "置为米：【" + PublicPileCardLabel(card) + "】", [card.Id]);
            if (r.Selected.Count == r.StoreRequired) Add("pay", $"确认将 {r.Selected.Count} 张牌置为米", []);
        }
        else
            foreach (var pile in RiceSources(f.OwnerSeat, GetProgramTrigger(f).Effects.Single(), f.SkillInstanceId))
            foreach (var card in PublicPileCards(pile)) Add("obtain-" + card.Id, "获得米：【" + PublicPileCardLabel(card) + "】", [card.Id]);
        return Array.AsReadOnly(result.ToArray());
    }
    private void PublishRiceChoice(ProgramSkillFrame f)
    {
        var r = f.RicePile!; var choices = RiceChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        if (r.Operation == SkillProgramEffectOp.DrawAndStoreTwoPublicRice &&
            (!_players[f.OwnerSeat].SkillGrants.Grants.Any(g => g.SkillId == f.SkillId && g.SkillInstanceId == f.SkillInstanceId) ||
             _publicPersistentPiles.GetValueOrDefault((r.Pile.OwnerSeat, r.Pile.SkillId, r.Pile.SkillInstanceId)) != r.Pile))
        { FinishRicePile(f, true); return; }
        if (choices.Count == 0 || !_players[r.BeneficiarySeat].IsAlive) { FinishRicePile(f, true); return; }
        ReplaceRuntimeTop(f = f with { RicePile = r with { Choices = choices } });
        _pendingDecision = new(DecisionKind.ProgramTrigger, r.BeneficiarySeat,
            r.Operation == SkillProgramEffectOp.DrawAndStoreTwoPublicRice ? $"请选择 {r.StoreRequired} 张牌置为米。" : "选择一张米获得。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = r.Operation == SkillProgramEffectOp.DrawAndStoreTwoPublicRice, TargetSeat = r.BeneficiarySeat,
            Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[r.BeneficiarySeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("rice-pile")]
    private void ResolveRiceChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Rice choice lost owner.");
        RequireRiceReceipt(f); var r = f.RicePile!;
        if (r.Stage != RicePileStage.StoreSelection || _pendingDecision?.PlayerSeat != r.BeneficiarySeat ||
            !AssistedChoicesEqual([choice], r.Choices.Where(c => c.Id == choice.Id).ToArray()) ||
            !AssistedChoicesEqual([choice], RiceChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Rice choice must retain its actual chooser and current exact physical entity.");
        ClearPendingDecision(); var token = choice.Parameters["rice-action"];
        if (r.Operation == SkillProgramEffectOp.DrawAndStoreTwoPublicRice && token.StartsWith("store-", StringComparison.Ordinal))
        { ReplaceRuntimeTop(f = f with { RicePile = r with { Selected = r.Selected.Append(choice.Cards.Single()).ToArray(), Choices = [] } }); PublishRiceChoice(f); return; }
        var ids = r.Operation == SkillProgramEffectOp.DrawAndStoreTwoPublicRice ? r.Selected.ToArray() : choice.Cards.ToArray();
        var pile = r.Operation == SkillProgramEffectOp.DrawAndStoreTwoPublicRice ? r.Pile :
            RiceSources(f.OwnerSeat, GetProgramTrigger(f).Effects.Single(), f.SkillInstanceId).Single(p => PublicPileCards(p).Any(c => c.Id == ids.Single()));
        if (r.Operation == SkillProgramEffectOp.DrawAndStoreTwoPublicRice &&
            (!_players[f.OwnerSeat].IsAlive || !_players[f.OwnerSeat].SkillGrants.Grants.Any(g => g.SkillId == f.SkillId && g.SkillInstanceId == f.SkillInstanceId) ||
             _publicPersistentPiles.GetValueOrDefault((pile.OwnerSeat, pile.SkillId, pile.SkillInstanceId)) != pile))
        { FinishRicePile(f, true); return; }
        var from = ids.Select(_cardZones.GetLocation).ToArray(); var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { RicePile = r with { Stage = RicePileStage.MovementChildren, Pile = pile,
            CardIds = ids, From = from, Selected = [], Choices = [], Before = before, After = before }, PendingMovementContinuation = new(r.BeneficiarySeat, 0, null) });
        if (ids.Length > 0) MoveProgramCardsFromMultipleSources(ids,
            r.Operation == SkillProgramEffectOp.DrawAndStoreTwoPublicRice ? pile.Location : CardLocation.Hand(r.BeneficiarySeat),
            new(r.Operation == SkillProgramEffectOp.DrawAndStoreTwoPublicRice ? RiceStoreReason : RiceGainReason));
        ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { RicePile = f.RicePile! with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new RicePilePaymentIssuedEvent(f.Id, f.RicePile!));
        if (!DrainRiceChildren(f)) ReturnRiceMovement(f);
    }
    private bool DrainRiceChildren(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, f.PendingMovementContinuation is not null ? PostEventContinuation.AwaitedProgramMovement : PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, f.PendingMovementContinuation is not null ? PostEventContinuation.AwaitedProgramMovement : PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void FinishRicePile(ProgramSkillFrame f, bool completed)
    {
        AdvanceEventRulesAndQueueFact(new RicePileCompletedEvent(f.Id, f.RicePile!.Operation, completed));
        ReplaceRuntimeTop(f = f with { RicePile = null, PendingMovementContinuation = null }); FinishProgramSkill(f, completed);
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 20800)]
    private bool ReturnRiceMovement(ProgramSkillFrame f)
    {
        if (f.RicePile is not { Stage: RicePileStage.DrawChildren or RicePileStage.MovementChildren } r || f.PendingMovementContinuation is null) return false;
        RequireRiceReceipt(f); if (DrainRiceChildren(f)) return true;
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (r.Stage == RicePileStage.DrawChildren && _players[f.OwnerSeat].IsAlive &&
            _players[f.OwnerSeat].SkillGrants.Grants.Any(g => g.SkillId == f.SkillId && g.SkillInstanceId == f.SkillInstanceId) &&
            _publicPersistentPiles.GetValueOrDefault((r.Pile.OwnerSeat, r.Pile.SkillId, r.Pile.SkillInstanceId)) == r.Pile)
        { ReplaceRuntimeTop(f = f with { RicePile = r with { Stage = RicePileStage.StoreSelection, StoreRequired = Math.Min(2, RiceStorageCards(f).Count()) } }); PublishRiceChoice(f); }
        else FinishRicePile(f, true);
        return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 20800)]
    private bool ResumeRicePile(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { RicePile: { } r } f || f.Id != id) return false;
        RequireRiceReceipt(f);
        if (r.Stage == RicePileStage.StoreSelection) { if (_pendingDecision is null) PublishRiceChoice(f); return true; }
        if (f.PendingMovementContinuation is not null) return ReturnRiceMovement(f);
        if (r.Stage == RicePileStage.RecoveryChildren) { if (!DrainRiceChildren(f)) FinishRicePile(f, true); return true; }
        throw new InvalidOperationException("Rice resumed without a typed draw, payment or recovery child.");
    }
    [ProgramAiChoice(SkillProgramEffectOp.DrawAndStoreTwoPublicRice, SkillProgramEffectOp.OfferRiceAfterDamagePoint)]
    private PromptChoice SelectAiRiceChoice(PendingDecision p, ProgramSkillFrame f)
    {
        if (f.RicePile!.Operation == SkillProgramEffectOp.DrawAndStoreTwoPublicRice)
            return p.Choices.FirstOrDefault(c => c.Parameters["rice-action"] == "pay") ?? p.Choices
                .OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[f.OwnerSeat])).First();
        return p.Choices.OrderByDescending(c => GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[f.RicePile.BeneficiarySeat])).First();
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 410)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 410)]
    private bool CanContinueRicePile(ProgramSkillFrame f)
    {
        if (f.RicePile is not { } r || r.Stage == RicePileStage.StoreSelection && r.Operation != SkillProgramEffectOp.DrawAndStoreTwoPublicRice) return false;
        RequireRiceReceipt(f); return true;
    }
    [ProgramCapability]
    private sealed class RicePileProgramCapability(GameEngine engine) : IRicePileProgramHost
    { public SkillProgramStepOutcome ExecuteRicePile(SkillProgramEffect e, ProgramSkillFrame f) => engine.BeginRicePile(e, f); }
}
