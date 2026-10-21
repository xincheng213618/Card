namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MubingReveal = "mubing-public-reveal";
    private CardConversionSource SpZhangLiaoSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    [ProgramCapability]
    private sealed class SpZhangLiaoCapability(GameEngine engine) : ISpZhangLiaoProgramHost
    { public SkillProgramStepOutcome Execute(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginSpZhangLiao(f, op); }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 26070)]
    private bool CanOfferSpZhangLiao(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context)
    {
        if (t.Effects.Any(e => e.Op == SkillProgramEffectOp.AwakenMubing))
            return !HasDiaolingAwakened(c.OwnerSeat) && (MubingMilitaryCount(c.OwnerSeat) >= 6 ||
                HasShenGuoJiaAwakeningCondition(c.OwnerSeat, c.SkillId, c.BindingId));
        if (!t.Effects.Any(e => e.Op == SkillProgramEffectOp.PreventDamageForHighestRankGift)) return true;
        return context is { SourceSeat: { } source, TargetSeat: { } target } && source == c.OwnerSeat && target != source &&
            _players[target].IsAlive && !ProgramEventHistory<ZiquDamagePreventedEvent>().Any(e => e.Source.OwnerSeat == source && e.VictimSeat == target);
    }

    private SkillProgramStepOutcome BeginSpZhangLiao(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (op == SkillProgramEffectOp.AwakenMubing)
        {
            if (HasDiaolingAwakened(f.OwnerSeat) || MubingMilitaryCount(f.OwnerSeat) < 6 &&
                !HasShenGuoJiaAwakeningCondition(f.OwnerSeat, f.SkillId, f.TriggerId)) throw new InvalidOperationException("Diaoling has no six-card acquisition receipt or its exact Huishi awakening grant.");
            AdvanceEventRulesAndQueueFact(new DiaolingAwakenedEvent(f.Id, f.OwnerSeat, MubingMilitaryCount(f.OwnerSeat)));
            return SkillProgramStepOutcome.Continue;
        }
        if (f.SpZhangLiao is not null || f.WindowContext is not { } context) throw new InvalidOperationException("The rank exchange requires one owning native program frame.");
        var ziqu = op == SkillProgramEffectOp.PreventDamageForHighestRankGift;
        var r = new SpZhangLiaoReceipt(SpZhangLiaoSource(f), f.GameplayHash, f.InstructionIndex, context.ParentFrameId,
            ziqu ? SpZhangLiaoStage.HighestCard : SpZhangLiaoStage.Payment, HasDiaolingAwakened(f.OwnerSeat), VictimSeat: ziqu ? context.TargetSeat : null,
            SequenceBefore: _movementSequence, SequenceAfter: _movementSequence);
        ReplaceRuntimeTop(f = f with { SpZhangLiao = r });
        if (ziqu)
        {
            PreventProgramCurrentDamage(f);
            AdvanceEventRulesAndQueueFact(new ZiquDamagePreventedEvent(f.Id, r.Source, r.VictimSeat!.Value, r.ParentFrameId));
        }
        else
        {
            RevealProgramTopCards(f.Id, f.OwnerSeat, r.Upgraded ? 4 : 3, null, MubingReveal, SkillProgramCardSetVisibility.Public);
            f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { SpZhangLiao = f.SpZhangLiao! with { SequenceAfter = _movementSequence } });
        }
        ContinueSpZhangLiao(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private bool DrainSpZhangLiaoChildren(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);

    private void ContinueSpZhangLiao(long id)
    {
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame { SpZhangLiao: { } r } f && f.Id == id)
        {
            if (DrainSpZhangLiaoChildren(id)) return;
            f = GetActiveProgramFrame(id); r = f.SpZhangLiao!;
            if (r.Stage is SpZhangLiaoStage.HighestChildren or SpZhangLiaoStage.CleanupChildren)
            { ReplaceRuntimeTop(f with { SpZhangLiao = null }); AdvanceRuntimeProgram(id); return; }
            if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None)
            { CleanupSpZhangLiao(f); return; }
            switch (r.Stage)
            {
                case SpZhangLiaoStage.PaymentChildren:
                    ReplaceRuntimeTop(f = f with { SpZhangLiao = r with { Stage = SpZhangLiaoStage.Obtain, Selected = [] } });
                    break;
                case SpZhangLiaoStage.ObtainChildren:
                    if (!r.Upgraded) { CleanupSpZhangLiao(f); return; }
                    ReplaceRuntimeTop(f = f with { SpZhangLiao = r with { Stage = SpZhangLiaoStage.GiftCards, Selected = [] } });
                    break;
                case SpZhangLiaoStage.GiftChildren:
                    ReplaceRuntimeTop(f = f with { SpZhangLiao = r with { Stage = SpZhangLiaoStage.GiftCards, Selected = [] } });
                    break;
            }
            PublishSpZhangLiao(f); return;
        }
    }

    private IReadOnlyList<PromptChoice> SpZhangLiaoChoices(ProgramSkillFrame f)
    {
        var r = f.SpZhangLiao!;
        PromptChoice Choice(string action, string label, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) =>
            new(new($"sp-zhang-liao.{f.Id}.{r.Stage}.{action}.{string.Join('.', cards ?? [])}.{string.Join('.', targets ?? [])}"), label,
                cards ?? [], targets ?? [], new Dictionary<string, string> { ["program-action"] = "sp-zhang-liao", ["option"] = action });
        var result = new List<PromptChoice>();
        if (r.Stage == SpZhangLiaoStage.HighestCard)
        {
            var victim = _players[r.VictimSeat!.Value];
            var cards = GetHand(victim).Concat(GetEquipment(victim)).ToArray();
            var max = cards.Length == 0 ? 0 : cards.Max(c => c.Rank);
            return Array.AsReadOnly(cards.Where(c => c.Rank == max).Select(c => Choice("highest", $"交出【{c.DisplayName}】 {c.RankText}", [c.Id])).ToArray());
        }
        if (r.Stage == SpZhangLiaoStage.GiftTarget)
            return Array.AsReadOnly(_players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat).Select(p => Choice("recipient", $"交给 {p.Name}", targets: [p.Seat])).ToArray());
        IEnumerable<int> available = r.Stage switch {
            SpZhangLiaoStage.Payment => GetHand(_players[f.OwnerSeat]).Where(c => !IsSelfHandCategoryDiscardForbidden(f.OwnerSeat, c, CardLocation.Hand(f.OwnerSeat), OwnedCardMoveIntent.Discard)).Select(c => c.Id),
            SpZhangLiaoStage.Obtain => MubingAvailableReveal(f),
            SpZhangLiaoStage.GiftCards => MubingAvailableGifts(f), _ => [] };
        foreach (var id in available)
        {
            var c = GetAdvancedCard(id); var picked = r.Selected.Contains(id);
            if (r.Stage == SpZhangLiaoStage.Obtain && !picked && r.SpentRank + c.Rank > r.Budget) continue;
            result.Add(Choice("card", $"{(picked ? "取消" : "选择")}【{c.DisplayName}】 {c.RankText}", [id]));
        }
        if (r.Stage != SpZhangLiaoStage.GiftCards || r.Selected.Count > 0 && _players.Any(p => p.IsAlive && p.Seat != f.OwnerSeat))
            result.Add(Choice("confirm", r.Stage == SpZhangLiaoStage.Payment ? $"弃置所选手牌（点数和 {r.Selected.Sum(id => GetAdvancedCard(id).Rank)}）" :
                r.Stage == SpZhangLiaoStage.Obtain ? $"获得所选牌（点数和 {r.SpentRank} / {r.Budget}）" : "分配所选牌", r.Selected));
        if (r.Stage == SpZhangLiaoStage.GiftCards) result.Add(Choice("finish", "保留其余牌，结束募兵"));
        return Array.AsReadOnly(result.ToArray());
    }

    private void PublishSpZhangLiao(ProgramSkillFrame f)
    {
        var r = f.SpZhangLiao!; var choices = SpZhangLiaoChoices(f);
        if (choices.Count == 0)
        {
            if (r.Stage == SpZhangLiaoStage.HighestCard)
            { ReplaceRuntimeTop(f with { SpZhangLiao = r with { Stage = SpZhangLiaoStage.HighestChildren } }); ContinueSpZhangLiao(f.Id); }
            else CleanupSpZhangLiao(f);
            return;
        }
        var chooser = r.Stage == SpZhangLiaoStage.HighestCard ? r.VictimSeat!.Value : f.OwnerSeat;
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        var skill = _contentRegistry.GetSkill(f.SkillId);
        var description = r.Upgraded && f.SkillId == "ol:mubing"
            ? "出牌阶段开始时，你可以亮出牌堆顶四张牌，然后你可以弃置任意张手牌，获得任意张亮出的牌（弃置牌的点数之和不得小于获得牌的点数之和），然后你可以将以此法获得的牌以任意方式交给其他角色。"
            : skill.Description;
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, $"{skill.Name}：{r.Stage switch { SpZhangLiaoStage.Payment => "选择要弃置的手牌", SpZhangLiaoStage.Obtain => "选择要获得的亮出牌", SpZhangLiaoStage.GiftCards => "分配本次获得的牌或保留", SpZhangLiaoStage.GiftTarget => "选择收牌角色", _ => "交出一张点数最大的牌" }}",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(skill.Id, skill.Name,
            r.Upgraded && f.SkillId == "ol:mubing" ? "募兵 · 调令后" : skill.Name, description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    [ProgramAnswer("sp-zhang-liao")]
    private void AnswerSpZhangLiao(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { SpZhangLiao: { } r } f ||
            !AssistedChoicesEqual([choice], SpZhangLiaoChoices(f).Where(c => c.Id == choice.Id).ToArray()) ||
            _pendingDecision?.PlayerSeat != (r.Stage == SpZhangLiaoStage.HighestCard ? r.VictimSeat : f.OwnerSeat))
            throw new InvalidOperationException("The rank exchange lost its actual chooser, physical source or budget.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var option = choice.Parameters["option"];
        if (option == "card")
        {
            var id = choice.Cards.Single(); var selected = r.Selected.Contains(id) ? r.Selected.Where(c => c != id).ToArray() : r.Selected.Append(id).ToArray();
            ReplaceRuntimeTop(f with { SpZhangLiao = r with { Selected = selected, SpentRank = r.Stage == SpZhangLiaoStage.Obtain ? selected.Sum(c => GetAdvancedCard(c).Rank) : r.SpentRank } });
        }
        else if (option == "confirm" && r.Stage == SpZhangLiaoStage.Payment)
        {
            var budget = r.Selected.Sum(id => GetAdvancedCard(id).Rank);
            ReplaceRuntimeTop(f = f with { SpZhangLiao = r with { Stage = SpZhangLiaoStage.PaymentChildren, Budget = budget, SequenceBefore = _movementSequence } });
            MoveSpZhangLiaoCards(f.Id, r.Selected, CardLocation.DiscardPile, "skill-program.ol:mubing.DiscardSelected");
        }
        else if (option == "confirm" && r.Stage == SpZhangLiaoStage.Obtain)
        {
            var ids = r.Selected; var military = ids.Count(id => IsMubingMilitaryCard(GetAdvancedCard(id))); var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { SpZhangLiao = r with { Stage = SpZhangLiaoStage.ObtainChildren, Obtained = ids, Selected = [], SequenceBefore = before } });
            MoveSpZhangLiaoCards(f.Id, ids, CardLocation.Hand(f.OwnerSeat), "skill-program.ol:mubing.ObtainSelected");
            var actual = _cardMovements.Where(m => m.Sequence > before && m.To == CardLocation.Hand(f.OwnerSeat) && m.From == CardLocation.Processing && ids.Contains(m.CardId)).ToArray();
            if (actual.Length != ids.Count) throw new InvalidOperationException("Mubing did not acquire its exact paid physical set.");
            AdvanceEventRulesAndQueueFact(new MubingCardsAcquiredEvent(f.Id, r.Source, ids.Count, military, r.Budget, r.SpentRank, before, _movementSequence));
        }
        else if (option == "confirm" && r.Stage == SpZhangLiaoStage.GiftCards)
            ReplaceRuntimeTop(f with { SpZhangLiao = r with { Stage = SpZhangLiaoStage.GiftTarget } });
        else if (option == "recipient")
        {
            var recipient = choice.Targets.Single();
            if (r.Selected.Any(id => !MubingAvailableGifts(f).Contains(id))) throw new InvalidOperationException("Only this acquisition's remaining physical cards may be gifted.");
            ReplaceRuntimeTop(f with { SpZhangLiao = r with { Stage = SpZhangLiaoStage.GiftChildren,
                Obtained = r.Obtained.Where(id => !r.Selected.Contains(id)).ToArray() } });
            MoveSpZhangLiaoCards(f.Id, r.Selected, CardLocation.Hand(recipient), "skill-program.ol:mubing.GiveSelected");
        }
        else if (option == "highest")
        {
            ReplaceRuntimeTop(f with { SpZhangLiao = r with { Stage = SpZhangLiaoStage.HighestChildren, Selected = choice.Cards } });
            MoveSpZhangLiaoCards(f.Id, choice.Cards, CardLocation.Hand(f.OwnerSeat), "skill-program.ol:ziqu.GiveSelected");
        }
        else if (option == "finish") { CleanupSpZhangLiao(f); return; }
        else throw new InvalidOperationException("Unknown rank exchange answer.");
        ContinueSpZhangLiao(f.Id);
    }

    private void CleanupSpZhangLiao(ProgramSkillFrame f)
    {
        if (f.SpZhangLiao!.Stage != SpZhangLiaoStage.CleanupChildren)
        {
            var remaining = f.CardSetBindings.Any(b => b.Name == MubingReveal) ? MubingAvailableReveal(f) : [];
            ReplaceRuntimeTop(f with { SpZhangLiao = f.SpZhangLiao with { Stage = SpZhangLiaoStage.CleanupChildren, Selected = [] } });
            MoveSpZhangLiaoCards(f.Id, remaining, CardLocation.DiscardPile, "skill-program.ol:mubing.reveal-cleanup");
        }
        ContinueSpZhangLiao(f.Id);
    }

    private void MoveSpZhangLiaoCards(long id, IReadOnlyList<int> cards, CardLocation destination, string reason)
    {
        var f = GetActiveProgramFrame(id);
        ReplaceRuntimeTop(f with { SpZhangLiao = f.SpZhangLiao! with { SequenceBefore = _movementSequence, SequenceAfter = _movementSequence } });
        if (destination == CardLocation.DiscardPile && reason == "skill-program.ol:mubing.DiscardSelected")
            MoveProgramLogicalDiscard(f, f.OwnerSeat, cards, new(reason));
        else MoveProgramCardsFromMultipleSources(cards, destination, new(reason), (f.SpZhangLiao!.Stage is SpZhangLiaoStage.HighestChildren or SpZhangLiaoStage.GiftChildren ? NativeGiveCompletionRecords(f.Id,f.SpZhangLiao!.Stage==SpZhangLiaoStage.HighestChildren?f.SpZhangLiao.VictimSeat!.Value:f.OwnerSeat,destination.OwnerSeat!.Value,cards,_movementSequence,null) : null));
        f = GetActiveProgramFrame(id);
        ReplaceRuntimeTop(f with { SpZhangLiao = f.SpZhangLiao! with { SequenceAfter = _movementSequence } });
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 26070)]
    private bool ResumeSpZhangLiao(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { SpZhangLiao: not null } f || f.Id != id) return false; ContinueSpZhangLiao(id); return true; }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 26070)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 26070)]
    private bool ContinueIssuedSpZhangLiao(ProgramSkillFrame f) => f.SpZhangLiao is not null;

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 26070)]
    private void AssertSpZhangLiao(ProgramSkillFrame f)
    {
        if (f.SpZhangLiao is not { } r) return;
        if (r.Source != SpZhangLiaoSource(f) || r.GameplayHash != f.GameplayHash || r.InstructionIndex != f.InstructionIndex ||
            r.ParentFrameId != f.WindowContext?.ParentFrameId || r.Budget < 0 || r.SpentRank < 0 || r.SpentRank > r.Budget ||
            r.Selected.Distinct().Count() != r.Selected.Count || r.Obtained.Distinct().Count() != r.Obtained.Count ||
            r.SequenceBefore < 0 || r.SequenceAfter < r.SequenceBefore && r.SequenceAfter != 0)
            throw new InvalidOperationException("SP Zhang Liao lost the owning native window or immutable rank payment.");
    }

    [ProgramAiChoice(SkillProgramEffectOp.ResolveMubingRankExchange, SkillProgramEffectOp.PreventDamageForHighestRankGift)]
    private PromptChoice AiSpZhangLiao(PendingDecision d, ProgramSkillFrame f)
    {
        var r = f.SpZhangLiao!;
        if (r.Stage == SpZhangLiaoStage.Payment)
        {
            var target = MubingAvailableReveal(f).Sum(id => GetAdvancedCard(id).Rank);
            var current = r.Selected.Sum(id => GetAdvancedCard(id).Rank);
            return d.Choices.Where(c => c.Parameters["option"] == "card" && !r.Selected.Contains(c.Cards.Single())).OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[f.OwnerSeat])).FirstOrDefault() is { } add && current < target ? add : d.Choices.Single(c => c.Parameters["option"] == "confirm");
        }
        if (r.Stage == SpZhangLiaoStage.Obtain)
            return d.Choices.FirstOrDefault(c => c.Parameters["option"] == "card" && !r.Selected.Contains(c.Cards.Single())) ?? d.Choices.Single(c => c.Parameters["option"] == "confirm");
        if (r.Stage == SpZhangLiaoStage.GiftCards) return d.Choices.Single(c => c.Parameters["option"] == "finish");
        return d.Choices.First();
    }
}
