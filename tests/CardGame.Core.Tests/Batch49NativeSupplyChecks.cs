using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class Batch49NativeSupplyChecks
{
    private const string Owner = "fixture:b49-saying-owner", Mode = "identity:classic-b49-supply";
    private const string Supplier = "fixture:b49-supplier", Provider = "fixture:b49-converted-provider", Watch = "fixture:b49-native-watch", Quiet = "fixture:b49-quiet";
    private static string Scene = "";

    public static void SayingZeroSuppliersKeepForeignActorAndNativeReturnsCold()
    {
        foreach (var scene in new[] { "round", "tiered", "slot" })
        {
            Scene = scene;
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
                ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
            Accept(g, new StartGameCommand());
            Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
            Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId));
            Reach(g, () => P(g) is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == "ol:saying" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
            Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            Reach(g, () => Own(g)?.SunYuan?.Stage == SunYuanStage.Target);
            Answer(g, c => c.Targets.SequenceEqual([1]));
            Reach(g, () => Own(g)?.SunYuan?.Stage == SunYuanStage.ObtainCard);
            Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "card" && c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
            Reach(g, () => P(g) is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == Watch);
            var f = Own(g)!; var frameId = f.Id; var issued = E<SayingSlashIssuedEvent>(g).Single(e => e.FrameId == frameId);
            var prepared = E<SayingSlashPreparedEvent>(g).Single(e => e.FrameId == frameId);
            var use = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == issued.UseId);
            Need(prepared.ActorSeat == 1 && prepared.ProviderSeat == 1 && prepared.TargetSeat == 0 &&
                f.SunYuan is { Stage: SunYuanStage.NativeUse, Selection.Materials.Count: 0 } &&
                use.Action is { Type: CardActionType.Use, ActorSeat: 1, ProviderSeat: 1, PhysicalCards.Count: 0 } action &&
                action.ConversionChain is [var source] && source.SkillId == Supplier &&
                !E<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 1 && e.TargetSeat == 0),
                "The actual AI donor supplies one genuinely paid zero-entity Slash to the different Saying owner before its real damage child.");
            Need(E<CardUseDebitRecordedEvent>(g).All(e => e.Debit.CardActionId != issued.ActionId),
                "The native forced Use does not debit an ordinary Play Slash allowance.");
            if (scene == "round")
                Need(E<RoundOnceCardlessBasicQuotaPaidEvent>(g).Count(e => e.Receipt.Source.OwnerSeat == 1 && e.Receipt.Source.SkillId == Supplier) == 1,
                    "The independent zero supplier really spends its once-per-round quota once.");
            else
            {
                var tiered = E<TieredRoundConversionUseIssuedEvent>(g).Single(e => e.Receipt.OwnerFrameId == issued.UseId).Receipt;
                Need(tiered.ActorSeat == 1 && tiered.MaterialCount == 0 && tiered.FrozenTier == 2,
                    "The accepted native tier-two supplier freezes a true zero-material invoice and the actual actor.");
                if (scene == "tiered")
                    Need(E<ConfiguredConversionTierChangedEvent>(g).Where(e => e.OwnerSeat == 1 && e.StateId == "b49-tier").Select(e => e.Tier).SequenceEqual([1, 2]),
                        "The shared tier fixture reaches level two through two real initialization program effects.");
                else
                {
                    var payment = E<EquipmentSlotNamedUsePaidEvent>(g).Single(e => e.Payment.Source.OwnerSeat == 1).Payment;
                    Need(payment.Intent == EquipmentSlotNamedUseIntent.SayingSlash && payment.OriginalParentId == frameId &&
                        E<EquipmentSlotNamedUseConsumedEvent>(g).Count(e => e.PaymentFrameId == payment.FrameId) == 1 &&
                        E<EquipmentSlotNamedUseIssuedEvent>(g).Count(e => e.PaymentFrameId == payment.FrameId && e.NativeOwnerId == issued.UseId) == 1,
                        "A shared configured Yin-basic slot fixture pays a genuine native equipment-slot cost before issuing this exact Saying child; it does not assert OL Youlong's initial face.");
                }
            }
            Need(g.CreateSnapshot(1).PendingDecision is null && g.CreateSnapshot(0).PendingDecision is { IsPrivate: true },
                "Only the actual human damage-child chooser sees its private continuation.");
            var hp = g.CreateSnapshot(0).Players[0].Hp;
            g = Cold(g, registry);
            Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            Reach(g, () => E<SunYuanCompletedEvent>(g).Any(e => e.FrameId == frameId));
            Need(E<SayingSlashReturnedEvent>(g).Count(e => e.FrameId == frameId && e.UseId == issued.UseId && e.ActionId == issued.ActionId) == 1 &&
                E<CardUseDeclaredEvent>(g).Count(e => e.ResolutionId == issued.UseId && e.SourceSeat == 1 && e.CardId == 0) == 1 &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == issued.UseId) == 1 &&
                E<SayingSlashMaterialPaidEvent>(g).All(e => e.UseId != issued.UseId) &&
                E<SunYuanCompletedEvent>(g).Count(e => e.FrameId == frameId) == 1 && g.CreateSnapshot(0).Players[0].Hp == hp - 1,
                "Cold recovery completes the real foreign zero Slash, damage and original Saying parent exactly once, without a fabricated physical cost.");
            if (scene == "round") Need(E<RoundOnceCardlessProgramRequestReturnedEvent>(g).Count(e => e.Receipt.ProgramFrameId == frameId) == 1,
                "The independent supplier's typed return precedes the completed original Saying request.");
            _ = Cold(g, registry);
        }
        SayingConvertedFactionSupplyCold();
    }

    private static void SayingConvertedFactionSupplyCold()
    {
        Scene = "faction-converted";
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(Scene));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(g, new StartGameCommand());
        Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId));
        Reach(g, () => P(g) is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == "ol:saying" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        var actor = g.CreateSnapshot(0).Players.Single(p => p.GeneralId == "fixture:b49-peer-1").Seat;
        Need(actor != 0 && g.GetAttackRange(actor) >= g.GetCombatDistance(actor, 0), "The actual public requester general, rather than its assumed seat, can normally target Saying's owner.");
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, () => Own(g)?.SunYuan?.Stage == SunYuanStage.Target);
        Answer(g, c => c.Targets.SequenceEqual([actor]));
        Reach(g, () => Own(g)?.SunYuan?.Stage == SunYuanStage.ObtainCard);
        Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "card" && c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
        Reach(g, () => P(g) is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == Watch);
        var f = Own(g)!; var prepared = E<SayingSlashPreparedEvent>(g).Single(e => e.FrameId == f.Id);
        var issued = E<SayingSlashIssuedEvent>(g).Single(e => e.FrameId == f.Id);
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == issued.UseId);
        var action = use.Action!; var cost = action.PhysicalCards.Single();
        var source = action.ConversionChain.Single();
        Need(prepared.ActorSeat == actor && prepared.ProviderSeat != actor && prepared.ProviderSeat != 0 && prepared.TargetSeat == 0 &&
            action.Type == CardActionType.Use && action.ActorSeat == actor && action.ProviderSeat == prepared.ProviderSeat && action.RequesterSeat == actor &&
            source.SkillId == Provider && source.OwnerSeat == prepared.ProviderSeat && source.BindingId == "crossbow-slash" &&
            f.SunYuan!.Selection!.Sources.SequenceEqual(action.ConversionChain) && cost.CardKind == CardKind.Crossbow && cost.From == CardLocation.Hand(prepared.ProviderSeat),
            "The real faction ally's selected conversion is frozen in Saying before native capture, with actor, physical provider and requester kept distinct.");
        Need(E<CardSupplyCompletionStartedEvent>(g).Count(e => e.ActionId == issued.ActionId) == 1 &&
            E<CardSupplyCompletedEvent>(g).Count(e => e.ActionId == issued.ActionId) == 1 &&
            E<SayingSlashMaterialPaidEvent>(g).Count(e => e.UseId == issued.UseId && e.CardId == cost.CardId && e.From == cost.From && e.To == CardLocation.Processing) == 1 &&
            E<ProgramBindingResolvedEvent>(g).Any(e => e.SkillId == Quiet && e.OwnerSeat == prepared.ProviderSeat && e.Completed) &&
            !E<DamageAppliedEvent>(g).Any(e => e.SourceSeat == actor && e.TargetSeat == 0),
            "The original converted Crossbow is paid once and its actual native supply-draw child completes before incoming damage: " + JsonSerializer.Serialize(new {
                Starts = E<CardSupplyCompletionStartedEvent>(g).Count(e => e.ActionId == issued.ActionId),
                Completes = E<CardSupplyCompletedEvent>(g).Count(e => e.ActionId == issued.ActionId),
                Paid = E<SayingSlashMaterialPaidEvent>(g).Where(e => e.UseId == issued.UseId).ToArray(),
                SupplyChildren = E<ProgramBindingResolvedEvent>(g).Count(e => e.SkillId == Quiet && e.OwnerSeat == prepared.ProviderSeat && e.Completed),
                Damage = E<DamageAppliedEvent>(g).Where(e => e.SourceSeat == actor && e.TargetSeat == 0).ToArray() }));
        var hp = g.CreateSnapshot(0).Players[0].Hp; var frameId = f.Id;
        g = Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(g, () => E<SunYuanCompletedEvent>(g).Any(e => e.FrameId == frameId));
        Need(E<SayingSlashReturnedEvent>(g).Count(e => e.FrameId == frameId && e.UseId == issued.UseId && e.ActionId == issued.ActionId) == 1 &&
            E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == issued.UseId) == 1 &&
            E<CardSupplyCompletedEvent>(g).Count(e => e.ActionId == issued.ActionId) == 1 &&
            g.CardMovements.Count(m => m.CardId == cost.CardId && m.From == cost.From && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1 &&
            g.CreateSnapshot(0).Players[0].Hp == hp - 1,
            "Cold recovery preserves the converted faction source, completes its whole Use and returns to Saying once without recharging its provider.");
        _ = Cold(g, registry);
    }

    private static ProgramSkillFrame? Own(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.SunYuan is not null);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException("scene=" + Scene + " " + why); }
    private static void Accept(GameEngine g, GameCommand command)
    {
        try { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Native supply command rejected."); }
        catch (Exception e) { throw new InvalidOperationException("scene=" + Scene + " " + command.GetType().Name + " cause=" + e.Message + " " + Diagnostic(g), e); }
    }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> match)
    { var p = P(g); var choice = p?.Choices.FirstOrDefault(match); Need(p?.PlayerSeat == 0 && choice is not null, "Missing actual human choice: " + Diagnostic(g)); Accept(g, new AnswerPromptCommand(0, p!.PromptId, choice!.Id, g.Revision)); }
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var n = 0; n < 120; n++)
        {
            if (done()) return;
            Need(g.State.Winner == Winner.None, "Terminal before the expected native boundary: " + Diagnostic(g));
            var p = P(g);
            if (p is null || p.PlayerSeat != 0) { Accept(g, new AdvanceOneStepCommand(g.Revision)); continue; }
            throw new InvalidOperationException("scene=" + Scene + " Unexpected human boundary: " + Diagnostic(g));
        }
        throw new InvalidOperationException("scene=" + Scene + " Missing native boundary: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.CurrentSeat, g.State.Phase,
        Prompt = P(g) is { } p ? new { p.PlayerSeat, p.Kind, Skill = p.SkillPrompt?.SkillId, Choices = p.Choices.Take(4).Select(c => c.Parameters) } : null,
        Frames = g.ResolutionStack.TakeLast(5).Select(f => new { f.Id, Type = f.GetType().Name, Stage = (f as ProgramSkillFrame)?.SunYuan?.Stage }), LastFacts = g.Events.TakeLast(4).Select(e => e.Payload.ToString()) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    { var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(copy), "Cold replay preserves the exact private request, supplier payment and native parent-child receipts."); return copy; }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-batch49-supply", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:sun-yuan");
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            var supplier = N($$"""
                {"id":"{{Supplier}}","revision":1,"viewAs":[{"id":"native-zero","inputKinds":[],"inputSuits":[],"inputCount":0,"sourceZones":[],"outputKind":"slash","forPlay":true,"forResponse":false}]}
                """);
            var rule = supplier["viewAs"]![0]!;
            if (scene == "faction-converted")
            {
                supplier["viewAs"] = new JsonArray();
                supplier["cardPolicies"] = N("""[{"id":"actual-request","kind":"factionResponseRequest","requiredCardKinds":["slash"],"factionId":"shu"}]""");
                supplier["modifiers"] = N("""[{"id":"native-request-range","query":"attackRange","operation":"add","value":3,"priority":0}]""");
            }
            else if (scene == "round") rule["roundOnceCardlessBasic"] = N("""{"usageGroup":"b49-native-round","usesPerRound":1}""");
            else
            {
                var state = scene == "slot" ? "equipment-slot-named-tier" : "b49-tier";
                rule["useOnly"] = true; rule["allowSameKind"] = true; rule["conversionStateId"] = state; rule["minimumTier"] = 2; rule["maximumTier"] = 2;
                rule["tieredRoundConversion"] = N($$"""{"stateId":"{{state}}","usageId":"yin"}""");
                if (scene == "tiered") supplier["triggers"] = N("""[{"id":"real-tier-initialization","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"upgradeConversionTier","target":"owner","stateId":"b49-tier"},{"op":"upgradeConversionTier","target":"owner","stateId":"b49-tier"}]}]""");
                else
                {
                    supplier["states"] = N("""[{"id":"yin","initialValue":true,"visibility":"public","resetScope":"game","reacquirePolicy":"preserveUntilGameEnd"}]""");
                    supplier["activations"] = N("""[{"id":"equipment-slot-payment","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"payEquipmentSlotForNamedUse","target":"owner"}]}]""");
                }
            }
            var defs = new JsonArray(supplier,
                N($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"true-damage-child","window":"beforeDamageApplied","subject":"damageTarget","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]}]}]}"""),
                N($$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""));
            if (scene == "faction-converted")
            {
                defs.Add(N($$"""{"id":"{{Provider}}","revision":1,"viewAs":[{"id":"crossbow-slash","inputKinds":["crossbow"],"inputSuits":[],"sourceZones":["hand"],"outputKind":"slash","forPlay":true,"forResponse":false}]}"""));
                defs[2]!["triggers"]!.AsArray().Add(N("""{"id":"actual-native-supply-child","window":"cardSupplyCompleted","ownerRelation":"actor","cardKinds":["slash","fireSlash","thunderSlash"],"optional":false,"effects":[{"op":"draw","target":"owner","amount":1}]}"""));
            }
            var labels = defs.Select(n => n!["id"]!.GetValue<string>()).ToDictionary(id => id, id => new { name = id, description = "真实飒影供应边界", optionLabels = id == Watch ? new Dictionary<string, string> { ["continue"] = "继续原生伤害" } : new Dictionary<string, string>() });
            var catalog = SkillProgramCatalog.Load(new JsonObject { ["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion, ["skills"] = defs }.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "真实原生供应") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            const string pick = "fixture:b49-pick";
            b.AddSkill(new(pick, "本人", "固定公开选择") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => -100000000d) });
            b.AddGeneral(new(Owner, "真实飒影本人", "supporter", pick, "wu", 12, ["ol:saying", Watch], GeneralGender.Female));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:b49-peer-" + i).ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "真实供给角色", "supporter", Quiet, scene == "faction-converted" ? "shu" : "wei", 12,
                [scene == "faction-converted" && peer != peers[0] ? Provider : Supplier], GeneralGender.Male));
            b.AddCard(new("fixture:b49-card", "诸葛连弩", "装备牌", "未转换的真实实体", CardKind.Crossbow));
            b.AddDeck(new("fixture:b49-deck", "固定同质真实牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 72).Select(i => new ContentDeckPhysicalCard("fixture:b49-card", Suit.Club, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "真实供应共享机制", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [scene == "faction-converted" ? nameof(Role.Renegade) : nameof(Role.Rebel)] = 3 }, "fixture:b49-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
