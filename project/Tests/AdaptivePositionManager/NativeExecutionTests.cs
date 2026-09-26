using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using OsEngine.Entity;
using OsEngine.Market.Servers;
using OsEngine.Market.Servers.Optimizer;
using OsEngine.Market.Servers.Tester;
using OsEngine.OsTrader.AdaptivePositionManager;

namespace OsEngine.AdaptivePositionManager.Tests
{
    /// <summary>
    /// Exact native method fixture. Skips server constructors so no settings, server threads, connectors or
    /// application are started. Establishes selected native fill rules only, not full Tester UI integration.
    /// </summary>
    internal sealed class NativeExecutionTests
    {
        private readonly List<string> _callbacks = new List<string>();
        private decimal _filled;
        private DateTime _clock;

        internal static void Run()
        {
            NativeExecutionTests test = new NativeExecutionTests();
            test.CheckServer(typeof(TesterServer), true);
            test.CheckServer(typeof(OptimizerServer), false);
            test.EmptyClock();
            test.CapabilityGuards();
        }

        private void CheckServer(Type serverType, bool equalTimeFills)
        {
            object server = RuntimeHelpers.GetUninitializedObject(serverType);
            Security security = new Security { Name = "APM_SYNTH", PriceStep = 1, PriceStepCost = 1 };
            Order order = new Order { NumberUser = 123, NumberMarket = "fixture", SecurityNameCode = security.Name,
                PortfolioNumber = "fixture", Side = Side.Buy, Volume = 10, Price = 100,
                TimeCreate = CoreTests.Start, TypeOrder = OrderPriceType.Market, State = OrderStateType.Active };
            Field(server, "OrdersActive", new List<Order> { order });
            if (server is TesterServer)
            {
                order.MySecurityInTester = new SecurityTester { Security = security };
                Field(server, "_portfolios", new List<Portfolio> { new Portfolio { Number = "fixture" } });
            }
            else Field(server, "_candleSeriesTesterActivate", new List<SecurityOptimizer> { new SecurityOptimizer { Security = security } });
            IServer native = (IServer)server;
            native.NewMyTradeEvent += Native_Fill;
            native.NewOrderIncomeEvent += Native_Order;
            MethodInfo checker = serverType.GetMethod("CheckOrdersInTickTest", BindingFlags.Instance | BindingFlags.NonPublic);
            Trade tick = new Trade { SecurityNameCode = security.Name, Time = CoreTests.Start, Price = 101, Volume = 1 };
            _callbacks.Clear(); _filled = 0;
            bool result = (bool)checker.Invoke(server, new object[] { order, tick, false, false });
            Program.Equal(equalTimeFills, result, serverType.Name + " equal-time market rule");
            if (!result)
            {
                tick.Time = tick.Time.AddSeconds(1);
                result = (bool)checker.Invoke(server, new object[] { order, tick, false, false });
            }
            Program.Check(result, "native next-time market fill");
            Program.Equal(10m, _filled, "native full-volume fill despite one-contract tape");
            Program.Equal("fill,Done", string.Join(",", _callbacks), "native fill-before-Done ordering");
            native.NewMyTradeEvent -= Native_Fill;
            native.NewOrderIncomeEvent -= Native_Order;
        }

        private void Native_Fill(MyTrade fill) { _callbacks.Add("fill"); _filled += fill.Volume; }
        private void Native_Order(Order order) { _callbacks.Add(order.State.ToString()); }
        private void Native_Clock(DateTime time) { _clock = time; }

        private void EmptyClock()
        {
            TesterServer tester = (TesterServer)RuntimeHelpers.GetUninitializedObject(typeof(TesterServer));
            tester.TimeNow = CoreTests.Start;
            tester.TimeEnd = CoreTests.Start.AddMinutes(1);
            Field(tester, "_candleSeriesTesterActivate", new List<SecurityTester> { new SecurityTester { IsActive = false } });
            tester.ReplayTimeAdvancedEvent += Native_Clock;
            typeof(TesterServer).GetMethod("LoadNextData", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(tester, null);
            Program.Equal(CoreTests.Start.AddSeconds(1), _clock, "native Tester timer without tick");
            tester.ReplayTimeAdvancedEvent -= Native_Clock;
            _clock = default;
            OptimizerServer optimizer = (OptimizerServer)RuntimeHelpers.GetUninitializedObject(typeof(OptimizerServer));
            optimizer.TimeNow = CoreTests.Start;
            Field(optimizer, "_storages", new List<DataStorage>());
            Field(optimizer, "_timeAddType", TimeAddInTestType.Second);
            Field(optimizer, "_candleSeriesTesterActivate", new List<SecurityOptimizer>
                { new SecurityOptimizer { RealEndTime = CoreTests.Start.AddMinutes(1), DataType = SecurityTesterDataType.Candle } });
            optimizer.ReplayTimeAdvancedEvent += Native_Clock;
            typeof(OptimizerServer).GetMethod("LoadNextData", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(optimizer, null);
            Program.Equal(CoreTests.Start.AddSeconds(1), _clock, "native Optimizer timer without tick");
            optimizer.ReplayTimeAdvancedEvent -= Native_Clock;
        }
        private static void Field(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);

        private void CapabilityGuards()
        {
            TesterServer tester = (TesterServer)RuntimeHelpers.GetUninitializedObject(typeof(TesterServer));
            OptimizerServer optimizer = (OptimizerServer)RuntimeHelpers.GetUninitializedObject(typeof(OptimizerServer));
            MethodInfo source = typeof(ApmOsEngineAdapter).GetMethod("ValidateSource", BindingFlags.NonPublic | BindingFlags.Static);
            Field(tester, "_typeTesterData", TesterDataType.TickAllCandleState);
            Field(optimizer, "_typeTesterData", TesterDataType.TickOnlyReadyCandle);
            source.Invoke(null, new object[] { StartProgram.IsTester, StartProgram.IsTester, tester });
            source.Invoke(null, new object[] { StartProgram.IsOsOptimizer, StartProgram.IsOsOptimizer, optimizer });
            Program.Check(true, "actual native tick research modes accepted");
            Reject(source, new object[] { StartProgram.IsTester, StartProgram.IsOsTrader, tester }, "APM-ADAPTER-001 caller mode cannot override actual tab");
            Reject(source, new object[] { StartProgram.IsTester, StartProgram.IsTester, optimizer }, "actual server must match tab mode");
            Reject(source, new object[] { StartProgram.IsTester, StartProgram.IsTester, null }, "unqualified missing server rejected");
            Field(tester, "_typeTesterData", TesterDataType.Candle);
            Reject(source, new object[] { StartProgram.IsTester, StartProgram.IsTester, tester }, "APM-ADAPTER-005 synthetic OHLC is not TradeOnly");
            Field(optimizer, "_typeTesterData", TesterDataType.MarketDepthOnlyReadyCandle);
            Reject(source, new object[] { StartProgram.IsOsOptimizer, StartProgram.IsOsOptimizer, optimizer }, "depth-generated trades are not TradeOnly");
            MethodInfo volume = typeof(ApmOsEngineAdapter).GetMethod("ValidateVolumeStep", BindingFlags.NonPublic | BindingFlags.Static);
            Security security = new Security { VolumeStep = 1, DecimalsVolume = 0 };
            volume.Invoke(null, new object[] { security, CoreTests.Spec() });
            Reject(volume, new object[] { security, CoreTests.Spec() with { VolumeStep = 0.5m } }, "APM-ADAPTER-004 different native lot rejected");
            security.VolumeStep = 0;
            Reject(volume, new object[] { security, CoreTests.Spec() }, "missing native lot has no silent fallback");
            security.VolumeStep = 0.5m;
            Reject(volume, new object[] { security, CoreTests.Spec() with { VolumeStep = 0.5m } }, "native decimal precision enforced");
            security.DecimalsVolume = 1;
            volume.Invoke(null, new object[] { security, CoreTests.Spec() with { VolumeStep = 0.5m } });
            Program.Check(true, "explicit fractional native metadata accepted");
        }

        private static void Reject(MethodInfo method, object[] arguments, string name)
        {
            bool rejected = false;
            try { method.Invoke(null, arguments); }
            catch (TargetInvocationException error) when (error.InnerException is NotSupportedException || error.InnerException is InvalidOperationException)
            { rejected = true; }
            Program.Check(rejected, name);
        }
    }
}
