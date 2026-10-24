namespace CardGame.Core;

internal readonly record struct ProgramSelectionParticipant(int Seat, bool IsAlive, int? Hp, int MaxHp,
    bool HasHpValue, int HandCount, int EquipmentCount, bool? RuleWounded = null);

internal interface IProgramSelectionSourceQuery : IProgramSelectionQuery
{
    IReadOnlyList<ProgramSelectionParticipant> Participants();
    IReadOnlyList<Card> Hand(int seat);
    IReadOnlyList<Card> Equipment(int seat);
    CardLocation Location(int cardId);
    bool IsHandRestricted(int seat, Card card);
    bool HasCardIdentity(int seat, Card card);
    bool IsUseForbidden(int seat, CardKind kind);
    bool IsPhysicalUseForbidden(int seat, int cardId);
    Suit EffectiveSuit(int seat, Card card);
    CardKind EffectiveInputKind(int seat, Card card) => card.Kind;
    IReadOnlyList<ProgramMultiCardViewAsSelection> Conversions(int seat, CardKind kind);
    bool HasHpLossTargets(int seat);
    bool CanUseSlash(int owner, int target, IReadOnlyList<Card>? cards, bool ignoreDistance);
    bool CanUseGlobal(int owner, CardKind kind);
    bool CanUseOrdinaryTrick(int owner, CardKind? kind, IReadOnlyList<int> physicalCards);
    bool CanRequestFactionSlash(int owner, string faction);
    bool HasNextUseAdjustment(int owner);
    bool IsTargetProhibited(int target, CardKind kind, int owner, IReadOnlyList<Card> cards);
    bool HasSuitShield(int owner, int target, IReadOnlyList<Card> cards);
    int HpLossMaximum(int owner, CardKind kind);
}

[ProgramSelectionModule(ProgramSelectionStage.BeforeSources, 10, SkillProgramEffectOp.RequestFactionCard)]
internal sealed class FactionRequestSelection : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries) =>
        queries.Get<IProgramSelectionSourceQuery>().CanRequestFactionSlash(context.OwnerSeat,
            context.Features.First(SkillProgramEffectOp.RequestFactionCard)!.ProviderFactionId!);
}

[ProgramSelectionModule(ProgramSelectionStage.BeforeSources, 20)]
internal sealed class UnequalHandPairSelection : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries) =>
        !context.Features.SelectsUnequalHandPair || queries.Get<IProgramSelectionSourceQuery>().Participants()
            .Where(player => player.IsAlive && player.Seat != context.OwnerSeat)
            .Select(player => player.HandCount).Distinct().Skip(1).Any();
}

[ProgramSelectionModule(ProgramSelectionStage.Sources, 10, SkillProgramEffectOp.UseSelectedCardsAs)]
internal sealed class ConvertedCardSelectionSource : IProgramSelectionSourceProvider
{
    internal static SkillProgramEffect? HpLossProducer(ProgramExecutionPlan plan) =>
        plan.Activation is { MinTargets: 1, MaxTargets: 1 } && plan.Instructions is
            [{ Op: SkillProgramEffectOp.UseSelectedCardsAs, OutputKind: CardKind.Slash or CardKind.FireSlash } effect] ? effect : null;

    public bool TryBuild(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        var effect = context.Features.Single(SkillProgramEffectOp.UseSelectedCardsAs);
        if (effect is null) return false;
        var query = queries.Get<IProgramSelectionSourceQuery>();
        var variable = effect is { SourceBind: { } bind, OutputKind: { } kind }
            ? context.Program.ViewAs.FirstOrDefault(rule => rule.VariableInputCount && rule.Id == bind &&
                rule.OutputKind == kind && rule.ForPlay && !rule.ForResponse) : null;
        var selections = query.Conversions(context.OwnerSeat, effect.OutputKind!.Value)
            .Where(item => item.Source.SkillId == context.SkillId && item.Source.BindingId == effect.SourceBind)
            .Where(item => !query.HasHpLossTargets(context.OwnerSeat) || HpLossProducer(context.Plan) is null ||
                item.Cards.All(card => !card.IsGeneralWeapon)).ToArray();
        candidates.MultiCardUses = Array.AsReadOnly(selections);
        candidates.VariableInput = variable is not null;
        candidates.Cards = variable is not null ? VariableCards(context.OwnerSeat, variable, query) :
            selections.SelectMany(item => item.Cards).Select(card => card.Id).Distinct().Order().ToArray();
        return true; // A claimed empty source must not fall back to ordinary sources.
    }

    public void BuildTargets(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        var query = queries.Get<IProgramSelectionSourceQuery>();
        var effect = context.Features.Single(SkillProgramEffectOp.UseSelectedCardsAs)!;
        candidates.Targets = candidates.VariableInput ? query.Participants()
            .Where(target => query.CanUseSlash(context.OwnerSeat, target.Seat, null, true)).Select(target => target.Seat).Order().ToArray() :
            (effect.OutputKind is CardKind.ArrowBarrage or CardKind.Peach) ? Array.Empty<int>() : query.Participants()
                .Where(target => candidates.MultiCardUses.Any(selection => query.CanUseSlash(context.OwnerSeat, target.Seat, selection.Cards, false)))
                .Select(target => target.Seat).Order().ToArray();
    }

    private static int[] VariableCards(int owner, SkillProgramViewAs rule, IProgramSelectionSourceQuery query)
    {
        if (!query.Participants().Single(player => player.Seat == owner).IsAlive || query.IsUseForbidden(owner, rule.OutputKind)) return [];
        return query.Hand(owner).Concat(query.Equipment(owner))
            .Where(card => !query.IsHandRestricted(owner, card) && !query.HasCardIdentity(owner, card))
            .Where(card => rule.SourceZones.Contains(query.Location(card.Id).Zone))
            .Where(card => rule.InputKinds.Count == 0 || ProgramCardKindMatch.Contains(rule.InputKinds, query.EffectiveInputKind(owner, card)))
            .Where(card => rule.InputSuits.Count == 0 || rule.InputSuits.Contains(rule.UseEffectiveInputSuit == true
                ? query.EffectiveSuit(owner, card) : card.Suit))
            .Where(card => !query.IsPhysicalUseForbidden(owner, card.Id)).Select(card => card.Id).Order().ToArray();
    }
}

[ProgramSelectionModule(ProgramSelectionStage.Sources, 20, SkillProgramEffectOp.UseAllHandCardsAsOrdinaryTrick)]
internal sealed class AllHandTrickSelectionSource : IProgramSelectionSourceProvider
{
    public bool TryBuild(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        var query = queries.Get<IProgramSelectionSourceQuery>();
        var hand = query.Hand(context.OwnerSeat);
        candidates.AllHandInput = true;
        candidates.Cards = hand.Any(card => query.IsHandRestricted(context.OwnerSeat, card)) ? [] : hand.Select(card => card.Id).Order().ToArray();
        return true;
    }
    public void BuildTargets(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries) =>
        candidates.Targets = [];
}

[ProgramSelectionModule(ProgramSelectionStage.Availability, 10, SkillProgramEffectOp.UseSelectedCardsAs)]
internal sealed class ConvertedSelectionAvailability : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries) =>
        (candidates.MultiCardUses.Count != 0 || candidates.VariableInput) &&
        (context.Features.Single(SkillProgramEffectOp.UseSelectedCardsAs) is not { OutputKind: CardKind.ArrowBarrage } ||
         queries.Get<IProgramSelectionSourceQuery>().CanUseGlobal(context.OwnerSeat, CardKind.ArrowBarrage));
}

[ProgramSelectionModule(ProgramSelectionStage.Availability, 20, SkillProgramEffectOp.UseAllHandCardsAsOrdinaryTrick)]
internal sealed class AllHandTrickAvailability : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries) =>
        candidates.Cards.Length != 0 && queries.Get<IProgramSelectionSourceQuery>().CanUseOrdinaryTrick(context.OwnerSeat,
            context.Features.Single(SkillProgramEffectOp.UseAllHandCardsAsOrdinaryTrick)!.OutputKind, candidates.Cards);
}

[ProgramSelectionModule(ProgramSelectionStage.FinalTargets, 10)]
internal sealed class AdjustedUseSelection : IProgramSelectionCandidateFilter
{
    internal static SkillProgramEffect? Producer(ProgramExecutionPlan plan) =>
        plan.Activation is { MinTargets: 1, MaxTargets: 1 } && plan.Instructions.Count == 1 &&
            plan.Instructions[0] is { Op: SkillProgramEffectOp.UseSelectedCardsAs, OutputKind: CardKind.Slash or CardKind.FireSlash } slash ? slash :
        plan.Activation is { MinTargets: 0, MaxTargets: 0 } && plan.Instructions.Count == 1 &&
            plan.Instructions[0] is { Op: SkillProgramEffectOp.UseSelectedCardsAs, OutputKind: CardKind.ArrowBarrage or CardKind.Peach } global ? global : null;

    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        var query = queries.Get<IProgramSelectionSourceQuery>();
        candidates.Targets = SelectTargets(context.OwnerSeat, context.Plan, candidates.Targets, candidates.MultiCardUses, query).ToArray();
        return true;
    }

    internal static IReadOnlyList<int> SelectTargets(int ownerSeat, ProgramExecutionPlan plan, IReadOnlyList<int> normal,
        IReadOnlyList<ProgramMultiCardViewAsSelection> selections, IProgramSelectionSourceQuery query)
    {
        var producer = query.HasNextUseAdjustment(ownerSeat) ? Producer(plan) : null;
        if (producer is { OutputKind: CardKind.ArrowBarrage or CardKind.Peach })
        {
            var kind = producer.OutputKind.Value;
            return query.Participants().Where(player => player.IsAlive && player.Seat != ownerSeat &&
                (kind != CardKind.Peach || (player.RuleWounded ?? player.Hp < player.MaxHp)) && selections.Any(selection =>
                    !query.IsTargetProhibited(player.Seat, kind, ownerSeat, selection.Cards) &&
                    !query.HasSuitShield(ownerSeat, player.Seat, selection.Cards)))
                .Select(player => player.Seat).Order().ToArray();
        }
        return normal;
    }
}

[ProgramSelectionModule(ProgramSelectionStage.Projection, 20)]
internal sealed class AdjustedUseSelectionMaximum : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        var query = queries.Get<IProgramSelectionSourceQuery>();
        var maximum = context.Activation.MaxTargets + (query.HasNextUseAdjustment(context.OwnerSeat) &&
            AdjustedUseSelection.Producer(context.Plan) is not null ? 1 : 0);
        candidates.MaxTargetCount = query.HasHpLossTargets(context.OwnerSeat) &&
            ConvertedCardSelectionSource.HpLossProducer(context.Plan) is { OutputKind: { } output }
            ? Math.Max(maximum, query.HpLossMaximum(context.OwnerSeat, output)) : maximum;
        return true;
    }
}

[ProgramSelectionModule(ProgramSelectionStage.Projection, 10, SkillProgramEffectOp.UseAllHandCardsAsOrdinaryTrick)]
internal sealed class AllHandSelectionCounts : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext context, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    {
        candidates.MinCardCount = candidates.Cards.Length;
        if (context.Activation.CardCountExpression is null) candidates.MaxCardCount = candidates.Cards.Length;
        return true;
    }
}
