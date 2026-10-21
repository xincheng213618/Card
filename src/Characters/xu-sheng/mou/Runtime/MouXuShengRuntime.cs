using System.Globalization;
namespace CardGame.Core;

[ProgramRuntimeModule, ProgramCapability]
internal sealed class MouXuShengRuntime(IMouXuShengFacts facts, IMouXuShengNative native, IProgramRuntimeFrames frames,
    IProgramRuntimeEvents events, IProgramRuntimePrompts prompts, IProgramOperationDraws draws) : IMouXuShengRuntime
{
    internal const string Action = "mou-xu-sheng", DrawReason = "skill-program.mou:qinqiang.draw",
        DiscardReason = "skill-program.mou:yizhen.discard", ClaimReason = "skill-program.mou:yizhen.claim";
    private static CardConversionSource Source(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId ?? f.ActivationId, f.OwnerSeat, f.SkillInstanceId);
    private bool Available(int owner, bool damage) => !facts.History<MouXuShengBenefitUsedEvent>().Any(e => e.OwnerSeat == owner && e.ActualTurn == facts.Turn && e.TurnOwner == facts.TurnOwner && e.Damage == damage);
    private CurrentTurnCardUseKindsRecordedEvent[] Uses(int owner) => facts.History<CurrentTurnCardUseKindsRecordedEvent>().Where(e =>
        e.TurnNumber == facts.Turn && e.TurnOwnerSeat == facts.TurnOwner && e.ActorSeat == owner && e.WholeActualTurnUseSequence is > 0).ToArray();
    private bool TrueHandUse(int owner, CardActionContext action) => action.ActorSeat == owner && action.ProviderSeat == owner && action.PhysicalCards.Count > 0 &&
        action.PhysicalCards.All(c => c.From == CardLocation.Hand(owner)) && Uses(owner).Any(e => e.CardActionId == action.ActionId);
    private int Consecutive(int owner, CardActionContext action)
    {
        var uses = Uses(owner); var index = Array.FindIndex(uses, e => e.CardActionId == action.ActionId);
        if (index < 0) throw new InvalidOperationException("Qinqiang cannot manufacture a true-Use history entry.");
        var actions = facts.History<CardUseAppearanceCapturedEvent>().Select(e => e.Action)
            .Concat(facts.History<CardActionAcceptedEvent>().Select(e => e.Action)).DistinctBy(a => a.ActionId).ToDictionary(a => a.ActionId);
        var count = 1; // The formal announcement and explicit user decision include this card.
        if (action.EffectiveIsRed is not { } color) return count;
        for (var i = index - 1; i >= 0; i--)
        {
            if (uses[i].CardActionId is not { } id || !actions.TryGetValue(id, out var previous) || previous.EffectiveIsRed != color) break;
            count++;
        }
        return count;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 34900, SkillProgramEffectOp.ResolveMouXuShengQinqiang, SkillProgramEffectOp.ResolveMouXuShengYizhen)]
    private bool Qualifies(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w) => c.SkillId == MouXuShengComposition.Qinqiang
        ? facts.Use(c, w) is { } use && TrueHandUse(c.OwnerSeat, use.Action) && (Available(c.OwnerSeat, true) || Available(c.OwnerSeat, false))
        : facts.SourcedDamage(w, c.OwnerSeat);
    public SkillProgramStepOutcome Resolve(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var f = frames.ActiveProgram(supplied.Id);
        if (f.MouXuSheng is not null) { Continue(f.Id); return SkillProgramStepOutcome.AwaitChild; }
        if (f.InstructionIndex != 1 || f.TriggerId != f.ActivationId || f.WindowContext is not { } w || facts.Program(f.SkillId)?.GameplayHash != f.GameplayHash)
            throw new InvalidOperationException("Mou Xu Sheng requires the original native trigger frame.");
        var use = operation == SkillProgramEffectOp.ResolveMouXuShengQinqiang ? facts.Use(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, 0), w) : null;
        if (operation == SkillProgramEffectOp.ResolveMouXuShengQinqiang ? use is null || !TrueHandUse(f.OwnerSeat, use.Action) : !facts.DamageParent(f))
            throw new InvalidOperationException("Mou Xu Sheng lost the actual Hand Use or sourced damage occurrence.");
        var r = new MouXuShengReceipt(Source(f), f.GameplayHash, operation, facts.Turn, facts.TurnOwner, w.ParentFrameId,
            use?.Action.ActionId ?? 0, use?.UseId ?? 0, w.DamageFrameId ?? 0, w.SourceSeat ?? -1, use is null ? 0 : Consecutive(f.OwnerSeat, use.Action))
        { Stage = use is null ? MouXuShengStage.Selecting : MouXuShengStage.Benefit, Accepted = use is null, Before = facts.MovementSequence, After = facts.MovementSequence };
        if (use is null)
            r = r with { Views = [new(f.OwnerSeat, r.OtherSeat) { Cards = facts.Hand(r.OtherSeat) }, new(r.OtherSeat, f.OwnerSeat) { Cards = facts.Hand(f.OwnerSeat) }] };
        frames.ReplaceTop(f = f with { MouXuSheng = r });
        events.AdvanceFact(new MouXuShengStartedEvent(f.Id, r.Source, r.GameplayHash, operation, r.ActualTurn, r.TurnOwner, r.WindowId, r.ActionId, r.UseId, r.DamageId, r.OtherSeat, r.X));
        Continue(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    internal bool Valid(ProgramSkillFrame f)
    {
        if (f.MouXuSheng is not { } r || r.Source != Source(f) || r.GameplayHash != f.GameplayHash || facts.Program(f.SkillId)?.GameplayHash != f.GameplayHash ||
            f.InstructionIndex != 1 || f.TriggerId != f.ActivationId || f.WindowContext?.ParentFrameId != r.WindowId || frames.ParentOf(f.Id)?.Id != r.WindowId ||
            !Enum.IsDefined(r.Stage) || r.Cursor < 0 || r.Cursor > r.Views.Count || r.PayCursor < 0 || r.PayCursor > r.Selected.Count || r.X < 0 ||
            r.Before < 0 || r.After < r.Before || r.After > facts.MovementSequence || r.Selected.Select(s => s.Card.CardId).Distinct().Count() != r.Selected.Count ||
            r.Paid.Select(s => s.Card.CardId).Distinct().Count() != r.Paid.Count || r.Paid.Any(p => !r.Selected.Any(s => s.Chooser == p.Chooser && s.Card.CardId == p.Card.CardId && s.Card.Holder == p.Card.Holder))) return false;
        if (r.Operation == SkillProgramEffectOp.ResolveMouXuShengQinqiang)
        {
            if (f.SkillId != MouXuShengComposition.Qinqiang || f.TriggerId != "true-hand-use" || r.Views.Count != 0 || r.X < 1 ||
                f.WindowContext is not { } w || facts.Use(new(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId, f.GameplayHash, 0), w) is not { } use || use.Action.ActionId != r.ActionId || use.UseId != r.UseId) return false;
        }
        else if (r.Operation != SkillProgramEffectOp.ResolveMouXuShengYizhen || f.SkillId != MouXuShengComposition.Yizhen || f.TriggerId != "mutual-private-hand" ||
            !facts.DamageParent(f) || r.Views is not [var first, var second] || first.Viewer != f.OwnerSeat || first.Holder != r.OtherSeat || second.Viewer != r.OtherSeat || second.Holder != f.OwnerSeat) return false;
        return facts.History<MouXuShengStartedEvent>().Count(e => e == new MouXuShengStartedEvent(f.Id, r.Source, r.GameplayHash, r.Operation,
            r.ActualTurn, r.TurnOwner, r.WindowId, r.ActionId, r.UseId, r.DamageId, r.OtherSeat, r.X)) == 1;
    }
    private int Chooser(ProgramSkillFrame f) => f.MouXuSheng is { Stage: MouXuShengStage.Selecting } r ? r.Views[r.Cursor].Viewer : f.OwnerSeat;
    private PromptChoice Option(ProgramSkillFrame f, string branch, string label, int? card = null) => new(
        new ChoiceId($"mou-xu-sheng.{f.Id}.{f.MouXuSheng!.Stage}.{f.MouXuSheng.Cursor}.{branch}.{card}"), label, card is { } id ? [id] : [], [],
        new Dictionary<string, string> { ["program-action"] = Action, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });
    private IReadOnlyList<PromptChoice> Choices(ProgramSkillFrame f)
    {
        var r = f.MouXuSheng!;
        if (r.Stage == MouXuShengStage.Benefit)
        {
            var choices = new List<PromptChoice>();
            if (Available(f.OwnerSeat, true)) choices.Add(Option(f, "damage", $"此牌伤害+{r.X}"));
            if (Available(f.OwnerSeat, false)) choices.Add(Option(f, "draw", $"摸{r.X}张牌"));
            choices.Add(Option(f, "decline", "不发动勤强")); return choices;
        }
        if (r.Stage == MouXuShengStage.Selecting)
        {
            var view = r.Views[r.Cursor];
            return view.Cards.Where(c => !r.Selected.Any(s => s.Card.CardId == c.CardId) && facts.Discardable(view.Viewer, c))
                .Select(c => Option(f, "card", "弃置对方的【" + facts.CardLabel(c.CardId) + "】", c.CardId)).ToArray();
        }
        return [];
    }
    private void Publish(ProgramSkillFrame f)
    {
        var chooser = Chooser(f); var choices = Choices(f);
        frames.ReplaceTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        prompts.Set(new PendingDecision(DecisionKind.ProgramTrigger, chooser, f.MouXuSheng!.Stage == MouXuShengStage.Benefit
            ? "勤强：本回合两项各限一次，请选择一项。" : "疑阵：私下观看对方手牌并选择其中一张弃置。", choices.SelectMany(c => c.Cards).Distinct().ToArray(), [], f.OwnerSeat)
        { PromptId = prompts.NextId(), IsPrivate = true, Choices = Array.AsReadOnly(choices.ToArray()) }, facts.Human(chooser));
        native.PublishState();
    }
    [ProgramAnswer(Action)]
    private void Answer(PromptChoice chosen)
    {
        if (frames.Top is not ProgramSkillFrame { MouXuSheng: { } r } f || !Valid(f) || prompts.Current?.PlayerSeat != Chooser(f) ||
            Choices(f).SingleOrDefault(c => c.Id == chosen.Id) is not { } canonical || !canonical.Cards.SequenceEqual(chosen.Cards) || !canonical.Targets.SequenceEqual(chosen.Targets) ||
            canonical.Parameters.Count != chosen.Parameters.Count || canonical.Parameters.Any(p => chosen.Parameters.GetValueOrDefault(p.Key) != p.Value))
            throw new InvalidOperationException("Mou Xu Sheng requires the actual currently published private choice.");
        prompts.Clear(); frames.ReplaceTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var branch = chosen.Parameters["branch"];
        if (r.Stage == MouXuShengStage.Benefit)
        {
            if (branch == "decline") { Complete(f); return; }
            var damage = branch == "damage";
            events.AdvanceFact(new MouXuShengBenefitUsedEvent(f.Id, f.OwnerSeat, r.ActualTurn, r.TurnOwner, r.ActionId, damage, r.X));
            frames.ReplaceTop(f = f with { MouXuSheng = r with { Accepted = true, Stage = damage ? MouXuShengStage.Benefit : MouXuShengStage.Drawing } });
            if (damage) { native.Boost(f); Complete(f); }
            else draws.BeginOperationProgramDraw(f, f.OwnerSeat, r.X, new(DrawReason));
            return;
        }
        var view = r.Views[r.Cursor]; var card = view.Cards.Single(c => c.CardId == chosen.Cards.Single());
        frames.ReplaceTop(f with { MouXuSheng = r with { Cursor = r.Cursor + 1, Selected = r.Selected.Append(new(view.Viewer, card)).ToArray() } });
        Continue(f.Id);
    }
    internal void Continue(long id)
    {
        while (prompts.Current is null && frames.Top is ProgramSkillFrame { MouXuSheng: { } r } f && f.Id == id)
        {
            if (!Valid(f)) throw new InvalidOperationException("Mou Xu Sheng lost his original frame, true Use or paid material cursor.");
            if (facts.Finished) { Complete(f); return; }
            if (r.Stage == MouXuShengStage.Benefit) { Publish(f); return; }
            if (r.Stage == MouXuShengStage.Drawing) return;
            if (r.Stage == MouXuShengStage.Selecting)
            {
                if (r.Cursor >= r.Views.Count) { frames.ReplaceTop(f with { MouXuSheng = r with { Stage = MouXuShengStage.Paying } }); continue; }
                if (!facts.Alive(Chooser(f)) || Choices(f).Count == 0) { frames.ReplaceTop(f with { MouXuSheng = r with { Cursor = r.Cursor + 1 } }); continue; }
                Publish(f); return;
            }
            if (r.Stage == MouXuShengStage.Paying)
            {
                if (r.PayCursor >= r.Selected.Count) { frames.ReplaceTop(f with { MouXuSheng = r with { Stage = MouXuShengStage.Claiming } }); continue; }
                var selected = r.Selected[r.PayCursor];
                frames.ReplaceTop(f = f with { MouXuSheng = r with { PayCursor = r.PayCursor + 1 } });
                if (!facts.Discardable(selected.Chooser, selected.Card)) continue;
                var before = facts.MovementSequence;
                frames.ReplaceTop(f = f with { MouXuSheng = f.MouXuSheng! with { Stage = MouXuShengStage.DiscardChildren, Paying = selected, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
                native.Discard(f); if (frames.Top?.Id != id) return; continue;
            }
            if (r.Stage is MouXuShengStage.DrawChildren or MouXuShengStage.DiscardChildren or MouXuShengStage.ClaimChildren)
            {
                if (native.Drain(f)) return;
                frames.ReplaceTop(f = f with { PendingMovementContinuation = null });
                if (r.Stage != MouXuShengStage.DiscardChildren) { Complete(f); return; }
                frames.ReplaceTop(f with { MouXuSheng = f.MouXuSheng! with { Stage = MouXuShengStage.Paying, Paying = null } }); continue;
            }
            if (r.Stage == MouXuShengStage.Claiming)
            {
                if (!facts.Alive(f.OwnerSeat) || r.Paid is not [var first, var second] || first.Card.IsRed is not { } color || second.Card.IsRed != color)
                { Complete(f); return; }
                var ids = r.Paid.Select(p => p.Card.CardId).Where(card => facts.Location(card) == CardLocation.DiscardPile).ToArray();
                if (ids.Length == 0) { Complete(f); return; }
                var before = facts.MovementSequence;
                frames.ReplaceTop(f = f with { MouXuSheng = r with { Stage = MouXuShengStage.ClaimChildren, Before = before, After = before }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
                native.Claim(f, ids); if (frames.Top?.Id != id) return; continue;
            }
        }
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMouXuShengQinqiang)]
    private void DrawReturned(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (!Valid(f) || f.MouXuSheng is not { Stage: MouXuShengStage.Drawing, Draw: null } r || outcome.RequestedCount != r.X || outcome.RecipientSeat != f.OwnerSeat)
            throw new InvalidOperationException("Qinqiang cannot replace or replay the exact accepted native draw.");
        frames.ReplaceTop(f = f with { MouXuSheng = r with { Stage = MouXuShengStage.DrawChildren, Draw = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        events.AdvanceFact(new MouXuShengDrawReturnedEvent(f.Id, r.X, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count, outcome.AttemptFrameId)); Continue(f.Id);
    }
    private void Complete(ProgramSkillFrame f)
    {
        events.AdvanceFact(new MouXuShengCompletedEvent(f.Id));
        frames.ReplaceTop(f = f with { MouXuSheng = null, PendingMovementContinuation = null }); native.Finish(f);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 34900)]
    private bool Resume(long id) { if (frames.Top is not ProgramSkillFrame { MouXuSheng: not null } f || f.Id != id) return false; Continue(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 34900)]
    private bool Movement(ProgramSkillFrame f) => f.MouXuSheng is not null && Resume(f.Id);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 34900)]
    private void Invariant(ProgramSkillFrame f) { if (f.MouXuSheng is not null && !Valid(f)) throw new InvalidOperationException("Invalid Mou Xu Sheng frame-owned receipt."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 34900)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 34900)]
    private bool Paid(ProgramSkillFrame f) => f.MouXuSheng is { Accepted: true } && Valid(f);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMouXuShengQinqiang, SkillProgramEffectOp.ResolveMouXuShengYizhen)]
    private PromptChoice Ai(PendingDecision prompt, ProgramSkillFrame f)
    {
        if (f.MouXuSheng!.Stage == MouXuShengStage.Benefit)
            return prompt.Choices.FirstOrDefault(c => c.Parameters["branch"] == "draw") ?? prompt.Choices.First();
        return prompt.Choices.OrderByDescending(c => facts.KeepValue(f.MouXuSheng.Views[f.MouXuSheng.Cursor].Holder, c.Cards.Single())).First();
    }
}
