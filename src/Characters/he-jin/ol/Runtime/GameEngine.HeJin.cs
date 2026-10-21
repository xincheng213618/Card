using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string HeJinGiftReason = "skill-program.he-jin.mouzhu.give", HeJinDiscardReason = global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramDiscardTopPlacement.HeJinDiscardReason;
    private static CardConversionSource HeJinSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_he_jin_ol_HeJin.HeJinSource(f);
    private SkillProgramStepOutcome BeginHeJinMouzhu(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.HeJinMouzhu is not null || f.SkillId != "ol:mouzhu" || f.TriggerId is not null || f.WindowContext is not null || f.InstructionIndex != 1 ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats is not [var actor] || !IsValidPlayerSeat(actor) || actor == f.OwnerSeat ||
            !_players[f.OwnerSeat].IsAlive || !_players[actor].IsAlive || GetHand(_players[actor]).Count == 0 || _currentSeat != f.OwnerSeat || _phase != TurnPhase.Play)
            throw new InvalidOperationException("Mouzhu requires its own once-per-Play hand demand against one other living hand owner.");
        var r = new HeJinMouzhuReceipt(HeJinSource(f), f.GameplayHash, f.InstructionIndex, _turnNumber, _cardUseDebitPhaseInstanceId, actor, _movementSequence)
        { HandIds = GetHand(_players[actor]).Select(c => c.Id).ToArray() };
        ReplaceRuntimeTop(f = f with { HeJinMouzhu = r });
        AdvanceEventRulesAndQueueFact(new HeJinMouzhuStartedEvent(f.Id, r.Source, r.GameplayHash, r.ActualTurn, r.PhaseInstanceId, actor, r.StartedAt, r.HandIds.Count));
        PublishHeJinPrompt(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> HeJinMouzhuChoices(ProgramSkillFrame f)
    {
        var r = f.HeJinMouzhu!;
        Dictionary<string, string> Params(string branch) => new() { ["program-action"] = "he-jin-mouzhu", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch };
        if (r.Stage == HeJinMouzhuStage.ChoosingGift)
            return Array.AsReadOnly(BuildOwnedCardPaymentChoices(f.Id, r.ActorSeat, r.ActorSeat, [CardZoneKind.Hand], OwnedCardMoveIntent.Transfer,
                canSelect: (_, card) => r.HandIds.Contains(card.Id)).Select(c => c with
                { Parameters = new Dictionary<string, string>(c.Parameters) { ["program-action"] = "he-jin-mouzhu", ["branch"] = "gift" } }).ToArray());
        if (r.Stage != HeJinMouzhuStage.ChoosingAssault) return [];
        return Array.AsReadOnly(new[] { CardKind.Slash, CardKind.Duel }.Where(k => CanIssueHeJinMouzhuCard(r.ActorSeat, f.OwnerSeat, k))
            .Select(k => new PromptChoice(new($"he-jin.{f.Id}.assault.{k}"), k == CardKind.Slash ? "视为对何进使用【杀】" : "视为对何进使用【决斗】", [], [f.OwnerSeat],
                new Dictionary<string, string>(Params("assault")) { ["card-kind"] = k.ToString() })).ToArray());
    }
    private void PublishHeJinPrompt(ProgramSkillFrame f)
    {
        var mouzhu = f.HeJinMouzhu; var seat = mouzhu?.ActorSeat ?? f.OwnerSeat;
        var choices = mouzhu is not null ? HeJinMouzhuChoices(f) : HeJinYanhuoChoices(f);
        if (choices.Count == 0) throw new InvalidOperationException("He Jin's exact private decision has no legal choice.");
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, seat, mouzhu?.Stage == HeJinMouzhuStage.ChoosingGift ? "选择交给何进的一张手牌。" :
            mouzhu is not null ? "选择视为对何进使用的牌。" : $"延祸：选择弃置杀手至多{f.HeJinYanhuo!.Limit}张牌，然后确认。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = seat, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[seat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("he-jin-mouzhu")]
    private void AnswerHeJinMouzhu(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Mouzhu lost its owning frame.");
        RequireHeJin(f); var r = f.HeJinMouzhu!;
        if (_pendingDecision is not { IsPrivate: true } p || p.PlayerSeat != r.ActorSeat || !HeJinMouzhuChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Mouzhu needs the actual donor's exact private answer.");
        ClearPendingDecision();
        if (r.Stage == HeJinMouzhuStage.ChoosingGift)
        {
            if (choice.Cards is not [var id] || !r.HandIds.Contains(id) || _cardZones.GetLocation(id) != CardLocation.Hand(r.ActorSeat))
                throw new InvalidOperationException("Mouzhu's original donor card moved before payment.");
            var before = _movementSequence;
            ReplaceRuntimeTop(f = f with { HeJinMouzhu = r with { Stage = HeJinMouzhuStage.GiftChildren, GiftId = id, Before = before, After = before } });
            MoveCard(GetAttackCard(id), CardLocation.Hand(r.ActorSeat), CardLocation.Hand(f.OwnerSeat), new(HeJinGiftReason), NativeGiveCompletion(f.Id,r.ActorSeat,f.OwnerSeat,[id],_movementSequence,null));
            f = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(f = f with { HeJinMouzhu = f.HeJinMouzhu! with { After = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new HeJinMouzhuGiftPaidEvent(f.Id, r.ActorSeat, f.OwnerSeat, before, _movementSequence));
            AdvanceRuntimeProgram(f.Id); return;
        }
        var kind = Enum.Parse<CardKind>(choice.Parameters["card-kind"]);
        var origin = new HeJinMouzhuUseOrigin(f.Id, r.InstructionIndex, _resolutionSequence + 1, r.Source, r.GameplayHash, r.ActualTurn, r.PhaseInstanceId,
            r.ActorSeat, f.OwnerSeat, kind);
        ReplaceRuntimeTop(f = f with { HeJinMouzhu = r with { Stage = HeJinMouzhuStage.AssaultIssued, Assault = origin } });
        AdvanceEventRulesAndQueueFact(new HeJinMouzhuAssaultIssuedEvent(origin));
        BeginHeJinMouzhuCard(f, origin);
    }
    private bool DrainHeJinChildren(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    [ProgramContinuation(ProgramContinuationKind.Resume, 26710)]
    private bool ResumeHeJin(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.HeJinMouzhu is null && f.HeJinYanhuo is null) return false;
        RequireHeJin(f);
        if (f.HeJinYanhuo is not null) { ResumeHeJinYanhuo(f); return true; }
        var r = f.HeJinMouzhu!;
        if (r.Stage is HeJinMouzhuStage.ChoosingGift or HeJinMouzhuStage.ChoosingAssault)
        { if (_pendingDecision is null) PublishHeJinPrompt(f); return true; }
        if (r.Stage == HeJinMouzhuStage.AssaultIssued) throw new InvalidOperationException("Mouzhu must return through its exact native card use.");
        if (DrainHeJinChildren(id)) return true;
        f = GetActiveProgramFrame(id); r = f.HeJinMouzhu!;
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !_players[r.ActorSeat].IsAlive || GetHand(_players[r.ActorSeat]).Count >= GetHand(_players[f.OwnerSeat]).Count)
        { FinishHeJinMouzhu(f, false); return true; }
        ReplaceRuntimeTop(f = f with { HeJinMouzhu = r with { Stage = HeJinMouzhuStage.ChoosingAssault } });
        if (HeJinMouzhuChoices(f).Count == 0) { FinishHeJinMouzhu(f, false); return true; }
        PublishHeJinPrompt(f); return true;
    }
    private bool MatchesHeJinMouzhu(ProgramSkillFrame f, HeJinMouzhuUseOrigin origin) =>
        ValidHeJinMouzhu(f) && f.HeJinMouzhu is { Stage: HeJinMouzhuStage.AssaultIssued, Assault: { } issued } && issued == (origin with { AttackStarted = false });
    private void FinishHeJinMouzhu(ProgramSkillFrame f, bool issued)
    {
        AdvanceEventRulesAndQueueFact(new HeJinMouzhuCompletedEvent(f.Id, issued));
        ReplaceRuntimeTop(f = f with { HeJinMouzhu = null }); FinishProgramSkill(f, true);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouzhuHandDemandAndAssault, SkillProgramEffectOp.ResolveYanhuoKillerDiscard)]
    private PromptChoice ChooseHeJinAi(PendingDecision p, ProgramSkillFrame f)
    {
        if (f.HeJinMouzhu is { Stage: HeJinMouzhuStage.ChoosingAssault })
            return p.Choices.OrderByDescending(c => c.Parameters.GetValueOrDefault("card-kind") == nameof(CardKind.Slash)).First();
        if (f.HeJinMouzhu is not null)
            return p.Choices.OrderBy(c => c.Cards is [var id] && GetAttackCard(id).Kind is CardKind.Peach or CardKind.Dodge ? 1 : 0).ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
        return p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "card" && c.Cards.Count > 0) ??
            p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "card") ?? p.Choices.First();
    }
    [ProgramCapability]
    private sealed class HeJinCapability(GameEngine e) : IHeJinProgramHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => op == SkillProgramEffectOp.ResolveMouzhuHandDemandAndAssault ? e.BeginHeJinMouzhu(f) : e.BeginHeJinYanhuo(f); }
}
