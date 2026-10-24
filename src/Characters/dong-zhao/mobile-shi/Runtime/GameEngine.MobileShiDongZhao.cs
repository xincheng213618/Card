namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShiDzMiaolue = "mobile:shi-miaolue", ShiDzYingjia = "mobile:shi-yingjia";
    private const string ShiDzGain = "skill-program.mobile-shi-dong-zhao.gain", ShiDzDraw = "skill-program.mobile-shi-dong-zhao.draw",
        ShiDzDiscard = "skill.mobile-shi-dong-zhao.discard";
    private static CardConversionSource ShiDzSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    [ProgramCapability]
    private sealed class MobileShiDongZhaoHost(GameEngine e) : IMobileShiDongZhaoHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op) => e.BeginShiDz(frame, op); }

    private int ShiDzSameNameMaximum(int seat) => ProgramEventHistory<ActualTurnTrickUseRecordedEvent>()
        .Where(e => e.TurnNumber == _turnNumber && e.ActorSeat == seat).GroupBy(e => e.EffectiveKind)
        .Select(g => g.Count()).DefaultIfEmpty(0).Max();
    private IReadOnlyList<PromptChoice> ShiDzPayments(ProgramSkillFrame f) => BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, f.OwnerSeat,
        [CardZoneKind.Hand], OwnedCardMoveIntent.Discard,
        canSelect: (zone, card) => !IsSelfHandCategoryDiscardForbidden(f.OwnerSeat, card, new(zone, f.OwnerSeat), OwnedCardMoveIntent.Discard));
    private MobileShiDongZhaoExtraGrantedEvent? ShiDzPendingExtra(int beneficiary)
    {
        if (_turnProgression.Kind != ActualTurnKind.Extra || _currentSeat != beneficiary || !_players[beneficiary].IsAlive ||
            ProgramEventHistory<ProgramExtraTurnPendedEvent>().LastOrDefault() is not { SkillId: ShiDzYingjia } last || last.Seat != beneficiary)
            return null;
        return ProgramEventHistory<MobileShiDongZhaoExtraGrantedEvent>().SingleOrDefault(e => e.FrameId == last.FrameId &&
            e.TargetSeat == beneficiary && e.IssuedTurn == _turnNumber && e.Source.SkillId == ShiDzYingjia &&
            e.GameplayHash == _contentRegistry.GetSkill(ShiDzYingjia).Program!.GameplayHash &&
            !ProgramEventHistory<MobileShiDongZhaoExtraEnteredEvent>().Any(x => x.GrantFrameId == e.FrameId));
    }

    [ProgramPipelineHook(ProgramPipelinePhase.IncludeTriggerCandidate, 47700)]
    private IReadOnlyList<ProgramTriggerCandidate> IncludeShiDzExtra(CharacterState owner, SkillProgramTriggerWindow window,
        IReadOnlyList<ProgramTriggerCandidate> ordinary)
    {
        if (window != SkillProgramTriggerWindow.TurnBeforeStart || !_contentRegistry.Skills.ContainsKey(ShiDzYingjia)) return ordinary;
        var rest = ordinary.Where(c => c.SkillId != ShiDzYingjia || c.BindingId != "issued-extra-start").ToArray();
        var grant = ShiDzPendingExtra(owner.Seat);
        return grant is null ? Array.AsReadOnly(rest) : Array.AsReadOnly(rest.Append(new ProgramTriggerCandidate(owner.Seat,
            ShiDzYingjia, "issued-extra-start", grant.Source.SkillInstanceId, grant.GameplayHash, 100)).ToArray());
    }

    [ProgramPipelineHook(ProgramPipelinePhase.IssuedTriggerCandidate, 47700)]
    private bool IsIssuedShiDzExtra(ProgramTriggerCandidate c, ProgramSkillWindowContext w) => c.SkillId == ShiDzYingjia &&
        c.BindingId == "issued-extra-start" && w.Window == SkillProgramTriggerWindow.TurnBeforeStart &&
        w.BeforeTurnPreludeProof is { } proof && proof.UpcomingOwnerSeat == c.OwnerSeat && ValidBeiMiHuBeforeTurnPrelude(proof) &&
        ShiDzPendingExtra(c.OwnerSeat) is { } grant && grant.Source.SkillInstanceId == c.SkillInstanceId && grant.GameplayHash == c.GameplayHash &&
        _resolutionStack.SingleOrDefault(f => f.Id == w.ParentFrameId) is ProgramLifecycleTriggerWindowFrame parent &&
        parent.Window == w.Window && parent.CandidateIndex < parent.Candidates.Count && parent.Candidates[parent.CandidateIndex] == c;

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 47700)]
    private bool CanOfferShiDz(ProgramTriggerLegalityContext q)
    {
        if (q.SkillId != ShiDzYingjia) return true;
        if (q.Trigger.Id == "issued-extra-start") return ShiDzPendingExtra(q.OwnerSeat) is not null;
        return ShiDzSameNameMaximum(q.OwnerSeat) >= 2 && GetHand(_players[q.OwnerSeat]).Any(card =>
            !IsForeignEquipmentDiscardPrevented(q.OwnerSeat, card, CardLocation.Hand(q.OwnerSeat), OwnedCardMoveIntent.Discard));
    }

    private SkillProgramStepOutcome BeginShiDz(ProgramSkillFrame input, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.MobileShiDongZhao is not null || f.InstructionIndex != 1 || f.WindowContext is not { } w)
            throw new InvalidOperationException("Shi Dong Zhao requires the original unstarted native trigger.");
        var grant = f.TriggerId == "issued-extra-start" ? ShiDzPendingExtra(f.OwnerSeat) ??
            throw new InvalidOperationException("Yingjia lost the exact pending extra-turn grant.") : null;
        var stage = grant is not null ? MobileShiDongZhaoStage.ExtraDrawChildren : f.TriggerId == "opening-two" ?
            MobileShiDongZhaoStage.GainChildren : op == SkillProgramEffectOp.ResolveMobileShiDongZhaoMiaolue ?
            MobileShiDongZhaoStage.Benefit : MobileShiDongZhaoStage.Payment;
        var r = new MobileShiDongZhaoReceipt(grant?.Source ?? ShiDzSource(f), f.GameplayHash, w.ParentFrameId, _turnNumber, stage)
            { Before = _movementSequence, After = _movementSequence, GrantFrameId = grant?.FrameId ?? 0,
                TargetSeat = grant?.TargetSeat ?? -1, Branch = f.TriggerId == "opening-two" ? "opening" : grant is not null ? "extra" : null };
        ReplaceRuntimeTop(f = f with { MobileShiDongZhao = r });
        AdvanceEventRulesAndQueueFact(new MobileShiDongZhaoStartedEvent(f.Id, r.Source, r.GameplayHash, r.WindowId, r.ActualTurn, f.TriggerId!));
        if (stage is MobileShiDongZhaoStage.Benefit or MobileShiDongZhaoStage.Payment) { PublishShiDz(f); return SkillProgramStepOutcome.AwaitChoice; }
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (grant is not null)
        {
            AdvanceEventRulesAndQueueFact(new MobileShiDongZhaoExtraEnteredEvent(grant.FrameId, f.Id, f.OwnerSeat, _turnNumber + 1));
            DrawProgramCards(f.Id, f.OwnerSeat, 2, null, null, SkillProgramCardSetVisibility.Private, new(ShiDzDraw));
        }
        else CreateMobileShiMantianGuohaiCards(f.OwnerSeat);
        SealShiDz(f.Id);
        ContinueShiDz(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }

    private IReadOnlyList<PromptChoice> ShiDzChoices(ProgramSkillFrame f)
    {
        var r = f.MobileShiDongZhao!;
        PromptChoice C(string branch, string label, int? target = null) => new(new($"shi-dong-zhao.{f.Id}.{r.Stage}.{branch}.{target ?? -1}"), label,
            [], target is { } seat ? [seat] : [], new Dictionary<string, string> { ["program-action"] = "mobile-shi-dong-zhao", ["branch"] = branch });
        if (r.Stage == MobileShiDongZhaoStage.Benefit) return [C("draw", "摸两张牌"), C("DrawTwo", "获得一张【无中生有】"),
            C("Dismantlement", "获得一张【过河拆桥】"), C("Nullification", "获得一张【无懈可击】")];
        if (r.Stage == MobileShiDongZhaoStage.Target) return Array.AsReadOnly(_players.Where(p => p.IsAlive)
            .Select(p => C("target", "令 " + p.Name + " 执行额外回合（开始时摸两张牌）", p.Seat)).ToArray());
        if (r.Stage != MobileShiDongZhaoStage.Payment) return [];
        return Array.AsReadOnly(ShiDzPayments(f).Select(c => c with { Parameters = new Dictionary<string, string>(c.Parameters)
            { ["program-action"] = "mobile-shi-dong-zhao", ["branch"] = "payment" } }).ToArray());
    }
    private void PublishShiDz(ProgramSkillFrame f)
    {
        var choices = ShiDzChoices(f);
        if (choices.Count == 0 || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishShiDz(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, rText(f.MobileShiDongZhao!.Stage),
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
            { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        static string rText(MobileShiDongZhaoStage stage) => stage == MobileShiDongZhaoStage.Benefit ? "妙略：摸两张牌，或指定一种智囊牌名。" :
            stage == MobileShiDongZhaoStage.Payment ? "迎驾：弃置一张手牌。" : "迎驾：选择额外回合的执行者。";
    }

    [ProgramAnswer("mobile-shi-dong-zhao")]
    private void AnswerShiDz(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiDongZhao: { } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !ShiDzChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Shi Dong Zhao lost the exact current benefit, private payment or target choice.");
        ClearPendingDecision(); var branch = choice.Parameters["branch"];
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (r.Stage == MobileShiDongZhaoStage.Target)
        {
            var target = choice.Targets.Single(); PendProgramExtraTurn(f, target);
            AdvanceEventRulesAndQueueFact(new MobileShiDongZhaoExtraGrantedEvent(f.Id, r.Source, r.GameplayHash, target, _turnNumber));
            FinishShiDz(GetActiveProgramFrame(f.Id)); return;
        }
        r = r with { Branch = branch, Before = _movementSequence, After = _movementSequence,
            Stage = r.Stage == MobileShiDongZhaoStage.Payment ? MobileShiDongZhaoStage.CostChildren : MobileShiDongZhaoStage.GainChildren };
        ReplaceRuntimeTop(f = f with { MobileShiDongZhao = r, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (branch == "payment")
        {
            var id = choice.Cards.Single(); ReplaceRuntimeTop(f = f with { MobileShiDongZhao = r with { CardId = id } });
            MoveProgramLogicalDiscard(f, f.OwnerSeat, [id], new(ShiDzDiscard)); SealShiDz(f.Id);
            var after = GetActiveProgramFrame(f.Id).MobileShiDongZhao!;
            AdvanceEventRulesAndQueueFact(new MobileShiDongZhaoCostPaidEvent(f.Id, id, after.Before, after.After));
        }
        else if (branch == "draw") { DrawProgramCards(f.Id, f.OwnerSeat, 2, null, null, SkillProgramCardSetVisibility.Private, new(ShiDzDraw)); SealShiDz(f.Id); }
        else
        {
            var kind = Enum.Parse<CardKind>(branch, false);
            var card = _cardZones.CardsAt(CardLocation.DrawPile).Reverse().FirstOrDefault(c => c.Kind == kind) ??
                _cardZones.CardsAt(CardLocation.DiscardPile).FirstOrDefault(c => c.Kind == kind);
            if (card is not null)
            {
                ReplaceRuntimeTop(f = f with { MobileShiDongZhao = r with { CardId = card.Id } });
                MoveCard(card, _cardZones.GetLocation(card.Id), CardLocation.Hand(f.OwnerSeat), new(ShiDzGain));
            }
            SealShiDz(f.Id);
        }
        if (branch != "payment")
        {
            var after = GetActiveProgramFrame(f.Id).MobileShiDongZhao!;
            AdvanceEventRulesAndQueueFact(new MobileShiDongZhaoBenefitEvent(f.Id, branch, after.CardId, after.Before, after.After));
        }
        ContinueShiDz(f.Id);
    }

    private void SealShiDz(long id)
    { var f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f with { MobileShiDongZhao = f.MobileShiDongZhao! with { After = _movementSequence } }); }
    private void ContinueShiDz(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiDongZhao: { } r } f || f.Id != id || _pendingDecision is not null) return;
        if (r.Stage is MobileShiDongZhaoStage.Benefit or MobileShiDongZhaoStage.Payment or MobileShiDongZhaoStage.Target) { PublishShiDz(f); return; }
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return;
        ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { PendingMovementContinuation = null });
        if (r.Stage == MobileShiDongZhaoStage.CostChildren && _players[f.OwnerSeat].IsAlive && _winner == Winner.None)
        { ReplaceRuntimeTop(f = f with { MobileShiDongZhao = f.MobileShiDongZhao! with { Stage = MobileShiDongZhaoStage.Target } }); PublishShiDz(f); return; }
        FinishShiDz(f);
    }
    private void FinishShiDz(ProgramSkillFrame f)
    {
        ReplaceRuntimeTop(f = f with { MobileShiDongZhao = f.MobileShiDongZhao! with { Stage = MobileShiDongZhaoStage.Finished }, PendingMovementContinuation = null });
        FinishProgramSkill(f, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 47700)]
    private bool ResumeShiDz(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiDongZhao: not null } f || f.Id != id) return false; ContinueShiDz(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 47700)]
    private bool ReturnShiDz(ProgramSkillFrame f)
    { if (f.MobileShiDongZhao is null) return false; ContinueShiDz(f.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 47700)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 47700)]
    private bool PaidShiDzCanReturn(ProgramSkillFrame f) => f.MobileShiDongZhao?.Stage is MobileShiDongZhaoStage.GainChildren or
        MobileShiDongZhaoStage.CostChildren or MobileShiDongZhaoStage.Target or MobileShiDongZhaoStage.ExtraDrawChildren ||
        f.MobileShiDongZhao is null && f.InstructionIndex is 0 or 1 && f.TriggerId == "issued-extra-start" && f.WindowContext is { } w &&
        IsIssuedShiDzExtra(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, 100), w);
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 47700)]
    private bool ShiDzMovementProof(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation movement) =>
        f.MobileShiDongZhao is { Stage: MobileShiDongZhaoStage.GainChildren or MobileShiDongZhaoStage.CostChildren or MobileShiDongZhaoStage.ExtraDrawChildren } &&
        effect?.Op is SkillProgramEffectOp.ResolveMobileShiDongZhaoMiaolue or SkillProgramEffectOp.ResolveMobileShiDongZhaoYingjia;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 47700)]
    private void AssertShiDz(ProgramSkillFrame f)
    {
        if (f.MobileShiDongZhao is not { } r) return;
        var signed = ProgramEventHistory<MobileShiDongZhaoStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source &&
            e.GameplayHash == r.GameplayHash && e.WindowId == r.WindowId && e.ActualTurn == r.ActualTurn && e.TriggerId == f.TriggerId) == 1;
        if (!signed || r.GameplayHash != f.GameplayHash || f.WindowContext?.ParentFrameId != r.WindowId || r.ActualTurn != _turnNumber ||
            !Enum.IsDefined(r.Stage) || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            f.TriggerId != "issued-extra-start" && r.Source != ShiDzSource(f) ||
            f.TriggerId == "issued-extra-start" && (r.GrantFrameId <= 0 || !ProgramEventHistory<MobileShiDongZhaoExtraEnteredEvent>().Any(e => e.GrantFrameId == r.GrantFrameId && e.FrameId == f.Id && e.TargetSeat == f.OwnerSeat)))
            throw new InvalidOperationException("Shi Dong Zhao lost its accepted original trigger, paid movement or exact extra-turn lease.");
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileShiDongZhaoMiaolue, SkillProgramEffectOp.ResolveMobileShiDongZhaoYingjia)]
    private PromptChoice ChooseShiDzAi(PendingDecision p, ProgramSkillFrame f) => f.MobileShiDongZhao!.Stage switch
    {
        MobileShiDongZhaoStage.Benefit => p.Choices.First(c => c.Parameters["branch"] == "draw"),
        MobileShiDongZhaoStage.Payment => p.Choices.OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[f.OwnerSeat])).First(),
        _ => p.Choices.OrderByDescending(c => AreProgramDistributionAllies(_players[f.OwnerSeat], _players[c.Targets.Single()]))
            .ThenByDescending(c => _players[c.Targets.Single()].Hp).First()
    };

    private IReadOnlyList<ProgramPublicRuleStateSnapshot> MobileShiDongZhaoPublicStates(CharacterState owner, string skillId)
    {
        if (skillId != ShiDzYingjia) return [];
        return Array.AsReadOnly(GetSkillBindingShard(owner).ProgramInstances.Where(i => i.SkillId == skillId).SelectMany(i => new[] {
            new ProgramPublicRuleStateSnapshot(i.SkillInstanceId, "same-tricks-ended", ProgramPublicRuleStateKind.ActivationLimit, 1,
                _skillRuntimeState.GetUsage(owner.Seat, skillId, "same-tricks-ended@" + i.SkillInstanceId, SkillUsageScope.Game), SkillUsageScope.Game),
            new ProgramPublicRuleStateSnapshot(i.SkillInstanceId, "same-name-tricks", ProgramPublicRuleStateKind.ActivationLimit, 2,
                ShiDzSameNameMaximum(owner.Seat), SkillUsageScope.Turn) }).ToArray());
    }
}
