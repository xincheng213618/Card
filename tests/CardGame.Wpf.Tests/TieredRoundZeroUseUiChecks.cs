using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class TieredRoundZeroUseUiChecks
{
    public static void PublicZeroUseTargetsAndRealCardSubmission()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Fixture.Mode, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, "fixture:zero-ui-owner", game.Revision, game.PendingDecision!.PromptId));
        Reach(game, p => p.Kind == DecisionKind.PlayCard);
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, game.Seed, true, store, useExpandedContent: false,
            contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);

        // Retain the real entity path in the same small game before tier2 exists.
        game = Program.Engine(model);
        var equip = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip && a.ConversionSource is null);
        var card = model.Hand.Single(c => c.Id == equip.CardId);
        model.SelectCardCommand.Execute(card);
        Program.Assert(model.CanConfirmSelected && model.Hand.All(c => c.Id > 0),
            "The existing physical equipment action remains selectable without a fake zero card.");
        model.ConfirmSelectedCommand.Execute(null);
        var physical = game.AcceptedCommands.OfType<PlayCardCommand>().Last();
        Program.Assert(physical.CardId == card.Id && physical.CardId > 0 && physical.ConversionSource is null &&
                       game.CreateSnapshot(0).Players[0].Equipment.Any(c => c.Id == card.Id) &&
                       game.CardMovements.Count(m => m.CardId == card.Id && m.From.Zone == CardZoneKind.Hand &&
                           m.To.Zone == CardZoneKind.Processing) == 1,
            "The old entity button submits the exact real PlayCard command and pays that card once.");
        Reach(game, p => p.Kind == DecisionKind.PlayCard);

        for (var tier = 0; tier < 2; tier++)
        {
            Accept(game, new UseProgramSkillCommand(0, Fixture.Driver, "hurt", [], [], game.Revision, game.PendingDecision!.PromptId));
            Reach(game, p => p.SkillPrompt?.SkillId == Fixture.Danxin && p.Choices.Any(c =>
                c.Parameters.GetValueOrDefault("program-action") == "activate"));
            var prompt = game.CreateSnapshot(0).PendingDecision!;
            Accept(game, new AnswerPromptCommand(0, prompt.PromptId,
                prompt.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "activate").Id, game.Revision));
            Reach(game, p => p.Kind == DecisionKind.PlayCard);
        }
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        model.LoadManualGameCommand.Execute(null);
        game = Program.Engine(model);
        var zero = model.HumanActiveSkillActions.First(a => a.CardId == 0 &&
            a.TieredRoundZeroUse is not null && a.PlayedCardKind == CardKind.Slash);
        Program.Assert(model.SupplementalActiveSkillActions.Contains(zero) && model.Hand.All(c => c.Id > 0),
            "A real published zero-material action is exposed through existing active-action buttons, without a card face.");
        var commandCount = game.AcceptedCommands.Count;
        model.SelectActiveSkillCommand.Execute(zero);
        Program.Assert(model.IsActiveSkillSelectionPending && !model.CanConfirmSelected &&
                       model.Seats.Where(s => s.IsLegalTarget).Select(s => s.Seat).ToHashSet().SetEquals(zero.TargetSeats),
            "The public action opens an exact target draft and does not submit before confirmation.");
        model.ClearSelectionCommand.Execute(null);
        Program.Assert(!model.IsActiveSkillSelectionPending && game.AcceptedCommands.Count == commandCount,
            "Cancelling an unconfirmed zero action neither issues a Use nor pays a material.");
        model.SelectActiveSkillCommand.Execute(zero);
        foreach (var target in zero.TargetSeats.Reverse())
            model.SelectTargetCommand.Execute(model.Seats.Single(s => s.Seat == target));
        Program.Assert(model.CanConfirmSelected, "The exact public target set enables the existing confirm control.");
        model.ConfirmSelectedCommand.Execute(null);
        var command = game.AcceptedCommands.OfType<PlayCardCommand>().Last();
        Program.Assert(command.CardId == 0 && command.PlayedCardKind == zero.PlayedCardKind &&
                       command.TargetCardId == zero.TargetCardId && command.TargetSeats!.SequenceEqual(zero.TargetSeats) &&
                       command.ConversionSource == zero.ConversionSource &&
                       (command.AdditionalConversionSources ?? []).SequenceEqual(zero.AdditionalConversionSources ?? []) &&
                       game.Events.Select(e => e.Payload).OfType<TieredRoundConversionUseIssuedEvent>().Count(e =>
                           e.Receipt.ActorSeat == 0 && e.Receipt.EffectiveKind == CardKind.Slash && e.Receipt.MaterialCount == 0) == 1 &&
                       game.CardMovements.All(m => m.CardId > 0) && model.Hand.All(c => c.Id > 0),
            "The UI submits the original PlayCard identity, ordered targets and all conversion sources, issuing one zero-material Use.");
    }

    public static void DrawFundedZeroUseSubmitsExactSourceAndPrivateDrawChild()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(drawFunded: true));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Fixture.Mode, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, "fixture:zero-ui-owner", game.Revision, game.PendingDecision!.PromptId));
        Reach(game, p => p.Kind == DecisionKind.PlayCard);
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, game.Seed, true, store, useExpandedContent: false,
            contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        game = Program.Engine(model);
        var zero = model.HumanActiveSkillActions.First(a => a.CardId == 0 && a.DrawFundedDistinctBasicUse is not null);
        var handBefore = game.CreateSnapshot(0).Players[0].Hand.ToArray();
        Program.Assert(handBefore.Length == 6 && game.State.Players[0].Hp == 6 && handBefore.All(c => c.Suit == Suit.Spade) &&
                       zero.TieredRoundZeroUse is null && zero.MinCardCount == 0 && zero.MaxCardCount == 0 && zero.SelectableCardIds.Count == 0 &&
                       model.SupplementalActiveSkillActions.Contains(zero) && model.Hand.All(c => c.Id > 0),
            "The actual whole-hand qualification publishes a distinct draw-funded zero action through the existing buttons, without selecting a hand entity.");
        var count = game.AcceptedCommands.Count;
        model.SelectActiveSkillCommand.Execute(zero with { DrawFundedDistinctBasicUse = new("fixture:unpublished-ui-method") });
        Program.Assert(!model.IsActiveSkillSelectionPending && game.AcceptedCommands.Count == count,
            "An otherwise identical action with another method policy cannot select the currently published action.");
        model.SelectActiveSkillCommand.Execute(zero);
        Program.Assert(model.IsActiveSkillSelectionPending && !model.CanConfirmSelected && model.Hand.All(c => !c.IsPlayable && !c.IsSelected) &&
                       model.Seats.Where(s => s.IsLegalTarget).Select(s => s.Seat).ToHashSet().SetEquals(zero.TargetSeats),
            "The draw-funded draft selects only the exact published targets and leaves every physical hand card unselected.");
        foreach (var target in zero.TargetSeats.Reverse())
            model.SelectTargetCommand.Execute(model.Seats.Single(s => s.Seat == target));
        Program.Assert(model.CanConfirmSelected, "The original target set enables confirmation without a material selection.");
        model.ConfirmSelectedCommand.Execute(null);
        var command = game.AcceptedCommands.OfType<PlayCardCommand>().Last();
        Program.Assert(command.CardId == 0 && command.PlayedCardKind == zero.PlayedCardKind && command.TargetCardId == zero.TargetCardId &&
                       command.TargetSeats!.SequenceEqual(zero.TargetSeats) && command.ConversionSource == zero.ConversionSource &&
                       (command.AdditionalConversionSources ?? []).SequenceEqual(zero.AdditionalConversionSources ?? []),
            "Confirmation submits the original real PlayCard command with exact ordered targets and conversion sources.");
        Reach(game, p => p.SkillPrompt?.SkillId == Fixture.DrawObserver);
        var payment = game.ResolutionStack.OfType<DrawFundedDistinctBasicFrame>().Single();
        var child = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Batch.ParentFrameId == payment.Id);
        var paid = game.Events.Select(e => e.Payload).OfType<DrawFundedDistinctBasicPaidEvent>().Single();
        var privatePrompt = game.CreateSnapshot(0).PendingDecision!;
        Program.Assert(paid.PaymentFrameId == payment.Id && paid.ActorSeat == 0 && paid.ActualDrawCount == 1 &&
                       payment.Payment.Source == zero.ConversionSource && payment.TargetSeats.SequenceEqual(zero.TargetSeats) &&
                       payment.ActiveChildFrameId == child.Id && child.ResumeDrawFundedDistinctBasicFrameId == payment.Id &&
                       child.Batch.Movements is [var draw] && draw.From == CardLocation.DrawPile && draw.To == CardLocation.Hand(0) &&
                       !game.Events.Select(e => e.Payload).OfType<DrawFundedDistinctBasicIssuedEvent>().Any() &&
                       privatePrompt.IsPrivate && privatePrompt.Choices.All(c => c.Cards.Count == 1) &&
                       privatePrompt.Choices.SelectMany(c => c.Cards).ToHashSet().SetEquals(game.CreateSnapshot(0).Players[0].Hand.Select(c => c.Id)) &&
                       Enumerable.Range(1, 3).All(viewer => game.CreateSnapshot(viewer).PendingDecision is null &&
                           game.CreateSnapshot(viewer).Players[0].Hand.Count == 0),
            "One real draw owns a private hand-choice child before the promised Use; other viewers receive neither its choices nor the owner's hand identities.");
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        game = Program.Engine(model);
        Program.Assert(model.IsSkillSelectionPending && model.SkillChoices.Select(c => c.Id).SequenceEqual(privatePrompt.Choices.Select(c => c.Id)) &&
                       model.HumanActiveSkillActions.Count == 0,
            "The shared UI restores only the exact private draw-child choices and exposes no Play action while payment is suspended.");
        model.SelectSkillChoiceCommand.Execute(model.SkillChoices.First());
        Reach(game, p => p.Kind == DecisionKind.PlayCard);
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        game = Program.Engine(model);
        var issued = game.Events.Select(e => e.Payload).OfType<DrawFundedDistinctBasicIssuedEvent>().Single();
        var action = game.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>().Single(e => e.Action.ActionId == issued.CardActionId).Action;
        Program.Assert(issued.PaymentFrameId == paid.PaymentFrameId && issued.Source == zero.ConversionSource &&
                       action.Type == CardActionType.Use && action.ActorSeat == 0 && action.PhysicalCards.Count == 0 &&
                       action.ConversionChain.SequenceEqual([zero.ConversionSource!]) && action.TargetSeats.SequenceEqual(zero.TargetSeats) &&
                       game.Events.Select(e => e.Payload).OfType<DrawFundedDistinctBasicPaidEvent>().Count() == 1 &&
                       handBefore.All(card => game.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == card.Id)) &&
                       game.CardMovements.All(m => m.CardId > 0) && model.Hand.All(c => c.Id > 0),
            "Returning the actual private child issues one zero-material Use without paying any original hand entity or repeating the draw.");
        count = game.AcceptedCommands.Count;
        var moves = game.CardMovements.Count;
        model.SelectActiveSkillCommand.Execute(zero);
        Program.Assert(!model.IsActiveSkillSelectionPending && game.AcceptedCommands.Count == count && game.CardMovements.Count == moves &&
                       model.HumanActiveSkillActions.All(a => a.DrawFundedDistinctBasicUse is null),
            "The old action is rejected after its actual draw and Use; stale buttons cannot submit another command or pay again.");
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "Expected the real fixture command to be accepted.");
    }

    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 80; step++)
        {
            if (game.CreateSnapshot(0).PendingDecision is { } prompt && predicate(prompt)) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The fixed small UI fixture did not reach its actual published prompt.");
    }

    private sealed class Fixture(bool drawFunded = false) : IGameContentPackage
    {
        internal const string Mode = "identity:classic-tiered-zero-ui";
        internal const string Driver = "fixture:zero-ui-driver";
        internal const string Danxin = "boundary:danxin-capped-current";
        internal const string Funded = "fixture:zero-ui-draw-funded", DrawObserver = "fixture:zero-ui-draw-observer";
        private const string Jiaozhao = "boundary:jiaozhao-round-current";
        public PackageManifest Manifest { get; } = new("fixture:tiered-zero-ui", "1.0.0", "公开零实体动作界面小夹具");
        public void Register(IContentRegistryBuilder b)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rs = assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.boundary-guo-huang-hou.rules.json")!;
            using var ps = assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.boundary-guo-huang-hou.presentation.json")!;
            using var rr = new StreamReader(rs); using var pr = new StreamReader(ps);
            var production = SkillProgramCatalog.Load(rr.ReadToEnd(), pr.ReadToEnd());
            foreach (var id in new[] { Jiaozhao, Danxin }) b.AddSkill(new(id, id, "真实当前共享能力") { Program = production.Programs[id] });
            var root = JsonNode.Parse("""
            {"skills":[
              {"id":"fixture:zero-ui-driver","revision":1,"activations":[{"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]}]},
              {"id":"fixture:zero-ui-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """)!;
            root["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            if (drawFunded)
            {
                root["skills"]!.AsArray().Add(JsonNode.Parse("""
                {"id":"fixture:zero-ui-draw-funded","revision":1,"viewAs":[{"id":"slash","inputKinds":[],"inputSuits":[],"inputCount":0,"sourceZones":[],"outputKind":"slash","forPlay":true,"forResponse":false,"useOnly":true,"drawFundedDistinctBasic":{"methodLedgerId":"fixture:ui-bingxin"}}]}
                """));
                root["skills"]!.AsArray().Add(JsonNode.Parse("""
                {"id":"fixture:zero-ui-draw-observer","revision":1,"triggers":[{"id":"draw","window":"cardsGained","subject":"owner","optional":false,"destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.draw-funded-distinct-basic.draw"],"effects":[{"op":"selectOwnedCards","target":"owner","minimumCards":1,"maximumCards":1,"zones":["hand"],"resultBind":"seen"}]}]}
                """));
            }
            var labels = new Dictionary<string, object>
            {
                [Driver] = new { name = "真实自伤", description = "两次实际伤害取得当前Tier2" },
                ["fixture:zero-ui-quiet"] = new { name = "安静回合", description = "固定小回合不出牌" }
            };
            if (drawFunded)
            {
                labels[Funded] = new { name = "实际摸牌基本牌", description = "同色整手等于HP，摸一张后使用零实体杀" };
                labels[DrawObserver] = new { name = "私有摸牌子链", description = "查看真实新手牌选择，不移动或支付所选实体" };
            }
            var catalog = SkillProgramCatalog.Load(root.ToJsonString(), JsonSerializer.Serialize(new
            {
                schemaVersion = 3, skills = labels
            }));
            foreach (var id in catalog.Programs.Keys) b.AddSkill(new(id, id, "实际小夹具") { Program = catalog.Programs[id] });
            b.AddSkill(new("fixture:zero-ui-idle", "固定候选", "无运行能力")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            b.AddGeneral(new("fixture:zero-ui-owner", "界面动作来源", "supporter", drawFunded ? Funded : Jiaozhao, "wei", drawFunded ? 5 : 8,
                drawFunded ? [DrawObserver] : [Danxin, Driver], GeneralGender.Female));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:zero-ui-target-{i}", "固定公开目标", "supporter",
                "fixture:zero-ui-idle", "qun", 8, ["fixture:zero-ui-quiet"], GeneralGender.Male));
            b.AddDeck(new("fixture:zero-ui-deck", "固定真实装备", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard("standard:crossbow", Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "公开零实体动作", 4, 4, new Dictionary<string, int>
            { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:zero-ui-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:zero-ui-owner", "fixture:zero-ui-target-1", "fixture:zero-ui-target-2", "fixture:zero-ui-target-3"]));
        }
    }
}
