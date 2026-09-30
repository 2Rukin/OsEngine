using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using OsEngine.Entity;
using OsEngine.Market.Servers;
using OsEngine.OsTrader;
using OsEngine.OsTrader.Grids.Futures2;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.Robots.MyBots;

namespace OsEngine.TradeHelpGrid.Tests
{
    /// <summary>Managed empty-stop preparation and owner identity tests, without native host construction or UI deletion.</summary>
    internal static class EmptyRemovalCases
    {
        internal static void Run()
        {
            Eligibility(); NativeEvidence(); DeferredPreparation(); DurableStop(); OwnerIdentity();
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly string DirectoryName = Path.Combine(Path.GetTempPath(), "Futures2-empty-" + Guid.NewGuid().ToString("N"));
            internal readonly Futures2Store Store;
            internal readonly BotTabSimple[] Tabs;
            internal readonly FixtureServerProxy Spy;
            internal readonly Futures2NativeAdapter Adapter;
            internal readonly Futures2Grid Robot;
            internal int Sends;
            internal Fixture()
            {
                BotTabSimple first = AdapterCases.Tab("TEST", out FixtureServerProxy spy);
                BotTabSimple second = AdapterCases.Tab("NEXT", out FixtureServerProxy unused);
                Tabs = new[] { first, second }; Spy = spy; spy.Execute = _ => Sends++;
                foreach (BotTabSimple tab in Tabs) tab.PositionOpenerToStop = new List<PositionOpenerToStopLimit>();
                Store = new Futures2Store(Path.Combine(DirectoryName, "state.json"));
                Adapter = new Futures2NativeAdapter(first, second, Store, true, _ => { });
                Futures2Plan plan = Program.Plan(); plan.EndpointIdentity = Adapter.EndpointIdentity(0); plan.PaperExecution = Adapter.IsPaper(0);
                Adapter.Engine.Configure(plan, new Futures2Policy { Trailing = true, EmptyStopMinutes = 1, RemoveEmptyRobot = true }, 210, 0);
                Adapter.Quotes[0] = Program.Quotes(time: DateTime.Now)[0]; Observe(0);
                Adapter.Reconcile(true);
                Adapter.Engine.Start(true, DateTime.Now.AddMinutes(-2));
                Adapter.Engine.Data.Trail.Reset(DateTime.Now.AddMinutes(-2));
                Adapter.Engine.Decide(Adapter.Quotes, DateTime.Now, true);
                Robot = AdapterCases.Robot(Adapter, Tabs, "empty-" + Guid.NewGuid().ToString("N"));
                AdapterCases.Set(Robot, "_live", true);
                Bool("Accept coordination", false); Bool("Policy.Trailing", true); Bool("Policy.RemoveEmptyRobot", true);
                ((StrategyParameterInt)Robot.Parameters.Single(p => p.Name == "Policy.EmptyStopMinutes")).ValueInt = 1;
                AdapterCases.Set(Robot, "_configuration", AdapterCases.Call(Robot, "Fingerprint"));
            }
            internal void Bool(string name, bool value) => ((StrategyParameterBool)Robot.Parameters.Single(p => p.Name == name)).ValueBool = value;
            internal void Observe(decimal net) => Spy.AccountChanged(new ExplicitAccount(new Portfolio { Number = "fixture", ValueCurrent = 1000 },
                false, new[] { new PositionOnBoard { SecurityNameCode = "TEST", ValueCurrent = net } }, DateTime.Now));
            public void Dispose()
            {
                Adapter.Dispose(); Futures2Coordination.Remove("Live/" + (string)typeof(Futures2Grid).GetField("_identity", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Robot));
                foreach (string file in new[] { "state.json", "state.json.bak", "state.json.tmp" }) File.Delete(Path.Combine(DirectoryName, file));
                if (Directory.Exists(DirectoryName)) Directory.Delete(DirectoryName);
            }
        }

        private static void Eligibility()
        {
            using Fixture fixture = new Fixture();
            Futures2NativeAdapter adapter = fixture.Adapter;
            Program.Check(!new Futures2Policy().RemoveEmptyRobot, "empty removal defaults off");
            Program.Equal<bool?>(null, JsonSerializer.Deserialize<Futures2Checkpoint>("{}").CoordinationUsed, "legacy participation remains unknown");
            Program.Equal<bool?>(false, adapter.Engine.Data.CoordinationUsed, "fresh adapter records isolated history");
            Program.Equal("Empty stop", adapter.Engine.Data.Trail.Cause, "actual helper decision latches empty stop");
            Program.Equal("", adapter.EmptyRemovalBlockReason, "never-active reconciled empty timeout is eligible");
            string baseline = JsonSerializer.Serialize(adapter.Engine.Data);
            Action<Futures2Checkpoint>[] changes = {
                d => d.Policy.RemoveEmptyRobot = false, d => d.Policy.Trailing = false, d => d.Policy.EmptyStopMinutes = 0,
                d => d.Trail.Disabled = false, d => d.Trail.Started = DateTime.Now, d => d.Trail.Closing = true,
                d => d.Trail.WasActivity = true, d => d.CoordinationUsed = null, d => d.CoordinationUsed = true,
                d => d.HadInventory = true, d => d.Book.InventoryRevision = 1,
                d => d.Book.Lots.Add(new Futures2Lot()), d => d.InventoryOperations.Add(new Futures2InventoryOperation()),
                d => d.PendingPlan = Program.Plan(), d => d.PendingPolicy = new Futures2Policy(), d => d.Emergency = true,
                d => d.Reducing = true, d => d.RecoverReduction = true, d => d.ManualEntry = 0, d => d.ManualExit = 0,
                d => d.EntryBatch = new Futures2EntryBatch(), d => d.LevelEdit = new Futures2LevelEdit(),
                d => d.ManualExitLots.Add("pending"), d => d.CancelAll = true, d => d.SelectedCancels.Add("pending"),
                d => d.Rollover = new Futures2Rollover(), d => d.Transfers.Add(new Futures2Transfer()),
                d => d.Receipts["credit"] = 0, d => d.GroupReceipts.Add("seen"), d => d.GroupCommands["peer"] = new Futures2PeerCommand(),
                d => d.GroupVersions["peer"] = 1, d => d.GroupSequence = 1, d => d.GroupClosing = true,
                d => d.GroupReductionId = "group", d => d.TransferId = "transfer", d => d.FundingDelta = 1,
                d => d.State = Futures2State.Reconciling, d => d.State = Futures2State.Faulted, d => d.State = Futures2State.Active
            };
            for (int index = 0; index < changes.Length; index++)
            {
                changes[index](adapter.Engine.Data);
                Program.Check(adapter.EmptyRemovalBlockReason.Length > 0 && !adapter.TryPrepareEmptyRemoval(), "empty removal guard " + index);
                AdapterCases.Set(adapter, "<Engine>k__BackingField", new Futures2Controller(JsonSerializer.Deserialize<Futures2Checkpoint>(baseline)));
            }
            foreach (Futures2IntentState state in Enum.GetValues<Futures2IntentState>())
            {
                Futures2Intent intent = Program.Entry(adapter.Engine.Data.Book, adapter.Engine.Data.Plans[adapter.Engine.Data.ActivePlan]); intent.State = state;
                Program.Check(!adapter.TryPrepareEmptyRemoval(), "any intent history refuses auto removal: " + state);
                adapter.Engine.Data.Book.Intents.Clear();
            }
            AdapterCases.Set(adapter, "_live", false);
            Program.Check(adapter.EmptyRemovalBlockReason.Contains("Tester/Optimizer"), "simulation keeps results and robot");
            AdapterCases.Set(adapter, "_live", true);
            Program.Equal(0, fixture.Sends + fixture.Spy.Cancels, "eligibility checks have no order or cancellation effects");
        }

        private static void NativeEvidence()
        {
            using Fixture fixture = new Fixture();
            Futures2NativeAdapter adapter = fixture.Adapter;
            for (int endpoint = 0; endpoint < 2; endpoint++)
            {
                fixture.Tabs[endpoint].PositionOpenerToStop.Add(new PositionOpenerToStopLimit());
                Program.Check(!adapter.TryPrepareEmptyRemoval(), "local pending opener blocks endpoint " + endpoint);
                fixture.Tabs[endpoint].PositionOpenerToStop.Clear(); fixture.Tabs[endpoint].PositionOpenerToStop = null;
                Program.Check(!adapter.TryPrepareEmptyRemoval(), "missing opener collection is not confirmed empty " + endpoint);
                fixture.Tabs[endpoint].PositionOpenerToStop = new List<PositionOpenerToStopLimit>();
                fixture.Tabs[endpoint].GetJournal().SetNewDeal(new Position { Number = 123 + endpoint, State = PositionStateType.Done });
                Program.Check(!adapter.TryPrepareEmptyRemoval(), "even closed native history blocks endpoint " + endpoint);
                fixture.Tabs[endpoint].PositionsAll.Clear();
            }
            fixture.Observe(1);
            Program.Check(!adapter.TryPrepareEmptyRemoval() && adapter.EmptyRemovalBlockReason.Contains("Account net"), "external account change blocks queued retirement");
            fixture.Observe(0);
            adapter.Quotes[0].Time = DateTime.Now.AddMinutes(-1);
            Program.Check(!adapter.TryPrepareEmptyRemoval(), "stale quote blocks removal");
            adapter.Quotes[0].Time = DateTime.Now;
            ((DateTime[])typeof(Futures2NativeAdapter).GetField("_accountAt", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter))[0] = DateTime.Now.AddMinutes(-1);
            Program.Check(!adapter.TryPrepareEmptyRemoval(), "stale account blocks removal");
            fixture.Observe(0);
            fixture.Tabs[0].Security.PriceStep = 0.001m;
            Program.Check(adapter.EmptyRemovalBlockReason.Contains("profile changed"), "native metadata change blocks removal");
            fixture.Tabs[0].Security.PriceStep = 0.00001m;
            Program.Equal("", adapter.EmptyRemovalBlockReason, "cleared native blockers restore eligibility");
            fixture.Spy.ConnectionChanged("Disconnect");
            Program.Check(!adapter.TryPrepareEmptyRemoval(), "disconnect invalidates eligibility");
        }

        private static void DeferredPreparation()
        {
            using Fixture fixture = new Fixture();
            Action deferred = null; bool? accepted = null;
            fixture.Robot.AutomaticDeletionRequested += panel => { deferred = () => accepted = panel.TryPrepareForAutomaticDeletion(); return true; };
            AdapterCases.Call(fixture.Robot, "RequestEmptyRemoval");
            Program.Check(deferred != null && !accepted.HasValue, "request acknowledges only deferred owner processing");
            fixture.Observe(1); deferred();
            Program.Equal<bool?>(false, accepted, "late account change refuses final owner preparation");
            Program.Check(fixture.Spy.AccountChanged != null, "refused preparation leaves callbacks subscribed");
            fixture.Observe(0);
            fixture.Bool("Policy.RemoveEmptyRobot", false);
            Program.Check(!fixture.Robot.TryPrepareForAutomaticDeletion(), "changed unaccepted settings refuse queued request");
            fixture.Bool("Policy.RemoveEmptyRobot", true);
            fixture.Bool("Accept coordination", true);
            AdapterCases.Call(fixture.Robot, "Coordinate");
            Program.Equal<bool?>(true, fixture.Store.Load().CoordinationUsed, "participation is durable before consenting publication returns");
            fixture.Bool("Accept coordination", false); AdapterCases.Call(fixture.Robot, "Coordinate");
            Program.Check(!fixture.Robot.TryPrepareForAutomaticDeletion(), "disabling coordination does not erase participation history");
            Program.Equal(0, fixture.Sends + fixture.Spy.Cancels, "deferred refusals never submit or cancel");
        }

        private static void DurableStop()
        {
            using (Fixture fixture = new Fixture())
            {
                Program.Check(fixture.Robot.TryPrepareForAutomaticDeletion(), "eligible robot durably prepares for owner removal");
                Program.Equal(Futures2State.Stopped, fixture.Store.Load().State, "Stopped checkpoint retained before host destruction");
                Program.Check(fixture.Spy.AccountChanged == null, "successful preparation detaches account callbacks");
                Program.Equal("Adapter disposed", fixture.Adapter.NativeStateBlockReason, "prepared adapter cannot advertise readiness");
                fixture.Adapter.Pump();
                Program.Check(!fixture.Robot.TryPrepareForAutomaticDeletion(), "repeat preparation cannot restart disposed robot");
                Program.Equal(0, fixture.Sends + fixture.Spy.Cancels, "retirement sends no broker operations");
                using Futures2NativeAdapter recovered = new Futures2NativeAdapter(fixture.Tabs[0], fixture.Tabs[1], fixture.Store, true, _ => { });
                Program.Equal(Futures2State.Reconciling, recovered.Engine.Data.State, "restart between stop and keeper removal requires reconciliation");
                Program.Check(!recovered.TryPrepareEmptyRemoval(), "restart is not an automatic deletion acknowledgement");
            }
            using (Fixture fixture = new Fixture())
            {
                string temporary = Path.Combine(fixture.DirectoryName, "state.json.tmp");
                fixture.Adapter.Save();
                using (FileStream blocked = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                    Program.Throws(() => fixture.Robot.TryPrepareForAutomaticDeletion(), "failed durable stop refuses destruction");
                Program.Check(fixture.Spy.AccountChanged != null, "failed write does not quiesce native callbacks");
                Program.Equal(Futures2State.Faulted, fixture.Adapter.Engine.Data.State, "failed write faults campaign");
                Program.Check(fixture.Store.Load().State != Futures2State.Stopped, "failed write cannot claim durable Stopped");
                Program.Equal(0, fixture.Sends + fixture.Spy.Cancels, "storage failure sends no broker operations");
            }
        }

        private sealed class ProbePanel : BotPanel
        {
            private ProbePanel() : base("unused", StartProgram.IsOsTrader) { }
            internal bool Allow;
            internal bool UseDefault;
            internal int Preparations;
            internal bool Request() => RequestAutomaticDeletion();
            public override bool TryPrepareForAutomaticDeletion() { Preparations++; return UseDefault ? base.TryPrepareForAutomaticDeletion() : Allow; }
            public override void ShowIndividualSettingsDialog() { }
        }
        private static void OwnerIdentity()
        {
            ProbePanel owned = (ProbePanel)AdapterCases.Empty(typeof(ProbePanel)); owned.NameStrategyUniq = "owned";
            ProbePanel stranger = (ProbePanel)AdapterCases.Empty(typeof(ProbePanel)); stranger.NameStrategyUniq = "owned";
            ProbePanel active = (ProbePanel)AdapterCases.Empty(typeof(ProbePanel)); active.NameStrategyUniq = "other";
            OsTraderMaster master = (OsTraderMaster)AdapterCases.Empty(typeof(OsTraderMaster));
            AdapterCases.Set(master, "_startProgram", StartProgram.IsOsTrader); AdapterCases.Set(master, "_activePanel", active);
            master.PanelsArray = new List<BotPanel> { owned, active };
            Program.Check(!(bool)AdapterCases.Call(master, "TryPrepareOwnedAutomaticDeletion", stranger) && stranger.Preparations == 0, "same-name foreign object never reaches preparation");
            Program.Check(!(bool)AdapterCases.Call(master, "TryPrepareOwnedAutomaticDeletion", owned), "owner respects robot veto");
            owned.Allow = true;
            Program.Check((bool)AdapterCases.Call(master, "TryPrepareOwnedAutomaticDeletion", owned), "exact owned nonactive panel can prepare");
            Program.Check(ReferenceEquals(active, typeof(OsTraderMaster).GetField("_activePanel", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(master)), "preparation does not switch active robot");
            AdapterCases.Set(master, "_activePanel", stranger);
            Program.Check(!(bool)AdapterCases.Call(master, "TryPrepareOwnedAutomaticDeletion", owned), "hot-update active alias blocks destructive preparation");
            AdapterCases.Set(master, "_activePanel", active); master.PanelsArray.Add(stranger);
            Program.Check(!(bool)AdapterCases.Call(master, "TryPrepareOwnedAutomaticDeletion", owned), "duplicate same-name owner membership blocks preparation");
            master.PanelsArray.Remove(stranger); owned.UseDefault = true;
            Program.Check(!(bool)AdapterCases.Call(master, "TryPrepareOwnedAutomaticDeletion", owned), "legacy panel default refuses automatic removal");
            owned.UseDefault = false; AdapterCases.Set(master, "_startProgram", StartProgram.IsTester);
            Program.Check(!(bool)AdapterCases.Call(master, "TryPrepareOwnedAutomaticDeletion", owned), "native tester owner refuses automatic removal");
            Program.Check(!owned.Request(), "unowned request has no implicit global owner");
            Func<BotPanel, bool> listener = panel => true;
            owned.AutomaticDeletionRequested += listener;
            Program.Check(owned.Request(), "single owner can acknowledge queueing");
            owned.AutomaticDeletionRequested += listener;
            Program.Check(!owned.Request(), "ambiguous multiple owner subscriptions fail closed");
        }
    }
}
