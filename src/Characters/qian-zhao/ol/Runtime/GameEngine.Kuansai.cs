namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramTargetWindow(ProgramTargetWindowStage.NormalizeImplicitDrawTwo, 120)]
    private bool KuansaiNormalizeImplicitDrawTwoTargetWindow(ProgramTargetCardWindowQuery query) =>
        HasKuansaiCapability;


    private const string KuansaiGiftReason = "skill-program.qian-zhao.kuansai.gift";
    private bool HasKuansaiCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveKuansaiTargetChoice);
    private CardUseFrame? KuansaiUse(ProgramSkillWindowContext context) => context is
        { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } c } && LifecycleCardUse(c.ParentCardUseFrameId) is
        { Action: { Type: CardActionType.Use } action } use && action.ActionId == c.CardActionId && action.EffectiveKind == use.CardKind &&
        !EquipmentCatalog.IsEquipment(use.CardKind) && _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Any(w =>
            w.Id == context.ParentFrameId && w.ParentFrameId == use.Id && w.Action.ActionId == action.ActionId) ? use : null;
    private static IReadOnlyList<int> KuansaiActualTargets(CardUseFrame use) => Array.AsReadOnly(use.Action!.EffectiveDesignatedTargetSeats.Distinct().ToArray());
    private bool CanKuansaiRecover(int owner) => _players[owner].IsAlive && _players[owner].HasHpValue && _players[owner].Hp < _players[owner].MaxHp;
    private IReadOnlyList<Card> KuansaiGiftCards(int chooser, int owner) => chooser == owner || !_players[chooser].IsAlive || !_players[owner].IsAlive ? [] :
        Array.AsReadOnly(GetHand(_players[chooser]).Concat(GetEquipment(chooser)).Where(c => !c.IsGeneralWeapon &&
            !IsForeignEquipmentDiscardPrevented(chooser, c, _cardZones.GetLocation(c.Id), OwnedCardMoveIntent.Transfer)).ToArray());
    private bool KuansaiHasChoice(int target, int owner) => IsValidPlayerSeat(target) && _players[target].IsAlive &&
        (CanKuansaiRecover(owner) || KuansaiGiftCards(target, owner).Count > 0);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 26620)]
    private bool QualifyQianZhao(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context)
    {
        if (t.Effects.Any(e => e.Op == SkillProgramEffectOp.OfferWeifuCategoryTargets))
            return HasIssuedWeifuCandidate(c, context) && LifecycleCardUse(context.CardUse!.ParentCardUseFrameId) is { WeifuUse.Offered: false } use && WeifuCanAddTargets(use);
        if (!t.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveKuansaiTargetChoice)) return true;
        return HasKuansaiCapability && _players[c.OwnerSeat].IsAlive && _players[c.OwnerSeat].HasHpValue && KuansaiUse(context) is { } native &&
            KuansaiActualTargets(native).Count >= _players[c.OwnerSeat].Hp && KuansaiActualTargets(native).Any(s => KuansaiHasChoice(s, c.OwnerSeat));
    }
    private SkillProgramStepOutcome BeginKuansai(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SkillId != "ol:kuansai" || f.Kuansai is not null || f.InstructionIndex != 1 || f.TriggerId is null || f.WindowContext is not { } context ||
            KuansaiUse(context) is not { } use || !_players[f.OwnerSeat].IsAlive || !_players[f.OwnerSeat].HasHpValue)
            throw new InvalidOperationException("Kuansai requires its actual finalized-use observer activation.");
        var targets = KuansaiActualTargets(use);
        if (targets.Count < _players[f.OwnerSeat].Hp) throw new InvalidOperationException("Kuansai cannot replace the actual target-count threshold.");
        var r = new KuansaiReceipt(QianZhaoSource(f), f.GameplayHash, f.InstructionIndex, _turnNumber, _turnProgression.OwnerSeat,
            use.Id, use.Action!.ActionId, _players[f.OwnerSeat].Hp) { Targets = targets };
        ReplaceRuntimeTop(f = f with { Kuansai = r });
        AdvanceEventRulesAndQueueFact(new KuansaiStartedEvent(f.Id, r.Source, r.GameplayHash, r.ActualTurn, r.TurnOwnerSeat, use.Id, r.ActionId, r.ThresholdHp, targets.Count));
        PublishQianZhaoChoice(f.OwnerSeat, f, KuansaiChoices(f), "款塞：选择此牌的一个实际目标。", false);
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> KuansaiChoices(ProgramSkillFrame f)
    {
        var r = f.Kuansai!; var result = new List<PromptChoice>();
        Dictionary<string, string> P(string branch) => new() { ["program-action"] = "qian-zhao", ["branch"] = branch };
        if (r.Stage == KuansaiStage.Target)
        {
            foreach (var seat in r.Targets.Where(s => KuansaiHasChoice(s, f.OwnerSeat))) result.Add(new(new($"kuansai.{f.Id}.target.{seat}"),
                "令" + _players[seat].Name + "选择款塞的效果", [], [seat], P("kuansai-target")));
        }
        else if (r.Stage == KuansaiStage.Benefit && r.ChooserSeat is { } chooser)
        {
            foreach (var card in KuansaiGiftCards(chooser, f.OwnerSeat)) result.Add(new(new($"kuansai.{f.Id}.gift.{card.Id}"),
                "交给" + _players[f.OwnerSeat].Name + "【" + card.DisplayName + "】", [card.Id], [], P("kuansai-gift")));
            if (CanKuansaiRecover(f.OwnerSeat)) result.Add(new(new($"kuansai.{f.Id}.recover"), "令" + _players[f.OwnerSeat].Name + "回复1点体力", [], [], P("kuansai-recover")));
        }
        return Array.AsReadOnly(result.ToArray());
    }
    private void PayKuansai(ProgramSkillFrame f, PromptChoice selected)
    {
        var r = f.Kuansai!; var chooser = r.ChooserSeat!.Value; var gift = selected.Parameters["branch"] == "kuansai-gift";
        var card = gift ? GetAdvancedCard(selected.Cards.Single()) : null;
        var from = card is null ? (CardLocation?)null : _cardZones.GetLocation(card.Id);
        ReplaceRuntimeTop(f = f with { Kuansai = r with { Stage = KuansaiStage.Children, Gift = gift, CardId = card?.Id, From = from, Before = _movementSequence, After = _movementSequence },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        var before = _movementSequence;
        if (card is not null) MoveCard(card, from!.Value, CardLocation.Hand(f.OwnerSeat), new(KuansaiGiftReason));
        else if (CanKuansaiRecover(f.OwnerSeat))
        {
            if (!TryQueueRecoveryReplacement(f.Id, f.OwnerSeat, f.OwnerSeat, 1, new(RecoveryAttemptProducer.Program, f.InstructionIndex)))
            {
                var recovery = BeginRecovery(f.Id, f.OwnerSeat, f.OwnerSeat, 1);
                var owner = _players[f.OwnerSeat]; owner.Hp = Math.Min(owner.MaxHp, owner.Hp + 1);
                AdvanceEventRulesAndQueueFact(new RecoveryAppliedEvent(f.OwnerSeat, f.OwnerSeat, 1, owner.Hp));
                PopResolutionFrame(recovery, ResolutionFrameKind.Recovery);
            }
        }
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f = f with { Kuansai = f.Kuansai! with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new KuansaiPaidEvent(f.Id, chooser, gift, card?.Id, from, before, _movementSequence));
        AdvanceRuntimeProgram(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 26621)]
    private bool ResumeKuansai(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Kuansai: { } r } f || f.Id != id) return false;
        AssertQianZhao(f);
        if (r.Stage == KuansaiStage.Children)
        { if (DrainQianZhaoChildren(id)) return true; FinishQianZhao(GetActiveProgramFrame(id)); return true; }
        var choices = KuansaiChoices(f);
        if (choices.Count == 0 || _winner != Winner.None || !_players[f.OwnerSeat].IsAlive) { FinishQianZhao(f); return true; }
        if (_pendingDecision is null) PublishQianZhaoChoice(r.ChooserSeat ?? f.OwnerSeat, f, choices,
            r.Stage == KuansaiStage.Target ? "款塞：选择此牌的一个实际目标。" : "款塞：交给牵招一张牌，或令其回复1点体力。", r.Stage == KuansaiStage.Benefit);
        return true;
    }
    [ProgramAnswer("qian-zhao")]
    private void AnswerQianZhao(PromptChoice selected)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Qian Zhao lost its owning choice frame.");
        AssertQianZhao(f);
        var choices = f.WeifuTargets is not null ? WeifuTargetChoices(f) : KuansaiChoices(f);
        var chooser = f.Kuansai is { Stage: KuansaiStage.Benefit, ChooserSeat: { } seat } ? seat : f.OwnerSeat;
        if (_pendingDecision?.PlayerSeat != chooser || !choices.Any(c => AssistedChoicesEqual([c], [selected])))
            throw new InvalidOperationException("Qian Zhao requires the exact actual participant's published choice.");
        ClearPendingDecision();
        if (f.WeifuTargets is { } targets)
        {
            if (selected.Targets.Count == 0) { CompleteWeifuTargets(f); return; }
            ReplaceRuntimeTop(f = f with { WeifuTargets = targets with { Selected = targets.Selected.Concat(selected.Targets).ToArray() } });
            var next = WeifuTargetChoices(f);
            if (next.Count == 1) CompleteWeifuTargets(f);
            else PublishQianZhaoChoice(f.OwnerSeat, f, next, "威抚：继续选择额外目标，或完成选择。", false);
            return;
        }
        var r = f.Kuansai!;
        if (r.Stage == KuansaiStage.Target)
        {
            var target = selected.Targets.Single();
            ReplaceRuntimeTop(f = f with { Kuansai = r with { Stage = KuansaiStage.Benefit, ChooserSeat = target } });
            AdvanceEventRulesAndQueueFact(new KuansaiTargetChosenEvent(f.Id, target));
            _ = ResumeKuansai(f.Id); return;
        }
        PayKuansai(f, selected);
    }
    [ProgramAiChoice(SkillProgramEffectOp.OfferWeifuCategoryTargets, SkillProgramEffectOp.ResolveKuansaiTargetChoice)]
    private PromptChoice ChooseQianZhaoAi(PendingDecision p, ProgramSkillFrame f)
    {
        if (f.WeifuTargets is not null)
        {
            var use = RequireWeifuTargetUse(f); var view = CreateSnapshot(f.OwnerSeat);
            var draw = use.CardKind is CardKind.DrawTwo or CardKind.Peach or CardKind.Alcohol;
            var hint = draw ? new SkillProgramAiHint(0, 0, 0, 1, 0, 0, false, false) : new(0, 0, 0, 0, 0, 1, false, false);
            return p.Choices.Select(c => (Choice: c, Score: c.Targets.Count == 0 ? 0 : _aiBrains[f.OwnerSeat].ScoreProgramTarget(view, c.Targets[0], hint)))
                .OrderByDescending(c => c.Score).ThenBy(c => c.Choice.Id.Value, StringComparer.Ordinal).First().Choice;
        }
        if (f.Kuansai is { Stage: KuansaiStage.Target })
            return p.Choices.OrderByDescending(c => KuansaiGiftCards(c.Targets.Single(), f.OwnerSeat).Count).ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
        return p.Choices.OrderByDescending(c => c.Parameters.GetValueOrDefault("branch") == "kuansai-recover").ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
    }
}
