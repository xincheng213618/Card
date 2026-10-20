namespace CardGame.Core;
public enum LianZhuStage { GiftChildren, RecipientChoice, SelectingDiscard, DiscardChildren, DrawChildren }
public sealed record LianZhuSegmentPaidEvent(long FrameId, CardConversionSource Source, string GameplayHash, LianZhuStage Stage, int ActorSeat, long Before, long After, int Count) : IGameEvent;
public sealed record LianZhuChoiceAmountReceipt(int RecipientHp, int Amount);
public sealed record LianZhuChoiceAmountCapturedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int RecipientSeat, int RecipientHp, int Amount, long GiftAfter) : IGameEvent;
public sealed record LianZhuReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex, int TurnNumber, int TurnOwnerSeat,
    int RecipientSeat, int GiftCardId, Suit GiftSuit, long GiftBefore, long GiftAfter, LianZhuStage Stage,
    long Before, long After, int Count, IReadOnlyList<int>? Selected = null, IReadOnlyList<CardLocation>? Locations = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CardLocation? GiftFrom { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public LianZhuChoiceAmountReceipt? CountPolicy { get; init; }
    private readonly IReadOnlyList<int> _selected = Array.AsReadOnly((Selected ?? []).ToArray());
    public IReadOnlyList<int> Selected { get => _selected; init => _selected = Array.AsReadOnly((value ?? []).ToArray()); }
    private readonly IReadOnlyList<CardLocation> _locations = Array.AsReadOnly((Locations ?? []).ToArray());
    public IReadOnlyList<CardLocation> Locations { get => _locations; init => _locations = Array.AsReadOnly((value ?? []).ToArray()); }
}
internal interface IDongBaiProgramHost { SkillProgramStepOutcome BeginLianZhu(ProgramSkillFrame frame, int recipient); }
internal sealed class RevealHandGiftAndResolveRecipientChoiceDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RevealHandGiftAndResolveRecipientChoice;
    public override ISkillProgramEffectHandler Handler { get; } = new RevealHandGiftAndResolveRecipientChoiceHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift,
        static (effect, context) => { if (effect.NumberExpression == SkillProgramNumberExpression.RecipientCurrentHp) context.GiveSelected(effect); });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition", "numberExpression");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.SelectedTarget) throw new InvalidOperationException("LianZhu requires its other selected recipient.");
        var expression = r.Has("numberExpression") ? r.RequiredEnum<SkillProgramNumberExpression>("numberExpression") : (SkillProgramNumberExpression?)null;
        if (expression is not (null or SkillProgramNumberExpression.RecipientCurrentHp))
            throw new InvalidOperationException($"{r.Path}.numberExpression: the gift recipient choice only supports recipientCurrentHp.");
        var e = new SkillProgramEffect(Op, SkillProgramEffectTarget.SelectedTarget, 0, r.Condition(), numberExpression: expression); RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget(), new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving), new ConsumeSelectedCards(1)];
}
internal sealed class RevealHandGiftAndResolveRecipientChoiceHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RevealHandGiftAndResolveRecipientChoice;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => h.GetCapability<IDongBaiProgramHost>().BeginLianZhu(f, seat);
}
internal static class DongBaiComposition
{
    internal static void Activation(string path, SkillProgramActivation a)
    {
        if (!a.Effects.Any(e => e.Op == SkillProgramEffectOp.RevealHandGiftAndResolveRecipientChoice)) return;
        var dynamic = a.Effects is [{ NumberExpression: SkillProgramNumberExpression.RecipientCurrentHp }];
        var zones = dynamic ? a.SourceZones.Count > 0 && a.SourceZones.Distinct().Count() == a.SourceZones.Count &&
            a.SourceZones.All(zone => zone is CardZoneKind.Hand or CardZoneKind.Equipment) : a.SourceZones.SequenceEqual([CardZoneKind.Hand]);
        if (a.Effects is not [{ Op: SkillProgramEffectOp.RevealHandGiftAndResolveRecipientChoice }] || a.MinCards != 1 || a.MaxCards != 1 || a.MinTargets != 1 || a.MaxTargets != 1 || a.TargetKind != SkillProgramTargetKind.OtherLiving || !zones || a.UsesPerPhase != 1 || a.Condition.Kind != SkillProgramConditionKind.Always) throw new InvalidOperationException($"{path}: LianZhu requires its exact once-per-Play one-card gift; HE sources require explicit recipientCurrentHp.");
    }
}
