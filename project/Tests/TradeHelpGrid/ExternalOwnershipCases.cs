using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using OsEngine.Entity;
using OsEngine.Market.Servers;
using OsEngine.OsTrader.Grids.Futures2;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.Robots.MyBots;

namespace OsEngine.TradeHelpGrid.Tests
{
    /// <summary>Offline shared-account observation and native eligibility fixtures, with physical sends counted by spies.</summary>
    /// <remarks>Uses actual Book/Journal/adapter/robot methods without their native constructors or worker loops.
    /// Two campaigns receive the same synthetic account observations. This does not qualify broker attribution,
    /// external inventory adoption, WPF interaction or a live account. Run through the TradeHelpGrid harness.</remarks>
    internal static class ExternalOwnershipCases
    {
        internal static void Run()
        {
            SharedAccount(); AttestedZero();
        }

        private static Futures2Intent Seed(Futures2NativeAdapter adapter, BotTabSimple tab, decimal quantity, int number, Side side = Side.Buy)
        {
            Futures2Plan plan = adapter.Engine.Data.Plans[adapter.Engine.Data.ActivePlan];
            Futures2Intent intent = Program.Entry(adapter.Engine.Data.Book, plan, quantity);
            intent.PositionNumber = number; intent.OrderNumber = number; intent.MarketNumber = "owned-" + number;
            Position position = new Position { Number = number, Direction = side, State = PositionStateType.Opening,
                SignalTypeOpen = "F2:" + adapter.Engine.Data.Campaign + ":" + intent.Id, Lots = 1,
                PriceStep = plan.Input.Tick, PriceStepCost = plan.Input.TickValue };
            Order order = new Order { NumberUser = number, NumberPosition = number, NumberMarket = intent.MarketNumber,
                SecurityNameCode = "TEST", PortfolioNumber = "fixture", Side = side, Volume = quantity,
                Price = 0, TimeCreate = Program.T0, State = OrderStateType.Active, UsesSignedPrice = true, SignedPercentBase = 2 };
            position.AddNewOpenOrder(order);
            position.SetTrade(new MyTrade { NumberTrade = "fill-" + number, NumberOrderParent = order.NumberMarket,
                SecurityNameCode = "TEST", Side = side, Volume = quantity, Price = 0, Time = Program.T0 });
            order.State = OrderStateType.Done;
            tab.GetJournal().SetNewDeal(position);
            adapter.Engine.Data.Book.Fill(intent, plan, "0:" + number + ":fill-" + number, quantity, 0, 0, false, Program.T0);
            return intent;
        }

        private static ExplicitAccount Observation(decimal net) => new ExplicitAccount(
            new Portfolio { Number = "fixture", ValueCurrent = 1000 }, false,
            new[] { new PositionOnBoard { SecurityNameCode = "TEST", ValueCurrent = net } }, DateTime.Now);

        private static void SharedAccount()
        {
            BotTabSimple a = AdapterCases.Tab("TEST", out FixtureServerProxy spyA);
            BotTabSimple ax = AdapterCases.Tab("NEXT", out FixtureServerProxy unusedA);
            BotTabSimple b = AdapterCases.Tab("TEST", out FixtureServerProxy spyB);
            BotTabSimple bx = AdapterCases.Tab("NEXT", out FixtureServerProxy unusedB);
            List<string> logs = new List<string>(); int sends = 0;
            spyA.Execute = _ => sends++; spyB.Execute = _ => sends++;
            using Futures2NativeAdapter first = new Futures2NativeAdapter(a, ax, null, true, logs.Add);
            using Futures2NativeAdapter second = new Futures2NativeAdapter(b, bx, null, true, logs.Add);
            first.Engine.Configure(Program.Plan(), new Futures2Policy(), 210, 0);
            second.Engine.Configure(Program.Plan(), new Futures2Policy(), 210, 0);
            first.Quotes[0] = Program.Quotes(time: DateTime.Now)[0]; second.Quotes[0] = Program.Quotes(time: DateTime.Now)[0];
            Seed(first, a, 2, 101); Seed(second, b, 3, 201);
            first.ExternalNet[0] = 3; second.ExternalNet[0] = 2;
            ExplicitAccount account = Observation(5); spyA.AccountChanged(account); spyB.AccountChanged(account);
            first.Reconcile(true); second.Reconcile(true);
            first.Engine.Start(true, DateTime.Now); second.Engine.Start(true, DateTime.Now);
            Program.Check(first.CanConfirmNativeState && second.CanConfirmNativeState, "shared account accepts each explicit own plus external allocation");
            Program.Check(first.DescribeNativeState().Contains("own=2; declared external=3; expected=5; observed=5; difference=0"), "status distinguishes own and nonzero declared external net");
            string name = "shared-" + Guid.NewGuid().ToString("N");
            Futures2Grid receiver = AdapterCases.Robot(first, new[] { a, ax }, name);
            Futures2Grid sender = AdapterCases.Robot(second, new[] { b, bx }, name + "-sender");
            try
            {
                AdapterCases.Call(receiver, "Coordinate");
                Program.Check(Futures2Coordination.Get("Tester/" + name).CanEnter, "matching receiving campaign advertises current native eligibility");
                Seed(second, b, 1, 202);
                account = Observation(6); spyA.AccountChanged(account); spyB.AccountChanged(account);
                Program.Check(first.Reconciled && !first.CanConfirmNativeState, "foreign campaign fill closes current gate without falsifying prior explicit reconciliation");
                Program.Check(second.CanConfirmNativeState, "foreign campaign own increase still matches its own external declaration");
                string before = JsonSerializer.Serialize(first.Engine.Data);
                string status = first.DescribeNativeState();
                Program.Check(status.Contains("BLOCKED: Endpoint 0: Account net differs") && status.Contains("observed=6; difference=1"), "mismatch visible despite prior accepted reconciliation");
                Program.Equal(before, JsonSerializer.Serialize(first.Engine.Data), "reading native status cannot change ownership, quantities or reservations");
                Program.Equal(2m, first.Engine.Data.Book.Lots.Sum(l => l.Quantity), "another campaign increase is not adopted");
                AdapterCases.Call(receiver, "Coordinate");
                Program.Check(!Futures2Coordination.Get("Tester/" + name).CanEnter, "mismatched receiver stops advertising entry readiness");
                first.Engine.Pause();
                Program.Throws(() => AdapterCases.Call(receiver, "Start"), "Start button rejects current mismatch despite prior reconciliation");
                ((StrategyParameterDecimal)receiver.Parameters.Single(p => p.Name == "External net endpoint 0")).ValueDecimal = 3;
                ((StrategyParameterString)receiver.Parameters.Single(p => p.Name == "Regime")).ValueString = "On";
                int beforeSettingsIntents = first.Engine.Data.Book.Intents.Count;
                AdapterCases.Set(first, "_processing", true);
                try
                {
                    AdapterCases.Call(receiver, "SettingsChanged");
                    Program.Check(first.Engine.Data.State != Futures2State.Active, "runtime Regime On cannot start with current mismatch");
                    first.Engine.Configure(first.Engine.Data.Plans[first.Engine.Data.ActivePlan], new Futures2Policy(), 210, 0);
                    AdapterCases.Call(receiver, "SettingsChanged");
                    Program.Check(first.Engine.Data.PendingPlan != null && !first.Engine.Data.ResumeAfterConfigure,
                        "runtime Regime On cannot arm pending resume with current mismatch");
                    first.Engine.Data.PendingPlan = null; first.Engine.Data.PendingPolicy = null;
                    first.Engine.Data.State = Futures2State.Active; // Previously running campaign, now blocked by a foreign change.
                    ((StrategyParameterBool)receiver.Parameters.Single(p => p.Name == "Policy.ForbidShort")).ValueBool = true;
                    AdapterCases.Call(receiver, "SettingsChanged");
                    Program.Check(first.Engine.Data.PendingPlan != null && !first.Engine.Data.ResumeAfterConfigure,
                        "policy edit while blocked clears automatic resume inherited from previous Active state");
                    Program.Equal(beforeSettingsIntents, first.Engine.Data.Book.Intents.Count, "rejected startup paths create no new native intent");
                }
                finally { AdapterCases.Set(first, "_processing", false); }
                first.Engine.Data.PendingPlan = null; first.Engine.Data.PendingPolicy = null;
                AdapterCases.Call(sender, "Add", "Transfer destination", name, "fixture");
                AdapterCases.Call(sender, "Add", "Transfer collateral", 7m, "fixture");
                Program.Throws(() => AdapterCases.Call(sender, "StartTransfer"), "transfer refuses currently mismatched receiver");
                Program.Check(second.Engine.Data.Transfers.Count == 0 && !second.Engine.Data.Reducing, "blocked transfer starts no source reduction");
                first.ExternalNet[0] = 4;
                Program.Check(first.CanConfirmNativeState, "explicit corrected external declaration restores account equality without adopting lots");
                first.ExternalNet[0] = -2; spyA.AccountChanged(Observation(0));
                Program.Check(first.CanConfirmNativeState && a.PositionsAll[0].OpenVolume == 2, "own long and foreign short can net to zero without making own position empty");
                Futures2Intent unknown = Program.Entry(first.Engine.Data.Book, first.Engine.Data.Plans[first.Engine.Data.ActivePlan], 1);
                unknown.State = Futures2IntentState.Unknown;
                status = first.DescribeNativeState();
                Program.Check(status.Contains("held=14; pending entries=7; total=21"), "unknown remainder stays in actual pending reserve, not double counted");
                Program.Equal(0, sends, "diagnostics and rejected transfer dispatch no external orders");
                Program.Equal(0, spyA.Cancels + spyB.Cancels, "diagnostics never cancel another campaign orders");
                DateTime[] times = (DateTime[])typeof(Futures2NativeAdapter).GetField("_accountAt", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(first);
                times[0] = DateTime.Now.AddMinutes(-2);
                Program.Check(first.NativeStateBlockReason.Contains("stale"), "stale account observation has a distinct visible cause");
                spyA.AccountChanged(new ExplicitAccount(new Portfolio { Number = "fixture", ValueCurrent = 1000 }, true, Array.Empty<PositionOnBoard>(), DateTime.Now));
                Program.Check(first.NativeStateBlockReason.Contains("stale"), "funds-only update does not clear a stale position cause");
            }
            finally
            {
                Futures2Coordination.Remove("Tester/" + name); Futures2Coordination.Remove("Tester/" + name + "-sender");
            }
        }

        private static void AttestedZero()
        {
            BotTabSimple first = AdapterCases.Tab("TEST", out FixtureServerProxy spy);
            BotTabSimple second = AdapterCases.Tab("NEXT", out FixtureServerProxy unused);
            using Futures2NativeAdapter adapter = new Futures2NativeAdapter(first, second, null, true, _ => { });
            adapter.Engine.Configure(Program.Plan(), new Futures2Policy(), 210, 0);
            adapter.Quotes[0] = Program.Quotes(time: DateTime.Now)[0];
            spy.AccountChanged(new ExplicitAccount(new Portfolio { Number = "fixture", ValueCurrent = 1000 }, true, Array.Empty<PositionOnBoard>(), DateTime.Now));
            Program.Check(adapter.NativeStateBlockReason.Contains("unknown"), "missing position is visibly unknown before attestation");
            adapter.Reconcile(true);
            Program.Check(adapter.DescribeNativeState().Contains("source=owner-attested zero"), "operator-attested absent zero is not labeled broker reported");
            spy.AccountChanged(Observation(0));
            Program.Check(adapter.DescribeNativeState().Contains("source=account observation") && !adapter.DescribeNativeState().Contains("owner-attested zero"), "actual newer position observation replaces attestation provenance");
            spy.ConnectionChanged("Disconnect");
            Program.Check(adapter.NativeStateBlockReason.Contains("unknown") && !adapter.DescribeNativeState().Contains("owner-attested zero"), "disconnect invalidates old attestation and exposes unknown account");
            adapter.Dispose(); Program.Equal("Adapter disposed", adapter.NativeStateBlockReason, "disposed adapter never advertises readiness");
        }
    }
}
