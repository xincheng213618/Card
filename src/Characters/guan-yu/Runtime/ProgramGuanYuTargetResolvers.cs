namespace CardGame.Core;

internal readonly record struct ProgramTargetParticipantFacts(int Seat, bool IsAlive, bool HasHp, int Hp, int MaxHp);

internal interface IGuanYuTargetSelectionQuery : IProgramSelectionQuery
{
    int RoundNumber { get; }
    long? CurrentProgramFrameId { get; }
    IReadOnlyList<ProgramTargetParticipantFacts> Participants { get; }
    IReadOnlyList<DemonRoundFocusEvent> FocusHistory { get; }
    IReadOnlyList<DemonSupportGrantedEvent> SupportHistory { get; }
}

[ProgramTargetResolver(SkillProgramTargetKind.OtherLivingNotLastRoundFocus)]
internal sealed class PreviousRoundFocusTargetResolver : IProgramTargetResolver
{
    public IReadOnlyList<int> Resolve(ProgramTargetResolverContext context, IProgramSelectionQueries queries)
    {
        var query = queries.Get<IGuanYuTargetSelectionQuery>();
        var history = query.FocusHistory;
        var previousRound = query.RoundNumber - 1;
        return Array.AsReadOnly(query.Participants.Where(player => player.IsAlive && player.Seat != context.OwnerSeat &&
            !history.Any(fact => fact.Source.OwnerSeat == context.OwnerSeat && fact.TargetSeat == player.Seat && fact.RoundNumber == previousRound))
            .Select(player => player.Seat).ToArray());
    }
}

[ProgramTargetResolver(SkillProgramTargetKind.ExclusiveRendeBeneficiary, SkillProgramTargetKind.ExclusiveQingnangBeneficiary)]
internal sealed class ExclusiveSupportTargetResolver : IProgramTargetResolver
{
    public IReadOnlyList<int> Resolve(ProgramTargetResolverContext context, IProgramSelectionQueries queries)
    {
        var query = queries.Get<IGuanYuTargetSelectionQuery>();
        var participants = query.Participants.ToDictionary(player => player.Seat);
        var skillId = context.TargetKind == SkillProgramTargetKind.ExclusiveRendeBeneficiary ? "magic:rende" : "magic:qingnang";
        return Array.AsReadOnly(query.SupportHistory.Where(fact => fact.RecipientSeat == context.OwnerSeat && fact.SkillId == skillId &&
                participants[fact.OwnerSeat].IsAlive)
            .Select(fact => fact.OwnerSeat).Distinct().Order()
            .Where(seat => context.TargetKind != SkillProgramTargetKind.ExclusiveQingnangBeneficiary ||
                participants[seat].HasHp && participants[seat].Hp < participants[seat].MaxHp).ToArray());
    }
}

[ProgramTargetResolver(SkillProgramTargetKind.OtherLivingWithoutEndingSupport)]
internal sealed class EndingSupportTargetResolver : IProgramTargetResolver
{
    public int MinimumTargets => 0;
    public string EmptySelectionLabel => "不授予此技能。";

    public IReadOnlyList<int> Resolve(ProgramTargetResolverContext context, IProgramSelectionQueries queries)
    {
        var query = queries.Get<IGuanYuTargetSelectionQuery>();
        var frameId = query.CurrentProgramFrameId;
        var history = query.SupportHistory;
        return Array.AsReadOnly(query.Participants.Where(player => player.IsAlive && player.Seat != context.OwnerSeat &&
            !history.Any(fact => fact.FrameId == frameId && fact.RecipientSeat == player.Seat)).Select(player => player.Seat).ToArray());
    }
}
