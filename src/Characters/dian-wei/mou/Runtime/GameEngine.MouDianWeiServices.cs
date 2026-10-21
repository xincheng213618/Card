namespace CardGame.Core;
public sealed partial class GameEngine
{
    private MouDianWeiRuntime MouDianWei => GetRuntimeModule<MouDianWeiRuntime>();
    [ProgramCapability]
    private sealed class MouDianWeiFacts(GameEngine e) : IMouDianWeiFacts
    {
        public int Turn => e._turnNumber; public int TurnOwner => e._turnProgression.OwnerSeat; public long Sequence => e._movementSequence; public bool Finished => e._winner != Winner.None;
        public int Hp(int s) => e._players[s].Hp; public int MaxHp(int s) => e._players[s].MaxHp; public int Hand(int s) => e.GetHand(e._players[s]).Count;
        public bool Alive(int s) => e.IsValidPlayerSeat(s) && e._players[s].IsAlive; public bool Human(int s) => e._players[s].IsHuman; public string Name(int s) => e._players[s].Name;
        public IEnumerable<T> History<T>() where T : IGameEvent => e.ProgramEventHistory<T>();
        public SkillProgram? Program(string s) => e._contentRegistry.Skills.GetValueOrDefault(s)?.Program;
        public bool Parent(ProgramSkillFrame f, SkillProgramEffectOp op)
        {
            if (op == SkillProgramEffectOp.ResolveMouDianWeiKuangzhan)
                return f.SkillId == MouDianWeiComposition.Kuangzhan && f.ActivationId == "draw-and-contest" && f.TriggerId is null && f.WindowContext is null &&
                    e.ProgramEventHistory<ProgramSkillStartedEvent>().Count(p => p.FrameId == f.Id && p.SkillId == f.SkillId && p.OwnerSeat == f.OwnerSeat && p.ActivationId == f.ActivationId) == 1;
            var window = op == SkillProgramEffectOp.ResolveMouDianWeiKangyongStart ? SkillProgramTriggerWindow.TurnStartBeforeNormalFlow : SkillProgramTriggerWindow.AfterTurnEnded;
            if (f.SkillId != MouDianWeiComposition.Kangyong || f.TriggerId != f.ActivationId || f.TriggerId != (window == SkillProgramTriggerWindow.AfterTurnEnded ? "repay-actual-turn-end" : "recover-actual-turn-start") ||
                f.WindowContext is not { } c || c.OwnerSeat != f.OwnerSeat || c.Window != window || f.OwnerSeat != TurnOwner) return false;
            var n = e._resolutionStack.FindIndex(x => x.Id == f.Id);
            if (window == SkillProgramTriggerWindow.AfterTurnEnded)
                return n > 0 && e._resolutionStack[n - 1] is DeferredTurnEndFrame ended && ended.Id == c.ParentFrameId && ended.OwnerSeat == f.OwnerSeat && ended.TurnNumber == Turn &&
                    c.SourceSeat == f.OwnerSeat && c.TargetSeat == f.OwnerSeat && e.IsActualAfterTurnEndedParent(ended) && MountObserverCandidateMatches(f, e.AfterTurnEndedCandidate(ended));
            return n > 0 && e._resolutionStack[n - 1] is ProgramLifecycleTriggerWindowFrame p && p.Id == c.ParentFrameId && p.Window == window &&
                p.CandidateIndex >= 0 && p.CandidateIndex < p.Candidates.Count && MountObserverCandidateMatches(f, p.Candidates[p.CandidateIndex]);
        }
        public IReadOnlyList<int> Opponents(int owner) => e.GetHand(e._players[owner]).Count == 0 && !e.CanWuLuKangDiscardForPindian(e._players[owner]) ? [] :
            e._players.Where(p => p.IsAlive && p.Seat != owner && e.CanBePindianTarget(owner, p.Seat) && (e.GetHand(p).Count > 0 || e.CanWuLuKangDiscardForPindian(p))).Select(p => p.Seat).Order().ToArray();
        public IReadOnlyList<int> NotWon(int owner) => e.EventsSinceLastBoundary(x => x is TurnStartedEvent or TurnEndedEvent).OfType<PindianResultDeterminedEvent>()
            .SelectMany(x => new[] { x.Result.SourceSeat, x.Result.OpponentSeat }.Where(s => !x.Result.WonBy(s))).Where(s => s != owner && Alive(s)).Distinct().Order().ToArray();
        public bool CanSlash(int actor, int target) => e.CanUseForeignTurnContestSlash(actor, target);
        public PindianResult Result(ProgramSkillFrame f)
        {
            var r = f.MouDianWei ?? throw new InvalidOperationException("Missing contest parent receipt."); var bind = MouDianWeiRuntime.Bind(r);
            var issued = e.ProgramEventHistory<MouDianWeiContestIssuedEvent>().Where(p => p.FrameId == f.Id && p.Index == r.Index).ToArray();
            var actual = e.ProgramEventHistory<PindianResultDeterminedEvent>().Where(p => p.FrameId == r.ContestId && p.SkillId == f.SkillId).ToArray();
            if (issued is not [var start] || start.ContestId != r.ContestId || start.Opponent != r.Opponent || start.Bind != bind || actual is not [var fact] ||
                f.PindianResultBindings.SingleOrDefault(b => b.Name == bind) is not { } b || b.SourceSeat != f.OwnerSeat || b.OpponentSeat != r.Opponent || b.SourceRank != fact.Result.SourceRank ||
                b.OpponentRank != fact.Result.OpponentRank || b.SourceWon != fact.Result.SourceWon || b.Visibility != SkillProgramCardSetVisibility.Public ||
                fact.Result.SourceSeat != f.OwnerSeat || fact.Result.OpponentSeat != r.Opponent) throw new InvalidOperationException("Kuangzhan requires its exact native Pindian result and typed binding.");
            return fact.Result;
        }
    }
    [ProgramCapability]
    private sealed class MouDianWeiNative(GameEngine e) : IMouDianWeiNative
    {
        public void Finish(ProgramSkillFrame f) => e.FinishProgramSkill(f, true);
        public void Publish() => e.AdvanceRulesAndPublishState();
        public bool Drain(ProgramSkillFrame f) => e.TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
            e.TryBeginAdvancedSkillsChanged(f.Id) || e.TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || e.TryBeginCardsMovedProgramWindow(f.Id);
        public void Recover(ProgramSkillFrame f)
        {
            if (!e.MouDianWei.Valid(f) || f.MouDianWei is not { Stage: MouDianWeiStage.RecoveryChildren }) throw new InvalidOperationException("Kangyong recovery requires its exact actual-turn-start parent.");
            e.RecoverProgramToMaximum(f);
        }
        public void Lose(ProgramSkillFrame f, int amount)
        {
            if (!e.MouDianWei.Valid(f) || f.MouDianWei is not { Stage: MouDianWeiStage.LossChildren } r || amount != r.ActualLoss || amount < 0 || amount > Math.Max(0, e._players[f.OwnerSeat].Hp - 1))
                throw new InvalidOperationException("Kangyong repays only actual recovered HP while preserving at least one HP.");
            e.ApplyProgramHpLoss(f.Id, f.SkillId, f.OwnerSeat, amount);
        }
        public void Pindian(ProgramSkillFrame f)
        {
            if (!e.MouDianWei.Valid(f) || f.MouDianWei is not { Stage: MouDianWeiStage.Pindian } r || !new MouDianWeiFacts(e).Opponents(f.OwnerSeat).Contains(r.Opponent))
                throw new InvalidOperationException("Kuangzhan requires one currently legal actual Pindian opponent.");
            var id = e._resolutionSequence + 1; var bind = MouDianWeiRuntime.Bind(r); var skill = e._contentRegistry.GetSkill(f.SkillId);
            e.ReplaceRuntimeFrame(f.Id, f = f with { MouDianWei = r with { ContestId = id, Before = e._movementSequence, After = e._movementSequence } });
            e.AdvanceEventRulesAndQueueFact(new MouDianWeiContestIssuedEvent(f.Id, r.Index, id, r.Opponent, bind));
            e.BeginSharedPindian(f.Id, new(f.SkillId, skill.Name, skill.Name, skill.Description), f.OwnerSeat, r.Opponent, programResultBind: bind, programResultVisibility: SkillProgramCardSetVisibility.Public);
        }
        public void Slash(ProgramSkillFrame f, int actor, IReadOnlyList<int> targets)
        {
            if (!e.MouDianWei.Valid(f) || f.MouDianWei is not { Stage: MouDianWeiStage.Slash, Slash: null } r || targets.Count == 0 || targets.Distinct().Count() != targets.Count ||
                targets.Any(s => !e.CanUseForeignTurnContestSlash(actor, s)) || e.ActiveCardAttack is not null || e.ActiveDuel is not null) throw new InvalidOperationException("Kuangzhan must issue its own original comparison's legal native Slash.");
            var result = new MouDianWeiFacts(e).Result(f);
            var expected = (result.SourceWon ? new MouDianWeiFacts(e).NotWon(f.OwnerSeat) : new[] { f.OwnerSeat }).Where(s => e.CanUseForeignTurnContestSlash(actor, s)).ToArray();
            if (actor != (result.SourceWon ? f.OwnerSeat : result.OpponentSeat) || !targets.SequenceEqual(expected)) throw new InvalidOperationException("Kuangzhan cannot substitute its real winner/loser or frozen target set.");
            var id = ++e._resolutionSequence; var source = e._players[actor];
            var action = e.CaptureFactionAction(new CardActionContext(++e._cardActionSequence, e._resolutionStack.OfType<CardUseFrame>().LastOrDefault()?.Action?.ActionId,
                CardActionType.Use, actor, actor, null, null, null, CardKind.Slash, targets, [], [], effectiveSuit: Suit.None, effectiveRank: 0));
            var ret = new MouDianWeiSlash(f.Id, r.Source, f.GameplayHash, r.Index, r.ContestId, id, action.ActionId, actor) { Targets = targets };
            e.ReplaceRuntimeFrame(f.Id, f with { MouDianWei = r with { Slash = ret } });
            e.PushRuntimeFrame(new CardUseFrame(id, actor, 0, CardKind.Slash, ret.Targets, PhysicalCardIds: []) { Action = action, MouDianWei = ret });
            foreach (var (target, index) in ret.Targets.Select((target, index) => (target, index)))
                e.AdvanceEventRulesAndQueueFact(new MouDianWeiSlashIssuedEvent(f.Id, r.Index, r.ContestId, id, action.ActionId, actor, target, index));
            if (e.TracksPlayCardHistory) e.AdvanceEventRulesAndQueueFact(new CardUseAppearanceCapturedEvent(action));
            e.AdvanceEventRulesAndQueueFact(new CardUseDeclaredEvent(id, 0, CardKind.Slash, actor)); e.AdvanceEventRulesAndQueueFact(new TargetsConfirmedEvent(id, ret.Targets));
            e.RecordYingboCardUse(id, actor, CardKind.Slash); e.RecordProgramUsedBasicCard(actor, CardKind.Slash); e.MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor, CardKind.Slash); e.RecordActualPlayPhaseUse(action);
            var attack = new CardAttackHandle(e, id, actor, ret.Targets[0], card: null, damageAmount: 1 + source.AlcoholDamageBonus,
                playedCardKind: CardKind.Slash, ignoresArmor: e.HasCardArmorBypass(source, e._players[ret.Targets[0]], CardKind.Slash), programSkillCardUseFrameId: f.Id);
            e.CaptureProgramAlcoholConsumption(id, source); source.HasAlcoholEffect = false; e.ActiveCardAttack = attack;
            e.AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, CardKind.Slash, actor, ret.Targets[0])); e.TryMarkProgramUseCommitted(id);
            if (!e.TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardUseCommitted, ret.Targets, ProgramCardContinuation.CommittedSlash)) e.BeginSlashTargetResolution(attack);
        }
    }
    internal bool IsMouDianWeiSlashUse(long id) => LifecycleCardUse(id) is { MouDianWei: { } ret } use && _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == ret.ProgramId) is { } f &&
        MouDianWei.Valid(f) && f.MouDianWei is { Stage: MouDianWeiStage.Slash } r && r.Slash == ret && use.Id == ret.UseId && use.CardId == 0 && use.PhysicalCardIds is { Count: 0 } &&
        (use.CurrentSlashFirePolicy?.OriginalAction ?? use.Action) is { } action && action.ActionId == ret.ActionId && action.ActorSeat == ret.Actor && action.ProviderSeat == ret.Actor && action.PhysicalCards.Count == 0 &&
        ret.Targets.Select((target, index) => new MouDianWeiSlashIssuedEvent(f.Id, ret.Index, ret.ContestId, ret.UseId, ret.ActionId, ret.Actor, target, index))
            .SequenceEqual(ProgramEventHistory<MouDianWeiSlashIssuedEvent>().Where(e => e.UseId == ret.UseId));
    [NativeAttackReturn(NativeAttackReturnPhase.AfterBorrowedSword, 35300)]
    private bool MouDianWeiSlashComplete(AttackCompletionReceipt completion)
    {
        if (completion.ProgramFrameId is not { } parent || _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == parent) is not { MouDianWei: { Stage: MouDianWeiStage.Slash, Slash: { } ret } } f) return false;
        if (ret.UseId != completion.ResolutionId || !MouDianWei.Valid(f) || _resolutionStack.OfType<CardUseFrame>().Any(u => u.Id == ret.UseId) || ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == ret.UseId) != 1)
            throw new InvalidOperationException("Kuangzhan's native multi-target Slash returned before its actual use finished.");
        MouDianWei.SlashReturned(f, ret); return true;
    }
}
