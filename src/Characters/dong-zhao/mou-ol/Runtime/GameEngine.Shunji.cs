using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static CardConversionSource MouDongZhaoSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private bool HasMouDongZhao => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMouDongZhaoShunji) ||
        _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveMouDongZhaoYishi);
    private Card[] MouDongZhaoHE(int owner) => GetHand(_players[owner]).Concat(GetEquipment(_players[owner])).ToArray();
    private MouDongZhaoNameCount[] MouDongZhaoNames(int owner) => MouDongZhaoHE(owner).GroupBy(c => AdvancedEffectiveHandKind(_players[owner], c))
        .Select(g => new MouDongZhaoNameCount(g.Key, g.Count())).ToArray();
    private bool MouShunjiSpent(int owner, int target) => ProgramEventHistory<MouDongZhaoShunjiAcceptedEvent>()
        .Any(e => e.Source.OwnerSeat == owner && e.RoundNumber == _roundNumber && e.TargetSeat == target);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 33500, SkillProgramEffectOp.ResolveMouDongZhaoShunji)]
    private bool CanOfferMouShunji(ProgramTriggerLegalityContext c) => c.SkillId == MouDongZhaoComposition.Shunji && c.Owner.IsAlive && _roundNumber > 0 &&
        c.Context is { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0, TargetSeat: { } target } &&
        IsValidPlayerSeat(target) && _players[target].IsAlive && !MouShunjiSpent(c.OwnerSeat, target);
    private bool ExactMouShunjiWindow(ProgramSkillFrame f) => f.WindowContext is { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0 } c &&
        _resolutionStack.SingleOrDefault(item => item.Id == c.ParentFrameId) is DamageTriggerWindowFrame window && window.TriggerWindow == c.Window &&
        window.ParentFrameId == c.DamageFrameId && window.TargetSeat == c.TargetSeat && window.Candidates.Any(candidate => candidate.ToProgramCandidate() is { } native && MountObserverCandidateMatches(f, native)) &&
        _resolutionStack.SingleOrDefault(item => item.Id == window.ParentFrameId) is DamageFrame damage && damage.TargetSeat == c.TargetSeat && damage.Amount == c.Amount;
    private SkillProgramStepOutcome BeginMouShunji(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MouDongZhao is not null) { ContinueMouDongZhao(f.Id); return SkillProgramStepOutcome.AwaitChild; }
        if (f.SkillId != MouDongZhaoComposition.Shunji || f.TriggerId != "damage-draw-and-gift" || f.ActivationId != f.TriggerId ||
            f.InstructionIndex != 1 || !ExactMouShunjiWindow(f) || f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0, TargetSeat: { } target } c ||
            !IsValidPlayerSeat(target) || !_players[f.OwnerSeat].IsAlive || !_players[target].IsAlive || _roundNumber <= 0 || MouShunjiSpent(f.OwnerSeat, target))
            throw new InvalidOperationException("Shunji requires its unused native Round-victim damage occurrence.");
        var receipt = new MouDongZhaoReceipt(MouDongZhaoSource(f), f.GameplayHash, SkillProgramEffectOp.ResolveMouDongZhaoShunji,
            MouDongZhaoStage.ChoosingDraw, c.ParentFrameId, _roundNumber, target);
        ReplaceRuntimeTop(f = f with { MouDongZhao = receipt });
        AdvanceEventRulesAndQueueFact(new MouDongZhaoShunjiAcceptedEvent(f.Id, receipt.Source, receipt.GameplayHash, receipt.WindowId, receipt.RoundNumber, target));
        PublishMouDongZhao(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private PromptChoice MouDongZhaoChoice(ProgramSkillFrame f, string verb, string label, IReadOnlyList<int>? cards = null,
        IReadOnlyList<int>? targets = null, IReadOnlyDictionary<string, string>? native = null) => new(
        new($"mou-dong-zhao.{f.Id}.{f.MouDongZhao!.Stage}.{verb}.{string.Join('.', cards ?? [])}.{string.Join('.', targets ?? [])}.{native?.GetValueOrDefault("source-zone")}.{native?.GetValueOrDefault("slot-index")}"),
        label, cards ?? [], targets ?? [], new Dictionary<string, string>(native ?? new Dictionary<string, string>())
        { ["program-action"] = "mou-dong-zhao", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["verb"] = verb });
    private IReadOnlyList<PromptChoice> MouDongZhaoChoices(ProgramSkillFrame f)
    {
        var r = f.MouDongZhao!;
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None) return [];
        if (r.Stage == MouDongZhaoStage.ChoosingDraw) return Array.AsReadOnly(Enumerable.Range(0, 3)
            .Select(n => MouDongZhaoChoice(f, "draw-" + n, $"摸{n}张牌，再交给{_players[r.TargetSeat].Name}等量牌")).ToArray());
        if (r.Stage == MouDongZhaoStage.ChoosingGift)
        {
            if (!_players[r.TargetSeat].IsAlive || MouDongZhaoHE(f.OwnerSeat).Length < r.GiftCount) return [];
            var choices = BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, f.OwnerSeat, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Transfer,
                canSelect: (_, card) => r.Selected.Contains(card.Id) || r.Selected.Count < r.GiftCount,
                destination: SkillProgramCardDestination.SelectedTargetHand, destinationSeat: r.TargetSeat)
                .Select(c => MouDongZhaoChoice(f, "gift-toggle", c.Description, c.Cards, native: c.Parameters)).ToList();
            if (r.Selected.Count == r.GiftCount) choices.Add(MouDongZhaoChoice(f, "gift-confirm", $"交出{r.GiftCount}张牌"));
            return Array.AsReadOnly(choices.ToArray());
        }
        return MouYishiChoices(f);
    }
    private void PublishMouDongZhao(ProgramSkillFrame f)
    {
        var choices = MouDongZhaoChoices(f);
        if (choices.Count == 0) { CompleteMouDongZhao(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, skill.Name + "：选择本次效果。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("mou-dong-zhao")]
    private void AnswerMouDongZhao(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouDongZhao: { } r } f || !ValidMouDongZhao(f) ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !MouDongZhaoChoices(f).Any(c => AssistedChoicesEqual([c], [selected])))
            throw new InvalidOperationException("OL Dong Zhao requires its current canonical native choice.");
        ClearPendingDecision(); var verb = selected.Parameters["verb"];
        if (r.Stage == MouDongZhaoStage.ChoosingDraw)
        {
            var count = int.Parse(verb[5..], CultureInfo.InvariantCulture);
            ReplaceRuntimeTop(f = f with { MouDongZhao = r with { RequestedDraw = count, Stage = count == 0 ? MouDongZhaoStage.Complete : MouDongZhaoStage.Drawing } });
            if (count == 0) CompleteMouDongZhao(f);
            else BeginOperationProgramDraw(f, f.OwnerSeat, count, new("skill-program.mou-ol:shunji.draw"));
            return;
        }
        if (r.Stage == MouDongZhaoStage.ChoosingGift)
        {
            if (verb == "gift-toggle")
            {
                var id = selected.Cards.Single();
                ReplaceRuntimeTop(f = f with { MouDongZhao = r with { Selected = r.Selected.Contains(id) ? r.Selected.Where(c => c != id).ToArray() : r.Selected.Append(id).ToArray() } });
                PublishMouDongZhao(f); return;
            }
            var names = MouDongZhaoNames(f.OwnerSeat);
            ReplaceRuntimeTop(f = f with { MouDongZhao = r with { NamesFrozen = true, Names = names } });
            // Giving one's own Hand back to oneself does not fabricate a move or loss.
            var moving = r.Selected.Where(id => _cardZones.GetLocation(id) != CardLocation.Hand(r.TargetSeat)).ToArray();
            MutateMouDongZhao(f, MouDongZhaoStage.GiftChildren, () =>
            {
                if (moving.Length > 0) MoveProgramCardsFromMultipleSources(moving, CardLocation.Hand(r.TargetSeat), new("skill-program.mou-ol:shunji.gift"), NativeGiveCompletionRecords(f.Id,f.OwnerSeat,r.TargetSeat,moving,_movementSequence,(_, moves) =>
                    {
                        foreach (var m in moves) AdvanceEventRulesAndQueueFact(new MouDongZhaoGiftSignedEvent(f.Id, m.CardId, m.CardKind, m.From, m.To, m.Sequence));
                        SpendMouShunjiNames(GetActiveProgramFrame(f.Id));
                    }));
            });
            return;
        }
        AnswerMouYishi(f, selected);
    }
    private void SpendMouShunjiNames(ProgramSkillFrame f)
    {
        var r = f.MouDongZhao!;
        if (r.TargetSeat == f.OwnerSeat) return;
        var after = MouDongZhaoNames(f.OwnerSeat);
        foreach (var name in r.Names.Where(n => n.Count > 0 && !after.Any(a => a.Kind == n.Kind && a.Count > 0) &&
            !ProgramEventHistory<MouDongZhaoNameSpentEvent>().Any(e => e.OwnerSeat == f.OwnerSeat && e.Kind == n.Kind)))
            AdvanceEventRulesAndQueueFact(new MouDongZhaoNameSpentEvent(f.Id, f.OwnerSeat, name.Kind));
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouDongZhaoShunji, SkillProgramEffectOp.DrawMouDongZhaoYishiLoss)]
    private void ReturnMouDongZhaoDraw(ProgramSkillFrame f, NativeDrawOutcome draw)
    {
        var r = f.MouDongZhao;
        if (r is not { Stage: MouDongZhaoStage.Drawing, Draw: null } || !ValidMouDongZhao(f) || draw.RecipientSeat != f.OwnerSeat || draw.RequestedCount != r.RequestedDraw)
            throw new InvalidOperationException("OL Dong Zhao's native draw must return to its single issued owner.");
        ReplaceRuntimeTop(f = f with { MouDongZhao = r with { Stage = MouDongZhaoStage.DrawChildren, Draw = draw,
            GiftCount = draw.ActualDrawIds.Count, Before = draw.SequenceBefore, After = draw.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MouDongZhaoDrawReturnedEvent(f.Id, draw.RequestedCount, draw.ActualDrawIds.Count, draw.ReplacementGainedIds.Count,
            draw.SequenceBefore, draw.SequenceAfter, draw.AttemptFrameId));
        SealMouDongZhaoMovements(f.Id, draw.SequenceBefore); ContinueMouDongZhao(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouDongZhaoShunji, SkillProgramEffectOp.ResolveMouDongZhaoYishi, SkillProgramEffectOp.DrawMouDongZhaoYishiLoss)]
    private PromptChoice AiMouDongZhao(PendingDecision prompt, ProgramSkillFrame f)
    {
        var r = f.MouDongZhao!;
        if (r.Stage == MouDongZhaoStage.ChoosingDraw) return prompt.Choices.Single(c => c.Parameters["verb"] == "draw-2");
        if (r.Stage == MouDongZhaoStage.ChoosingGift)
            return prompt.Choices.FirstOrDefault(c => c.Parameters["verb"] == "gift-confirm") ?? prompt.Choices.First(c =>
                c.Parameters["verb"] == "gift-toggle" && !r.Selected.Contains(c.Cards.Single()));
        return AiMouYishi(prompt, f);
    }
    [ProgramCapability]
    private sealed class MouDongZhaoCapability(GameEngine engine) : IMouDongZhaoHost
    {
        public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation) => operation switch
        {
            SkillProgramEffectOp.ResolveMouDongZhaoShunji => engine.BeginMouShunji(frame),
            SkillProgramEffectOp.ResolveMouDongZhaoYishi => engine.BeginMouYishi(frame),
            _ => engine.BeginMouYishiLoss(frame)
        };
    }
}
