namespace CardGame.Core;

internal interface ITeamActivityProgramHost
{
    SkillProgramStepOutcome ExecuteTeamActivity(ProgramSkillFrame frame, SkillProgramEffect effect);
}

internal sealed class SelectAllPublicTeamTargetsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectAllPublicTeamTargets;
    public override ISkillProgramEffectHandler Handler { get; } = new TeamActivityEffectHandler(SkillProgramEffectOp.SelectAllPublicTeamTargets);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.SelectTargets,
        static (effect, context) => context.SelectTargets(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "targetKind", "condition");
        var kind = r.RequiredEnum<SkillProgramTargetKind>("targetKind");
        if (kind is not (SkillProgramTargetKind.PublicTeamEnemies or SkillProgramTargetKind.PublicTeamWoundedAllies or
            SkillProgramTargetKind.OtherPublicTeamWoundedAllies or SkillProgramTargetKind.PublicTeamUnchainedEnemies))
            throw new InvalidOperationException($"{r.Path}: a public-team target set is required.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), targetKind: kind,
            minimumTargets: 1, maximumTargets: 8);
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new SelectTargetSet(1, 8)];
}

internal sealed class RepeatCurrentRedTrickDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RepeatCurrentRedTrick;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.UseSelectedCardsAs,
        static (_, _) => { });
    public override ISkillProgramEffectHandler Handler { get; } = new TeamActivityEffectHandler(SkillProgramEffectOp.RepeatCurrentRedTrick);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted)];
}

internal sealed class TeamActivityEffectHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<ITeamActivityProgramHost>().ExecuteTeamActivity(frame, effect);
}

public sealed record PublicTeamTargetsBoundEvent(long ProgramFrameId, int OwnerSeat, SkillProgramTargetKind Kind, int Count) : IGameEvent;
public sealed record AutomaticExtraCardEffectIssuedEvent(long ProgramFrameId, CardConversionSource Source,
    long CardUseFrameId, long ActionId, string GameplayHash) : IGameEvent;

public sealed partial class GameEngine
{
    private bool PublicTeamEnemy(int owner, CharacterState target) => _players[owner].TeamId is { } team &&
        target.TeamId is { } other && team != other;

    [ProgramCapability]
    private sealed class TeamActivityProgramHost(GameEngine engine) : ITeamActivityProgramHost
    {
        public SkillProgramStepOutcome ExecuteTeamActivity(ProgramSkillFrame f, SkillProgramEffect e) => engine.ExecuteTeamActivity(f, e);
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 27400)]
    private bool CanOfferTeamActivity(ProgramTriggerCandidate c, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (trigger.Effects.FirstOrDefault(e => e.Op == SkillProgramEffectOp.SelectAllPublicTeamTargets) is { TargetKind: { } kind } &&
            GetProgramTargetSeats(c.OwnerSeat, kind, context).Count == 0) return false;
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.RepeatCurrentRedTrick))
            return TryGetExtraEffectUse(c.OwnerSeat, context, out var use) && IsOrdinaryTrick(use.CardKind) &&
                context.Facts?.CardActionCardIsRed == true;
        return true;
    }

    private SkillProgramStepOutcome ExecuteTeamActivity(ProgramSkillFrame input, SkillProgramEffect effect)
    {
        var frame = GetActiveProgramFrame(input.Id);
        if (effect.Op == SkillProgramEffectOp.SelectAllPublicTeamTargets)
        {
            var targets = GetProgramTargetSeats(frame.OwnerSeat, effect.TargetKind!.Value, frame.WindowContext);
            if (targets.Count == 0) throw new InvalidOperationException("The accepted public-team target set became empty.");
            ReplaceRuntimeTop(frame with { SelectedTargetSeats = targets.ToArray() });
            AdvanceEventRulesAndQueueFact(new PublicTeamTargetsBoundEvent(frame.Id, frame.OwnerSeat, effect.TargetKind.Value, targets.Count));
            return SkillProgramStepOutcome.Continue;
        }
        if (effect.Op != SkillProgramEffectOp.RepeatCurrentRedTrick || !TryGetExtraEffectUse(frame.OwnerSeat, frame.WindowContext, out var use) ||
            !IsOrdinaryTrick(use.CardKind) || frame.WindowContext?.Facts?.CardActionCardIsRed != true)
            throw new InvalidOperationException("A red-trick repeat requires its original accepted ordinary-trick use.");
        var window = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w => w.Id == frame.WindowContext.ParentFrameId);
        var source = new CardConversionSource(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
        ReplaceRuntimeFrame(use.Id, use with { ExtraCardEffect = new()
        {
            Source = source, ProgramFrameId = frame.Id, ActionId = use.Action!.ActionId,
            AutomaticGameplayHash = frame.GameplayHash, Simple = window.SimpleContinuation, Trick = window.TrickContinuation
        } });
        AdvanceEventRulesAndQueueFact(new AutomaticExtraCardEffectIssuedEvent(frame.Id, source, use.Id, use.Action!.ActionId, frame.GameplayHash));
        return SkillProgramStepOutcome.Continue;
    }
}
