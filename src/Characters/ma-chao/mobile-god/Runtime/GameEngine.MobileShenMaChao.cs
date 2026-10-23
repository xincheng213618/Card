using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string MobileMaChaoYuli = "mobile-god:yuli", MobileMaChaoTingwei = "mobile-god:tingwei", MobileMaChaoJimie = "mobile-god:jimie";
    private bool HasMobileShenMaChao => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMobileShenMaChaoTingwei);
    private static CardConversionSource MobileMaChaoSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private DamageNature MobileMaChaoYuliNature(IDamageAttempt attack, DamageNature priorNature) => !attack.IsSourceLess && IsValidPlayerSeat(attack.SourceSeat) &&
        HasRuntimeSkill(_players[attack.SourceSeat], MobileMaChaoYuli) ? DamageNature.Thunder : priorNature;
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 40625)] private int MobileMaChaoSourceDamageBonus(IDamageAttempt attack) =>
        !attack.IsSourceLess && !attack.IsChainPropagation && IsValidPlayerSeat(attack.SourceSeat) && HasRuntimeSkill(_players[attack.SourceSeat], MobileMaChaoYuli) &&
        GetDamageNatureBeforeMobileMaChao(attack) == DamageNature.Thunder ? 1 : 0;
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 40626)] private int MobileTingweiCardDamageBonus(IDamageAttempt attack) =>
        attack.IsChainPropagation ? 0 : (LifecycleCardUse(attack.ResolutionId)?.MobileTingweiBoosts ?? [])
            .Count(b => b.Target == attack.TargetSeat && b.UseId == attack.ResolutionId && LifecycleCardUse(b.UseId)?.Action?.ActionId == b.ActionId);
    private bool IsMobileMaChaoTingweiGrantQualified(CharacterState owner, SkillGrant grant) =>
        !HasMobileShenMaChao || _contentRegistry.GetSkill(grant.SkillId).Tags.HasFlag(SkillTag.Locked) ||
        !ProgramEventHistory<MobileTingweiSuppressedEvent>().Any(e => e.Target == owner.Seat && !ProgramEventHistory<MobileTingweiExpiredEvent>().Any(x => x.FrameId == e.FrameId));
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 40625)] private void ExpireMobileTingwei(IGameEvent fact)
    {
        if (!HasMobileShenMaChao || fact is not (TurnEndedEvent or PlayerDiedEvent)) return;
        foreach (var e in ProgramEventHistory<MobileTingweiSuppressedEvent>().Where(e =>
            !ProgramEventHistory<MobileTingweiExpiredEvent>().Any(x => x.FrameId == e.FrameId) &&
            (fact is TurnEndedEvent t && t.ActorSeat == e.Target && t.TurnNumber > e.ActualTurn || fact is PlayerDiedEvent d && d.VictimSeat == e.Target)).ToArray())
        { AdvanceEventRulesAndQueueFact(new MobileTingweiExpiredEvent(e.FrameId, e.Target, _turnNumber)); _currentTurnSkillSuppressionRevision++; }
    }
    private long LastMobileJimieUse(int owner) => ProgramEventHistory<MobileMaChaoJimieUsedEvent>().LastOrDefault(e => e.Source.OwnerSeat == owner)?.FrameId ?? 0;
    private bool CanMobileJimie(int owner) => LastMobileJimieUse(owner) is 0 || ProgramEventHistory<MobileMaChaoJimieRefreshedEvent>().Any(e => e.Owner == owner && e.UseFrameId == LastMobileJimieUse(owner));
    private void RecordMobileYuli(ProgramSkillFrame f, int part)
    {
        var last = LastMobileJimieUse(f.OwnerSeat);
        AdvanceEventRulesAndQueueFact(new MobileMaChaoYuliExecutedEvent(f.Id, f.OwnerSeat, part, last));
        if (last == 0 || CanMobileJimie(f.OwnerSeat)) return;
        var used = ProgramEventHistory<MobileMaChaoYuliExecutedEvent>().Where(e => e.Owner == f.OwnerSeat && e.JimieUseFrameId == last).Select(e => e.Part).ToHashSet();
        if (used.SetEquals([1, 2])) AdvanceEventRulesAndQueueFact(new MobileMaChaoJimieRefreshedEvent(last, f.OwnerSeat));
    }
    private CardActionContext? MobileMaChaoAction(ProgramSkillWindowContext? c) => c is null ? null :
        _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId)?.Action;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 40625)] private bool MobileMaChaoTriggerEligible(ProgramTriggerLegalityContext g)
    {
        var op = GetProgramTrigger(g.Candidate).Effects[0].Op;
        if (op == SkillProgramEffectOp.ResolveMobileShenMaChaoJimie) return CanMobileJimie(g.OwnerSeat) && g.Owner.Markers.GetValueOrDefault(PlayerMarkerKind.MobileShenMaChaoTing) >= 8;
        if (op == SkillProgramEffectOp.ResolveMobileShenMaChaoTingwei) return MobileMaChaoAction(g.WindowContext) is { Type: CardActionType.Use } a &&
            a.ActorSeat == g.OwnerSeat && IsSlashCard(a.EffectiveKind) && a.TargetSeats.Any(t => _players[t].IsAlive);
        if (op != SkillProgramEffectOp.ResolveMobileShenMaChaoYuli) return true;
        return g.WindowContext is { Amount: > 0 } && CurrentDamageAttempt is { } damage &&
            (g.Trigger.Id == "source-thunder" ? !damage.IsSourceLess && damage.SourceSeat == g.OwnerSeat : damage.TargetSeat == g.OwnerSeat && GetDamageNature(damage) == DamageNature.Thunder);
    }
    [ProgramCapability] private sealed class MobileMaChaoHost(GameEngine e) : IMobileShenMaChaoHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => e.BeginMobileMaChao(f, op); }
    private SkillProgramStepOutcome BeginMobileMaChao(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MobileShenMaChao is not null || f.InstructionIndex != 1 || f.TriggerId is null) throw new InvalidOperationException("Mobile Ma Chao requires his original accepted trigger instruction.");
        var r = new MobileMaChaoReceipt(MobileMaChaoSource(f), f.GameplayHash, 1, op, MobileMaChaoStage.Children);
        if (op == SkillProgramEffectOp.ResolveMobileShenMaChaoYuli)
        {
            RecordMobileYuli(f, f.TriggerId == "source-thunder" ? 1 : 2);
            if (f.TriggerId == "source-thunder") return SkillProgramStepOutcome.Continue;
            var amount = f.WindowContext!.Amount;
            PreventProgramCurrentDamage(f);
            ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { MobileShenMaChao = r with { Stage = MobileMaChaoStage.Drawing, DrawCount = amount, Target = f.OwnerSeat } });
            BeginOperationProgramDraw(f, f.OwnerSeat, amount, new("skill.mobile-shen-ma-chao.yuli.draw")); return SkillProgramStepOutcome.AwaitChild;
        }
        if (op == SkillProgramEffectOp.ResolveMobileShenMaChaoTingwei)
        {
            var a = MobileMaChaoAction(f.WindowContext) ?? throw new InvalidOperationException("Tingwei lost its actual whole Slash action.");
            var use = _resolutionStack.OfType<CardUseFrame>().Single(u => u.Action?.ActionId == a.ActionId);
            r = r with { Stage = MobileMaChaoStage.Target, UseId = use.Id, ActionId = a.ActionId, Targets = a.TargetSeats.ToArray() };
            ReplaceRuntimeTop(f = f with { MobileShenMaChao = r });
            AdvanceEventRulesAndQueueFact(new MobileTingweiStartedEvent(f.Id, r.Source, use.Id, a.ActionId));
            MutateParticipantMarker(f, f.OwnerSeat, PlayerMarkerKind.MobileShenMaChaoTing, 4);
        }
        else
        {
            if (!CanMobileJimie(f.OwnerSeat) || _players[f.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.MobileShenMaChaoTing) < 8)
                throw new InvalidOperationException("Jimie lost its available limited use or eight real Ting.");
            r = r with { Stage = MobileMaChaoStage.Target, Targets = _players.Where(p => p.IsAlive).OrderBy(p => RelativeTableOrder(f.OwnerSeat, p.Seat)).Select(p => p.Seat).ToArray() };
            ReplaceRuntimeTop(f = f with { MobileShenMaChao = r });
        }
        ContinueMobileMaChao(f.Id); return SkillProgramStepOutcome.AwaitChoice;
    }
    private PromptChoice MobileMaChaoChoice(ProgramSkillFrame f, string option, string text, int? target = null) =>
        new(new($"mobile-ma-chao.{f.Id}.{f.MobileShenMaChao!.Stage}.{option}"), text, [], target is { } seat ? [seat] : [],
            new Dictionary<string, string> { ["program-action"] = "mobile-ma-chao", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["option"] = option });
    private IReadOnlyList<PromptChoice> MobileMaChaoChoices(ProgramSkillFrame f)
    {
        var r = f.MobileShenMaChao!;
        if (r.Stage == MobileMaChaoStage.Target) return Array.AsReadOnly(r.Targets.Where(t => _players[t].IsAlive)
            .Select(t => MobileMaChaoChoice(f, "target:" + t, "选择" + _players[t].Name, t)).ToArray());
        if (r.Stage == MobileMaChaoStage.Gift) return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id, r.Target, r.Target,
            [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Transfer, cardCategories: [SkillProgramCardCategory.Equipment], canSelect: (_, c) => !c.IsGeneralWeapon)
            .Select(c => PromptChoiceProjection.Freeze(c with { Id = new($"mobile-ma-chao.{f.Id}.gift.{c.Id.Value}"),
                Parameters = new Dictionary<string, string>(c.Parameters) { ["program-action"] = "mobile-ma-chao", ["option"] = "gift" } })).ToArray());
        if (r.Stage != MobileMaChaoStage.Options) return [];
        var gift = GetHand(_players[r.Target]).Concat(GetEquipment(_players[r.Target])).Any(c => EquipmentCatalog.IsEquipment(c.Kind) && !c.IsGeneralWeapon);
        var discard = MobileMaChaoDiscardable(f.OwnerSeat, r.Target).Length > 0;
        var names = new[] { "非锁定技失效至下回合结束", "交给神马超一张装备牌", "此牌对自己伤害+1", "随机弃置一张牌" };
        return Array.AsReadOnly(Enumerable.Range(0, 16).Where(mask => (gift || (mask & 2) == 0) && (discard || (mask & 8) == 0))
            .Select(mask => MobileMaChaoChoice(f, "mask:" + mask, mask == 0 ? "均不选择，进入连环状态" : string.Join("；", Enumerable.Range(0, 4).Where(i => (mask & (1 << i)) != 0).Select(i => names[i])))).ToArray());
    }
    private Card[] MobileMaChaoDiscardable(int source, int target) => GetHand(_players[target]).Concat(GetEquipment(_players[target]))
        .Where(c => !IsForeignEquipmentDiscardPrevented(target, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Discard)).ToArray();
    private void PublishMobileMaChao(ProgramSkillFrame f)
    {
        var r = f.MobileShenMaChao!; var choices = MobileMaChaoChoices(f);
        if (choices.Count == 0) { ReplaceRuntimeTop(f with { MobileShenMaChao = r with { Stage = MobileMaChaoStage.Execute } }); ContinueMobileMaChao(f.Id); return; }
        var chooser = r.Stage == MobileMaChaoStage.Target ? f.OwnerSeat : r.Target; var s = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, r.Stage == MobileMaChaoStage.Options ? "霆威：一次选择要执行的任意项。" : "请选择。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, s.Name, s.Name, s.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("mobile-ma-chao")] private void AnswerMobileMaChao(PromptChoice c)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShenMaChao: { } r } f ||
            _pendingDecision?.PlayerSeat != (r.Stage == MobileMaChaoStage.Target ? f.OwnerSeat : r.Target) ||
            !MobileMaChaoChoices(f).Any(x => AssistedChoicesEqual([x], [c]))) throw new InvalidOperationException("Mobile Ma Chao requires the exact frozen target, subset or genuine equipment payment.");
        AssertMobileMaChao(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (r.Stage == MobileMaChaoStage.Target)
        {
            var target = c.Targets.Single();
            if (r.Op == SkillProgramEffectOp.ResolveMobileShenMaChaoJimie)
            {
                if (!CanMobileJimie(f.OwnerSeat) || _players[f.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.MobileShenMaChaoTing) < 8)
                    throw new InvalidOperationException("Jimie cannot spend unavailable Ting or reuse an unrefreshed limit.");
                ReplaceRuntimeTop(f = f with { MobileShenMaChao = r with { Target = target, Stage = MobileMaChaoStage.Children } });
                MutateParticipantMarker(f, f.OwnerSeat, PlayerMarkerKind.MobileShenMaChaoTing, -8);
                AdvanceEventRulesAndQueueFact(new MobileMaChaoJimieUsedEvent(f.Id, r.Source, target, 8, _turnNumber));
            }
            else ReplaceRuntimeTop(f = f with { MobileShenMaChao = r with { Target = target, Stage = MobileMaChaoStage.Options } });
        }
        else if (r.Stage == MobileMaChaoStage.Options)
        {
            var mask = int.Parse(c.Parameters["option"][5..], CultureInfo.InvariantCulture); var paid = System.Numerics.BitOperations.PopCount((uint)mask);
            if (_players[f.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.MobileShenMaChaoTing) < paid) throw new InvalidOperationException("Tingwei cannot spend absent real markers.");
            ReplaceRuntimeTop(f = f with { MobileShenMaChao = r with { Mask = mask, Cursor = 1, Stage = MobileMaChaoStage.Execute } });
            if (paid > 0) MutateParticipantMarker(f, f.OwnerSeat, PlayerMarkerKind.MobileShenMaChaoTing, -paid);
            AdvanceEventRulesAndQueueFact(new MobileTingweiSelectedEvent(f.Id, r.Target, mask, paid));
            if (mask == 0) new ProgramSkillHost(this).SetChainedState(f, true, r.Target);
        }
        else
        {
            var zone = Enum.Parse<CardZoneKind>(c.Parameters["source-zone"]); var slot = int.Parse(c.Parameters["slot-index"], CultureInfo.InvariantCulture);
            var from = new CardLocation(zone, r.Target); var card = _cardZones.CardsAt(from)[slot]; var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { MobileShenMaChao = r with { Stage = MobileMaChaoStage.Execute, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveCard(card, from, CardLocation.Hand(f.OwnerSeat), new("skill.mobile-shen-ma-chao.tingwei.gift"));
            f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { MobileShenMaChao = f.MobileShenMaChao! with { After = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new MobileMaChaoMovedEvent(f.Id, "gift", r.Target, before, _movementSequence));
        }
        ContinueMobileMaChao(f.Id);
    }
    private bool DrainMobileMaChao(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueMobileMaChao(long id)
    {
        while (_pendingDecision is null && _resolutionStack.LastOrDefault() is ProgramSkillFrame { MobileShenMaChao: { } r } f && f.Id == id)
        {
            if (r.Stage == MobileMaChaoStage.Drawing || f.AttackAttempt is not null) return;
            if (DrainMobileMaChao(id)) return;
            f = GetActiveProgramFrame(id); r = f.MobileShenMaChao!;
            if (f.PendingMovementContinuation is not null) ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
            if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || r.Target >= 0 && !_players[r.Target].IsAlive) { FinishMobileMaChao(f); return; }
            if (r.Stage is MobileMaChaoStage.Target or MobileMaChaoStage.Options or MobileMaChaoStage.Gift) { PublishMobileMaChao(f); return; }
            if (r.Op == SkillProgramEffectOp.ResolveMobileShenMaChaoJimie && r.Stage == MobileMaChaoStage.Children)
            {
                var amount = _players[r.Target].MaxHp;
                ReplaceRuntimeTop(f = f with { MobileShenMaChao = r with { Damage = amount, Stage = MobileMaChaoStage.Damage } });
                BeginProgramSkillDamage(f, r.Target, amount); return;
            }
            if (r.Stage == MobileMaChaoStage.Execute && r.Cursor <= 4)
            {
                var option = r.Cursor;
                ReplaceRuntimeTop(f = f with { MobileShenMaChao = r with { Cursor = option + 1 } });
                if ((r.Mask & (1 << (option - 1))) == 0) continue;
                if (option == 1)
                {
                    AdvanceEventRulesAndQueueFact(new MobileTingweiSuppressedEvent(f.Id, r.Source, r.Target, _turnNumber)); _currentTurnSkillSuppressionRevision++;
                }
                else if (option == 2) ReplaceRuntimeTop(f with { MobileShenMaChao = f.MobileShenMaChao! with { Stage = MobileMaChaoStage.Gift } });
                else if (option == 3)
                {
                    var b = new MobileTingweiBoost(f.Id, r.Source, r.UseId, r.ActionId, r.Target);
                    UpdateLifecycleCardUse(r.UseId, u => u with { MobileTingweiBoosts = (u.MobileTingweiBoosts ?? []).Append(b).ToArray() });
                    AdvanceEventRulesAndQueueFact(new MobileTingweiBoostedEvent(b));
                }
                else
                {
                    var cards = MobileMaChaoDiscardable(f.OwnerSeat, r.Target); if (cards.Length == 0) continue;
                    var randomBefore = _random.State; var card = cards[_random.Next(cards.Length)]; var before = _movementSequence;
                    ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { MobileShenMaChao = GetActiveProgramFrame(id).MobileShenMaChao! with { Before = before, After = before,
                        RandomBefore = randomBefore, RandomAfter = _random.State }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
                    MoveProgramLogicalDiscard(f, r.Target, [card.Id], new("skill.mobile-shen-ma-chao.tingwei.random-discard"));
                    f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f with { MobileShenMaChao = f.MobileShenMaChao! with { After = _movementSequence } });
                    AdvanceEventRulesAndQueueFact(new MobileMaChaoMovedEvent(id, "discard", r.Target, before, _movementSequence));
                }
                continue;
            }
            FinishMobileMaChao(f); return;
        }
    }
    private void FinishMobileMaChao(ProgramSkillFrame f) { ReplaceRuntimeTop(f with { MobileShenMaChao = null, PendingMovementContinuation = null }); AdvanceRuntimeProgram(f.Id); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 40625)] private bool ResumeMobileMaChao(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShenMaChao: not null } f || f.Id != id) return false; ContinueMobileMaChao(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 40625)] private bool ReturnMobileMaChaoMovement(ProgramSkillFrame f) => f.MobileShenMaChao is not null && ResumeMobileMaChao(f.Id);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMobileShenMaChaoYuli)] private void ReturnMobileMaChaoDraw(ProgramSkillFrame f, NativeDrawOutcome o)
    {
        if (f.MobileShenMaChao is not { Stage: MobileMaChaoStage.Drawing } r || o.RecipientSeat != f.OwnerSeat || o.RequestedCount != r.DrawCount ||
            ProgramEventHistory<MobileMaChaoDrawReturnedEvent>().Any(e => e.FrameId == f.Id)) throw new InvalidOperationException("Yuli lost its exact prevented native damage producer return.");
        ReplaceRuntimeTop(f with { MobileShenMaChao = r with { Stage = MobileMaChaoStage.Children } });
        AdvanceEventRulesAndQueueFact(new MobileMaChaoDrawReturnedEvent(f.Id, f.OwnerSeat, o.RequestedCount, o.ActualDrawIds.Count, o.SequenceBefore, o.SequenceAfter)); ResumeMobileMaChao(f.Id);
    }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 40625)] private bool MobileMaChaoMovementProof(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        f.MobileShenMaChao is { Stage: MobileMaChaoStage.Execute } && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 40625)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 40625)] private bool RetainMobileMaChao(ProgramSkillFrame f) => f.MobileShenMaChao is not null;
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileShenMaChaoTingwei)] private PromptChoice ChooseMobileTingwei(PendingDecision p, ProgramSkillFrame f) => p.Choices.First();
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileShenMaChaoJimie)] private PromptChoice ChooseMobileJimie(PendingDecision p, ProgramSkillFrame f) => p.Choices.Last();
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileShenMaChaoYuli)] private PromptChoice ChooseMobileYuli(PendingDecision p, ProgramSkillFrame f) => p.Choices.First();
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 40625)] private void AssertMobileMaChao(ProgramSkillFrame f)
    {
        if (f.MobileShenMaChao is not { } r) return;
        if (r.Source != MobileMaChaoSource(f) || r.GameplayHash != f.GameplayHash || r.InstructionIndex != 1 || f.InstructionIndex != 1 || !Enum.IsDefined(r.Stage) ||
            r.Targets.Distinct().Count() != r.Targets.Count || r.Targets.Any(t => !IsValidPlayerSeat(t)) || r.Mask is < -1 or > 15 || r.Cursor is < 0 or > 5 || r.After < r.Before)
            throw new InvalidOperationException("Mobile Ma Chao lost his original trigger, frozen targets/subset or movement cursor.");
        if (r.Op == SkillProgramEffectOp.ResolveMobileShenMaChaoTingwei && (LifecycleCardUse(r.UseId) is not { Action: { } a } || a.ActionId != r.ActionId || a.ActorSeat != f.OwnerSeat || !IsSlashCard(a.EffectiveKind) ||
            !a.TargetSeats.SequenceEqual(r.Targets) || ProgramEventHistory<MobileTingweiStartedEvent>().Count(e => e == new MobileTingweiStartedEvent(f.Id, r.Source, r.UseId, r.ActionId)) != 1 ||
            r.Mask >= 0 && ProgramEventHistory<MobileTingweiSelectedEvent>().Count(e => e == new MobileTingweiSelectedEvent(f.Id, r.Target, r.Mask, System.Numerics.BitOperations.PopCount((uint)r.Mask))) != 1))
            throw new InvalidOperationException("Tingwei lost its actual whole Slash, chosen target or once-paid subset.");
        if (r.Op == SkillProgramEffectOp.ResolveMobileShenMaChaoJimie && r.Stage != MobileMaChaoStage.Target &&
            ProgramEventHistory<MobileMaChaoJimieUsedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.Target == r.Target && e.Paid == 8) != 1)
            throw new InvalidOperationException("Jimie lost its once-paid eight Ting and actual rechargeable limited use.");
    }
}
