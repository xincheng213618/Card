using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class GhostVirtualTrickChecks
{
    private const string Skill = "fixture:ghost-virtual-trick";
    private const string Observer = "fixture:ghost-response-child";
    private const string Armor = "fixture:ghost-virtual-vine";
    private const string Quiet = "fixture:ghost-quiet-peer";
    private const string Owner = "fixture:ghost-virtual-owner";
    private const string Armored = "fixture:ghost-virtual-armored";
    private const string Mode = "fixture:ghost-virtual-activity";
    private const string Transfer = "fixture:ghost-transfer-aoe";
    private const string TransferWatch = "fixture:ghost-transfer-observer";
    private const string TransferQuiet = "fixture:ghost-transfer-quiet";

    public static void GroupUsesRetainNativeResponsesNullificationAndColdReturn()
    {
        foreach (var kind in new[] { CardKind.BarbarianAssault, CardKind.ArrowBarrage })
        {
            _ = Load(kind);
            Reject(kind, window: "playEnding");
            Reject(kind, target: "selectedTarget");
            Reject(kind, condition: "\"condition\":{\"kind\":\"pindianWon\",\"sourceBind\":\"unissued\"},");
            Exercise(kind, counter: false);
            Exercise(kind, counter: true);
        }
        Reject(CardKind.DrawTwo);
        Reject(CardKind.Dismantlement);
        ExerciseTransferredDamage();
    }

    private static void Exercise(CardKind kind, bool counter)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(kind));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
            HumanTeamId = "team:allies", ModeId = Mode,
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false,
            MaxTurns = 4
        }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, p => p.Kind == DecisionKind.SelectGeneral);
        Require(game.PendingDecision!.ValidContentIds.Contains(Owner) &&
                !game.PendingDecision.ValidContentIds.Contains(Armored),
            "The actual activity draft offers player generals and keeps the fixed boss out of the human pool.");
        game = Cold(game, registry);
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, game.PendingDecision!.PromptId));
        Reach(game, p => p.Kind == DecisionKind.Nullification);
        var use = game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.VirtualOrdinaryTrickOrigin is not null);
        var origin = use.VirtualOrdinaryTrickOrigin!;
        var producer = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == origin.ParentProgramFrameId);
        var boundary = game.ResolutionStack.OfType<PlayPhaseStartingBoundaryFrame>().Single();
        var armoredSeat = game.CreateSnapshot(0).Players.Single(p => p.GeneralId == Armored).Seat;
        Require(registry.Modes[Mode] is { ModeKind: ContentModeKind.Team, BossActivity: not null } && armoredSeat == 3 &&
                game.CreateSnapshot(0).Players.Count(p => p.TeamId == "team:allies") == 3 &&
                game.CreateSnapshot(0).Players.Count(p => p.TeamId == "team:boss") == 1,
            "The fixture uses an actual non-identity BossActivity with a virtual-armored NPC and two ordinary AI allies.");
        Require(use.CardId == 0 && use.CardKind == kind && use.PhysicalCardIds is { Count: 0 } &&
                use.TargetSeats.SequenceEqual([1, 2, 3]) && use.Action is { ActorSeat: 0, ProviderSeat: 0, PhysicalCards.Count: 0 } &&
                producer.WindowContext is { Window: SkillProgramTriggerWindow.PlayPhaseStarting } context &&
                context.ParentFrameId == boundary.Id && origin.LifecycleFrameId == boundary.Id &&
                boundary.Items[boundary.ItemIndex].Candidate?.SkillId == Skill,
            "The zero-material global trick must retain the actual play-start candidate and native ordered targets.");
        game = Cold(game, registry);
        var beforeWrong = State(game);
        var first = game.PendingDecision!;
        Require(!game.Submit(new AnswerPromptCommand(1, first.PromptId, first.Choices[0].Id, game.Revision)).Accepted &&
                State(game) == beforeWrong, "A foreign actor cannot answer the exact native counterspell prompt.");
        var counterPaid = false;
        var nativeResponseChildren = 0;
        for (var step = 0; step < 100; step++)
        {
            var p = game.PendingDecision;
            if (p?.SkillPrompt?.SkillId == Skill && p.Kind == DecisionKind.ProgramTrigger) break;
            if (p is { Kind: DecisionKind.Nullification, PlayerSeat: 0 })
            {
                var window = game.ResolutionStack.OfType<NullificationWindowFrame>().Single();
                Require(window.ParentFrameId == use.Id && window.EffectCardId == 0 && window.EffectCardKind == kind,
                    "Nullification is a real child of the issued virtual global card.");
                game = Cold(game, registry);
                var choice = game.PendingDecision!.Choices.First(c => c.Parameters.GetValueOrDefault("response") ==
                    (counter && !counterPaid ? "nullification" : "pass"));
                if (counter && !counterPaid) counterPaid = true;
                Accept(game, new AnswerPromptCommand(0, game.PendingDecision.PromptId, choice.Id, game.Revision));
            }
            else if (p?.SkillPrompt?.SkillId == Observer)
            {
                var response = game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>()
                    .Last(f => f.Action.Type == CardActionType.Response);
                Require(game.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == use.Id) &&
                        game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == producer.Id) &&
                        response.Action.Type == CardActionType.Response && response.Action.PhysicalCards.Count == 1,
                    "The real paid Slash or Dodge response suspends both global use and play-start producer.");
                nativeResponseChildren++;
                game = Cold(game, registry);
                Accept(game, new AnswerPromptCommand(0, game.PendingDecision!.PromptId,
                    game.PendingDecision.Choices.Single().Id, game.Revision));
            }
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        Require(game.PendingDecision?.SkillPrompt?.SkillId == Skill,
            "The global child must finish and return to its exact producer's next instruction.");
        var responses = Facts<GroupResponseEvent>(game).Where(e => e.ResolutionId == use.Id).ToArray();
        Require(Facts<NullificationResolvedEvent>(game).Single(e => e.EffectCardKind == kind).EffectNullified == counter &&
                Facts<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == use.Id && e.CardKind == kind && e.CardId == 0) == 1 &&
                !game.ResolutionStack.Any(f => f.Id == use.Id) &&
                game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == producer.Id) &&
                !Facts<ProgramBindingResolvedEvent>(game).Any(e => e.SkillId == Skill && e.Completed),
            "Counterspell outcome and card completion occur once before the typed producer return completes.");
        Require(counter ? counterPaid && responses.Length == 0 && nativeResponseChildren == 0 :
                responses.Length == 2 && nativeResponseChildren == 2 &&
                responses.All(r => r.UsedResponse && r.ResponseCardId > 0 && r.RequiredCardKind ==
                    (kind == CardKind.BarbarianAssault ? CardKind.Slash : CardKind.Dodge)) &&
                responses.Select(r => r.ResponderSeat).Order().SequenceEqual(Enumerable.Range(1, 3).Where(s => s != armoredSeat)) &&
                Facts<ArmorEffectAppliedEvent>(game).Any(e => e.ResolutionId == use.Id && e.TargetSeat == armoredSeat),
            "Native responses pay real Slash/Dodge cards; virtual Tengjia prevents its own effect without spending a hand card. " +
            JsonSerializer.Serialize(new { kind, counter, counterPaid, nativeResponseChildren, armoredSeat, responses,
                armor = Facts<ArmorEffectAppliedEvent>(game), players = game.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.Hp, p.MaxHp, p.HandCount }) }));
        Require(!Facts<DamageAppliedEvent>(game).Any() && game.CardMovements.All(m => m.CardId != 0),
            "Answered or nullified global tricks cause no synthetic damage or movement of the virtual appearance.");
        game = Cold(game, registry);
        Accept(game, new AnswerPromptCommand(0, game.PendingDecision!.PromptId, game.PendingDecision.Choices.Single().Id, game.Revision));
        Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        Require(Facts<ProgramBindingResolvedEvent>(game).Count(e => e.SkillId == Skill && e.Completed) == 1 &&
                game.ResolutionStack.All(f => f.Id != producer.Id && f.Id != boundary.Id),
            "The exact play-start parent advances once after the native global child and its continuation finish.");
        _ = Cold(game, registry);
    }

    private static void ExerciseTransferredDamage()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new TransferFixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = "team:allies", ModeId = Mode,
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false, MaxTurns = 8
        }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, game.PendingDecision!.PromptId));
        Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId));
        Reach(game, p => p.SkillPrompt?.SkillId == "classic:tianxiang");
        game = Cold(game, registry);
        void Answer(Func<PromptChoice, bool> choose)
        {
            var p = game.PendingDecision!;
            Accept(game, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(choose).Id, game.Revision));
        }
        Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Answer(c => c.Parameters.GetValueOrDefault("program-action") == "select-target" && c.Targets.SequenceEqual([3]));
        var payment = game.PendingDecision!.Choices.First(c =>
            c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards").Cards.Single();
        game = Cold(game, registry);
        Answer(c => c.Cards.SequenceEqual([payment]));
        Reach(game, p => p.SkillPrompt?.SkillId == TransferWatch);
        var use = game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.VirtualOrdinaryTrickOrigin is not null);
        var receipt = Facts<ProgramDamageTransferredEvent>(game).Single();
        Require(use is { CardId: 0, CardKind: CardKind.ArrowBarrage, TargetIndex: 0 } &&
                use.TargetSeats.SequenceEqual([0, 1, 2]) && use.CardAttack is { TargetSeat: 3, DamageRedirected: true } &&
                receipt.ResolutionId == use.Id && receipt.OwnerSeat == 0 && receipt.SourceSeat == 3 &&
                receipt.TargetSeat == 3 && receipt.DiscardedCardId == payment && receipt.DamageAmount == 1 &&
                receipt.Nature == DamageNature.Normal &&
                Facts<DamageAppliedEvent>(game).Any(e => e.SourceSeat == 3 && e.TargetSeat == 3 && e.Amount == 1),
            "A real paid Tianxiang transfer keeps the original group target frozen while its trusted receipt names the boss recipient.");
        game = Cold(game, registry);
        Answer(_ => true);
        for (var i = 0; i < 80 && !Facts<CardUseFinishedEvent>(game).Any(e => e.ResolutionId == use.Id); i++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Require(Facts<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == use.Id) == 1 &&
                Facts<ProgramDamageTransferredEvent>(game).Count(e => e.ResolutionId == use.Id) == 1 &&
                game.CardMovements.Count(m => m.CardId == payment && m.From == CardLocation.Hand(0) &&
                    m.To == CardLocation.DiscardPile) == 1 && game.CreateSnapshot(0).Players[0].Hp == 8,
            "The virtual group use resumes after one paid transfer, finishes once, and never charges Tianxiang twice.");
        _ = Cold(game, registry);
    }

    private static string Effects(CardKind kind, string target = "owner", string condition = "") =>
        $$"""{"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"owner"},"targetRef":{"kind":"{{target}}"},{{condition}}"outputKind":"{{char.ToLowerInvariant(kind.ToString()[0]) + kind.ToString()[1..]}}"}""";

    private static SkillProgramCatalog Load(CardKind kind, string window = "playPhaseStarting", string target = "owner", string condition = "") =>
        SkillProgramCatalog.Load($$"""
        {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Skill}}","revision":1,
        "viewAs":[{"id":"counterspell","inputKinds":["{{(kind == CardKind.BarbarianAssault ? "slash" : "dodge")}}"],"inputSuits":[],"outputKind":"nullification","forPlay":false,"forResponse":true}],
        "triggers":[{"id":"ghost-opening","window":"{{window}}","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,
        "effects":[{{Effects(kind, target, condition)}},{"op":"chooseOption","target":"owner","resultBind":"tail","options":[{"id":"continue"}]}]}]},
        {"id":"{{Observer}}","revision":1,"triggers":[{"id":"paid-response-child","window":"cardResponseAccepted","ownerRelation":"observer","cardKinds":["slash","dodge"],"optional":false,
        "effects":[{"op":"chooseOption","target":"owner","resultBind":"return","options":[{"id":"continue"}]}]}]},
        {"id":"{{Armor}}","revision":1,"cardPolicies":[{"id":"virtual-vine","kind":"virtualEquipment","cardKinds":["tengjia"]}]}]}
        """, JsonSerializer.Serialize(new
        {
            schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
            skills = new[] { Skill, Observer, Armor }.ToDictionary(id => id, id => new
            {
                name = id, description = "真实虚拟群体锦囊与响应返回",
                optionLabels = id == Armor ? new Dictionary<string, string>() : new Dictionary<string, string> { ["continue"] = "继续" }
            })
        }));

    private static void Reject(CardKind kind, string window = "playPhaseStarting", string target = "owner", string condition = "")
    {
        try { _ = Load(kind, window, target, condition); }
        catch (InvalidOperationException e) when (e.Message.Contains("Invalid skill program", StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("An invalid virtual-global-trick participant, branch or phase was accepted.");
    }

    private sealed class Fixture(CardKind kind) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("ghost-virtual-trick-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var catalog = Load(kind);
            foreach (var (id, program) in catalog.Programs)
                b.AddSkill(new(id, id, "真实群体锦囊边界") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new(Quiet, "无附加行为", "普通响应角色"));
            b.AddGeneral(new(Owner, "实际虚拟锦囊使用者", "supporter", Skill, "shu", 8, [Observer]));
            var peers = new[] { Armored, "fixture:ghost-virtual-peer-2", "fixture:ghost-virtual-peer-3" };
            foreach (var id in peers)
                // At one HP the public response policy spends a native Slash
                // to avoid lethal damage.
                b.AddGeneral(new(id, "实际其他角色", "supporter", id == Armored ? Armor : Quiet, "wei", id == Armored ? 8 : 1));
            b.AddDeck(new("fixture:ghost-virtual-deck", "确定性真实响应牌", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard(
                    kind == CardKind.BarbarianAssault ? "standard:slash" : "standard:dodge", Suit.Spade, i % 13 + 1)).ToArray()
            });
            b.AddMode(new(Mode, "真实鬼怪群体锦囊", 4, 4, new Dictionary<string, int>(),
                "fixture:ghost-virtual-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers],
                ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { ["team:allies"] = 3, ["team:boss"] = 1 })
            {
                BossActivity = new BossActivityDefinition(
                    PlayerPoolIds: [Owner, .. peers.Where(id => id != Armored)],
                    Stages: [new("armor", [Armored])], PlayerTier: 1, PlayerSkillIds: [])
            });
        }
    }

    private sealed class TransferFixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("ghost-virtual-transfer-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "classic:xiao-qiao");
            var catalog = SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
            {"id":"{{Transfer}}","revision":1,"triggers":[{"id":"opening","window":"playPhaseStarting","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,
            "effects":[{"op":"useVirtualOrdinaryTrick","target":"owner","actorRef":{"kind":"owner"},"targetRef":{"kind":"owner"},"outputKind":"arrowBarrage"}]}]},
            {"id":"{{TransferWatch}}","revision":1,"triggers":[{"id":"redirected-damage-child","window":"afterDamageApplied","subject":"any","damageOccurrence":"perDamage","optional":false,"usageScope":"game","usageLimit":1,
            "effects":[{"op":"chooseOption","target":"owner","resultBind":"resume","options":[{"id":"continue"}]}]}]},
            {"id":"{{TransferQuiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,
            "effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
            """, JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Transfer, TransferWatch, TransferQuiet }.ToDictionary(id => id, id => new
                {
                    name = id, description = "真实虚拟群体锦囊转移伤害",
                    optionLabels = id == TransferWatch ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>()
                })
            }));
            foreach (var (id, program) in catalog.Programs)
                b.AddSkill(new(id, id, "原生群体锦囊与天香") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Owner, "真实天香玩家", "xiao_qiao", "classic:tianxiang", "wu", 8, [TransferWatch]));
            var peers = new[] { "fixture:ghost-transfer-peer-1", "fixture:ghost-transfer-peer-2" };
            foreach (var id in peers) b.AddGeneral(new(id, "安静人机搭档", "supporter", TransferQuiet, "shu", 8));
            b.AddGeneral(new(Armored, "真实虚拟万箭首领", "supporter", Transfer, "qun", 8));
            b.AddDeck(new("fixture:ghost-transfer-deck", "真实红桃支付牌", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard("standard:slash", Suit.Heart, i % 13 + 1)).ToArray()
            });
            b.AddMode(new(Mode, "真实万箭天香转移活动", 4, 4, new Dictionary<string, int>(),
                "fixture:ghost-transfer-deck", GeneralCandidateCount: 3, GeneralPoolIds: [Owner, .. peers, Armored],
                ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { ["team:allies"] = 3, ["team:boss"] = 1 })
            {
                BossActivity = new([Owner, .. peers], [new("transfer", [Armored])], 1, [])
            });
        }
    }

    private static T[] Facts<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Reach(GameEngine g, Func<PendingDecision, bool> ready)
    {
        for (var i = 0; i < 80; i++)
        {
            if (g.PendingDecision is { } p && ready(p)) return;
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Expected native boundary was not reached: " + State(g));
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics()
    });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(State(g) == State(restored), "Cold replay preserves native parent/child frames, private views, payments and movements.");
        return restored;
    }
    private static void Accept(GameEngine g, GameCommand command)
    {
        var result = g.Submit(command);
        Require(result.Accepted, result.Error?.ToString() ?? "Rejected actual virtual-trick command.");
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
