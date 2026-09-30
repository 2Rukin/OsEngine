using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using OsEngine.Entity;
using OsEngine.Market.Connectors;
using OsEngine.Market.Servers;
using OsEngine.Market.Servers.Optimizer;
using OsEngine.Market.Servers.Tester;

namespace OsEngine.TradeHelpGrid.Tests
{
    /// <summary>Exact native-method fixtures using uninitialized instances: no constructors, servers or background loops.</summary>
    internal static class NativeCases
    {
        internal static void Run()
        {
            OrderRoundtrip(); PositionMark(); Quotes(); Emulator();
            Simulator(typeof(TesterServer)); Simulator(typeof(OptimizerServer));
        }
        private static Order Order(decimal price = 0) => new Order { NumberUser = 123, NumberMarket = "fixture", NumberPosition = 1,
            SecurityNameCode = "TEST", PortfolioNumber = "fixture", Side = Side.Buy, Volume = 2, Price = price,
            TimeCreate = Program.T0, TypeOrder = OrderPriceType.Limit, State = OrderStateType.Active,
            UsesSignedPrice = true, SignedPercentBase = 2 };
        private static MyTrade Trade(Order order, string id, decimal quantity, decimal price) => new MyTrade { NumberTrade = id,
            NumberOrderParent = order.NumberMarket, SecurityNameCode = order.SecurityNameCode, NumberPosition = "1",
            Volume = quantity, Price = price, Side = order.Side, Time = Program.T0.AddSeconds(1) };
        private static void OrderRoundtrip()
        {
            Order original = Order(); string text = original.GetStringForSave().ToString();
            Order restored = new Order(); restored.SetOrderFromString(text);
            Program.Check(restored.UsesSignedPrice, "signed order serialization"); Program.Equal(0m, restored.Price, "literal zero order roundtrip");
            Program.Equal(2m, restored.SignedPercentBase, "independent percentage base roundtrip");
            string legacy = string.Join("@", text.Split('@').Take(26));
            Order old = new Order(); old.SetOrderFromString(legacy);
            Program.Check(!old.UsesSignedPrice && old.SignedPercentBase == 0, "legacy record retains legacy defaults");
        }
        private static void PositionMark()
        {
            Position position = new Position { Number = 1, Direction = Side.Buy, State = PositionStateType.Opening, Lots = 1, PriceStep = 1, PriceStepCost = 1 };
            Order entry = Order(); position.AddNewOpenOrder(entry); position.SetTrade(Trade(entry, "entry", 2, 0));
            position.SetSignedBidAsk(true, -1, true, 0);
            Program.Equal(-1m, position.ProfitOperationAbs, "native zero-entry negative mark"); Program.Equal(-50m, position.ProfitOperationPercent, "positive independent percent basis");
            position.SetBidAsk(50, 60); Program.Equal(-1m, position.ProfitOperationAbs, "legacy quote event cannot overwrite signed mark");
            position.SetSignedBidAsk(false, 0, true, 0); Program.Equal(-1m, position.ProfitOperationAbs, "missing liquidation side retains previous mark");
            Order close = Order(1); close.NumberUser = 124; close.NumberMarket = "close"; close.Side = Side.Sell;
            position.AddNewCloseOrder(close); position.SetTrade(Trade(close, "closefill", 2, 1));
            position.SetSignedBidAsk(false, 0, false, 0);
            Program.Equal(1m, position.ProfitOperationAbs, "closed zero-entry native pnl needs no quote");
            position.CommissionType = CommissionType.OneLotFix; position.CommissionValue = 1;
            Program.Equal(4m, position.CommissionTotal(), "fixed commission counts actual zero-priced fills");
            position.CommissionType = CommissionType.Percent; position.CommissionValue = 1;
            Program.Equal(0.02m, position.CommissionTotal(), "percentage fee on explicit absolute turnover");
            Position negative = new Position { Number = 1, Direction = Side.Buy, State = PositionStateType.Opening, Lots = 1, PriceStep = 1, PriceStepCost = 1,
                CommissionType = CommissionType.Percent, CommissionValue = 1 };
            entry = Order(-2); negative.AddNewOpenOrder(entry); negative.SetTrade(Trade(entry, "ne", 2, -2));
            Program.Equal(0.04m, negative.CommissionTotal(), "negative fill cannot turn commission into rebate");
        }
        private static void Quotes()
        {
            MarketDepth depth = new MarketDepth { SecurityNameCode = "TEST", Time = Program.T0,
                Bids = new List<MarketDepthLevel> { new MarketDepthLevel { Price = 0, Bid = 1 } },
                Asks = new List<MarketDepthLevel> { new MarketDepthLevel { Price = 0, Ask = 1 } } };
            ExplicitQuote quote = ExplicitQuote.FromDepth(depth);
            Program.Check(quote.HasBid && quote.HasAsk && quote.Bid == 0 && quote.Ask == 0, "native depth represents literal zero");
            depth.Asks.Clear(); quote = ExplicitQuote.FromDepth(depth);
            Program.Check(quote.HasBid && !quote.HasAsk, "native one-sided depth presence"); depth.Bids.Clear(); quote = ExplicitQuote.FromDepth(depth);
            Program.Check(!quote.HasBid && !quote.HasAsk, "empty book invalidates both sides");
        }
        private static void Emulator()
        {
            OrderExecutionEmulator emulator = (OrderExecutionEmulator)RuntimeHelpers.GetUninitializedObject(typeof(OrderExecutionEmulator));
            Field(emulator, "_executorLocker", "offline fixture lock"); Field(emulator, "ordersOnBoard", new List<Order>());
            Field(emulator, "_ordersToSend", new ConcurrentQueue<Order>()); Field(emulator, "_myTradesToSend", new ConcurrentQueue<MyTrade>());
            List<MyTrade> fills = new List<MyTrade>(); List<Order> reports = new List<Order>();
            emulator.MyTradeEvent += fills.Add; emulator.OrderChangeEvent += reports.Add;
            emulator.ProcessExplicitQuote(new ExplicitQuote("TEST", true, 0, true, 0, Program.T0));
            Order signed = Order(); emulator.OrderExecute(signed); Call(emulator, "CheckOrders");
            Program.Equal(1, fills.Count, "signed emulator fills at zero"); Program.Equal(0m, fills[0].Price, "signed emulator zero not order fallback");
            Program.Equal("TEST TestPaper", fills[0].SecurityNameCode, "native paper identity explicit");
            Program.Check(reports.All(o => o.UsesSignedPrice && o.SignedPercentBase == 2), "emulator metadata survives callbacks");
            emulator.ProcessExplicitQuote(new ExplicitQuote("TEST", false, 0, false, 0, Program.T0.AddSeconds(1)));
            Order missing = Order(); missing.NumberUser = 125; missing.TypeOrder = OrderPriceType.Market;
            emulator.OrderExecute(missing); Call(emulator, "CheckOrders");
            Program.Equal(1, fills.Count, "signed market cannot fill without known side");
            emulator.ProcessExplicitQuote(new ExplicitQuote("TEST", true, -1, true, -1, Program.T0.AddSeconds(2))); Call(emulator, "CheckOrders");
            Program.Equal(2, fills.Count, "signed market resumes on fresh explicit side"); Program.Equal(-1m, fills[1].Price, "signed negative market fill");
            emulator.MyTradeEvent -= fills.Add; emulator.OrderChangeEvent -= reports.Add;
        }
        private static void Simulator(Type type)
        {
            object server = RuntimeHelpers.GetUninitializedObject(type);
            Security security = new Security { Name = "TEST", PriceStep = 0.00001m, PriceStepCost = 0.00001m };
            Order order = Order(); Field(server, "OrdersActive", new List<Order> { order });
            if (server is TesterServer)
            {
                order.MySecurityInTester = new SecurityTester { Security = security };
                Field(server, "_portfolios", new List<Portfolio> { new Portfolio { Number = "fixture" } });
            }
            else Field(server, "_candleSeriesTesterActivate", new List<SecurityOptimizer> { new SecurityOptimizer { Security = security } });
            List<MyTrade> fills = new List<MyTrade>(); List<string> sequence = new List<string>();
            IServer native = (IServer)server;
            native.NewMyTradeEvent += trade => { fills.Add(trade); sequence.Add("fill"); };
            native.NewOrderIncomeEvent += report => sequence.Add(report.State.ToString());
            Trade tick = new Trade { SecurityNameCode = "TEST", Time = Program.T0.AddSeconds(1), Price = -0.00001m, Volume = 1 };
            bool filled = (bool)Call(server, "CheckOrdersInTickTest", order, tick, false, false);
            Program.Check(filled, type.Name + " literal zero limit accepted");
            Program.Equal(0m, fills[0].Price, type.Name + " zero limit execution retained");
            Program.Equal(2m, fills[0].Volume, type.Name + " native full-order fill model documented");
            Program.Equal("fill,Done", string.Join(",", sequence), type.Name + " native fill-before-Done ordering");
        }
        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);
        private static void Field(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    }
}
