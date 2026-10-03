namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasPindianRandomPolicy(int seat) =>
        CardPolicies(_players[seat], SkillProgramCardPolicyKind.PindianOpponentRandomHand).Any();

    private bool PindianPolicySourceLive(int seat, CardConversionSource source, SkillProgramCardPolicyKind kind) =>
        _players[seat].IsAlive && CardPolicies(_players[seat], kind).Any(b =>
            b.Source.SkillId == source.SkillId && b.Source.SkillInstanceId == source.SkillInstanceId &&
            b.Policy.Id == source.BindingId);

    private PindianRandomSelection CreatePindianRandomSelection(PindianFrame frame,
        bool deferredTop = false, int? deferredCard = null)
    {
        var opponent = frame.OpponentSeat!.Value;
        var candidates = new[] { (Owner: frame.SourceSeat, Target: opponent), (Owner: opponent, Target: frame.SourceSeat) }
            .SelectMany(p => CardPolicies(_players[p.Owner], SkillProgramCardPolicyKind.PindianOpponentRandomHand)
                .Take(1).Select(b => new PindianRandomCandidate(p.Owner, p.Target,
                    new(b.Source.SkillId, b.Policy.Id, p.Owner, b.Source.SkillInstanceId)))).ToArray();
        return new(Array.AsReadOnly(candidates), Array.AsReadOnly(Array.Empty<PindianRandomReceipt>()),
            DeferredSourceTop: deferredTop, DeferredSourceCardId: deferredCard);
    }

    private bool TryContinuePindianRandomSelection(PindianFrame frame)
    {
        if (frame.OpponentSeat is not { } opponent || frame.Result is not null) return false;
        if (frame.RandomSelection is null)
        {
            if (!HasPindianRandomPolicy(frame.SourceSeat) && !HasPindianRandomPolicy(opponent)) return false;
            frame = frame with { RandomSelection = CreatePindianRandomSelection(frame) };
            ReplaceRuntimeTop(frame);
        }
        var state = frame.RandomSelection!;
        while (state.Index < state.Candidates.Count)
        {
            var candidate = state.Candidates[state.Index];
            if (!PindianPolicySourceLive(candidate.OwnerSeat, candidate.Source, SkillProgramCardPolicyKind.PindianOpponentRandomHand) ||
                !_players[candidate.TargetSeat].IsAlive || GetHand(_players[candidate.TargetSeat]).Count == 0)
            {
                state = state with { Index = state.Index + 1 };
                frame = frame with { RandomSelection = state };
                ReplaceRuntimeTop(frame);
                continue;
            }
            var skill = _contentRegistry.GetSkill(candidate.Source.SkillId);
            var choices = new[]
            {
                new PromptChoice(new ChoiceId($"pindian.{frame.Id}.random-{state.Index}"), "令对方使用随机手牌拼点", [], [],
                    new Dictionary<string, string> { ["action"] = "pindian-random", ["take"] = "true" }),
                new PromptChoice(new ChoiceId($"pindian.{frame.Id}.random-decline-{state.Index}"), "不发动", [], [],
                    new Dictionary<string, string> { ["action"] = "pindian-random", ["take"] = "false" })
            };
            SetPindianPrompt(frame, candidate.OwnerSeat, DecisionKind.SkillModule,
                $"【{skill.Name}】：是否令 {_players[candidate.TargetSeat].Name} 使用随机手牌拼点？", choices, [], [],
                new(candidate.Source.SkillId, skill.Name, skill.Name + " · 随机拼点", skill.Description));
            return true;
        }
        if (state.DeferredSourceTop || state.DeferredSourceCardId is not null)
        {
            var forced = state.Receipts.Any(r => r.TargetSeat == frame.SourceSeat);
            if (!forced)
                frame = frame with
                {
                    SourceCardId = state.DeferredSourceTop ? ReserveOrReadPindianTop(_players[frame.SourceSeat], true) : state.DeferredSourceCardId,
                    SourceUsesDrawPileTop = state.DeferredSourceTop
                };
            state = state with { DeferredSourceTop = false, DeferredSourceCardId = null };
            frame = frame with { RandomSelection = state };
        }
        frame = frame with { PindianStep = frame.SourceCardId is null ? PindianStep.ChooseSourceCard : PindianStep.ChooseOpponentCard };
        ReplaceRuntimeTop(frame);
        if (frame.SourceCardId is not null && state.ForcedOpponentCardId is { } forcedOpponent)
        { RevealPindian(frame, forcedOpponent); return true; }
        return false;
    }

    private bool TryResolvePindianRandomChoice(PindianFrame frame, PromptChoice selected)
    {
        if (selected.Parameters.GetValueOrDefault("action") == "pindian-random")
        {
            var state = frame.RandomSelection ?? throw new InvalidOperationException("Random Pindian lost its owning receipt.");
            var candidate = state.Candidates[state.Index];
            ClearPendingDecision();
            if (selected.Parameters["take"] == "true" &&
                PindianPolicySourceLive(candidate.OwnerSeat, candidate.Source, SkillProgramCardPolicyKind.PindianOpponentRandomHand) &&
                _players[candidate.TargetSeat].IsAlive && GetHand(_players[candidate.TargetSeat]).Count > 0)
            {
                if (candidate.TargetSeat == frame.SourceSeat && frame.SourceUsesDrawPileTop)
                    throw new InvalidOperationException("Random Pindian cannot replace an already reserved top source.");
                var hand = GetHand(_players[candidate.TargetSeat]).OrderBy(card => card.Id).ToArray();
                var before = _random.State;
                var cardId = hand[_random.Next(hand.Length)].Id;
                var receipt = new PindianRandomReceipt(candidate.OwnerSeat, candidate.TargetSeat, candidate.Source, cardId, before, _random.State);
                state = state with { Receipts = Array.AsReadOnly(state.Receipts.Append(receipt).ToArray()) };
                if (candidate.TargetSeat == frame.SourceSeat)
                {
                    frame = frame with { SourceCardId = cardId, SourceUsesDrawPileTop = false };
                }
                else state = state with { ForcedOpponentCardId = cardId };
                // Commit the identity and RNG state on the original frame before publishing the public activation.
                frame = frame with { RandomSelection = state };
                ReplaceRuntimeTop(frame);
                AdvanceEventRulesAndQueueFact(new PindianRandomHandCommittedEvent(frame.Id, candidate.OwnerSeat, candidate.TargetSeat, candidate.Source));
                AddLog("SkillTriggered", $"{_players[candidate.OwnerSeat].Name} 发动【{_contentRegistry.GetSkill(candidate.Source.SkillId).Name}】，令对方使用随机手牌拼点。", candidate.OwnerSeat, candidate.TargetSeat);
            }
            frame = frame with { RandomSelection = state with { Index = state.Index + 1 } };
            ReplaceRuntimeTop(frame);
            PublishPindianSelection(frame);
            return true;
        }
        if (frame.PindianStep != PindianStep.ChooseParticipants || frame.RandomSelection is not null) return false;
        var opponent = selected.Targets.Single();
        if (!HasPindianRandomPolicy(frame.SourceSeat) && !HasPindianRandomPolicy(opponent)) return false;
        ClearPendingDecision();
        frame = frame with { OpponentSeat = opponent, PindianStep = PindianStep.ChooseSourceCard };
        frame = frame with { RandomSelection = CreatePindianRandomSelection(frame,
            selected.Parameters.GetValueOrDefault("action") == "pindian-top", selected.Cards.Count == 1 ? selected.Cards[0] : null) };
        ReplaceRuntimeTop(frame);
        PublishPindianSelection(frame);
        return true;
    }

    private bool IsPindianRandomPrompt(PindianFrame frame) => frame.RandomSelection is { } state && state.Index < state.Candidates.Count;

    private bool AssertPindianRandomSelection(PindianFrame frame, PendingDecision decision)
    {
        if (frame.RandomSelection is not { } state) return false;
        if (state.Candidates is not System.Collections.ObjectModel.ReadOnlyCollection<PindianRandomCandidate> ||
            state.Receipts is not System.Collections.ObjectModel.ReadOnlyCollection<PindianRandomReceipt> ||
            state.Index < 0 || state.Index > state.Candidates.Count ||
            state.Receipts.GroupBy(r => r.TargetSeat).Any(g => g.Count() != 1) ||
            state.Receipts.Any(r => r.Source.OwnerSeat != r.OwnerSeat || r.OwnerSeat == r.TargetSeat ||
                r.RandomBefore == r.RandomAfter || frame.Result is null &&
                !GetHand(_players[r.TargetSeat]).Any(c => c.Id == r.CardId)))
            throw new InvalidOperationException("Random Pindian lost its frozen identities or RNG receipt.");
        if (!IsPindianRandomPrompt(frame)) return false;
        var candidate = state.Candidates[state.Index];
        if (decision.PlayerSeat != candidate.OwnerSeat || decision.SkillPrompt!.SkillId != candidate.Source.SkillId ||
            decision.ValidCardIds.Count != 0 || decision.ValidTargetSeats.Count != 0 || decision.Choices.Count != 2 ||
            decision.Choices.Any(c => c.Cards.Count != 0 || c.Targets.Count != 0 || c.Parameters.GetValueOrDefault("action") != "pindian-random"))
            throw new InvalidOperationException("Random Pindian selection leaked a hand identity or lost its owner.");
        return true;
    }
}
