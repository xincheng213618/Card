using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<PromptChoice> MouXqJueyanResponseChoices(CharacterState actor, CardKind kind, bool dying = false)
    {
        if (!actor.IsAlive || IsCardUseForbidden(actor.Seat, kind, CardActionType.Use)) return [];
        if (dying)
        {
            if (ActiveDying is not { } d || d.ResponderSeat != actor.Seat || !_players[d.VictimSeat].IsAlive || _players[d.VictimSeat].Hp > 0 ||
                kind == CardKind.Peach && !CanUsePeachToRescue(actor.Seat, d.VictimSeat) ||
                kind == CardKind.Alcohol && (d.VictimSeat != actor.Seat || HasSelfCardTargetProhibition(actor.Seat)) ||
                HasBeneficiarySuitShield(actor.Seat, d.VictimSeat, Suit.None)) return [];
        }
        else if (kind == CardKind.Dodge)
        { if (!IsProgramResponseCardUse(actor, kind) || ActiveGroupCard is not null || ActiveFactionDefense is not null ||
            ActiveCardAttack is { } slash && (IsWenjiResponseForbidden(slash.ResolutionId, actor.Seat) || ConvertedSlashSameColorResponseColor(slash) is not null)) return []; }
        else if (kind == CardKind.Nullification)
        {
            if (ActiveNullificationWindow is not { } n || n.CandidateIndex >= n.CandidateSeats.Count || n.CandidateSeats[n.CandidateIndex] != actor.Seat ||
                n.ChainDepth == 0 && IsNearbyTargetResponseProhibited(n.SourceSeat, actor.Seat, n.EffectCardKind, n.TargetSeats)) return [];
        }
        else return [];
        var source = CollectProgramTriggerCandidates(actor, SkillProgramTriggerWindow.PublicHandRevealed).FirstOrDefault(c => c.SkillId == MouXiaoQiaoOlRuntime.Jueyan);
        if (source is null) return [];
        var conversion = new CardConversionSource(source.SkillId, "retained-hand-use", actor.Seat, source.SkillInstanceId);
        return Array.AsReadOnly(GetHand(actor).Where(card => AdvancedEffectiveHandKind(actor, card) == kind && !IsTurnHandCardRestricted(actor, card) &&
            MouXiaoQiaoOl.JueyanAvailable(actor.Seat, card.Id)).Select(card =>
        {
            var parameters = new Dictionary<string, string> { ["response"] = "mou-xiao-qiao-jueyan", ["output-kind"] = kind.ToString(), ["dying-use"] = dying ? "true" : "false" };
            AddConversionParameters(parameters, conversion);
            return new PromptChoice(new($"jueyan.response.{actor.Seat}.{card.Id}.{kind}.{source.SkillInstanceId}"),
                $"绝颜：展示并保留【{card.DisplayName}】，视为使用之。", [card.Id], [], parameters);
        }).ToArray());
    }
    private bool BeginMouXqJueyanResponse(PromptChoice selected)
    {
        if (_pendingDecision is not { } prompt || !prompt.Choices.Any(c => AssistedChoicesEqual([c], [selected])) || selected.Cards is not [var id] ||
            !Enum.TryParse<CardKind>(selected.Parameters.GetValueOrDefault("output-kind"), out var kind)) throw new InvalidOperationException("Jueyan needs its actual published Hand response choice.");
        var actor = _players[prompt.PlayerSeat]; var dying = selected.Parameters.GetValueOrDefault("dying-use") == "true";
        if (!MouXqJueyanResponseChoices(actor, kind, dying).Any(c => AssistedChoicesEqual([c], [selected])) ||
            prompt.Kind != (dying ? DecisionKind.RescueDying : kind == CardKind.Dodge ? DecisionKind.RespondDodge : DecisionKind.Nullification))
            throw new InvalidOperationException("Jueyan cannot replace pure Played or another native responder.");
        var source = RequireConversionSource(selected); var program = _contentRegistry.GetSkill(source.SkillId).Program!;
        var parent = dying ? ActiveDying!.Id : kind == CardKind.Nullification ? ActiveNullificationWindow!.Id : ActiveCardAttack!.ResolutionId;
        var target = dying ? ActiveDying!.VictimSeat : kind == CardKind.Nullification ? ActiveNullificationWindow!.SourceSeat : ActiveCardAttack!.SourceSeat;
        var request = new MouXiaoQiaoJueyanResponseRequest(parent, prompt.Kind, kind, actor.Seat, target, _resolutionStack[^1].Id);
        ClearPendingDecision();
        var f = new ProgramSkillFrame(++_resolutionSequence, actor.Seat, source.SkillId, source.BindingId, program.GameplayHash, 0, [id], [])
        { SkillInstanceId = source.SkillInstanceId!, ResponseDecision = prompt, MouXiaoQiaoJueyanResponse = request };
        PushRuntimeFrame(f); AdvanceEventRulesAndQueueFact(new ProgramSkillStartedEvent(f.Id, actor.Seat, f.SkillId, f.ActivationId));
        AdvanceRuntimeProgram(f.Id); return true;
    }
    private bool IsMouXqJueyanResponseRequest(ProgramSkillFrame f)
    {
        if (f.MouXiaoQiaoJueyanResponse is not { } r || f.ResponseDecision is not { } d || r.ActorSeat != f.OwnerSeat || d.PlayerSeat != f.OwnerSeat ||
            d.Kind != r.DecisionKind || _resolutionStack.SingleOrDefault(x => x.Id == r.OriginalResponseFrameId) is not { } parent ||
            _resolutionStack.IndexOf(parent) + 1 != _resolutionStack.FindIndex(x => x.Id == f.Id)) return false;
        return r.DecisionKind switch
        {
            DecisionKind.RespondDodge => parent is ResponseWindowFrame w && w.ParentFrameId == r.ParentFrameId && w.ResponderSeat == f.OwnerSeat &&
                LifecycleCardUse(r.ParentFrameId) is { CardAttack: { } attack } && attack.TargetSeat == f.OwnerSeat && attack.SourceSeat == r.TargetSeat && IsSlashCard(attack.EffectiveCardKind ?? CardKind.Slash),
            DecisionKind.Nullification => parent is NullificationWindowFrame n && n.Id == r.ParentFrameId && n.SourceSeat == r.TargetSeat &&
                n.CandidateIndex < n.CandidateSeats.Count && n.CandidateSeats[n.CandidateIndex] == f.OwnerSeat,
            DecisionKind.RescueDying => parent is DyingFrame dying && dying.Id == r.ParentFrameId && dying.VictimSeat == r.TargetSeat && dying.ResponderSeat == f.OwnerSeat,
            _ => IsMouXqForcedRequest(r,parent)
        };
    }
    private void ReturnMouXqJueyanResponse(ProgramSkillFrame f)
    {
        if (!IsMouXqJueyanResponseRequest(f) || f.MouXiaoQiaoOl is not { Paid: true } r || f.MouXiaoQiaoJueyanResponse is not { } request ||
            !ProgramEventHistory<MouXiaoQiaoJueyanUsedEvent>().Any(e => e.FrameId == f.Id && e.CardId == r.SelectedCard && e.OutputKind == request.OutputKind))
            throw new InvalidOperationException("Jueyan lost its actual public retained-Hand response invoice.");
        var grant = new MouXiaoQiaoJueyanResponseGrant(f.Id, r.Source, f.GameplayHash, r.Round, r.SelectedCard, request);
        var parent = _resolutionStack.Single(x => x.Id == request.ParentFrameId);
        ReplaceRuntimeFrame(parent.Id, parent with { MouXiaoQiaoJueyanResponseGrant = grant });
        AdvanceEventRulesAndQueueFact(new MouXiaoQiaoJueyanResponseGrantedEvent(grant));
        FinishProgramSkill(f, true);
        var actor = _players[f.OwnerSeat]; var kind = request.OutputKind;
        if (_winner != Winner.None) return;
        if (!actor.IsAlive) { CancelMouXqResponseAfterReveal(request,actor); return; }
        if(request.DecisionKind is DecisionKind.RespondSlash or DecisionKind.QinglongCrescentBlade or DecisionKind.ProgramTrigger)
        {IssueMouXqRequestedSlash(grant);return;}
        if (request.DecisionKind == DecisionKind.RescueDying)
        {
            var d = ActiveDying ?? throw new InvalidOperationException("Jueyan rescue lost its original unresolved Dying.");
            SetDyingFrameStep(d.Id, ResolutionFrameStep.ResolvingEffect);
            var victim = _players[d.VictimSeat];
            var policies = kind == CardKind.Peach && actor.Seat != victim.Seat
                ? CardPolicies(victim, SkillProgramCardPolicyKind.RescueRecoveryBonus, CardKind.Peach).Where(p => string.Equals(GetEffectiveFactionId(actor), p.Policy.FactionId, StringComparison.Ordinal)).ToArray() : [];
            ResolveRecoveryCard(actor, victim, new(0, kind, Suit.None, 0), kind == CardKind.Peach ? "桃" : "酒", kind,
                1 + policies.Sum(p => p.Policy.Value), recoveryPolicySources: policies.Select(p => (p.Source.SkillId, p.Policy.Id)).ToArray(), conversionSource: grant.Source,
                dyingResponse: new(d.Id, actor.Seat, kind == CardKind.Peach, kind == CardKind.Peach ? 0 : null, kind == CardKind.Alcohol, kind == CardKind.Alcohol ? 0 : null), physicalCards: []);
        }
        else if (kind == CardKind.Dodge) IssueMouXqDodge(actor, grant);
        else IssueMouXqCounterspell(actor, grant);
    }
    private void CancelMouXqResponseAfterReveal(MouXiaoQiaoJueyanResponseRequest request,CharacterState actor)
    {
        ClearPendingDecision();
        if(request.DecisionKind==DecisionKind.RescueDying&&ActiveDying is{}dying&&dying.Id==request.ParentFrameId&&dying.ResponderSeat==actor.Seat)
        {ApplyDyingResponse(actor,false,null,false,null);return;}
        if(request.DecisionKind==DecisionKind.Nullification&&ActiveNullificationWindow is{}counter&&counter.Id==request.ParentFrameId)
        {ResolveNullificationChoice(counter,actor,null);return;}
        if(request.DecisionKind==DecisionKind.RespondDodge&&ActiveCardAttack is{}attack&&attack.ResolutionId==request.ParentFrameId)
        {PopResponseWindow(attack.ResolutionId);SetCardUseStep(attack.ResolutionId,ResolutionFrameStep.ResolvingEffect);CompleteAttack(attack);return;}
        if(request.DecisionKind==DecisionKind.RespondSlash&&ActiveBorrowedSword is{}borrowed&&borrowed.ResolutionId==request.ParentFrameId)
        {CompleteBorrowedSwordWithoutSlash(borrowed,transferWeapon:false);return;}
        if(request.DecisionKind==DecisionKind.QinglongCrescentBlade&&ActiveQinglongCrescentBlade is{}blade&&blade.Attack.ResolutionId==request.ParentFrameId)
        {
            ActiveQinglongCrescentBlade=null;SetCardUseStep(blade.Attack.ResolutionId,ResolutionFrameStep.ResolvingEffect);
            AdvanceEventRulesAndQueueFact(new QinglongCrescentBladeResolvedEvent(blade.Attack.ResolutionId,actor.Seat,request.TargetSeat,false,[],null));
            RestoreFactionRequestAfterQinglongChoice(blade.Attack.ResolutionId);CompleteAttack(blade.Attack);return;
        }
        if(request.DecisionKind==DecisionKind.ProgramTrigger&&_resolutionStack.LastOrDefault()is ProgramSkillFrame f&&f.Id==request.ParentFrameId)
        {CancelProgramBindingAndCleanup(f,"The accepted retained-card Slash responder died during the public reveal.");return;}
        throw new InvalidOperationException("Jueyan lost its exact unavailable responder's native return.");
    }
    private void SignMouXqResponse(ResolutionFrame owner, MouXiaoQiaoJueyanResponseGrant grant, CardActionContext action)
    {
        ReplaceRuntimeFrame(owner.Id, owner with { MouXiaoQiaoJueyanResponseGrant = grant with { ActionId = action.ActionId } });
        AdvanceEventRulesAndQueueFact(new MouXiaoQiaoJueyanResponseIssuedEvent(owner.Id, grant.ProgramFrameId, action.ActionId, action.ActorSeat, action.EffectiveKind));
    }
    private void IssueMouXqDodge(CharacterState actor, MouXiaoQiaoJueyanResponseGrant grant)
    {
        var attack = ActiveCardAttack ?? throw new InvalidOperationException("Jueyan Dodge lost its actual Slash."); var use = LifecycleCardUse(attack.ResolutionId)!;
        PopResponseWindow(attack.ResolutionId); ClearPendingDecision(); SetCardUseStep(use.Id, ResolutionFrameStep.ResolvingEffect);
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence, use.Action?.ActionId, CardActionType.Response, actor.Seat, actor.Seat,
            null, actor.Seat, attack.SourceSeat, CardKind.Dodge, [], [], [grant.Source], effectiveSuit: Suit.None, effectiveRank: 0));
        SignMouXqResponse(LifecycleCardUse(use.Id)!, grant, action);
        RecordProgramUsedBasicCard(actor.Seat, CardKind.Dodge); RecordActualPlayPhaseUse(action);
        AdvanceEventRulesAndQueueFact(new CardRespondedEvent(0, actor.Seat, attack.SourceSeat, CardKind.Dodge)); AdvanceEventRulesAndQueueFact(new CardActionAcceptedEvent(action));
        if (TryBeginCommittedResponseUsePrograms(attack, action, ProgramCardContinuation.Dodge)) return;
        if (TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardResponseAccepted, [attack.SourceSeat], ProgramCardContinuation.Dodge)) return;
        if (HasResponseUseCompletionObserver(action, ProgramCardContinuation.Dodge) || HasCardResponseCompletedObserver(action, ProgramCardContinuation.Dodge)) ContinueAcceptedCardResponse(attack, action, ProgramCardContinuation.Dodge);
        else CompleteSuccessfulDodgeResponse(attack);
    }
    private void IssueMouXqCounterspell(CharacterState actor, MouXiaoQiaoJueyanResponseGrant grant)
    {
        var pending = ActiveNullificationWindow ?? throw new InvalidOperationException("Jueyan counterspell lost its actual trick response window.");
        var policy = FreezeUnrespondableCounterspellSource(actor, []); ClearPendingDecision();
        pending = (NullificationWindowFrame)_resolutionStack.Single(x => x.Id == pending.Id);
        pending = ReplaceNullificationWindowFrame(pending with { EffectNullified = !pending.EffectNullified, ChainDepth = pending.ChainDepth + 1,
            CandidateSeats = BuildNullificationCandidateSeats(actor.Seat), CandidateIndex = 0, Step = ResolutionFrameStep.AwaitingResponse });
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence, NullificationParentActionId(pending), CardActionType.Response, actor.Seat, actor.Seat,
            null, actor.Seat, pending.SourceSeat, CardKind.Nullification, [], [], [grant.Source], effectiveSuit: Suit.None, effectiveRank: 0));
        SignMouXqResponse(pending, grant, action);
        AdvanceEventRulesAndQueueFact(new CardRespondedEvent(0, actor.Seat, pending.SourceSeat, CardKind.Nullification));
        AdvanceEventRulesAndQueueFact(new NullificationRespondedEvent(pending.ParentFrameId, pending.EffectCardId, pending.EffectCardKind, actor.Seat, 0, pending.EffectNullified, pending.ChainDepth)
            { WindowFrameId = pending.Id, TargetSeat = GetNullificationTargetSeat(pending) });
        RecordActualPlayPhaseUse(action); AdvanceEventRulesAndQueueFact(new CardActionAcceptedEvent(action)); CaptureCaoXiNativeCounterspellOffset(pending, action, false);
        pending = (NullificationWindowFrame)_resolutionStack.Single(x => x.Id == pending.Id);
        if (TryBeginPolicyCounterspellPayment(pending, action, policy, [])) return;
        if (TryBeginCommittedResponseUsePrograms(null, action, ProgramCardContinuation.NullificationResponse)) return;
        if (TryBeginProgramCardWindow(null, action, SkillProgramTriggerWindow.CardResponseAccepted, [], ProgramCardContinuation.NullificationResponse)) return;
        ContinueNullificationAfterResponseUse(action);
    }
    private bool IsIssuedMouXqResponse(ResolutionFrame owner, CardActionContext action)
    {
        if (owner.MouXiaoQiaoJueyanResponseGrant is not { } g || g.Request.ParentFrameId != owner.Id || g.ActionId != action.ActionId ||
            action.Type != CardActionType.Response || action.ActorSeat != g.Source.OwnerSeat || action.ProviderSeat != action.ActorSeat ||
            action.RequesterSeat is not null || action.ResponderSeat != action.ActorSeat || action.PhysicalCards.Count != 0 ||
            action.EffectiveKind != g.Request.OutputKind || action.EffectiveSuit != Suit.None || action.EffectiveRank != 0 || action.EffectiveIsRed is not null ||
            !action.ConversionChain.SequenceEqual([g.Source])) return false;
        return ProgramEventHistory<MouXiaoQiaoJueyanResponseGrantedEvent>().Count(e => e.Grant == (g with { ActionId = 0 })) == 1 &&
            ProgramEventHistory<MouXiaoQiaoJueyanResponseIssuedEvent>().Count(e => e == new MouXiaoQiaoJueyanResponseIssuedEvent(owner.Id, g.ProgramFrameId, action.ActionId, action.ActorSeat, action.EffectiveKind)) == 1 &&
            (owner switch
            {
                NullificationWindowFrame n => action.ParentActionId == NullificationParentActionId(n) && action.OpponentSeat == n.SourceSeat,
                CardUseFrame { CardAttack: { } attack } use => action.ParentActionId == use.Action?.ActionId && action.ActorSeat == attack.TargetSeat && action.OpponentSeat == attack.SourceSeat,
                _ => false
            });
    }
    private bool HasMouXqJueyanResponseContinuation(long parentId) => _resolutionStack.OfType<ProgramSkillFrame>().Any(f =>
        f.MouXiaoQiaoJueyanResponse?.ParentFrameId == parentId && IsMouXqJueyanResponseRequest(f) && f.MouXiaoQiaoOl is { Paid: true });
    private bool HasMouXqJueyanDyingResponse(CharacterState actor) => MouXqJueyanResponseChoices(actor, CardKind.Peach, true).Count > 0 ||
        MouXqJueyanResponseChoices(actor, CardKind.Alcohol, true).Count > 0;
    private bool TryMouXqJueyanAiResponse(CardKind kind, bool dying = false)
    {
        if (dying ? ActiveDying is null : _pendingDecision is null) return false;
        var actor = _players[dying ? ActiveDying!.ResponderSeat : _pendingDecision!.PlayerSeat];
        if (actor.IsHuman) return false;
        var choices = MouXqJueyanResponseChoices(actor, kind, dying);
        if (choices.Count == 0) return false;
        var view = CreateSnapshot(actor.Seat); bool use; AiThoughtRecord thought;
        if (dying)
        {
            (use, thought) = _aiBrains[actor.Seat].ChooseProgramDyingRescue(view, ActiveDying!.VictimSeat, "绝颜", ++_thoughtSequence);
        }
        else if (kind == CardKind.Nullification)
        {
            var n = ActiveNullificationWindow!;
            (use, thought) = _aiBrains[actor.Seat].ChooseTieredZeroCounterspell(view, n.EffectCardKind, n.SourceSeat,
                n.TargetSeats, n.EffectNullified, n.ChainDepth, ++_thoughtSequence);
        }
        else
        {
            var attack = ActiveCardAttack!;
            (use, _, thought) = _aiBrains[actor.Seat].ChooseDodgeResponse(view, attack.SourceSeat, ++_thoughtSequence, attack.IgnoresArmor, true);
        }
        AddThought(thought); if (!use) return false;
        if (dying)
            _pendingDecision = new(DecisionKind.RescueDying, actor.Seat, "绝颜：选择真实已发布的手牌展示救援。", choices.SelectMany(c => c.Cards).ToArray(), [], ActiveDying!.VictimSeat)
            { PromptId = CreatePromptId(), TargetSeat = ActiveDying!.VictimSeat, Choices = choices };
        BeginMouXqJueyanResponse(choices[0]); return true;
    }
}
