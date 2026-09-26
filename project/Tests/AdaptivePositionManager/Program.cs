using System;
using OsEngine.Entity;
using OsEngine.OsTrader.Panels.Tab;

namespace OsEngine.AdaptivePositionManager.Tests
{
    /// <summary>
    /// Default execution runs offline component tests without an app or server.
    /// Explicit --native-tester runs an owner-authorized synthetic native replay in a fresh temporary directory.
    /// Run with dotnet run --project Tests/AdaptivePositionManager/OsEngine.AdaptivePositionManager.Tests.csproj.
    /// Native entity tests establish local accounting only, not exchange execution capability.
    /// </summary>
    internal static class Program
    {
        private static int _passed;

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 2 && args[0] == "--ui-smoke") return UiSmoke.Run(args[1]);
                if (args.Length == 2 && args[0] == "--load") return LoadQualification.Run(args[1]);
                if (args.Length >= 3 && args[0] == "--native-tester")
                    return NativeTesterRun.Run(args[1], args[2], args.Length > 3 ? args[3] : "plain");
                if (args.Length >= 3 && args[0] == "--native-optimizer")
                    return NativeOptimizerRun.Run(args[1], int.Parse(args[2]), args.Length > 3 ? args[3] : "B1");
                NativeSmoke();
                CoreTests.Run();
                DataTests.Run();
                ContractTests.Run();
                ScheduleTests.Run();
                LifecycleTests.Run();
                DiagnosticsTests.Run();
                NativeExecutionTests.Run();
                ControllerTests.Run();
                RobotLifecycleTests.Run();
                OptimizerStudyTests.Run();
                ResearchModelTests.Run();
                OperationsTests.Run();
                Console.WriteLine("PASS " + _passed + "/" + _passed);
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                Console.Error.WriteLine("FAIL after " + _passed + " checks");
                return 1;
            }
        }

        private static void NativeSmoke()
        {
            Trade tick = new Trade();
            tick.SetTradeFromString("20260925,100000,100.125,2,Buy,123456,9007199254740993");
            Equal(100.125m, tick.Price, "native decimal price");
            Equal("9007199254740993", tick.Id, "native identifier precision");
            Equal(123456, tick.MicroSeconds, "native separate microsecond field");
            Equal(Side.Buy, tick.Side, "native side");
            Position position = new Position { Number = 1, Direction = Side.Buy };
            Order entry = MakeOrder("entry", Side.Buy, 10);
            position.AddNewOpenOrder(entry);
            MyTrade first = MakeFill("f1", "entry", Side.Buy, 2, 100);
            position.SetTrade(first);
            Equal(2m, position.OpenVolume, "partial initial volume");
            Equal(PositionStateType.Open, position.State, "first partial opens native position");
            position.SetTrade(first);
            Equal(2m, position.OpenVolume, "native duplicate fill");
            position.SetTrade(MakeFill("f2", "entry", Side.Buy, 8, 100));
            entry.State = OrderStateType.Done;
            position.AddNewCloseOrder(MakeOrder("reduce", Side.Sell, 4));
            position.SetTrade(MakeFill("f3", "reduce", Side.Sell, 4, 104));
            Equal(6m, position.OpenVolume, "partial close remaining volume");
            position.AddNewOpenOrder(MakeOrder("restore", Side.Buy, 2));
            position.SetTrade(MakeFill("f4", "restore", Side.Buy, 2, 102));
            Equal(8m, position.OpenVolume, "native position accepts re-add");
            Check(typeof(BotTabSimple).GetMethod("CloseOrder", new Type[] { typeof(Order) }) != null,
                "individual cancellation API");
            Check(typeof(BotTabSimple).GetMethod("BuyAtMarketToPosition", new Type[] { typeof(Position), typeof(decimal) }) != null,
                "safe native re-add API");
            Check(typeof(BotTabSimple).GetEvent("MyTradeEvent") != null, "native fill callback");
        }

        private static Order MakeOrder(string id, Side side, decimal volume)
        {
            return new Order { NumberMarket = id, SecurityNameCode = "APM_SYNTH", Side = side,
                Volume = volume, Price = 100, State = OrderStateType.Active };
        }

        private static MyTrade MakeFill(string id, string order, Side side, decimal volume, decimal price)
        {
            return new MyTrade { NumberTrade = id, NumberOrderParent = order, SecurityNameCode = "APM_SYNTH",
                Side = side, Volume = volume, Price = price, Time = new DateTime(2026, 9, 25, 10, 0, 0) };
        }

        internal static void Equal<T>(T expected, T actual, string name)
        {
            if (!object.Equals(expected, actual))
                throw new InvalidOperationException(name + ": expected " + expected + ", actual " + actual);
            _passed++;
        }

        internal static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            _passed++;
        }
    }
}
