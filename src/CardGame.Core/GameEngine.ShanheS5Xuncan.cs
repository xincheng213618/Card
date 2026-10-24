namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShanheS5YunshenSkill = "shanhe:s5-yunshen-3226";
    private const string ShanheS5ShangshenSkill = "shanhe:s5-shangshen-3227";
    private const string ShanheS5FenchaiSkill = "shanhe:s5-fenchai-3228";

    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 5771)]
    private void RecordShanheS5FirstElementalDamage(DamageAppliedEvent damage)
    {
        if (damage.Amount <= 0 || damage.Nature == DamageNature.Normal ||
            !_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveShanheS5Xuncan) ||
            ProgramEventHistory<ShanheS5FirstElementalDamageEvent>().Any(e => e.TurnNumber == _turnNumber)) return;
        var native = _resolutionStack.OfType<DamageFrame>().LastOrDefault();
        if (native is null || native.SourceSeat != damage.SourceSeat || native.TargetSeat != damage.TargetSeat || native.Nature != damage.Nature)
            throw new InvalidOperationException("The first elemental damage must belong to its real applied damage segment.");
        AdvanceEventRulesAndQueueFact(new ShanheS5FirstElementalDamageEvent(_turnNumber, native.Id, damage.TargetSeat, damage.Nature));
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 5771, SkillProgramEffectOp.ResolveShanheS5Xuncan)]
    private bool CanOfferShanheS5Xuncan(ProgramTriggerLegalityContext c) =>
        c.Trigger.Effects.Any(e => e.StateId == "leijie") ||
        c.Context is { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0, DamageFrameId: { } damageId } &&
        ProgramEventHistory<ShanheS5FirstElementalDamageEvent>().Any(e => e.TurnNumber == _turnNumber && e.DamageFrameId == damageId);

    private ShanheS5SkillTargetEvent RequireShanheS5SkillTarget(ProgramSkillFrame f)
    {
        var facts = ProgramEventHistory<ShanheS5SkillTargetEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (facts is not [var target] || target.Source != new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId) ||
            target.GameplayHash != f.GameplayHash || !IsValidPlayerSeat(target.TargetSeat) ||
            (f.SkillId == ShanheS5YunshenSkill ? f.SelectedTargetSeats is not [var selected] || selected != target.TargetSeat :
                f.SkillId != ShanheS5ShangshenSkill || f.WindowContext?.TargetSeat != target.TargetSeat))
            throw new InvalidOperationException("Xuncan lost his original accepted skill target and source instance.");
        return target;
    }

    private void CaptureShanheS5SkillTarget(ProgramSkillFrame f, int targetSeat)
    {
        if (ProgramEventHistory<ShanheS5SkillTargetEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("Xuncan cannot accept the same skill target twice.");
        AdvanceEventRulesAndQueueFact(new ShanheS5SkillTargetEvent(f.Id,
            new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId), f.GameplayHash, targetSeat));
        var owner = _players[f.OwnerSeat]; var target = _players[targetSeat];
        // Only these printed skills declare a target. Acquiring an unrelated skill
        // never retroactively binds Fenchai to an unrelated character.
        if (owner.Gender == target.Gender || !(owner.General.Skills.Any(s => s.ContentId == f.SkillId) || owner.SecondaryGeneral?.Skills.Any(s => s.ContentId == f.SkillId) == true) ||
            ProgramEventHistory<ShanheS5FenchaiBoundEvent>().Any(e => e.OwnerSeat == f.OwnerSeat && e.FenchaiSkillId == ShanheS5FenchaiSkill)) return;
        var policy = CardPolicies(owner, SkillProgramCardPolicyKind.ShanheFenchaiJudgment).FirstOrDefault();
        if (policy.Source is null || policy.Source.SkillId != ShanheS5FenchaiSkill) return;
        AdvanceEventRulesAndQueueFact(new ShanheS5FenchaiBoundEvent(f.Id, f.OwnerSeat, policy.Source.SkillId, policy.Source.SkillInstanceId, targetSeat));
    }

    private Suit ShanheS5FenchaiJudgmentSuit(JudgmentFrame judgment, Suit currentSuit)
    {
        var owner = _players[judgment.TargetSeat];
        if (!HasCardPolicy(owner, SkillProgramCardPolicyKind.ShanheFenchaiJudgment)) return currentSuit;
        var bound = ProgramEventHistory<ShanheS5FenchaiBoundEvent>().SingleOrDefault(e => e.OwnerSeat == owner.Seat && e.FenchaiSkillId == ShanheS5FenchaiSkill);
        if (bound is null) return currentSuit;
        if (!IsValidPlayerSeat(bound.TargetSeat) || !ProgramEventHistory<ShanheS5SkillTargetEvent>().Any(e => e.FrameId == bound.ProducerFrameId && e.Source.OwnerSeat == owner.Seat && e.TargetSeat == bound.TargetSeat))
            throw new InvalidOperationException("Fenchai lost its first real opposite-gender skill target.");
        return _players[bound.TargetSeat].IsAlive ? Suit.Heart : Suit.Spade;
    }

    private SkillProgramStepOutcome ResolveShanheS5Xuncan(ProgramSkillFrame supplied, SkillProgramEffect effect)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        switch (effect.StateId)
        {
            case "yunshen-bind":
                if (f.InstructionIndex != 1 || f.TriggerId is not null || f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play ||
                    f.SelectedTargetSeats is not [var target] || target == f.OwnerSeat || !_players[target].IsAlive)
                    throw new InvalidOperationException("Yunshen requires one real other living Play target.");
                CaptureShanheS5SkillTarget(f, target); return SkillProgramStepOutcome.Continue;
            case "yunshen-slash":
                var accepted = RequireShanheS5SkillTarget(f);
                if (f.InstructionIndex != 3 || f.ShanheS5Yunshen is not null)
                    throw new InvalidOperationException("Yunshen cannot repeat its directional Cold Slash.");
                var directions = ShanheS5YunshenDirections(f.OwnerSeat, accepted.TargetSeat);
                if (directions.Length == 0) return SkillProgramStepOutcome.Continue;
                ReplaceRuntimeTop(f = f with { ShanheS5Yunshen = new(accepted.Source, f.GameplayHash, accepted.TargetSeat) });
                PublishShanheS5YunshenChoice(f, directions); return SkillProgramStepOutcome.AwaitChoice;
            case "shangshen-bind":
                if (f.InstructionIndex != 1 || f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, DamageFrameId: { } damage, TargetSeat: { } victim, Amount: > 0 } ||
                    !ProgramEventHistory<ShanheS5FirstElementalDamageEvent>().Any(e => e.TurnNumber == _turnNumber && e.DamageFrameId == damage && e.TargetSeat == victim))
                    throw new InvalidOperationException("Shangshen requires the first actual elemental segment this turn.");
                CaptureShanheS5SkillTarget(f, victim); return SkillProgramStepOutcome.Continue;
            case "shangshen-lightning":
                _ = RequireShanheS5SkillTarget(f);
                var lightning = RequireShanheS5Outcome(f, effect, f.OwnerSeat);
                var hit = lightning.Suit == Suit.Spade && lightning.Rank is >= 2 and <= 9;
                RecordShanheS5JudgmentEffect(f, lightning, effect.StateId, f.OwnerSeat, hit, hit ? 3 : 0);
                return hit && _players[f.OwnerSeat].IsAlive ? BeginProgramSkillDamage(f, f.OwnerSeat, 3, nature: DamageNature.Thunder, sourceLess: true) : SkillProgramStepOutcome.Continue;
            case "shangshen-refill":
                var recipient = RequireShanheS5SkillTarget(f).TargetSeat;
                if (!_players[recipient].IsAlive) return SkillProgramStepOutcome.Continue;
                var count = Math.Max(0, 4 - GetHand(_players[recipient]).Count);
                if (ProgramEventHistory<ShanheS5RefillRequestedEvent>().Any(e => e.FrameId == f.Id)) throw new InvalidOperationException("Shangshen cannot pay its refill twice.");
                AdvanceEventRulesAndQueueFact(new ShanheS5RefillRequestedEvent(f.Id, recipient, count));
                return BeginOperationProgramDraw(f, recipient, count, new("shanhe.s5.shangshen.refill"));
            case "leijie":
                if (f.SelectedTargetSeats is not [var judged]) throw new InvalidOperationException("Leijie lost its selected judgment target.");
                var result = RequireShanheS5Outcome(f, effect, judged);
                var struck = result.Suit == Suit.Spade && result.Rank is >= 2 and <= 9;
                RecordShanheS5JudgmentEffect(f, result, effect.StateId, judged, struck, 2);
                if (!_players[judged].IsAlive) return SkillProgramStepOutcome.Continue;
                return struck ? BeginProgramSkillDamage(f, judged, 2, nature: DamageNature.Thunder) :
                    BeginOperationProgramDraw(f, judged, 2, new("shanhe.s5.leijie.draw"));
            default: throw new InvalidOperationException("Unknown S5 Xuncan stage.");
        }
    }

    private ProgramJudgmentOutcomeBinding RequireShanheS5Outcome(ProgramSkillFrame f, SkillProgramEffect effect, int target)
    {
        var outcomes = f.JudgmentOutcomeBindings?.Where(o => o.Name == effect.SourceBind).ToArray();
        if (outcomes is not [var outcome] || outcome.TargetSeat != target || !IsValidProgramJudgmentOutcome(f, outcome))
            throw new InvalidOperationException("The S5 judgment lost its exact finalized native outcome.");
        return outcome;
    }
    private void RecordShanheS5JudgmentEffect(ProgramSkillFrame f, ProgramJudgmentOutcomeBinding outcome, string stage, int target, bool hit, int requested)
    {
        if (ProgramEventHistory<ShanheS5JudgmentEffectEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("An S5 judgment result cannot be applied twice.");
        AdvanceEventRulesAndQueueFact(new ShanheS5JudgmentEffectEvent(f.Id, outcome.JudgmentFrameId, stage, target, hit, requested));
    }

    private int[] ShanheS5YunshenDirections(int owner, int target) => new[] { owner, target }.Where(actor =>
        _players[actor].IsAlive && CanUseVirtualSlashTarget(_players[actor], _players[actor == owner ? target : owner],
            CardKind.ColdSlash, physicalSuit: Suit.None, physicalCardIds: []) &&
        !IsCardTargetProhibited(_players[actor == owner ? target : owner], CardKind.ColdSlash, Suit.None)).ToArray();

    private void PublishShanheS5YunshenChoice(ProgramSkillFrame f, int[] actors)
    {
        var r = f.ShanheS5Yunshen!; var skill = _contentRegistry.GetSkill(f.SkillId);
        var choices = actors.Select(actor => new PromptChoice(new ChoiceId($"shanhe-yunshen.frame-{f.Id}.actor-{actor}"),
            $"{_players[actor].Name} 对 {_players[actor == f.OwnerSeat ? r.OriginalTargetSeat : f.OwnerSeat].Name} 使用冰【杀】", [], [],
            new Dictionary<string, string> { ["program-action"] = "shanhe-s5-yunshen", ["frame-id"] = f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), ["actor-seat"] = actor.ToString(System.Globalization.CultureInfo.InvariantCulture) })).ToArray();
        _pendingDecision = new PendingDecision(DecisionKind.ProgramTrigger, f.OwnerSeat, "请选择熨身的冰【杀】方向。", [], [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = Array.AsReadOnly(choices), SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    [ProgramAnswer("shanhe-s5-yunshen")]
    private void AnswerShanheS5Yunshen(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShanheS5Yunshen: { UseId: null, ActorSeat: null } r } f ||
            f.InstructionIndex != 3 || choice.Parameters.GetValueOrDefault("frame-id") != f.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            !int.TryParse(choice.Parameters.GetValueOrDefault("actor-seat"), out var actor) ||
            !ShanheS5YunshenDirections(f.OwnerSeat, r.OriginalTargetSeat).Contains(actor) || choice.Cards.Count != 0 || choice.Targets.Count != 0 || _pendingDecision?.PlayerSeat != f.OwnerSeat)
            throw new InvalidOperationException("Yunshen lost its exact native direction choice.");
        _ = RequireShanheS5SkillTarget(f); ClearPendingDecision();
        BeginShanheS5YunshenSlash(f, actor, actor == f.OwnerSeat ? r.OriginalTargetSeat : f.OwnerSeat);
    }

    [ProgramAiChoice(SkillProgramEffectOp.ResolveShanheS5Xuncan)]
    private PromptChoice SelectAiShanheS5Yunshen(PendingDecision decision, ProgramSkillFrame f) =>
        decision.Choices.OrderByDescending(c => c.Parameters.GetValueOrDefault("actor-seat") == f.OwnerSeat.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();

    private void BeginShanheS5YunshenSlash(ProgramSkillFrame f, int actorSeat, int targetSeat)
    {
        if (ActiveCardAttack is not null || ActiveDuel is not null) throw new InvalidOperationException("Yunshen cannot overwrite a native attack.");
        var source = _players[actorSeat]; var target = _players[targetSeat]; var useId = ++_resolutionSequence;
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence, _resolutionStack.OfType<CardUseFrame>().LastOrDefault()?.Action?.ActionId,
            CardActionType.Use, actorSeat, actorSeat, null, null, null, CardKind.ColdSlash, [targetSeat], [], [f.ShanheS5Yunshen!.Source], effectiveSuit: Suit.None, effectiveRank: 0));
        ReplaceRuntimeTop(f = f with { ShanheS5Yunshen = f.ShanheS5Yunshen! with { ActorSeat = actorSeat, SlashTargetSeat = targetSeat, UseId = useId } });
        AdvanceEventRulesAndQueueFact(new ShanheS5YunshenIssuedEvent(f.Id, useId, action.ActionId, actorSeat, targetSeat));
        PushRuntimeFrame(new CardUseFrame(useId, actorSeat, 0, CardKind.ColdSlash, [targetSeat], PhysicalCardIds: [])
        { Action = action, FirstOwnPlayUseDistanceUnlimited = HasFirstActualPlayUseDistance(source) });
        if (TracksPlayCardHistory) AdvanceEventRulesAndQueueFact(new CardUseAppearanceCapturedEvent(action));
        AdvanceEventRulesAndQueueFact(new CardUseDeclaredEvent(useId, 0, CardKind.ColdSlash, actorSeat));
        AdvanceEventRulesAndQueueFact(new TargetsConfirmedEvent(useId, [targetSeat]));
        RecordYingboCardUse(useId, actorSeat, CardKind.ColdSlash);
        RecordProgramUsedBasicCard(actorSeat, CardKind.ColdSlash);
        // Only the actual current Play actor pays its normal quota. The opposite
        // direction is an outside-turn use by the selected role, never Xuncan's debit.
        if (_phase == TurnPhase.Play && actorSeat == _currentSeat && LifecycleCardUse(useId)?.UnlimitedUse != true)
            RecordSlashUseDebit(useId, actorSeat);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actorSeat, CardKind.ColdSlash);
        var attack = new CardAttackHandle(this, useId, actorSeat, targetSeat, card: null, damageAmount: 1 + source.AlcoholDamageBonus,
            playedCardKind: CardKind.ColdSlash, ignoresArmor: HasCardArmorBypass(source, target, CardKind.ColdSlash), programSkillCardUseFrameId: f.Id);
        CaptureProgramAlcoholConsumption(useId, source); source.HasAlcoholEffect = false; ActiveCardAttack = attack;
        ObservePhaseNamePredictionUse(action);
        if (HasPhaseStartCardUseParticipant(source.Seat)) RecordActualPlayPhaseUse(action);
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, CardKind.ColdSlash, actorSeat, targetSeat)); TryMarkProgramUseCommitted(useId);
        if (!TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardUseCommitted, action.TargetSeats, ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
    }

    private bool IsShanheS5YunshenUse(long useId) => ProgramEventHistory<ShanheS5YunshenIssuedEvent>().Any(e => e.UseId == useId);

    private void AssertShanheS5XuncanNativeUse(CardUseFrame use)
    {
        var issued = ProgramEventHistory<ShanheS5YunshenIssuedEvent>().Where(e => e.UseId == use.Id).ToArray();
        if (issued.Length == 0) return;
        if (issued is not [var fact] || _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.Id == fact.FrameId) is not { ShanheS5Yunshen: { Returned: false } r } parent ||
            r.UseId != use.Id || r.ActorSeat != fact.ActorSeat || r.SlashTargetSeat != fact.TargetSeat || parent.InstructionIndex != 3 ||
            use.CardId != 0 || use.CardKind != CardKind.ColdSlash || use.SourceSeat != fact.ActorSeat || use.PhysicalCardIds is not { Count: 0 } ||
            use.Action is not { Type: CardActionType.Use, EffectiveKind: CardKind.ColdSlash,
                PhysicalCards.Count: 0, EffectiveSuit: Suit.None } action ||
            action.ActionId != fact.ActionId || action.ActorSeat != fact.ActorSeat || action.ProviderSeat != fact.ActorSeat ||
            !action.ConversionChain.SequenceEqual([r.Source]) || r.Source.OwnerSeat != parent.OwnerSeat)
            throw new InvalidOperationException("Yunshen's zero-entity native Cold Slash lost its original owner, actual actor or source frame.");
        _ = RequireShanheS5SkillTarget(parent);
    }

    [NativeAttackReturn(NativeAttackReturnPhase.BeforeParentResume, 5771)]
    private bool ReturnShanheS5YunshenSlash(AttackCompletionReceipt completion)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShanheS5Yunshen: { } r } f || r.UseId != completion.ResolutionId) return false;
        if (r.Returned || completion.ProgramFrameId != f.Id || !completion.RequestedSlashChild ||
            ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == r.UseId && e.CardKind == CardKind.ColdSlash) != 1)
            throw new InvalidOperationException("Yunshen lost its exact completed native Cold Slash return.");
        ReplaceRuntimeTop(f with { ShanheS5Yunshen = r with { Returned = true } }); AdvanceRuntimeProgram(f.Id); return true;
    }

    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveShanheS5Xuncan)]
    private void ReturnShanheS5XuncanDraw(ProgramSkillFrame supplied, NativeDrawOutcome draw)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        var request = ProgramEventHistory<ShanheS5RefillRequestedEvent>().SingleOrDefault(e => e.FrameId == f.Id);
        var judgment = ProgramEventHistory<ShanheS5JudgmentEffectEvent>().SingleOrDefault(e => e.FrameId == f.Id);
        var target = request?.TargetSeat ?? judgment?.TargetSeat; var count = request?.Requested ?? judgment?.Requested;
        if (target != draw.RecipientSeat || count != draw.RequestedCount || f.NativeDraw is not null ||
            request is null && judgment is not { Stage: "leijie", LightningHit: false } || draw.SequenceAfter != _movementSequence ||
            ProgramEventHistory<ShanheS5XuncanDrawPaidEvent>().Any(e => e.FrameId == f.Id))
            throw new InvalidOperationException("The S5 refill lost its exact once-only native Draw return.");
        AdvanceEventRulesAndQueueFact(new ShanheS5XuncanDrawPaidEvent(f.Id, draw.RecipientSeat, draw.RequestedCount, draw.ActualDrawIds.Count, draw.SequenceBefore, draw.SequenceAfter));
        AdvanceRuntimeProgram(f.Id);
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 5771)]
    private void AssertShanheS5Yunshen(ProgramSkillFrame f)
    {
        if (f.ShanheS5Yunshen is not { } r) return;
        var accepted = RequireShanheS5SkillTarget(f);
        if (f.InstructionIndex != 3 || r.Source != accepted.Source || r.GameplayHash != f.GameplayHash || r.OriginalTargetSeat != accepted.TargetSeat ||
            r.UseId.HasValue != r.ActorSeat.HasValue || r.UseId.HasValue != r.SlashTargetSeat.HasValue ||
            r.ActorSeat is { } actor && (actor != f.OwnerSeat && actor != r.OriginalTargetSeat || r.SlashTargetSeat != (actor == f.OwnerSeat ? r.OriginalTargetSeat : f.OwnerSeat)) ||
            r.UseId is { } id && ProgramEventHistory<ShanheS5YunshenIssuedEvent>().Count(e => e.FrameId == f.Id && e.UseId == id && e.ActorSeat == r.ActorSeat && e.TargetSeat == r.SlashTargetSeat) != 1 ||
            r.Returned && ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == r.UseId && e.CardKind == CardKind.ColdSlash) != 1)
            throw new InvalidOperationException("Yunshen lost its original frame, actual direction or exact native use identity.");
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 5771)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 5771)]
    private bool ContinueAcceptedShanheS5Xuncan(ProgramSkillFrame f) =>
        f.SkillId is ShanheS5YunshenSkill or ShanheS5ShangshenSkill && ProgramEventHistory<ShanheS5SkillTargetEvent>().Any(e => e.FrameId == f.Id) ||
        f.SkillId == "shanhe:s5-leijie-3231" && (_resolutionStack.OfType<JudgmentFrame>().Any(j => j.ParentFrameId == f.Id && j.Reason == "shanhe.s5.leijie") ||
            f.JudgmentOutcomeBindings?.Any(o => o.Reason == "shanhe.s5.leijie" && IsValidProgramJudgmentOutcome(f, o)) == true) ||
        ProgramEventHistory<ShanheS5JudgmentEffectEvent>().Any(e => e.FrameId == f.Id);

    private sealed partial class ProgramSkillHost : IShanheS5XuncanHost
    { public SkillProgramStepOutcome ResolveShanheS5Xuncan(ProgramSkillFrame f, SkillProgramEffect e) => engine.ResolveShanheS5Xuncan(f, e); }
}
