namespace CardGame.Core;

internal interface IPublicPersistentPileProgramHost
{
    SkillProgramStepOutcome ExecutePublicPile(SkillProgramEffect effect, ProgramSkillFrame frame);
}

internal abstract class PublicPileOperationDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Move, static (effect, context) => context.PublicPersistentPile(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "skillIds", "condition");
        var owner = FilterBoundCardsProgramOperationDescriptor.Owner(r);
        var capacity = r.RequiredInt("amount");
        var skills = r.Has("skillIds") ? r.RequiredIdentifierArray("skillIds") : [];
        if (capacity is < 1 or > 16 || (Op == SkillProgramEffectOp.StoreTopCardInPublicPile ? skills.Count != 0 : skills.Count != 1))
            throw new InvalidOperationException($"Public pile requires a bounded capacity and one explicit source skill for exchange/distribution at {r.Path}.");
        var effect = new SkillProgramEffect(Op, owner, capacity, r.Condition(), skillIds: skills);
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        Op == SkillProgramEffectOp.StoreTopCardInPublicPile ? [new RequireContext(ProgramContextCapability.CardAction)] : [new RequireTriggerWindow(SkillProgramTriggerWindow.DrawPhaseEnded)];
}
internal sealed class StoreTopCardInPublicPileDescriptor : PublicPileOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.StoreTopCardInPublicPile;
    public override ISkillProgramEffectHandler Handler { get; } = new StoreTopCardInPublicPileHandler();
}
internal sealed class ExchangePublicPileDescriptor : PublicPileOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ExchangePublicPile;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler { get; } = new ExchangePublicPileHandler();
}
internal sealed class DistributePublicPileIfAllSuitsDescriptor : PublicPileOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DistributePublicPileIfAllSuits;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler { get; } = new DistributePublicPileIfAllSuitsHandler();
}
public abstract class PublicPileHandler : ISkillProgramEffectHandler
{
    public abstract SkillProgramEffectOp Op { get; }
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        ((IPublicPersistentPileProgramHost)host).ExecutePublicPile(effect, frame);
}
public sealed class StoreTopCardInPublicPileHandler : PublicPileHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.StoreTopCardInPublicPile; }
public sealed class ExchangePublicPileHandler : PublicPileHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ExchangePublicPile; }
public sealed class DistributePublicPileIfAllSuitsHandler : PublicPileHandler { public override SkillProgramEffectOp Op => SkillProgramEffectOp.DistributePublicPileIfAllSuits; }

public sealed record PublicPersistentPileSource(int OwnerSeat, string SkillId, string SkillInstanceId, int Capacity,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? PublicPileId = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public CardLocation Location => new(CardZoneKind.PublicPersistentPile, OwnerSeat, PublicPileId);
}
public sealed record ProgramPublicPileDraft(int OwnerSeat, string SourceSkillId, string Stage, int Capacity,
    IReadOnlyList<int> OwnedIds, IReadOnlyList<CardLocation> OwnedLocations, IReadOnlyList<int> PileIds,
    IReadOnlyList<int> RemainingIds,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? SourceSkillInstanceId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] CardLocation? SourceLocation = null);

public sealed record PublicPersistentPileSnapshot(int OwnerSeat, string SourceSkillId, string SourceSkillInstanceId,
    string? Name, IReadOnlyList<CardSnapshot> Cards, int Count, CardLocation Location);
