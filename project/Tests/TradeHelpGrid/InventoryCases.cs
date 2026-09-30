using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OsEngine.Entity;
using OsEngine.OsTrader.Grids.Futures2;
using OsEngine.OsTrader.Panels.Tab;

namespace OsEngine.TradeHelpGrid.Tests
{
    /// <summary>Offline ownership/recovery fixtures using actual managed Position, Journal and adapter methods.</summary>
    internal static class InventoryCases
    {
        internal static void Run()
        {
            NativeBasis(); NativeReplay(); Registration(); CrashRecovery(); CrashBoundaries(); NativeClose(); GroupsAndAccount();
            HistoricalDayBudget(); FaultCleanup();
        }

        private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value));
        private static Position Position() => new Position { Number = 80001, Direction = Side.Buy,
            SecurityName = "TEST", NameBot = "TEST", SignalTypeOpen = "F2:fixture:entry",
            Lots = 1, PriceStep = 1, PriceStepCost = 1, State = PositionStateType.Opening };

        private static void Fill(Position position, int number, decimal quantity, decimal price, bool entry, string tradeId = null)
        {
            Order order = (entry ? position.OpenOrders : position.CloseOrders)?.Find(o => o.NumberUser == number);
            if (order == null)
            {
                order = new Order { NumberUser = number, NumberMarket = "m" + number, NumberPosition = position.Number,
                    SecurityNameCode = "TEST", PortfolioNumber = "fixture", ServerName = "fixture",
                    Side = entry ? Side.Buy : Side.Sell, Volume = quantity, Price = price, TimeCreate = Program.T0,
                    TypeOrder = OrderPriceType.Limit, State = OrderStateType.Active, UsesSignedPrice = true, SignedPercentBase = 100 };
                if (entry) position.AddNewOpenOrder(order); else position.AddNewCloseOrder(order);
            }
            position.SetTrade(new MyTrade { NumberTrade = tradeId ?? "t" + number, NumberOrderParent = order.NumberMarket,
                SecurityNameCode = "TEST", Volume = quantity, Price = price, Side = order.Side, Time = Program.T0.AddSeconds(number) });
        }

        private static void NativeBasis()
        {
            Position position = Position(); Fill(position, 1, 10, 100, true); Fill(position, 2, 4, 110, false);
            string legacy = position.GetStringForSave().ToString(); Position old = new Position(); old.SetDealFromString(legacy);
            Program.Check(old.Inventory == null && old.OpenVolume == 6, "legacy native format still readable");
            Program.Equal("F2:fixture:entry", old.SignalTypeOpen, "signed owner tag survives native serialization");
            position.EnableInventory("fixture", "fixture", 100, Program.T0);
            PositionInventoryValue basis = Copy(position.Inventory.Value); basis.Cost = 720; basis.LastBasis = 120;
            PositionInventoryAdjustment adjustment = position.PrepareInventory("basis", basis);
            position.ApplyInventory(adjustment);
            Program.Equal(40m, position.Inventory.Value.Realized, "basis preserves historical realized");
            Fill(position, 3, 2, 130, false);
            Program.Equal(60m, position.Inventory.Value.Realized, "subsequent close uses corrected remaining basis");
            Program.Equal(4m, position.OpenVolume, "partial actual close reduces registered projection");
            Program.Equal(120m, position.EntryPrice, "remaining basis after partial close");
            position.SetSignedBidAsk(true, 120, true, 121);
            Program.Equal(60m, position.ProfitPortfolioAbs, "native marked PnL includes preserved realization");
            position.ApplyInventory(adjustment);
            Program.Equal(4m, position.OpenVolume, "replay committed basis does not erase later fill");
            Position loaded = new Position(); loaded.SetDealFromString(position.GetStringForSave().ToString());
            Program.Equal(60m, loaded.Inventory.Value.Realized, "inventory envelope keeps realized");
            Program.Equal(4m, loaded.OpenVolume, "inventory envelope keeps remaining quantity");
            Fill(loaded, 3, 2, 130, false);
            Program.Equal(4m, loaded.OpenVolume, "duplicate actual fill remains idempotent after load");
            Program.Throws(() => loaded.SetDealFromString("INV2:unknown"), "unknown inventory format rejected");
            PositionInventoryAdjustment conflict = Copy(adjustment); conflict.After.Cost++;
            Program.Throws(() => position.ApplyInventory(conflict), "same native operation id different payload rejected");
        }

        private static void NativeReplay()
        {
            Position position = Position(); position.EnableInventory("fixture", "fixture", 100, Program.T0);
            PositionInventoryValue value = Copy(position.Inventory.Value);
            value.Quantity = value.Entered = 10; value.Cost = -0.12345m; value.LastBasis = -0.012345m; value.ClosedAt = default;
            PositionInventoryAdjustment register = position.PrepareInventory("register", value); position.ApplyInventory(register);
            Program.Check(position.OpenOrders == null && position.MyTrades.Count == 0, "native registration invents no orders or trades");
            Program.Equal(Program.T0, position.TimeCreate, "registered-only native creation time");
            Program.Equal("fixture", position.PortfolioName, "registered-only native account identity");
            value = Copy(position.Inventory.Value); value.Cost = 0; value.LastBasis = 0;
            PositionInventoryAdjustment zero = position.PrepareInventory("zero", value);
            // Crash boundary: operation prepared, real fill accepted by Journal before local commit.
            Fill(position, 10, 2, 1, false);
            position.ApplyInventory(zero);
            Program.Equal(8m, position.OpenVolume, "prepared adjustment preserves later close quantity");
            Program.Equal(2m, position.Inventory.Value.Realized, "later close replayed on corrected zero basis");
            value = Copy(position.Inventory.Value); value.Quantity = 0; value.Cost = 0; value.ClosedAt = Program.T0.AddMinutes(1);
            position.ApplyInventory(position.PrepareInventory("release", value));
            Program.Equal(2m, position.Inventory.Value.Realized, "release to zero invents no realized sale");
            Program.Equal(PositionStateType.Done, position.State, "release to zero native state");
            Fill(position, 11, 1, 1, false);
            Program.Check(position.Inventory.Fault.Length > 0 && position.CloseOrders.Last().MyTrades.Count == 1,
                "over-close after release preserves actual execution and faults inventory");
            Program.Equal(PositionStateType.ClosingSurplus, position.State, "over-close fault is not overwritten by flat state");
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly BotTabSimple First;
            internal readonly BotTabSimple Second;
            internal readonly FixtureServerProxy Spy;
            internal readonly Futures2NativeAdapter Adapter;
            internal readonly Futures2Store Store;
            internal readonly string DirectoryPath;
            internal Futures2Plan Plan => Adapter.Engine.Data.Plans[Adapter.Engine.Data.ActivePlan];
            internal Fixture(Action<string> log = null, bool live = false)
            {
                First = AdapterCases.Tab("TEST", out FixtureServerProxy spy); Spy = spy;
                Second = AdapterCases.Tab("NEXT", out FixtureServerProxy unused);
                DirectoryPath = Path.Combine(Path.GetTempPath(), "Futures2-inventory-" + Guid.NewGuid().ToString("N"));
                Store = new Futures2Store(Path.Combine(DirectoryPath, "state.json"));
                Adapter = new Futures2NativeAdapter(First, Second, Store, live, log ?? (_ => { }));
                AdapterCases.Set(Adapter, "_now", Program.T0);
                Futures2Plan plan = Program.Plan(); plan.EndpointIdentity = Adapter.EndpointIdentity(0);
                Adapter.Engine.Configure(plan, new Futures2Policy(), 210, 0);
                Adapter.Save();
            }
            internal Futures2InventoryRequest Request(string id, Futures2InventoryKind kind, decimal quantity = 0, decimal? price = null)
                => new Futures2InventoryRequest { Id = id, PlanId = Plan.Id, Endpoint = 0, Levels = new List<int> { 0 },
                    Kind = kind, Quantity = quantity, Price = price };
            public void Dispose()
            {
                Adapter.Dispose();
                foreach (string path in System.IO.Directory.GetFiles(DirectoryPath)) File.Delete(path);
                System.IO.Directory.Delete(DirectoryPath);
            }
        }

        private static void Registration()
        {
            using Fixture fixture = new Fixture(); Futures2NativeAdapter adapter = fixture.Adapter;
            int sends = 0; fixture.Spy.Execute = _ => sends++;
            adapter.Engine.Data.Book.Realized = 12; adapter.Engine.Data.Book.DaySpent = 19;
            Futures2InventoryRequest register = fixture.Request("register", Futures2InventoryKind.SetQuantity, 6, -0.12345m);
            adapter.AdjustInventory(register);
            Program.Equal(6m, fixture.First.PositionsAll.Single().OpenVolume, "registration publishes native volume");
            Program.Equal(6m, adapter.Engine.Data.Book.Lots.Sum(l => l.Quantity), "registration publishes Book volume");
            Program.Equal(12m, adapter.Engine.Data.Book.Realized, "registration preserves realized money");
            Program.Equal(19m, adapter.Engine.Data.Book.DaySpent, "registration does not spend day execution budget");
            Program.Check(!adapter.CanConfirmNativeState, "registration requires explicit reconciliation");
            adapter.AdjustInventory(register);
            Program.Equal(1, fixture.First.PositionsAll.Count, "duplicate registration does not duplicate native position");
            Futures2InventoryRequest conflict = Copy(register); conflict.Quantity++;
            Program.Throws(() => adapter.AdjustInventory(conflict), "duplicate campaign ID payload conflict");
            adapter.AdjustInventory(fixture.Request("basis", Futures2InventoryKind.SetBasis, price: 0));
            Program.Equal(0m, fixture.First.PositionsAll.Single().EntryPrice, "zero is an explicit native accounting basis");
            adapter.AdjustInventory(fixture.Request("release", Futures2InventoryKind.SetQuantity, 4));
            Program.Equal(4m, fixture.First.PositionsAll.Single().OpenVolume, "set quantity is target not delta");
            Futures2InventoryRequest sale = fixture.Request("external", Futures2InventoryKind.ExternalDecrease, 1, 2);
            sale.ExecutionReference = "broker-manual-exec-1"; sale.Fee = 0.5m; sale.ExecutedAt = Program.T0;
            adapter.AdjustInventory(sale); adapter.AdjustInventory(sale);
            Program.Equal(13.5m, adapter.Engine.Data.Book.Realized, "explicit external sale economics applied once");
            Program.Equal(3m, fixture.First.PositionsAll.Single().OpenVolume, "external sale decreases native ownership once");
            Futures2InventoryRequest secondId = Copy(sale); secondId.Id = "duplicate-reference";
            Program.Throws(() => adapter.AdjustInventory(secondId), "external execution reference cannot be reattributed in campaign");
            Program.Check(sends == 0 && fixture.Spy.Cancels == 0 && adapter.Engine.Data.Book.Intents.Count == 0
                && fixture.First.PositionsAll.All(p => p.OpenOrders == null && p.CloseOrders == null), "local inventory commands neither send nor fabricate broker facts");
            Program.Equal(2, fixture.Store.Load().Schema, "manual inventory upgrades checkpoint schema");
            adapter.Reconcile(false);
            Program.Check(adapter.CanConfirmNativeState, "registered inventory reconciles in offline native fixture");
            Futures2Intent pending = Program.Entry(adapter.Engine.Data.Book, fixture.Plan, 1);
            Program.Throws(() => adapter.AdjustInventory(fixture.Request("pending", Futures2InventoryKind.SetQuantity, 2)), "pending order blocks ownership adjustment");
            pending.State = Futures2IntentState.Unknown;
            Program.Throws(() => adapter.AdjustInventory(fixture.Request("unknown", Futures2InventoryKind.SetBasis, price: 1)), "unknown order blocks basis correction");
            pending.State = Futures2IntentState.Canceled;
            adapter.AdjustInventory(fixture.Request("clear", Futures2InventoryKind.SetQuantity, 0));
            Program.Equal(0, fixture.First.PositionsOpenAll.Count, "release to zero refreshes Journal open arrays");
        }

        private static void CrashRecovery()
        {
            using Fixture fixture = new Fixture();
            fixture.Adapter.AdjustInventory(fixture.Request("crash-register", Futures2InventoryKind.SetQuantity, 5, -1));
            Futures2Checkpoint committed = fixture.Store.Load();
            Futures2InventoryOperation operation = committed.InventoryOperations.Single();
            Futures2Checkpoint prepared = Copy(committed);
            prepared.InventoryOperations[0].Committed = false;
            prepared.Book.Lots = Copy(operation.Before); prepared.Book.InventoryRevision = operation.Revision;
            prepared.Book.Realized = operation.RealizedBefore; prepared.Book.Day = operation.DayBefore; prepared.Book.DaySpent = operation.DaySpentBefore;
            fixture.Store.Save(prepared);
            BotTabSimple first = AdapterCases.Tab("TEST", out FixtureServerProxy spy);
            BotTabSimple second = AdapterCases.Tab("NEXT", out FixtureServerProxy unused);
            using (Futures2NativeAdapter recovered = new Futures2NativeAdapter(first, second, fixture.Store, false, _ => { }))
            {
                Program.Equal(5m, first.PositionsAll.Single().OpenVolume, "crash after Prepared restores native once");
                Program.Equal(5m, recovered.Engine.Data.Book.Lots.Sum(l => l.Quantity), "crash after Prepared commits Book once");
                Program.Check(fixture.Store.Load().InventoryOperations.Single().Committed, "recovery durably commits Prepared operation");
                recovered.Reconcile(false);
                Program.Equal(5m, first.PositionsAll.Single().OpenVolume, "repeated recovery is idempotent");
            }
            fixture.Store.Save(prepared);
            using (Futures2NativeAdapter recovered = new Futures2NativeAdapter(first, second, fixture.Store, false, _ => { }))
                Program.Equal(5m, first.PositionsAll.Single().OpenVolume, "crash after native apply before Commit does not double inventory");
            fixture.Store.Save(committed);
            first = AdapterCases.Tab("TEST", out spy); second = AdapterCases.Tab("NEXT", out unused);
            using (Futures2NativeAdapter recovered = new Futures2NativeAdapter(first, second, fixture.Store, false, _ => { }))
                Program.Equal(5m, first.PositionsAll.Single().OpenVolume, "Committed repairs missing asynchronous Journal record");
            Futures2Checkpoint invalid = Copy(committed); invalid.Schema = 1;
            Program.Throws(() => Futures2Store.Validate(invalid), "schema one cannot hide inventory ledger");
            invalid = Copy(prepared); invalid.Book.InventoryRevision++;
            Program.Throws(() => Futures2Store.Validate(invalid), "stale prepared Book revision rejected");
        }

        private static void CrashBoundaries()
        {
            foreach (string boundary in new[] { "Inventory prepared ", "Inventory native applied " })
            {
                Fixture active = null; bool observed = false;
                using Fixture fixture = new Fixture(message =>
                {
                    if (!message.StartsWith(boundary, StringComparison.Ordinal)) return;
                    Futures2Checkpoint disk = active.Store.Load();
                    observed = disk.InventoryOperations.Single().Committed == false && disk.Book.Lots.Count == 0;
                    throw new IOException("Synthetic crash boundary");
                });
                active = fixture;
                Program.Throws(() => fixture.Adapter.AdjustInventory(fixture.Request("durable", Futures2InventoryKind.SetQuantity, 4, 0)), "interrupt " + boundary);
                Program.Check(observed, "Prepared durable before " + boundary);
                Program.Equal(boundary == "Inventory prepared " ? 0 : 1, fixture.First.PositionsAll.Count, "native state at " + boundary);
                // Read and recover before fixture disposal can save anything.
                using Futures2NativeAdapter recovered = new Futures2NativeAdapter(fixture.First, fixture.Second, fixture.Store, false, _ => { });
                Program.Equal(4m, recovered.Engine.Data.Book.Lots.Sum(l => l.Quantity), "Book recovery after " + boundary);
                Program.Equal(4m, fixture.First.PositionsAll.Single().OpenVolume, "native recovery after " + boundary);
            }
            using (Fixture fixture = new Fixture())
            {
                // An existing directory at the temp-file location rejects Prepared before native publication.
                System.IO.Directory.CreateDirectory(Path.Combine(fixture.DirectoryPath, "state.json.tmp"));
                Program.Throws(() => fixture.Adapter.AdjustInventory(fixture.Request("write-fail", Futures2InventoryKind.SetQuantity, 3, 0)), "Prepared storage failure surfaces");
                Program.Check(fixture.First.PositionsAll.Count == 0 && fixture.Adapter.Engine.Data.Book.Lots.Count == 0,
                    "failed Prepared save changes neither native nor Book quantity");
                System.IO.Directory.Delete(Path.Combine(fixture.DirectoryPath, "state.json.tmp"));
            }
        }

        private static void NativeClose()
        {
            using Fixture fixture = new Fixture(); Futures2NativeAdapter adapter = fixture.Adapter;
            adapter.AdjustInventory(fixture.Request("native-close", Futures2InventoryKind.SetQuantity, 5, 0));
            Position position = fixture.First.PositionsAll.Single();
            Futures2Lot lot = adapter.Engine.Data.Book.Lots.Single();
            Futures2Intent intent = new Futures2Intent { PlanId = fixture.Plan.Id, Endpoint = 0, Entry = false,
                Quantity = 5, Price = 1, Created = Program.T0, PositionNumber = position.Number,
                Allocations = new List<Futures2Allocation> { new Futures2Allocation { LevelId = 0, LotId = lot.Id, Quantity = 5 } } };
            adapter.Engine.Data.Book.Record(intent);
            Order native = null;
            fixture.Spy.Execute = order => { native = order; order.NumberMarket = "registered-close"; };
            fixture.First.SubmitSignedOrder(position, Side.Sell, 5, 1, false, 2, "F2:" + adapter.Engine.Data.Campaign + ":" + intent.Id,
                (p, order) => { intent.OrderNumber = order.NumberUser; intent.State = Futures2IntentState.SubmitPending; adapter.Save(); });
            OsEngine.Journal.Journal journal = fixture.First._journal;
            native.State = OrderStateType.Active; journal.SetNewOrder(native, false);
            Program.Check(position.OpenOrders == null && position.CloseOrders.Count == 1, "registered-only position supports real native close ack");
            MyTrade partial = new MyTrade { NumberTrade = "partial", NumberOrderParent = native.NumberMarket, SecurityNameCode = "TEST",
                Side = Side.Sell, Volume = 2, Price = 1, Time = Program.T0.AddSeconds(1) };
            Program.Check(journal.SetNewMyTrade(partial), "native Journal accepts registered-position partial close");
            AdapterCases.Call(adapter, "OnFill", 0, partial);
            Program.Equal(3m, position.OpenVolume, "registered partial close native quantity");
            Program.Equal(3m, adapter.Engine.Data.Book.Lots.Single().Quantity, "registered partial close Book quantity");
            adapter.Save(); Futures2Checkpoint disk = fixture.Store.Load();
            // Committed manual operation replay must retain both later native and projected execution.
            using (Futures2NativeAdapter recovered = new Futures2NativeAdapter(fixture.First, fixture.Second, fixture.Store, false, _ => { }))
            {
                Program.Equal(3m, recovered.Engine.Data.Book.Lots.Single().Quantity, "restart does not reset post-registration Book fill");
                Program.Equal(3m, position.OpenVolume, "restart does not reset post-registration native fill");
                MyTrade final = new MyTrade { NumberTrade = "final", NumberOrderParent = native.NumberMarket, SecurityNameCode = "TEST",
                    Side = Side.Sell, Volume = 3, Price = 1, Time = Program.T0.AddSeconds(2) };
                journal.SetNewMyTrade(final); AdapterCases.Call(recovered, "OnFill", 0, final);
                Program.Equal(0m, position.OpenVolume, "registered actual final close flat");
                Program.Equal(5m, recovered.Engine.Data.Book.Realized, "registered actual partial/final close economics");
                Program.Equal(0, fixture.First.PositionsOpenAll.Count, "native close updates registered Journal arrays");
            }
        }

        private static void GroupsAndAccount()
        {
            using (Fixture fixture = new Fixture())
            {
                Futures2InventoryRequest group = fixture.Request("group", Futures2InventoryKind.RegisterToCapacity);
                group.UseLevelPrices = true; group.Levels = new List<int> { 0, 2 };
                fixture.Adapter.AdjustInventory(group);
                Program.Equal(20m, fixture.Adapter.Engine.Data.Book.Lots.Sum(l => l.Quantity), "group registration counts full levels not contracts");
                Program.Check(fixture.Adapter.Engine.Data.Book.Lots.Select(l => l.Average).SequenceEqual(new decimal?[] { 1, -1 }),
                    "group registration uses explicit per-level prices");
                Program.Equal(2, fixture.First.PositionsAll.Count, "group registration binds separate native positions");
                Futures2InventoryRequest capacity = fixture.Request("capacity", Futures2InventoryKind.SetQuantity, 11, 0);
                Program.Throws(() => fixture.Adapter.AdjustInventory(capacity), "registration respects level capacity");
                fixture.Adapter.Engine.Data.Capital = 140;
                Futures2InventoryRequest budget = fixture.Request("capital", Futures2InventoryKind.SetQuantity, 1, 0); budget.Levels[0] = 1;
                Program.Throws(() => fixture.Adapter.AdjustInventory(budget), "registration respects campaign capital");
                Program.Equal(2, fixture.First.PositionsAll.Count, "rejected requests publish no native positions");
                fixture.Adapter.AdjustInventory(new Futures2InventoryRequest { Id = "group-release", PlanId = fixture.Plan.Id,
                    Kind = Futures2InventoryKind.SetQuantity, Levels = new List<int> { 0, 2 }, Quantity = 5 });
                Program.Equal(10m, fixture.Adapter.Engine.Data.Book.Lots.Sum(l => l.Quantity), "group quantity means target per selected level");
                Futures2Checkpoint stored = fixture.Store.Load();
                BotTabSimple missing = AdapterCases.Tab("TEST", out FixtureServerProxy spy);
                BotTabSimple next = AdapterCases.Tab("NEXT", out FixtureServerProxy unused);
                using (Futures2NativeAdapter recovered = new Futures2NativeAdapter(missing, next, fixture.Store, false, _ => { }))
                    Program.Equal(10m, missing.PositionsAll.Sum(p => p.OpenVolume), "missing Journal restores committed operation chain");
                Position foreign = Position(); foreign.Number = stored.Book.Lots[0].PositionNumber;
                foreign.SignalTypeOpen = "foreign"; missing = AdapterCases.Tab("TEST", out spy); missing.GetJournal().SetNewDeal(foreign);
                Program.Throws(() => new Futures2NativeAdapter(missing, next, fixture.Store, false, _ => { }), "recovery refuses foreign position number collision");
                Program.Equal("foreign", missing.PositionsAll.Single().SignalTypeOpen, "collision does not steal native ownership");
                missing = AdapterCases.Tab("WRONG", out spy);
                Program.Throws(() => new Futures2NativeAdapter(missing, next, fixture.Store, false, _ => { }), "recovery refuses changed endpoint identity");
            }
            using (Fixture fixture = new Fixture(live: true))
            {
                fixture.Adapter.ExternalNet[0] = 3;
                fixture.Spy.AccountChanged(new OsEngine.Market.Servers.ExplicitAccount(new Portfolio { Number = "fixture", ValueCurrent = 1000 }, false,
                    new[] { new PositionOnBoard { SecurityNameCode = "TEST", ValueCurrent = 5 } }, DateTime.Now));
                fixture.Adapter.AdjustInventory(fixture.Request("own-two", Futures2InventoryKind.SetQuantity, 2, 0));
                Program.Equal(2m, fixture.First.PositionsAll.Single().OpenVolume, "live-adapter attribution respects declared peer inventory");
                Program.Throws(() => fixture.Adapter.AdjustInventory(fixture.Request("steal-peer", Futures2InventoryKind.SetQuantity, 3, 0)),
                    "post-operation account mismatch blocks extra attribution");
                fixture.Spy.AccountChanged(new OsEngine.Market.Servers.ExplicitAccount(new Portfolio { Number = "fixture", ValueCurrent = 1000 }, false,
                    new[] { new PositionOnBoard { SecurityNameCode = "TEST", ValueCurrent = 6 } }, DateTime.Now.AddMilliseconds(1)));
                Program.Check(fixture.Adapter.NativeStateBlockReason.Contains("Account net differs"), "neighbor execution still blocks strict account gate after registration");
                Program.Equal(2m, fixture.Adapter.Engine.Data.Book.Lots.Sum(l => l.Quantity), "neighbor execution never changes own registered inventory");
            }
            Position overflow = Position(); overflow.EnableInventory("fixture", "fixture", 100, Program.T0);
            PositionInventoryValue value = Copy(overflow.Inventory.Value); value.Quantity = value.Entered = 1;
            value.Cost = value.LastBasis = decimal.MaxValue; value.ClosedAt = default;
            overflow.ApplyInventory(overflow.PrepareInventory("huge", value));
            Fill(overflow, 90, 1, 1, true);
            Program.Check(overflow.Inventory.Fault.Length > 0 && overflow.OpenOrders.Single().MyTrades.Count == 1,
                "numeric overflow preserves real execution and sets inventory fault");
            Program.Equal(PositionStateType.ClosingSurplus, overflow.State, "numeric inventory fault takes precedence over native state");
        }

        private static void HistoricalDayBudget()
        {
            using Fixture fixture = new Fixture(); Futures2Book book = fixture.Adapter.Engine.Data.Book;
            fixture.Adapter.AdjustInventory(fixture.Request("day-seed", Futures2InventoryKind.SetQuantity, 4, 0));
            book.Day = Program.T0.Date; book.DaySpent = 100;
            fixture.Adapter.Engine.Data.Policy.DayLimit = 100;
            fixture.Adapter.Engine.Data.Policy.CreditDayExits = true;
            Futures2InventoryRequest yesterdaySale = fixture.Request("yesterday-sale", Futures2InventoryKind.ExternalDecrease, 1, 1);
            yesterdaySale.ExecutionReference = "yesterday-sale-reference"; yesterdaySale.ExecutedAt = Program.T0.AddDays(-1); yesterdaySale.Fee = 0.1m;
            fixture.Adapter.AdjustInventory(yesterdaySale);
            Program.Equal(100m, book.DaySpent, "historical sale cannot free today's day budget");
            Program.Equal(Program.T0.Date, book.Day, "historical sale never rewinds current counter day");
            Program.Equal(0.9m, book.Realized, "historical sale still applies actual realized and fee");
            Program.Equal(3m, book.Lots.Sum(l => l.Quantity), "historical sale still reduces ownership");
            Futures2InventoryRequest yesterdayBuy = fixture.Request("yesterday-buy", Futures2InventoryKind.ExternalIncrease, 1, 1);
            yesterdayBuy.ExecutionReference = "yesterday-buy-reference"; yesterdayBuy.ExecutedAt = Program.T0.AddDays(-1); yesterdayBuy.Fee = 0.2m;
            fixture.Adapter.AdjustInventory(yesterdayBuy);
            Program.Equal(100m, book.DaySpent, "historical increase cannot consume today's day budget");
            Program.Equal(0.7m, book.Realized, "historical increase still applies explicit fee");
            Program.Equal(4m, book.Lots.Sum(l => l.Quantity), "historical increase still registers executed quantity");
            Futures2InventoryRequest todaySale = fixture.Request("today-sale", Futures2InventoryKind.ExternalDecrease, 1, 2);
            todaySale.ExecutionReference = "today-sale-reference"; todaySale.ExecutedAt = Program.T0;
            fixture.Adapter.AdjustInventory(todaySale);
            Program.Equal(93m, book.DaySpent, "same-day confirmed external sale still credits configured collateral");
            fixture.Adapter.AdjustInventory(todaySale);
            Program.Equal(93m, book.DaySpent, "same-day replay does not credit twice");
            Futures2InventoryRequest todayBuy = fixture.Request("today-buy", Futures2InventoryKind.ExternalIncrease, 1, 0);
            todayBuy.ExecutionReference = "today-buy-reference"; todayBuy.ExecutedAt = Program.T0;
            fixture.Adapter.AdjustInventory(todayBuy);
            Program.Equal(100m, book.DaySpent, "same-day external increase still consumes collateral budget");
        }

        private static void FaultCleanup()
        {
            using Fixture fixture = new Fixture();
            fixture.Adapter.AdjustInventory(fixture.Request("fault-seed", Futures2InventoryKind.SetQuantity, 1, 0));
            Position position = fixture.First.PositionsAll.Single();
            fixture.Adapter.AdjustInventory(fixture.Request("fault-release", Futures2InventoryKind.SetQuantity, 0));
            Fill(position, 999, 1, 1, false);
            Program.Check(position.Inventory.Fault.Length > 0 && position.OpenOrders == null,
                "surplus cleanup fixture has actual late fill and no opening order");
            int sends = 0; fixture.Spy.Execute = _ => sends++;
            AdapterCases.Call(fixture.First, "CheckSurplusPositions");
            Program.Check(position.State == PositionStateType.ClosingSurplus && sends == 0 && fixture.Spy.Cancels == 0,
                "legacy surplus cleanup leaves explicit inventory fault for owner reconciliation");
            position.CloseOrders.Single().Volume = 2; position.CloseOrders.Single().State = OrderStateType.Active;
            AdapterCases.Call(fixture.First, "CheckSurplusPositions");
            Program.Check(sends == 0 && fixture.Spy.Cancels == 0,
                "legacy surplus cleanup does not cancel active orders on faulted inventory");
            Program.Check(fixture.Adapter.NativeStateBlockReason.Length > 0 && !fixture.Adapter.CanConfirmNativeState,
                "inventory conflict remains blocked after legacy cleanup pass");
        }
    }
}
