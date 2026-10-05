using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsDelegatedJudgment(SkillProgramTrigger trigger) =>
        trigger.Effects.FirstOrDefault()?.Op == SkillProgramEffectOp.DelegateJudgmentReplacement;
    private PromptChoice DelegatedJudgmentChoice(JudgmentFrame j, string action, string label, int? card = null) =>
        new(new($"delegated-judgment.{j.Id}.{action}.{card}"), label, card is { } id ? [id] : [], [],
            new Dictionary<string, string> { ["action"] = action, ["judgment-id"] = j.Id.ToString(CultureInfo.InvariantCulture),
                ["card-id"] = card?.ToString(CultureInfo.InvariantCulture) ?? string.Empty });
    private IReadOnlyList<PromptChoice> DelegatedJudgmentChoices(JudgmentFrame j)
    {
        var d = j.DelegatedReplacement ?? throw new InvalidOperationException("Delegated judgment has no owning draft.");
        return d.Stage == DelegatedJudgmentStage.Offered
            ? Array.AsReadOnly(new[] { DelegatedJudgmentChoice(j, "delegated-judgment-accept", "发动缓释，让被判定者观看手牌并选择一张牌。"),
                DelegatedJudgmentChoice(j, "program-judgment-replace-skip", "保留当前判定牌，不发动缓释。") })
            : d.Stage == DelegatedJudgmentStage.Choosing
                ? Array.AsReadOnly(d.Material.Select(c => DelegatedJudgmentChoice(j, "delegated-judgment-select",
                    $"选择 {_players[d.Source.OwnerSeat].Name} 的【{c.DisplayName}】作为自己的判定牌。", c.Id)).ToArray())
                : Array.Empty<PromptChoice>();
    }
    private bool ValidDelegatedJudgmentDraft(JudgmentFrame j, DelegatedJudgmentDraft d)
    {
        var candidate = CurrentJudgmentCandidate(j);
        var identity = d.JudgmentFrameId == j.Id && d.SubjectSeat == j.TargetSeat &&
            candidate is not null && d.Source.OwnerSeat == candidate.OwnerSeat &&
            d.Source.SkillId == candidate.ProgramId && d.Source.SkillInstanceId == candidate.SkillInstanceId &&
            d.GameplayHash == candidate.GameplayHash && d.TriggerId == candidate.ProgramTriggerId && d.Source.BindingId == d.TriggerId &&
            d.Material.Count > 0 && d.Material.Select(c => c.Id).Distinct().Count() == d.Material.Count &&
            (d.Stage == DelegatedJudgmentStage.Paid || GetJudgmentCard(j)?.Id == d.OldCardId);
        if (!identity || !Enum.IsDefined(d.Stage)) return false;
        if (d.Stage is DelegatedJudgmentStage.Chosen or DelegatedJudgmentStage.Paid)
            return d.SelectedCardId is { } selected && d.Material.Any(c => c.Id == selected);
        var owner = _players[d.Source.OwnerSeat]; var (_, trigger) = GetProgramJudgmentReplacement(candidate!);
        return d.SelectedCardId is null && owner.IsAlive && _players[d.SubjectSeat].IsAlive &&
            HasRuntimeSkillInstance(owner, d.Source.SkillId, d.Source.SkillInstanceId) &&
            d.Material.SequenceEqual(GetProgramJudgmentReplacementCards(owner, trigger, j.TargetSeat, j.Reason).Select(ToSnapshot)) &&
            (d.Stage == DelegatedJudgmentStage.Offered ? d.HandView.Count == 0 : d.HandView.SequenceEqual(GetHand(owner).Select(ToSnapshot)));
    }
    private void BeginDelegatedJudgmentChoice(JudgmentFrame j, JudgmentTriggerCandidate candidate,
        SkillProgram program, SkillProgramTrigger trigger, IReadOnlyList<Card> cards)
    {
        var d = new DelegatedJudgmentDraft(j.Id, new(program.Id, trigger.Id, candidate.OwnerSeat, candidate.SkillInstanceId!),
            program.GameplayHash, trigger.Id, j.TargetSeat, GetJudgmentCard(j)!.Id,
            DelegatedJudgmentStage.Offered, cards.Select(ToSnapshot).ToArray(), []);
        SetJudgmentFrameStep(j.Id, ResolutionFrameStep.AwaitingResponse);
        ReplaceJudgmentFrame(j = GetJudgmentFrame(j.Id) with { DelegatedReplacement = d });
        var current = GetJudgmentCard(j)!;
        AdvanceEventRulesAndQueueFact(new JudgmentReplacementRequestedEvent(j.Id, j.Id, j.TargetSeat,
            candidate.OwnerSeat, j.Reason, current.Id, current.Kind, EffectiveSuit(_players[j.TargetSeat], current)));
        PublishDelegatedJudgmentChoice(j);
    }
    private void PublishDelegatedJudgmentChoice(JudgmentFrame j)
    {
        var d = j.DelegatedReplacement!;
        if (!ValidDelegatedJudgmentDraft(j, d)) throw new InvalidOperationException("Delegated judgment lost its exact source and parent.");
        var chooser = d.Stage == DelegatedJudgmentStage.Offered ? d.Source.OwnerSeat : d.SubjectSeat;
        var choices = DelegatedJudgmentChoices(j);
        var presentation = _contentRegistry.GetSkill(d.Source.SkillId);
        _pendingDecision = new(DecisionKind.ProgramJudgmentReplacement, chooser,
            d.Stage == DelegatedJudgmentStage.Offered ? "缓释：是否让被判定者观看并选择你的牌？" : "缓释：观看诸葛瑾的手牌，从其可选牌中选择一张替换自己的判定牌。",
            d.Stage == DelegatedJudgmentStage.Choosing ? d.Material.Select(c => c.Id).ToArray() : [], [], d.Source.OwnerSeat,
            IncomingCard: GetJudgmentCard(j)!.Kind)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = d.SubjectSeat, Choices = choices,
            SkillPrompt = new(d.Source.SkillId, presentation.Name, "缓释 · 判定替换", presentation.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private bool TryResolveDelegatedJudgmentChoice(PromptChoice selected)
    {
        if (selected.Parameters.GetValueOrDefault("action") is not ("delegated-judgment-accept" or "delegated-judgment-select")) return false;
        var j = ActiveJudgment ?? throw new InvalidOperationException("Delegated judgment lost its actual judgment.");
        var d = j.DelegatedReplacement ?? throw new InvalidOperationException("Delegated judgment has no owning draft.");
        if (!ValidDelegatedJudgmentDraft(j, d) || _pendingDecision is not { IsPrivate: true } p ||
            p.PlayerSeat != (d.Stage == DelegatedJudgmentStage.Offered ? d.Source.OwnerSeat : d.SubjectSeat) ||
            !AssistedChoicesEqual([selected], DelegatedJudgmentChoices(j).Where(c => c.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("The delegated choice no longer matches its private chooser or material.");
        var candidate = CurrentJudgmentCandidate(j)!; var (_, trigger) = GetProgramJudgmentReplacement(candidate);
        var owner = _players[d.Source.OwnerSeat];
        if (!owner.IsAlive || !_players[d.SubjectSeat].IsAlive || !HasRuntimeSkillInstance(owner, d.Source.SkillId, d.Source.SkillInstanceId!))
            throw new InvalidOperationException("An unpaid delegated replacement lost its living exact source.");
        var current = GetProgramJudgmentReplacementCards(owner, trigger, j.TargetSeat, j.Reason).Select(ToSnapshot).ToArray();
        if (!current.SequenceEqual(d.Material)) throw new InvalidOperationException("Delegated replacement material changed before payment.");
        ClearPendingDecision();
        if (d.Stage == DelegatedJudgmentStage.Offered)
        {
            ReplaceJudgmentFrame(j = j with { DelegatedReplacement = d with
                { Stage = DelegatedJudgmentStage.Choosing, HandView = GetHand(owner).Select(ToSnapshot).ToArray() } });
            AdvanceEventRulesAndQueueFact(new DelegatedJudgmentViewedEvent(j.Id, d.Source, d.SubjectSeat, GetHand(owner).Count));
            PublishDelegatedJudgmentChoice(j); return true;
        }
        var id = selected.Cards.Single();
        ReplaceJudgmentFrame(j with { DelegatedReplacement = d with { Stage = DelegatedJudgmentStage.Chosen, SelectedCardId = id } });
        ResolveProgramJudgmentReplacementChoice(new(new($"delegated-judgment.{j.Id}.paid-{id}"), selected.Label, [id], [],
            new Dictionary<string, string> { ["action"] = "program-judgment-replace", ["card-id"] = id.ToString(CultureInfo.InvariantCulture) }));
        return true;
    }
    private bool IsDelegatedJudgmentPromptValid(JudgmentFrame j, PendingDecision? p)
    {
        if (j.DelegatedReplacement is not { Stage: DelegatedJudgmentStage.Offered or DelegatedJudgmentStage.Choosing } d ||
            !ValidDelegatedJudgmentDraft(j, d) || p is not { IsPrivate: true, Kind: DecisionKind.ProgramJudgmentReplacement } ||
            p.PlayerSeat != (d.Stage == DelegatedJudgmentStage.Offered ? d.Source.OwnerSeat : d.SubjectSeat) ||
            p.SourceSeat != d.Source.OwnerSeat || p.TargetSeat != d.SubjectSeat) return false;
        return p.ValidCardIds.SequenceEqual(d.Stage == DelegatedJudgmentStage.Choosing ? d.Material.Select(c => c.Id) : []) &&
            AssistedChoicesEqual(p.Choices, DelegatedJudgmentChoices(j));
    }
    private IEnumerable<CardSnapshot> DelegatedJudgmentPrivateCards(int viewer) =>
        ActiveJudgment is { DelegatedReplacement: { Stage: DelegatedJudgmentStage.Choosing } d } j &&
        d.SubjectSeat == viewer && IsDelegatedJudgmentPromptValid(j, _pendingDecision)
            ? d.HandView : Array.Empty<CardSnapshot>();
    private bool ResolveAiDelegatedJudgmentChoice()
    {
        if (ActiveJudgment is not { DelegatedReplacement: { Stage: DelegatedJudgmentStage.Offered or DelegatedJudgmentStage.Choosing } d } j) return false;
        var p = _pendingDecision ?? throw new InvalidOperationException("AI delegate has no private prompt.");
        var view = CreateSnapshot(p.PlayerSeat);
        var target = view.Players.Single(c => c.Seat == d.SubjectSeat);
        PromptChoice choice;
        if (d.Stage == DelegatedJudgmentStage.Offered)
        {
            var self = view.Players.Single(c => c.Seat == p.PlayerSeat);
            var helps = self.Seat == target.Seat || self.TeamId is not null && self.TeamId == target.TeamId ||
                (self.Role is Role.Lord or Role.Loyalist) && (target.Role is Role.Lord or Role.Loyalist) ||
                self.Role == Role.Rebel && target.Role == Role.Rebel;
            choice = p.Choices.Single(c => c.Parameters["action"] == (helps ? "delegated-judgment-accept" : "program-judgment-replace-skip"));
        }
        else
        {
            var cards = view.PrivateRevealedCards ?? [];
            var owner = view.Players.Single(c => c.Seat == d.Source.OwnerSeat);
            var available = cards.Concat(owner.Equipment).DistinctBy(c => c.Id).Where(c => p.ValidCardIds.Contains(c.Id));
            var reason = j.Reason;
            bool favorable(CardSnapshot c) => reason switch
            { JudgmentReasons.Lightning => !(c.Suit == Suit.Spade && c.Rank is >= 2 and <= 9),
                JudgmentReasons.Indulgence => c.Suit == Suit.Heart, JudgmentReasons.SupplyShortage => c.Suit == Suit.Club,
                _ => c.Suit is Suit.Heart or Suit.Diamond };
            var id = available.OrderByDescending(favorable).ThenBy(c => CardCatalog.Get(c.Kind).HandKeepValue).ThenBy(c => c.Id).First().Id;
            choice = p.Choices.Single(c => c.Cards.SequenceEqual([id]));
        }
        ResolveProgramJudgmentReplacementChoice(choice); AdvanceRulesAndPublishState(); return true;
    }
}
