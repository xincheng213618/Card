using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string TianChouTopReason = "skill-program.tian-chou.shandao.top";
    private static CardConversionSource TianChouSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_tian_chou_ol_TianChou.TianChouSource(f);
    private bool TianChouHasCards(int seat) => _players[seat].IsAlive && GetHand(_players[seat]).Concat(GetEquipment(seat)).Any();
    [ProgramCapability] private sealed class TianChouCapability(GameEngine e) : ITianChouHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f) => e.BeginTianChou(f); }
    [ProgramActivationLegality(ProgramActivationLegalityPhase.AfterCommonStart, 27090, SkillProgramEffectOp.ResolveShandaoTopAndGlobalTricks)]
    private bool CanActivateTianChou(CharacterState owner) => _players.Any(p => TianChouHasCards(p.Seat));
    private SkillProgramStepOutcome BeginTianChou(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.TianChouShandao is not null || f.SkillId != "ol:shandao" || f.ActivationId != "top-and-two-tricks" || f.TriggerId is not null || f.WindowContext is not null ||
            f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None ||
            _currentSeat != f.OwnerSeat || _phase != TurnPhase.Play || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
            throw new InvalidOperationException("Shandao requires its own actual once-per-Play living source.");
        var candidates = Enumerable.Range(0, _playerCount).Select(i => TableStepSeat(f.OwnerSeat, i)).Where(TianChouHasCards).ToArray();
        if (candidates.Length == 0) throw new InvalidOperationException("Shandao needs at least one actual HE owner.");
        var r = new TianChouShandaoReceipt(TianChouSource(f), f.GameplayHash, 1, _turnNumber, _cardUseDebitPhaseInstanceId, _movementSequence) { Candidates = candidates };
        ReplaceRuntimeTop(f = f with { TianChouShandao = r });
        AdvanceEventRulesAndQueueFact(new TianChouShandaoStartedEvent(f.Id, r.Source, r.GameplayHash, r.ActualTurn, r.PhaseInstanceId, r.StartedAt, candidates.Length));
        for (var i = 0; i < candidates.Length; i++) AdvanceEventRulesAndQueueFact(new TianChouShandaoCandidateEvent(f.Id, i, candidates[i]));
        PublishTianChou(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> TianChouChoices(ProgramSkillFrame f)
    {
        var r = f.TianChouShandao!;
        Dictionary<string, string> Args(string branch) => new() { ["program-action"] = "tian-chou-shandao", ["branch"] = branch, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) };
        if (r.Stage == TianChouShandaoStage.Targets)
        {
            var result = r.Candidates.Where(s => !r.Targets.Contains(s) && TianChouHasCards(s)).Select(s =>
                new PromptChoice(new($"shandao.{f.Id}.target.{s}"), $"选择{_players[s].Name}（五谷丰登目标）", [], [s], Args("target"))).ToList();
            if (r.Targets.Count > 0) result.Add(new(new($"shandao.{f.Id}.confirm"), "确认所选角色，逐人置顶一张牌", [], [], Args("confirm")));
            return Array.AsReadOnly(result.ToArray());
        }
        if (r.Stage != TianChouShandaoStage.Card || r.Cursor >= r.Targets.Count || !_players[r.Targets[r.Cursor]].IsAlive) return [];
        return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, r.Targets[r.Cursor], [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Transfer,
            destination: SkillProgramCardDestination.DrawPileTop).Select(c => PromptChoiceProjection.Freeze(c with
            { Parameters = new Dictionary<string, string>(c.Parameters) { ["program-action"] = "tian-chou-shandao", ["branch"] = "card", ["cursor"] = r.Cursor.ToString(CultureInfo.InvariantCulture) } })).ToArray());
    }
    private void PublishTianChou(ProgramSkillFrame f)
    {
        var choices = TianChouChoices(f); if (choices.Count == 0) throw new InvalidOperationException("Shandao cannot publish an empty current choice.");
        var skill = _contentRegistry.GetSkill(f.SkillId); var r = f.TianChouShandao!;
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, r.Stage == TianChouShandaoStage.Targets ? "善刀：选择至少一名有手牌或装备牌的角色，然后确认。" : $"善刀：将{_players[r.Targets[r.Cursor]].Name}的一张手牌或装备牌公开置于牌堆顶。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = r.Stage == TianChouShandaoStage.Card, Choices = choices, TargetSeat = r.Stage == TianChouShandaoStage.Card ? r.Targets[r.Cursor] : null, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("tian-chou-shandao")]
    private void AnswerTianChou(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { TianChouShandao: { } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !TianChouChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Shandao requires the owner's exact currently published target or private card slot.");
        AssertTianChou(f);
        if (!_players[f.OwnerSeat].IsAlive || r.Tops.Count == 0 && !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
            throw new InvalidOperationException("An unpaid Shandao choice lost its living source.");
        ClearPendingDecision();
        if (r.Stage == TianChouShandaoStage.Targets)
        {
            if (choice.Parameters["branch"] == "target")
            {
                var seat = choice.Targets.Single(); AdvanceEventRulesAndQueueFact(new TianChouShandaoSelectedEvent(f.Id, r.Targets.Count, seat));
                ReplaceRuntimeTop(f = f with { TianChouShandao = r with { Targets = r.Targets.Append(seat).ToArray() } }); PublishTianChou(f); return;
            }
            var ordered = Enumerable.Range(0, _playerCount).Select(i => TableStepSeat(f.OwnerSeat, i)).Where(r.Targets.Contains).ToArray();
            ReplaceRuntimeTop(f = f with { TianChouShandao = r with { Stage = TianChouShandaoStage.Card, TargetsFrozen = true, Targets = ordered } });
            AdvanceEventRulesAndQueueFact(new TianChouShandaoTargetsFrozenEvent(f.Id, ordered.Length));
            for (var i = 0; i < ordered.Length; i++) AdvanceEventRulesAndQueueFact(new TianChouShandaoTargetOrderedEvent(f.Id, i, ordered[i]));
            ContinueTianChou(f.Id); return;
        }
        var owner = r.Targets[r.Cursor]; var zone = Enum.Parse<CardZoneKind>(choice.Parameters["source-zone"]); var slot = int.Parse(choice.Parameters["slot-index"], CultureInfo.InvariantCulture);
        var from = new CardLocation(zone, owner); var card = _cardZones.CardsAt(from)[slot]; var before = _movementSequence;
        var grain = card.Kind == CardKind.WoodenOx && zone == CardZoneKind.Equipment ? GetWoodenOxGrain(_players[owner]).Select(c => c.Id).ToArray() : [];
        ReplaceRuntimeTop(f = f with { TianChouShandao = r with { Stage = TianChouShandaoStage.TopChildren } });
        // A selected foreign hand remains hidden until this real card is revealed and moved.
        ReplaceRuntimeTop(f=f with{PublicHandRevealSelection=new("shandao-hand-top",f.InstructionIndex,0,card.Id,card.Kind,from,CardLocation.DrawPile,f.OwnerSeat,f.OwnerSeat)});
        AdvanceEventRulesAndQueueFact(new CardsRevealedEvent(f.Id, Array.AsReadOnly(new[] { ToSnapshot(card) })));
        if(TryBeginPublicHandRevealWindow(f.Id,"shandao-hand-top"))return;
        ContinueShandaoAfterPublicReveal(GetActiveProgramFrame(f.Id));

    }
    private bool DrainTianChou(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueTianChou(long id)
    {
        while (_resolutionStack.LastOrDefault() is ProgramSkillFrame { TianChouShandao: { } r } f && f.Id == id)
        {
            AssertTianChou(f);
            if (r.Stage is TianChouShandaoStage.TopChildren or TianChouShandaoStage.NativeChildren)
            {
                if (DrainTianChou(id)) return;
                f = GetActiveProgramFrame(id); r = f.TianChouShandao!;
                r = r.Stage == TianChouShandaoStage.TopChildren ? r with { Stage = TianChouShandaoStage.Card, Cursor = r.Cursor + 1 } : r with { Stage = TianChouShandaoStage.TrickReady, NativeUse = null };
                ReplaceRuntimeTop(f = f with { TianChouShandao = r });
            }
            if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive) { FinishTianChou(f, "owner-dead-or-game-ended"); return; }
            if (r.Stage == TianChouShandaoStage.NativeUse) return;
            if (r.Stage == TianChouShandaoStage.Targets) { if (_pendingDecision is null) PublishTianChou(f); return; }
            if (r.Stage == TianChouShandaoStage.Card)
            {
                if (r.Cursor < r.Targets.Count)
                {
                    if (TianChouChoices(f).Count > 0) { if (_pendingDecision is null) PublishTianChou(f); return; }
                    AdvanceEventRulesAndQueueFact(new TianChouShandaoSkippedEvent(f.Id, r.Cursor, r.Targets[r.Cursor]));
                    ReplaceRuntimeTop(f with { TianChouShandao = r with { Cursor = r.Cursor + 1 } }); continue;
                }
                ReplaceRuntimeTop(f = f with { TianChouShandao = r = r with { Stage = TianChouShandaoStage.TrickReady } });
            }
            if (r.TrickIndex >= 2) { FinishTianChou(f, "completed"); return; }
            var kind = r.TrickIndex == 0 ? CardKind.FiveGrains : CardKind.ArrowBarrage;
            var nominated = r.TrickIndex == 0 ? r.Targets : Enumerable.Range(1, _playerCount - 1).Select(i => TableStepSeat(f.OwnerSeat, i)).Where(s => !r.Targets.Contains(s)).ToArray();
            var targets = TianChouShandaoTrickTargets(f.OwnerSeat, kind, nominated);
            if (targets.Count == 0)
            {
                AdvanceEventRulesAndQueueFact(new TianChouShandaoTrickSkippedEvent(f.Id, kind));
                ReplaceRuntimeTop(f with { TianChouShandao = r with { TrickIndex = r.TrickIndex + 1 } }); continue;
            }
            var origin = new TianChouShandaoUseOrigin(f.Id, r.InstructionIndex, _resolutionSequence + 1, r.Source, r.GameplayHash, r.ActualTurn, r.PhaseInstanceId, f.OwnerSeat, kind) { TargetSeats = targets };
            ReplaceRuntimeTop(f = f with { TianChouShandao = r with { Stage = TianChouShandaoStage.NativeUse, NativeUse = origin } });
            AdvanceEventRulesAndQueueFact(new TianChouShandaoNativeIssuedEvent(f.Id, origin.CardUseFrameId, kind, f.OwnerSeat, targets.Count));
            for (var i = 0; i < targets.Count; i++) AdvanceEventRulesAndQueueFact(new TianChouShandaoNativeTargetEvent(f.Id, origin.CardUseFrameId, i, targets[i]));
            BeginTianChouShandaoTrick(f, origin); return;
        }
    }
    private void FinishTianChou(ProgramSkillFrame f, string reason)
    {
        var r = f.TianChouShandao!;
        AdvanceEventRulesAndQueueFact(new TianChouShandaoCompletedEvent(f.Id, r.Tops.Count, ProgramEventHistory<TianChouShandaoNativeIssuedEvent>().Count(e => e.FrameId == f.Id), reason));
        ReplaceRuntimeTop(f = f with { TianChouShandao = null }); FinishProgramSkill(f, true);
    }
    private bool MatchesTianChouShandao(ProgramSkillFrame f, TianChouShandaoUseOrigin o) => f.TianChouShandao is { Stage: TianChouShandaoStage.NativeUse, NativeUse: { } expected } r &&
        o.ProgramFrameId == f.Id && o.InstructionIndex == f.InstructionIndex && o.Source == r.Source && o.GameplayHash == f.GameplayHash && o.ActualTurn == r.ActualTurn && o.PhaseInstanceId == r.PhaseInstanceId &&
        o.ActorSeat == f.OwnerSeat && o.CardUseFrameId == expected.CardUseFrameId && o.CardKind == expected.CardKind && o.TargetSeats.SequenceEqual(expected.TargetSeats) && ValidTianChou(f);
    private void CompleteTianChouShandaoTrick(ProgramSkillFrame f, TianChouShandaoUseOrigin origin)
    {
        if (_resolutionStack.LastOrDefault()?.Id != f.Id || !MatchesTianChouShandao(f, origin)) throw new InvalidOperationException("Shandao lost its exact native global-trick return.");
        var r = f.TianChouShandao!;
        AdvanceEventRulesAndQueueFact(new TianChouShandaoNativeReturnedEvent(f.Id, origin.CardUseFrameId, origin.CardKind));
        ReplaceRuntimeTop(f with { TianChouShandao = r with { Stage = TianChouShandaoStage.NativeChildren, TrickIndex = r.TrickIndex + 1 } }); ContinueTianChou(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 27090)] private bool ResumeTianChou(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { TianChouShandao: not null } f || f.Id != id) return false; ContinueTianChou(id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 27090)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 27090)]
    private bool ContinuePaidTianChou(ProgramSkillFrame f) => f.TianChouShandao is { Tops.Count: > 0 } && ValidTianChou(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveShandaoTopAndGlobalTricks)]
    private PromptChoice AiTianChou(PendingDecision p, ProgramSkillFrame f)
    {
        var r = f.TianChouShandao!;
        if (r.Stage == TianChouShandaoStage.Targets)
        {
            var view = CreateSnapshot(p.PlayerSeat); var benefit = new SkillProgramAiHint(0, 0, 0, 1, 0, 0, false, false);
            var next = p.Choices.Where(c => c.Targets.Count == 1).Select(c => (Choice: c, Score: _aiBrains[p.PlayerSeat].ScoreProgramTarget(view, c.Targets[0], benefit))).OrderByDescending(x => x.Score).ThenBy(x => x.Choice.Targets[0]).FirstOrDefault();
            return next.Choice is not null && (r.Targets.Count == 0 || next.Score > 0) ? next.Choice : p.Choices.Single(c => c.Parameters["branch"] == "confirm");
        }
        // Visible equipment can deliberately be stripped; a foreign hand is only a blind slot.
        return p.Choices.OrderBy(c => c.Cards is [var id] ? GetKeepValue(GetAdvancedCard(id), _players[r.Targets[r.Cursor]]) : 0).ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
    }
    [PublicHandRevealReturn("shandao-hand-top")]
    private void ContinueShandaoAfterPublicReveal(ProgramSkillFrame f)
    {
        var r=f.TianChouShandao??throw new InvalidOperationException("Shandao lost its actual selection cursor.");
        var payment=f.PublicHandRevealSelection??throw new InvalidOperationException("Shandao lost its selected entity.");
        if(payment.ReturnKey!="shandao-hand-top"||payment.InstructionIndex!=f.InstructionIndex||r.Stage!=TianChouShandaoStage.TopChildren)throw new InvalidOperationException("Shandao changed its selected-source obligation.");
        ReplaceRuntimeTop(f=f with{PublicHandRevealSelection=null});
        var card=GetAdvancedCard(payment.CardId);var from=payment.From;var owner=from.OwnerSeat!.Value;var before=_movementSequence;
        if(_cardZones.GetLocation(card.Id)!=from){AdvanceEventRulesAndQueueFact(new TianChouShandaoSkippedEvent(f.Id,r.Cursor,owner));ReplaceRuntimeTop(f with{TianChouShandao=r with{Stage=TianChouShandaoStage.Card,Cursor=r.Cursor+1}});ContinueTianChou(f.Id);return;}
        var grain=card.Kind==CardKind.WoodenOx&&from.Zone==CardZoneKind.Equipment?GetWoodenOxGrain(_players[owner]).Select(c=>c.Id).ToArray():[];
        MoveCard(card, from, CardLocation.DrawPile, new(TianChouTopReason));
        var after = _movementSequence; var move = _cardMovements.Single(m => m.Sequence > before && m.Sequence <= after && m.CardId == card.Id && m.From == from && m.Reason.Value == TianChouTopReason);
        var top = new TianChouShandaoTop(r.Cursor, owner, card.Id, card.Kind, from, move.To, before, after, move.Sequence) { WoodenOxGrainIds = grain };
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { TianChouShandao = f.TianChouShandao! with { Tops = r.Tops.Append(top).ToArray() } });
        AdvanceEventRulesAndQueueFact(new TianChouShandaoTopPaidEvent(f.Id, r.Cursor, owner, card.Id, card.Kind, from, move.To, before, after, move.Sequence));
        ContinueTianChou(f.Id);
    }

}
