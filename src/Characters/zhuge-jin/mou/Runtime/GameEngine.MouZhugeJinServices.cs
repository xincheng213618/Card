namespace CardGame.Core;
public sealed partial class GameEngine
{
    private MouZhugeJinRuntime MouZhugeJin => GetRuntimeModule<MouZhugeJinRuntime>();
    private bool MouZhugeJinCooling(int owner) => ProgramEventHistory<MouZhugeJinCooldownIssuedEvent>().Any(e => e.Source.OwnerSeat == owner && e.DueRound > _roundNumber);
    private bool HasMouZhugeJinZijin(int seat) => EnabledSkillPrograms(_players[seat]).Any(p => p.Id == MouZhugeJinComposition.Zijin);
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeOwnerFacts, 35600, SkillProgramEffectOp.ResolveMouZhugeJinTaozhou)]
    private bool MouZhugeJinCanActivate(ProgramActivationLegalityContext c) => c.SkillId == MouZhugeJinComposition.Taozhou && c.Owner.IsAlive && _phase == TurnPhase.Play && _currentSeat == c.OwnerSeat &&
        !MouZhugeJinCooling(c.OwnerSeat) && new MouZhugeJinFacts(this).Targets(c.OwnerSeat).Count > 0;
    [ProgramCapability]
    private sealed class MouZhugeJinFacts(GameEngine e) : IMouZhugeJinFacts
    {
        public int Turn => e._turnNumber; public int TurnOwner => e._turnProgression.OwnerSeat; public int Round => e._roundNumber; public long Sequence => e._movementSequence; public bool Finished => e._winner != Winner.None;
        public bool Alive(int s) => e.IsValidPlayerSeat(s) && e._players[s].IsAlive; public bool Human(int s) => e._players[s].IsHuman; public int Hp(int s) => e._players[s].Hp; public string Name(int s) => e._players[s].Name;
        public bool Friendly(int owner, int other) => e._aiBrains[owner].ScoreProgramTarget(e.CreateSnapshot(owner), other, new(0, 0, 0, 1, 0, 0, false, false)) > 0;
        public SkillProgram? Program(string s) => e._contentRegistry.Skills.GetValueOrDefault(s)?.Program;
        public IEnumerable<T> History<T>() where T : IGameEvent => e.ProgramEventHistory<T>();
        public IReadOnlyList<int> Targets(int owner) => e._players.Where(p => p.IsAlive && p.Seat != owner && e.GetHand(p).Count > 0 && !e.HasMouZhugeJinZijin(p.Seat)).Select(p => p.Seat).Order().ToArray();
        public IReadOnlyList<MouZhugeJinCard> Hand(int s) => Array.AsReadOnly(e.GetHand(e._players[s]).Select(c => new MouZhugeJinCard(c.Id, CardLocation.Hand(s), c.DisplayName)).ToArray());
        public IReadOnlyList<MouZhugeJinCard> Costs(int chooser, int holder) => !Alive(chooser) || !Alive(holder) ? [] : Array.AsReadOnly(new[] { CardZoneKind.Hand, CardZoneKind.Equipment }.SelectMany(zone =>
            e._cardZones.CardsAt(new(zone, holder)).Where(c => !e.IsForeignEquipmentDiscardPrevented(chooser, c, new(zone, holder), OwnedCardMoveIntent.Discard) &&
                (chooser != holder || !e.IsSelfHandCategoryDiscardForbidden(chooser, c, new(zone, holder), OwnedCardMoveIntent.Discard)))
            .Select(c => new MouZhugeJinCard(c.Id, new(zone, holder), c.DisplayName))).ToArray());
        private MouZhugeJinUse? NativeUse(ProgramTriggerCandidate c, ProgramSkillWindowContext w)
        {
            if (w.OwnerSeat != c.OwnerSeat || w.CardUse is not { } card || e._resolutionStack.SingleOrDefault(x => x.Id == w.ParentFrameId) is not ProgramCardTriggerWindowFrame window ||
                window.Action.ActionId != card.CardActionId || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count || window.Candidates[window.CandidateIndex] is not { } current ||
                current.OwnerSeat != c.OwnerSeat || current.SkillId != c.SkillId || current.TriggerId != c.BindingId || current.SkillInstanceId != c.SkillInstanceId || current.GameplayHash != c.GameplayHash) return null;
            var use = e._resolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.Action?.ActionId == window.Action.ActionId);
            return new(window.Id, use?.Id ?? 0, window.Action, window.Action.EffectiveIsRed == true);
        }
        public MouZhugeJinUse? Use(ProgramTriggerCandidate c, ProgramSkillWindowContext w, SkillProgramEffectOp op)
        {
            var result = NativeUse(c, w); if (result is null) return null; var action = result.Action;
            if (op == SkillProgramEffectOp.ResolveMouZhugeJinZijin)
                return w.Window == SkillProgramTriggerWindow.CardUseCompleted && action.ActorSeat == c.OwnerSeat && w.Facts?.CardUseCausedDamage == false &&
                    History<CurrentTurnCardUseKindsRecordedEvent>().Any(p => p.CardActionId == action.ActionId && p.ActorSeat == c.OwnerSeat && p.WholeActualTurnUseSequence is > 0) ? result : null;
            if (op != SkillProgramEffectOp.ResolveMouZhugeJinHoude || w.Window != SkillProgramTriggerWindow.CardUseBeforeTargetEffects || result.UseId == 0 || e._phase != TurnPhase.Play || e._currentSeat == c.OwnerSeat ||
                action.ActorSeat != e._currentSeat || !action.TargetSeats.Contains(c.OwnerSeat) || !HoudeKind(action, result.Red)) return null;
            var history = e.EventsSinceLastBoundary(p => p is PhaseChangedEvent or TurnStartedEvent or TurnEndedEvent);
            var first = history.Select(p => p switch { CardUseAppearanceCapturedEvent appearance => appearance.Action, CardActionAcceptedEvent accepted => accepted.Action, _ => null }).OfType<CardActionContext>()
                .Append(action).Where(a => a.ActorSeat == action.ActorSeat && a.TargetSeats.Contains(c.OwnerSeat) && HoudeKind(a, result.Red)).DistinctBy(a => a.ActionId).MinBy(a => a.ActionId);
            return first?.ActionId == action.ActionId ? result : null;
        }
        private static bool HoudeKind(CardActionContext a, bool red) => a.EffectiveIsRed == red && (red ? IsSlashCard(a.EffectiveKind) : IsOrdinaryTrick(a.EffectiveKind));
        public bool Parent(ProgramSkillFrame f, SkillProgramEffectOp op)
        {
            if (op == SkillProgramEffectOp.ResolveMouZhugeJinTaozhou) return f.SkillId == MouZhugeJinComposition.Taozhou && f.ActivationId == "secret-number-and-give" && f.TriggerId is null && f.WindowContext is null &&
                f.SelectedCardIds.Count == 0 && f.SelectedTargetSeats.Count == 0 && History<ProgramSkillStartedEvent>().Count(p => p.FrameId == f.Id && p.SkillId == f.SkillId && p.OwnerSeat == f.OwnerSeat && p.ActivationId == f.ActivationId) == 1;
            if (f.WindowContext is not { } w || f.TriggerId != f.ActivationId || f.SkillId != (op == SkillProgramEffectOp.ResolveMouZhugeJinHoude ? MouZhugeJinComposition.Houde : MouZhugeJinComposition.Zijin) ||
                w.Window != (op == SkillProgramEffectOp.ResolveMouZhugeJinHoude ? SkillProgramTriggerWindow.CardUseBeforeTargetEffects : SkillProgramTriggerWindow.CardUseCompleted)) return false;
            var n = e._resolutionStack.FindIndex(x => x.Id == f.Id);
            return n > 0 && e._resolutionStack[n - 1] is ProgramCardTriggerWindowFrame window && window.Id == w.ParentFrameId &&
                NativeUse(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, 0), w) is not null;
        }
    }
    [ProgramCapability]
    private sealed class MouZhugeJinNative(GameEngine e) : IMouZhugeJinNative
    {
        public void Finish(ProgramSkillFrame f) => e.FinishProgramSkill(f, true); public void Publish() => e.AdvanceRulesAndPublishState();
        public bool Drain(ProgramSkillFrame f) => e.TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
            e.TryBeginAdvancedSkillsChanged(f.Id) || e.TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCardsMovedProgramWindow(f.Id);
        public void Give(ProgramSkillFrame f)
        {
            if (!e.MouZhugeJin.Valid(f) || f.MouZhugeJin is not { Stage: MouZhugeJinStage.GiveChildren, Committed: true } r || r.Cards.Count == 0 || r.Cards.Any(id => e._cardZones.GetLocation(id) != CardLocation.Hand(r.Target)))
                throw new InvalidOperationException("Taozhou requires the giver's committed current Hand entities.");
            e.MoveProgramCardsFromMultipleSources(r.Cards, CardLocation.Hand(f.OwnerSeat), new(MouZhugeJinRuntime.GiveReason), (_, moves) => {
                var current = e.GetActiveProgramFrame(f.Id); var paid = current.MouZhugeJin!;
                if (!e.MouZhugeJin.Valid(current) || moves.Count != r.Cards.Count || moves.Any(m => !r.Cards.Contains(m.CardId) || m.From != CardLocation.Hand(r.Target) || m.Sequence <= paid.Before ||
                    m.To != CardLocation.Hand(f.OwnerSeat) && m.To != CardLocation.OutsideGame)) throw new InvalidOperationException("Taozhou cannot substitute a primary real gift receipt.");
                e.ReplaceRuntimeFrame(f.Id, current with { MouZhugeJin = paid with { Given = moves.Count(m => m.To == CardLocation.Hand(f.OwnerSeat)), After = moves.Max(m => m.Sequence) } });
                foreach (var m in moves.Where(m => m.To == CardLocation.Hand(f.OwnerSeat))) e.AdvanceEventRulesAndQueueFact(new MouZhugeJinGivenEvent(f.Id, r.Target, f.OwnerSeat, m.CardId, m.Sequence)); });
        }
        public MouZhugeJinCard RandomHand(ProgramSkillFrame f, IReadOnlyList<MouZhugeJinCard> cards)
        {
            if (!e.MouZhugeJin.Valid(f) || f.MouZhugeJin is not { Operation: SkillProgramEffectOp.ResolveMouZhugeJinHoude, Red: false } r || cards.Count == 0 || cards.Any(c => c.From != CardLocation.Hand(r.Target)))
                throw new InvalidOperationException("Houde's hidden Hand choice requires the original other actor's actual Hand.");
            var before = e._random.State; var card = cards[e._random.Next(cards.Count)]; e.AdvanceEventRulesAndQueueFact(new MouZhugeJinHandChosenEvent(f.Id, card.Id, before, e._random.State, cards.Count)); return card;
        }
        public void Discard(ProgramSkillFrame f)
        {
            if (!e.MouZhugeJin.Valid(f) || f.MouZhugeJin is not { Stage: MouZhugeJinStage.CostChildren, Paying: { } card } r || !new MouZhugeJinFacts(e).Costs(f.OwnerSeat, card.From.OwnerSeat!.Value).Contains(card))
                throw new InvalidOperationException("Houde/Zijin requires its currently payable actual HE cost.");
            e.MoveProgramLogicalDiscard(f, f.OwnerSeat, [card.Id], new(MouZhugeJinRuntime.DiscardReason), (_, moves) => {
                var current = e.GetActiveProgramFrame(f.Id); var paid = current.MouZhugeJin!;
                if (!e.MouZhugeJin.Valid(current) || paid.Paying != card || moves is not [var m] || !e._cardMovements.Contains(m) || m.CardId != card.Id || m.From != card.From || m.Sequence <= paid.Before ||
                    m.To != CardLocation.DiscardPile && m.To != CardLocation.OutsideGame) throw new InvalidOperationException("Houde/Zijin lost its original primary logical discard receipt.");
                e.ReplaceRuntimeFrame(f.Id, current with { MouZhugeJin = paid with { CostPaid = true, After = m.Sequence } });
                e.AdvanceEventRulesAndQueueFact(new MouZhugeJinCostPaidEvent(f.Id, f.OwnerSeat, card.From.OwnerSeat!.Value, card.Id, m.Sequence)); });
        }
        public void Lose(ProgramSkillFrame f)
        {
            if (!e.MouZhugeJin.Valid(f) || f.MouZhugeJin is not { Operation: SkillProgramEffectOp.ResolveMouZhugeJinZijin, Stage: MouZhugeJinStage.LossChildren }) throw new InvalidOperationException("Zijin requires the accepted native undamaging-use loss branch.");
            e.ApplyProgramHpLoss(f.Id, f.SkillId, f.OwnerSeat, 1);
        }
        public void Nullify(ProgramSkillFrame f)
        {
            if (!e.MouZhugeJin.Valid(f) || f.MouZhugeJin is not { Operation: SkillProgramEffectOp.ResolveMouZhugeJinHoude, CostPaid: true }) throw new InvalidOperationException("Houde cannot nullify before paying its true discard.");
            e.NullifyCurrentProgramCardEffect(f);
        }
        public void Grant(ProgramSkillFrame f)
        {
            if (!e.MouZhugeJin.Valid(f) || f.MouZhugeJin is not { Stage: MouZhugeJinStage.GrantChildren } r || r.SecretNumber - r.Given < 2 || e.ProgramEventHistory<MouZhugeJinZijinGrantedEvent>().Any(p => p.FrameId == f.Id))
                throw new InvalidOperationException("Taozhou grants derived Zijin only after its exact committed actual-gift shortfall.");
            e.AcquireRuntimeSkills(e._players[r.Target], $"taozhou:{f.OwnerSeat}:{f.SkillInstanceId}:{f.Id}", [MouZhugeJinComposition.Zijin]);
            e.AdvanceEventRulesAndQueueFact(new MouZhugeJinZijinGrantedEvent(f.Id, r.Target));
        }
    }
    private IEnumerable<MouZhugeJinBudgetIssuedEvent> MouZhugeJinBudgets(int target) => ProgramEventHistory<MouZhugeJinBudgetIssuedEvent>().Where(e => e.Target == target && e.Count > 0 &&
        _contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash == e.GameplayHash && ProgramEventHistory<MouZhugeJinBudgetHitEvent>().Count(hit => hit.FrameId == e.FrameId) < e.Count);
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 35600)] private int MouZhugeJinDamageBonus(IDamageAttempt a) => MouZhugeJinBudgets(a.TargetSeat).Count();
    [ProgramDamageHook(ProgramDamagePhase.AppliedBeforeDying, 35600)]
    private bool MouZhugeJinDamageApplied(IDamageAttempt a, long id)
    {
        if (!a.DamageWasApplied || _resolutionStack.SingleOrDefault(x => x.Id == id) is not DamageFrame { Amount: > 0 } damage || damage.TargetSeat != a.TargetSeat) return false;
        foreach (var grant in MouZhugeJinBudgets(a.TargetSeat).ToArray())
            if (!ProgramEventHistory<MouZhugeJinBudgetHitEvent>().Any(hit => hit.FrameId == grant.FrameId && hit.DamageId == id)) AdvanceEventRulesAndQueueFact(new MouZhugeJinBudgetHitEvent(grant.FrameId, id, a.TargetSeat)); return false;
    }
}
