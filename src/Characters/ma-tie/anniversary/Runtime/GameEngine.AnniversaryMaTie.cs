using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MaTieAction = "anniversary-ma-tie", MaTieObtainReason = "skill-program.anniversary:quxian.obtain";
    private static CardConversionSource MaTieSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability] private sealed class AnniversaryMaTieHost(GameEngine e) : IAnniversaryMaTieHost
    {
        public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => op switch
        {
            SkillProgramEffectOp.ResolveAnniversaryMaTieQuxian => e.BeginMaTie(f),
            SkillProgramEffectOp.ResolveAnniversaryMaTieQuxianReward => e.BeginMaTieReward(f),
            SkillProgramEffectOp.RecordAnniversaryMaTieZhuiji => e.RecordMaTieZhuiji(f),
            _ => throw new InvalidOperationException("Unknown Ma Tie operation.")
        };
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 46700)]
    private bool CanOfferMaTie(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext c)
    {
        if (candidate.SkillId == AnniversaryMaTieComposition.Quxian)
        {
            if (trigger.Id == "issued-slash-reward") return MaTieRewardUse(candidate.OwnerSeat, c) is { AnniversaryMaTieOrigin: { } origin } && origin.Source.SkillInstanceId == candidate.SkillInstanceId;
            return c.Window is SkillProgramTriggerWindow.TurnStartBeforeNormalFlow or SkillProgramTriggerWindow.AfterTurnEnded &&
                c.SourceSeat == candidate.OwnerSeat && c.TargetSeat == candidate.OwnerSeat && _turnProgression.OwnerSeat == candidate.OwnerSeat;
        }
        if (candidate.SkillId != AnniversaryMaTieComposition.Zhuiji) return true;
        return MaTieCardWindow(c, SkillProgramTriggerWindow.CardUseCompleted) is { Action: { Type: CardActionType.Use } a } &&
            a.ActorSeat == candidate.OwnerSeat && a.EffectiveDesignatedTargetSeats.Any(t => t != candidate.OwnerSeat);
    }
    private SkillProgramStepOutcome BeginMaTie(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.AnniversaryMaTie is not null || !MaTieTurnParent(f) || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0)
            throw new InvalidOperationException("Quxian requires its exact optional own actual start or after-turn-ended candidate.");
        var r = new AnniversaryMaTieReceipt(MaTieSource(f), f.GameplayHash, f.WindowContext!.ParentFrameId, _turnNumber, _turnProgression.OwnerSeat, AnniversaryMaTieStage.ObtainChildren)
        { Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { AnniversaryMaTie = r });
        AdvanceEventRulesAndQueueFact(new AnniversaryMaTieStartedEvent(f.Id, r.Source, r.Hash, r.ParentId, r.Turn, r.TurnOwner));
        var card = _cardZones.CardsAt(CardLocation.DrawPile).FirstOrDefault(c => IsSlashCard(c.Kind) && !c.IsGeneralWeapon);
        if (card is not null)
        {
            ReplaceRuntimeTop(f = f with { AnniversaryMaTie = r with { ObtainedCard = card.Id } });
            MoveProgramCardsFromMultipleSources([card.Id], CardLocation.Hand(f.OwnerSeat), new(MaTieObtainReason), (_, _) =>
            {
                var current = GetActiveProgramFrame(f.Id);
                ReplaceRuntimeTop(current with { AnniversaryMaTie = current.AnniversaryMaTie! with { After = _movementSequence } });
            });
            if (AwaitProgramBoundCardMovements(f.Id, f.OwnerSeat) == SkillProgramStepOutcome.AwaitChild) return SkillProgramStepOutcome.AwaitChild;
        }
        ContinueMaTie(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private PromptChoice MaTieChoice(ProgramSkillFrame f, string branch, string text, int? target = null, int option = -1) => new(
        new($"ma-tie.{f.Id}.{f.AnniversaryMaTie!.Cursor}.{branch}.{target ?? option}"), text, [], target is { } t ? [t] : [],
        new Dictionary<string, string> { ["program-action"] = MaTieAction, ["branch"] = branch, ["option"] = option.ToString(CultureInfo.InvariantCulture) });
    private IReadOnlyList<PromptChoice> MaTieChoices(ProgramSkillFrame f)
    {
        var r = f.AnniversaryMaTie!;
        if (r.Stage == AnniversaryMaTieStage.Target) return Array.AsReadOnly(_players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat)
            .Select(p => MaTieChoice(f, "target", "令攻击范围内的角色依次对 " + p.Name + " 使用杀", p.Seat)).ToArray());
        if (r.Stage != AnniversaryMaTieStage.SlashChoice || r.Cursor >= r.Actors.Count || !_players[r.Target].IsAlive || !_players[r.Actors[r.Cursor]].IsAlive) return [];
        var actor = r.Actors[r.Cursor]; var result = new List<PromptChoice>(); var plans = MaTieSlashPlans(f);
        for (var i = 0; i < plans.Count; i++) result.Add(new(new($"ma-tie.{f.Id}.{r.Cursor}.slash.{i}"), plans[i].Action.Description, plans[i].Materials, plans[i].Action.TargetSeats,
            new Dictionary<string, string> { ["program-action"] = MaTieAction, ["branch"] = "slash", ["option"] = i.ToString(CultureInfo.InvariantCulture) }));
        result.AddRange(KenshangProgramSlashChoices(f, actor, [r.Target]));
        result.AddRange(TousuiProgramSlashChoices(f, actor, [r.Target]));
        if (CanRequestMaTieFaction(f)) result.Add(MaTieChoice(f, "faction", "发动自己的势力供杀能力"));
        result.Add(MaTieChoice(f, "decline", "不使用杀"));
        return Array.AsReadOnly(result.ToArray());
    }
    private void PublishMaTie(ProgramSkillFrame f)
    {
        var r = f.AnniversaryMaTie!; var choices = MaTieChoices(f);
        if (choices.Count == 0) { FinishMaTie(f); return; }
        var actor = r.Stage == AnniversaryMaTieStage.Target ? f.OwnerSeat : r.Actors[r.Cursor]; var skill = _contentRegistry.GetSkill(f.SkillId);
        var prompt = new PendingDecision(DecisionKind.ProgramTrigger, actor,
            r.Stage == AnniversaryMaTieStage.Target ? "驱险：选择一名其他角色。" : $"驱险：可对 {_players[r.Target].Name} 使用杀；若无人使其受到伤害，未使用者各失去实际使用人数点体力。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = actor, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse, AnniversaryMaTie = r with { OriginalDecision = RequestedDeckBasicFrame.FreezeDecision(prompt) } });
        _pendingDecision = prompt; _status = _players[actor].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer(MaTieAction)] private void AnswerMaTie(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryMaTie: { } r } f || !ValidMaTie(f) ||
            _pendingDecision?.PlayerSeat != (r.Stage == AnniversaryMaTieStage.Target ? f.OwnerSeat : r.Actors[r.Cursor]) ||
            !MaTieChoices(f).Any(c => AssistedChoicesEqual([choice], [c]))) throw new InvalidOperationException("Quxian changed its private original actor, target or native plan.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        switch (choice.Parameters["branch"])
        {
            case "target":
                var target = choice.Targets.Single();
                var queue = Enumerable.Range(0, _playerCount).Select(i => TableStepSeat(_currentSeat, i)).Where(s => s != target && _players[s].IsAlive && IsWithinAttackRange(s, target)).ToArray();
                ReplaceRuntimeTop(f = f with { AnniversaryMaTie = r with { Target = target, Actors = queue, Stage = AnniversaryMaTieStage.SlashChoice } });
                for (var i = 0; i < queue.Length; i++) AdvanceEventRulesAndQueueFact(new AnniversaryMaTieQueueEvent(f.Id, target, i, queue[i]));
                AdvanceEventRulesAndQueueFact(new AnniversaryMaTieSelectedEvent(f.Id, target, queue.Length)); ContinueMaTie(f.Id); return;
            case "slash": IssueMaTieSlash(f, int.Parse(choice.Parameters["option"], CultureInfo.InvariantCulture)); return;
            case "faction": BeginMaTieFaction(f); return;
            case "decline": NextMaTieActor(f); return;
            default: throw new InvalidOperationException("Unknown Quxian choice.");
        }
    }
    private bool DrainMaTie(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, f.PendingMovementContinuation is null ? PostEventContinuation.Program : PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, f.PendingMovementContinuation is null ? PostEventContinuation.Program : PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueMaTie(long id)
    {
        var f = GetActiveProgramFrame(id); var r = f.AnniversaryMaTie!;
        if (r.Stage is AnniversaryMaTieStage.ObtainChildren or AnniversaryMaTieStage.RewardChildren or AnniversaryMaTieStage.HpChildren)
        {
            if (DrainMaTie(f)) return;
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null }); r = f.AnniversaryMaTie!;
            if (r.Stage == AnniversaryMaTieStage.RewardChildren) { FinishMaTie(f); return; }
            if (r.Stage == AnniversaryMaTieStage.ObtainChildren) { ReplaceRuntimeTop(f = f with { AnniversaryMaTie = r with { Stage = AnniversaryMaTieStage.Target } }); PublishMaTie(f); return; }
            if (!r.HpPaid && _players[r.HpTarget].IsAlive)
            {
                var result = ApplyProgramHpLoss(f.Id, f.SkillId, r.HpTarget, r.Used.Count, (before, after, lost) =>
                {
                    var current = GetActiveProgramFrame(f.Id); var receipt = current.AnniversaryMaTie!;
                    ReplaceRuntimeFrame(current.Id, current with { AnniversaryMaTie = receipt with { HpPaid = true, HpBefore = before, HpAfter = after } });
                    AdvanceEventRulesAndQueueFact(new AnniversaryMaTieHpPaidEvent(f.Id, receipt.PenaltyCursor, receipt.HpTarget, lost, before, after));
                });
                if (result == SkillProgramStepOutcome.AwaitChild) return;
                if (DrainMaTie(GetActiveProgramFrame(f.Id))) return;
            }
            f = GetActiveProgramFrame(f.Id); r = f.AnniversaryMaTie!;
            ReplaceRuntimeTop(f = f with { AnniversaryMaTie = r with { Stage = AnniversaryMaTieStage.Penalties, PenaltyCursor = r.PenaltyCursor + 1, HpTarget = -1, HpPaid = false, HpBefore = 0, HpAfter = 0 } });
            ContinueMaTie(f.Id); return;
        }
        if (r.Stage == AnniversaryMaTieStage.NativeReturned) { NextMaTieActor(f); return; }
        if (r.Stage == AnniversaryMaTieStage.SlashChoice)
        {
            if (_winner != Winner.None) { FinishMaTie(f); return; }
            if (r.Cursor >= r.Actors.Count || !_players[r.Target].IsAlive)
            { ReplaceRuntimeTop(f = f with { AnniversaryMaTie = r with { Stage = AnniversaryMaTieStage.Penalties } }); ContinueMaTie(f.Id); return; }
            if (!_players[r.Actors[r.Cursor]].IsAlive) { NextMaTieActor(f); return; }
            PublishMaTie(f); return;
        }
        if (r.Stage == AnniversaryMaTieStage.Penalties)
        {
            if (r.TargetDamaged || r.Used.Count == 0 || _winner != Winner.None) { FinishMaTie(f); return; }
            var cursor = r.PenaltyCursor;
            while (cursor < r.Actors.Count && (r.Used.Contains(r.Actors[cursor]) || !_players[r.Actors[cursor]].IsAlive)) cursor++;
            if (cursor >= r.Actors.Count) { FinishMaTie(f); return; }
            ReplaceRuntimeTop(f = f with { AnniversaryMaTie = r with { Stage = AnniversaryMaTieStage.HpChildren, PenaltyCursor = cursor, HpTarget = r.Actors[cursor], HpPaid = false } });
            ContinueMaTie(f.Id); return;
        }
        if (r.Stage == AnniversaryMaTieStage.Target) PublishMaTie(f);
    }
    private void NextMaTieActor(ProgramSkillFrame f)
    {
        var r = f.AnniversaryMaTie!;
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, RoundOnceCardlessProgramRequest = null, AnniversaryMaTie = r with { Stage = AnniversaryMaTieStage.SlashChoice, Cursor = r.Cursor + 1, SelectedAction = null, Materials = [], Provider = -1, UseId = 0, ActionId = 0, FactionPolicy = null, FactionTried = false, OriginalDecision = null } });
        ContinueMaTie(f.Id);
    }
    private void FinishMaTie(ProgramSkillFrame f)
    {
        var r = f.AnniversaryMaTie!;
        AdvanceEventRulesAndQueueFact(new AnniversaryMaTieCompletedEvent(f.Id, r.Used.Count, r.TargetDamaged));
        ReplaceRuntimeTop(f = f with { AnniversaryMaTie = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryMaTieQuxian)] private PromptChoice ChooseMaTie(PendingDecision p, ProgramSkillFrame f)
    {
        var r = f.AnniversaryMaTie!;
        if (r.Stage == AnniversaryMaTieStage.Target) return p.Choices.OrderBy(c => AreProgramDistributionAllies(_players[f.OwnerSeat], _players[c.Targets.Single()]) ? 1 : 0).ThenBy(c => c.Id.Value, StringComparer.Ordinal).First();
        if (!AreProgramDistributionAllies(_players[r.Actors[r.Cursor]], _players[r.Target])) return p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "slash") ?? p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == "faction") ?? p.Choices.Last();
        return p.Choices.Last();
    }
}
