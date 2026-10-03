namespace CardGame.Core;

/// <summary>A paid one-card recast owned by its exact program instruction.</summary>
public sealed record ProgramBoundCardRecastReceipt(int InstructionIndex, string SourceBind,
    int CardId, CardKind CardKind, CardLocation SourceLocation,
    bool DrawApplied = false, int DrawCount = 0);

public interface IRecastBoundCardProgramEffectHost
{
    SkillProgramStepOutcome RecastBoundCard(ProgramSkillFrame frame, string sourceBind);
}

public sealed class RecastBoundCardSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RecastBoundCard;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) =>
        ((IRecastBoundCardProgramEffectHost)host).RecastBoundCard(frame, effect.SourceBind!);
}

internal sealed class RecastBoundCardProgramOperationDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecastBoundCard;
    public override ISkillProgramEffectHandler Handler { get; } = new RecastBoundCardSkillProgramEffectHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Move,
        static (effect, context) =>
        {
            context.Move(new(SkillProgramEffectOp.MoveBoundCards, SkillProgramEffectTarget.Owner, 0,
                new(SkillProgramConditionKind.Always, 0, []), sourceBind: effect.SourceBind,
                destination: SkillProgramCardDestination.DiscardPile));
            context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, new(SkillProgramConditionKind.Always, 0, [])));
        });

    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "condition");
        var target = r.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != SkillProgramEffectTarget.Owner)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}.target: recastBoundCard requires owner.");
        var effect = new SkillProgramEffect(Op, target, 0, r.Condition(), sourceBind: r.RequiredIdentifier("sourceBind"));
        RequireAlways(effect, r.Path);
        return effect;
    }

    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.DrawPhaseEnded),
         new RequireOwnedCardSet(effect.SourceBind!, SkillProgramEffectTarget.Owner, 1,
            [CardZoneKind.Hand, CardZoneKind.Equipment]),
         new ReadSingleCardSet(effect.SourceBind!),
         new MoveCardSet(effect.SourceBind!, null, SkillProgramCardDestination.DiscardPile)];
}

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IRecastBoundCardProgramEffectHost
    {
        public SkillProgramStepOutcome RecastBoundCard(ProgramSkillFrame frame, string sourceBind) =>
            engine.BeginProgramBoundCardRecast(frame.Id, sourceBind);
    }

    private SkillProgramStepOutcome BeginProgramBoundCardRecast(long frameId, string sourceBind)
    {
        var frame = GetActiveProgramFrame(frameId);
        if (frame.BoundCardRecast is not null)
            throw new InvalidOperationException("A bound-card recast cannot repay its completed cost.");
        var source = GetProgramCardSet(frame, sourceBind);
        var owner = _players[frame.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, frame.SkillId, frame.SkillInstanceId) ||
            source.CardIds.Count != 1 || source.SourceLocations.Count != 1 ||
            source.SourceLocations[0].OwnerSeat != owner.Seat ||
            source.SourceLocations[0].Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
            _cardZones.GetLocation(source.CardIds[0]) != source.SourceLocations[0])
        {
            CancelProgramBindingAndCleanup(frame, "重铸牌或技能来源已失效，未支付成本。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        var card = _cardZones.CardsAt(source.SourceLocations[0]).Single(c => c.Id == source.CardIds[0]);
        ReplaceRuntimeTop(frame with { BoundCardRecast = new(frame.InstructionIndex, sourceBind,
            card.Id, card.Kind, source.SourceLocations[0]) });
        MoveCard(card, source.SourceLocations[0], CardLocation.DiscardPile, CardMoveReasons.RecastDiscard);
        // The paid instruction yields to a fresh dispatcher. Its exact program
        // return owns both cost and reward observers before the executor resumes.
        AdvanceRuntimeProgram(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private bool ResumeProgramBoundCardRecast(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame frame || frame.Id != frameId ||
            frame.BoundCardRecast is not { } receipt) return false;
        var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
        if (frame.InstructionIndex != receipt.InstructionIndex || receipt.InstructionIndex < 1 ||
            receipt.InstructionIndex > plan.Instructions.Count ||
            plan.Instructions[receipt.InstructionIndex - 1].Op != SkillProgramEffectOp.RecastBoundCard ||
            plan.Instructions[receipt.InstructionIndex - 1].SourceBind != receipt.SourceBind ||
            receipt.SourceLocation.OwnerSeat != frame.OwnerSeat ||
            receipt.SourceLocation.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment))
            throw new InvalidOperationException("A paid bound-card recast lost its exact owning instruction.");
        if (TryBeginQueuedRecoveryReplacement(frame.Id, PostEventContinuation.Program) ||
            TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.Program) ||
            TryBeginCardsMovedProgramWindow(frame.Id)) return true;
        if (!receipt.DrawApplied)
        {
            ReplaceRuntimeTop(frame with { BoundCardRecast = receipt with { DrawApplied = true } });
            var owner = _players[frame.OwnerSeat];
            var drawn = _winner == Winner.None && owner.IsAlive &&
                HasRuntimeSkillInstance(owner, frame.SkillId, frame.SkillInstanceId)
                ? DrawCards(owner, 1, true, CardMoveReasons.RecastDraw) : Array.Empty<int>();
            frame = GetActiveProgramFrame(frame.Id);
            ReplaceRuntimeTop(frame with { BoundCardRecast = frame.BoundCardRecast! with { DrawCount = drawn.Count } });
            AdvanceEventRulesAndQueueFact(new CardRecastEvent(owner.Seat, receipt.CardId, receipt.CardKind, drawn.Count));
            return ResumeProgramBoundCardRecast(frame.Id);
        }
        ReplaceRuntimeTop(frame with { BoundCardRecast = null });
        if (_winner != Winner.None)
        {
            CancelProgramBindingAndCleanup(GetActiveProgramFrame(frame.Id), "游戏已结束，重铸后续收益已取消。");
            return true;
        }
        return false;
    }
}
