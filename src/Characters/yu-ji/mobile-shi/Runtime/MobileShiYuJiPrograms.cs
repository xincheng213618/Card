namespace CardGame.Core;

// 符济 marks travel with the physical card: one fact per given card, replayed
// from the command journal. All use-time effects derive from these facts only.
public sealed record MobileShiFujiCardMarkedEvent(
    long FrameId, CardConversionSource Source, string GameplayHash, int OwnerSeat,
    int RecipientSeat, int CardId, CardKind Kind, Suit Suit) : IGameEvent;

public sealed record MobileShiFujiCardUsedEvent(
    long ActionId, int UserSeat, int CardId, CardKind Kind, CardKind Name, bool Empowered) : IGameEvent;

public sealed record MobileShiFujiSuitGainIssuedEvent(
    long ActionId, int UserSeat, int CardId, Suit Suit, int GainedCardId, long MovementSequence) : IGameEvent;

public sealed record MobileShiFujiEmpowerIssuedEvent(long FrameId, int OwnerSeat) : IGameEvent;

public sealed record MobileShiFujiDodgeDrawIssuedEvent(long ActionId, int UserSeat, int CardId, bool Empowered) : IGameEvent;

public sealed record MobileShiDaozhuanUseRecordedEvent(
    int OwnerSeat, CardKind Name, long ActionId, int TurnNumber, int RoundNumber, bool OwnerIsTurnPlayer) : IGameEvent;

internal static class MobileShiYuJiComposition
{
    internal const string FujiSkill = "mobile:shi-fuji";
    internal const string DaozhuanSkill = "mobile:shi-daozhuan";
    internal const string FujiEmpoweredState = "fuji-empowered";
    internal const string FujiGiftActivation = "gift";
    internal const string FujiUseEffectsTrigger = "fuji-use-effects";
    internal const string FujiDodgeDrawTrigger = "fuji-dodge-draw";

    internal static CardKind Name(CardKind kind) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Mobile_Shi_Yu_Ji.NormalizedName(kind);

    private static bool IsSingleCommittedUse(SkillProgramTrigger trigger) =>
        trigger.Window == SkillProgramTriggerWindow.CardUseCommitted && trigger.Subject is null &&
        !trigger.Optional && !trigger.IsSkillInvocation && trigger.IncludeResponseUses && trigger.SingleActionInstance &&
        trigger.UsageScope is null && trigger.UsageLimit is null && trigger.DynamicUsageLimit is null &&
        trigger.NamedUsageGroup is null && trigger.MarkerCost is null && trigger.CardKinds.Count == 0 &&
        trigger.CardCategories.Count == 0 && trigger.RequireNoCardConversion is null;

    internal static bool IsFujiUseTrigger(SkillProgramTrigger trigger) =>
        IsSingleCommittedUse(trigger) && trigger.Id == FujiUseEffectsTrigger &&
        trigger.OwnerRelation == SkillProgramCardActionOwnerRelation.Observer && !trigger.AllowNoEventTarget &&
        trigger.SourceSkillId is null && trigger.SourceViewAsId is null &&
        trigger.Condition.Kind == SkillProgramTriggerConditionKind.Always && trigger.Effects is
        [{ Op: SkillProgramEffectOp.ResolveMobileShiFujiUseEffects, Target: SkillProgramEffectTarget.Owner,
            Condition.Kind: SkillProgramConditionKind.Always }];

    [ProgramCatalogValidation(67200)]
    internal static void ValidateProgram(string path, SkillProgram program)
    {
        if (program.Id == FujiSkill)
        {
            if (program.ViewAs.Count != 0 || program.Modifiers.Count != 0 || program.DamageModifiers.Count != 0 ||
                program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 || program.HpSkillGrants.Count != 0 ||
                program.Contributions.Count != 0 || program.Activations is not [{ Id: FujiGiftActivation }] ||
                program.BooleanStates is not [{ Id: FujiEmpoweredState }] ||
                program.Triggers is not [{ Id: "empowered-expire" }, { Id: FujiUseEffectsTrigger }, { Id: FujiDodgeDrawTrigger }] ||
                program.Activations[0].Effects is not
                    [{ Op: SkillProgramEffectOp.GiveSelected, Target: SkillProgramEffectTarget.SelectedTarget },
                     { Op: SkillProgramEffectOp.ResolveMobileShiFujiGift, Target: SkillProgramEffectTarget.Owner }] ||
                !IsFujiUseTrigger(program.Triggers[1]) ||
                program.Triggers[2] is not { Window: SkillProgramTriggerWindow.CardResponseCompleted,
                    OwnerRelation: SkillProgramCardActionOwnerRelation.Observer, Subject: null, Optional: false,
                    SingleActionInstance: false, IncludeResponseUses: false, AllowNoEventTarget: false } ||
                program.Triggers[2].Effects is not [{ Op: SkillProgramEffectOp.ResolveMobileShiFujiDodgeDraw, Target: SkillProgramEffectTarget.Owner }])
                throw new InvalidOperationException(path + ": Fuji requires one give-then-mark activation, its empowered state and the exact use-effect triggers.");
            return;
        }
        if (program.Id != DaozhuanSkill) return;
        if (program.Modifiers.Count != 0 || program.DamageModifiers.Count != 0 || program.CardPolicies.Count != 0 ||
            program.CardIdentities.Count != 0 || program.HpSkillGrants.Count != 0 || program.Contributions.Count != 0 ||
            program.Activations.Count != 0 || program.BooleanStates.Count != 0 || program.Triggers.Count != 0 ||
            program.ViewAs.Count != 6 ||
            program.ViewAs.Select(rule => rule.OutputKind).Distinct().Count() != 6 ||
            program.ViewAs.Any(rule => rule.OutputKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or
                CardKind.Dodge or CardKind.Peach or CardKind.Alcohol)) ||
            program.ViewAs.Any(rule => rule.InputCount != 1 || rule.VariableInputCount ||
                !rule.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) ||
                !rule.UseOnly || !rule.AllowSameKind || rule.UsesPerPhase is not null || rule.UsageGroup is not null ||
                rule.ExtendedUse || rule.UsesPerActualTurn is not null ||
                rule.ForPlay != (rule.OutputKind != CardKind.Dodge) || rule.ForResponse != (rule.OutputKind == CardKind.Dodge) ||
                rule.Condition.Kind != SkillProgramConditionKind.Always))
            throw new InvalidOperationException(path + ": Daozhuan requires exactly its six one-card basic conversions with native ledger quotas.");
    }
}

internal sealed class MobileShiFujiCommittedUseContract : IProgramCommittedUseContract
{
    public string SkillId => MobileShiYuJiComposition.FujiSkill;
    public bool AllowsSingleActionInstance => true;
    public void ValidateTrigger(SkillProgramTrigger trigger)
    {
        if (!MobileShiYuJiComposition.IsFujiUseTrigger(trigger))
            throw new InvalidOperationException("Fuji requires its exact compulsory single committed-use observer and native use-effect operation.");
    }
}

public interface IMobileShiYuJiProgramHost
{
    SkillProgramStepOutcome ResolveFujiGift(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveFujiUseEffects(ProgramSkillFrame frame);
    SkillProgramStepOutcome ResolveFujiDodgeDraw(ProgramSkillFrame frame);
}

internal sealed class ResolveMobileShiFujiGiftDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShiFujiGift;
    public override ISkillProgramEffectHandler Handler { get; } = new MobileShiYuJiHandler(SkillProgramEffectOp.ResolveMobileShiFujiGift);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift,
        static (_, context) => context.PublicControlValue(2));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
}

internal sealed class ResolveMobileShiFujiUseEffectsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShiFujiUseEffects;
    public override ISkillProgramEffectHandler Handler { get; } = new MobileShiYuJiHandler(SkillProgramEffectOp.ResolveMobileShiFujiUseEffects);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted)];
}

internal sealed class ResolveMobileShiFujiDodgeDrawDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMobileShiFujiDodgeDraw;
    public override ISkillProgramEffectHandler Handler { get; } = new MobileShiYuJiHandler(SkillProgramEffectOp.ResolveMobileShiFujiDodgeDraw);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(new(SkillProgramEffectOp.Draw, SkillProgramEffectTarget.Owner, 1, effect.Condition)));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardResponseCompleted)];
}

internal sealed class MobileShiYuJiHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) => op switch
    {
        SkillProgramEffectOp.ResolveMobileShiFujiUseEffects => host.GetCapability<IMobileShiYuJiProgramHost>().ResolveFujiUseEffects(f),
        SkillProgramEffectOp.ResolveMobileShiFujiDodgeDraw => host.GetCapability<IMobileShiYuJiProgramHost>().ResolveFujiDodgeDraw(f),
        _ => host.GetCapability<IMobileShiYuJiProgramHost>().ResolveFujiGift(f)
    };
}
