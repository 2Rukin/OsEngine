/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/
using OsEngine.Entity;
using OsEngine.OsData.OrderFlow;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Xml.Linq;

namespace OsEngine.OrderFlowResearch.Tests
{
    internal static partial class Program
    {
        #region Saved evidence and filters

        private static void TestCloudSavedPairFloors(string root)
        {
            OrderFlowImbalanceProfile profile = new OrderFlowImbalanceProfile(1, new OrderFlowImbalanceSettings());
            foreach ((decimal price, decimal buy, decimal sell) in new[] { (100m, 10m, 1m), (200m, 100m, 20m), (300m, 1000m, 250m) })
            { profile.Change(ImbalanceTick(price, sell, Side.Sell)); profile.Change(ImbalanceTick(price + 1, buy, Side.Buy)); }
            OrderFlowImbalanceSnapshot snapshot = profile.Snapshot();
            AssertTrue(snapshot.Pairs.Keys.SequenceEqual(new decimal[] { 100, 200, 300 }), "All comparable pairs retained in price order");
            AssertEqual(100m, snapshot.BestBuy.LowerPrice, "Raw best ratio is smallest pair");
            OrderFlowCloud cloud = new OrderFlowCloud { InsideImbalance = snapshot, PriceStep = 1, BuyCount = 3, SellCount = 3 };
            foreach ((decimal floor, decimal expected) in new[] { (0m, 100m), (50m, 200m), (900m, 300m) })
            {
                OrderFlowCloudFilter filter = new OrderFlowCloudFilter(new OrderFlowImbalanceSettings { Source = OrderFlowImbalanceSource.Inside,
                    MinimumRatioPercent = 300, MinimumDominantVolume = floor });
                OrderFlowCloud view = filter.Apply(cloud);
                AssertTrue(view.ImbalancePassed && filter.Passes(cloud), "Post-hoc floor can find a previously unselected witness");
                AssertEqual(expected, view.InsideImbalance.EligibleBuy.LowerPrice, "Correct witness after arbitrary new floor");
                AssertTrue(ReferenceEquals(snapshot.Pairs, view.InsideImbalance.Pairs), "Immutable pair root shared by display views");
            }
            OrderFlowCloudFilter difference = new OrderFlowCloudFilter(new OrderFlowImbalanceSettings { Source = OrderFlowImbalanceSource.Inside,
                MinimumRatioPercent = 300, MinimumDifference = 81 });
            AssertEqual(300m, difference.Apply(cloud).InsideImbalance.EligibleBuy.LowerPrice, "Difference floor rescans all saved pairs");
            profile.Change(ImbalanceTick(201, 900, Side.Buy));
            profile.Change(ImbalanceTick(100, 1, Side.Sell), true);
            AssertEqual(100m, snapshot.Pairs[200].Buy, "Later update leaves old snapshot root intact");
            AssertTrue(snapshot.Pairs.ContainsKey(100), "Later expiry leaves old snapshot root intact");
            AssertFalse(profile.Snapshot().Pairs.ContainsKey(100), "Current root expires pair");
        }

        private static void TestCloudFinalCountFilter(string root)
        {
            OrderFlowResearchRequest request = CloudRequest(root, Row(Start, 100, 10, Side.Buy),
                Row(Start.AddMilliseconds(100), 101, 20, Side.Sell), Row(Start.AddMilliseconds(200), 102, 30, Side.Buy));
            OrderFlowCloud cloud = Replay(request).Clouds.Single();
            AssertEqual(1, cloud.Qualified.TradeCount, "First threshold reached at first tick");
            AssertEqual(3, cloud.TradeCount, "Final Cloud contains all three ticks");
            OrderFlowImbalanceSettings off = new OrderFlowImbalanceSettings();
            AssertFalse(new OrderFlowCloudFilter(off, 0, 1).Passes(cloud), "Disabled imbalance still applies final count filter");
            AssertTrue(new OrderFlowCloudFilter(off, 3, 3).Passes(cloud), "Inclusive lower and upper count");
            AssertTrue(new OrderFlowCloudFilter(off, 3, 0).Passes(cloud), "Zero maximum is unlimited");
            AssertFalse(new OrderFlowCloudFilter(off, 4, 0).Passes(cloud), "Minimum count enforced");
            Expect<ArgumentException>(() => new OrderFlowCloudFilter(off, 3, 2));
            Expect<ArgumentException>(() => new OrderFlowCloudFilter(off, -1, 0));
            OrderFlowCloudFilter frozen = new OrderFlowCloudFilter(off, 3, 3);
            off.Source = OrderFlowImbalanceSource.Inside;
            AssertTrue(frozen.Passes(cloud), "UI parameter mutation cannot alter installed filter");
        }

        private static void TestCloudPostfilterNoIo(string root)
        {
            OrderFlowResearchRequest request = TwoCloudRequest(root);
            OrderFlowResearchResult result = Export(request); string before = JsonSerializer.Serialize(result);
            Dictionary<string, string> hashes = Directory.GetFiles(result.ArtifactDirectory).ToDictionary(Path.GetFileName, StoredHash);
            File.Delete(request.TicksFilePath);
            OrderFlowResearchChart chart = new OrderFlowResearchChart(); chart.SetResult(result); chart.SetLayers(false, true, true);
            chart.SetCloudFilter(new OrderFlowCloudFilter(new OrderFlowImbalanceSettings(), 2, 0), true);
            RenderDrawingChart(chart);
            AssertEqual(0, CloudHits(chart, true).Count, "Already calculated singles rejected without an input file");
            AssertEqual(result.Clouds.Count, CloudHits(chart, false).Count, "Other layer unchanged");
            AssertEqual(result.Clouds2.Count, chart.CloudViewRows(true).Count, "Failed view rows remain in table");
            AssertTrue(chart.CloudViewRows(true).All(cloud => !cloud.ImbalancePassed), "View verdict differs from original");
            string id = result.Clouds2[0].CloudId;
            chart.SetCloudFilter(new OrderFlowCloudFilter(new OrderFlowImbalanceSettings()).WithAllowedClouds(new[] { id }), true);
            RenderDrawingChart(chart);
            AssertEqual(id, CloudHits(chart, true).Single().Cloud.CloudId, "Recommendation mask applies saved IDs without IO");
            AssertEqual(before, JsonSerializer.Serialize(result), "Raw DTO, hashes, qualification and pair evidence unchanged");
            foreach (KeyValuePair<string, string> pair in hashes)
            { AssertEqual(pair.Value, StoredHash(Path.Combine(result.ArtifactDirectory, pair.Key)), "Raw artifact unchanged " + pair.Key); }
        }

        private static void TestCloudPairArtifacts(string root)
        {
            OrderFlowResearchRequest request = CloudRequest(root, Row(Start, 100, 1, Side.Sell), Row(Start, 101, 10, Side.Buy),
                Row(Start, 102, 20, Side.Sell), Row(Start, 103, 100, Side.Buy));
            request.CalculateCloud2 = true; request.Cloud2.MinimumTickVolume = 1;
            OrderFlowResearchResult result = Export(request);
            foreach ((string file, List<OrderFlowCloud> clouds) in new[] { ("cloud-pairs.csv", result.Clouds), ("cloud2-pairs.csv", result.Clouds2) })
            {
                string[] rows = File.ReadAllLines(Path.Combine(result.ArtifactDirectory, file));
                AssertEqual("cloud_id,profile,lower_price,buy_volume,sell_volume", rows[0], "Saved-pair schema");
                AssertEqual(1 + clouds.Sum(cloud => cloud.InsideImbalance.Pairs.Count + cloud.ContextImbalance.Pairs.Count), rows.Length,
                    "Every frozen inside/context pair is exported");
                AssertTrue(rows.Skip(1).All(row => row.Split(',').Length == 5), "Stable CSV width");
            }
            AssertEqual(result.ArtifactDirectory, Export(request).ArtifactDirectory, "Full-pair export deterministic and reusable");
            RecordingReplay observer = new RecordingReplay();
            new OrderFlowResearchEngine().Run(request, System.Threading.CancellationToken.None, observer);
            OrderFlowImbalanceSnapshot early = observer.Frames[1].Result.Clouds[0].InsideImbalance;
            AssertEqual(1, early.Pairs.Count, "Earlier replay prefix retains only then-known pair");
            AssertEqual(10m, early.Pairs[100].Buy, "Final run does not mutate replay pair volumes");
        }

        #endregion

        #region Chart geometry and owned help

        private static void TestCloudSquareHitsAndFocus(string root)
        {
            OrderFlowResearchResult result = Replay(TwoCloudRequest(root));
            OrderFlowResearchChart chart = new OrderFlowResearchChart(); chart.SetResult(result); chart.SetLayers(false, false, true);
            RenderDrawingChart(chart);
            (Point center, double radius, OrderFlowCloud single) = CloudHits(chart, true)[0];
            Point corner = new Point(center.X + radius * 0.75, center.Y + radius * 0.75);
            AssertEqual(single.CloudId, chart.CloudAt(corner)?.CloudId, "Square corner outside inscribed circle is interactive");
            chart.SetCloudFilter(new OrderFlowCloudFilter(new OrderFlowImbalanceSettings(), 2, 0), true);
            RenderDrawingChart(chart); AssertEqual(0, CloudHits(chart, true).Count, "Filtered layer initially hidden");
            foreach (OrderFlowDisplayTimeFrame frame in OrderFlowChartTimeFrames.GetMenuValues())
            {
                chart.SetTimeFrame(frame); chart.SelectCloud(single.CloudId); RenderDrawingChart(chart);
                AssertTrue(CloudHits(chart, true).Any(hit => hit.Cloud.CloudId == single.CloudId), "Exact filtered Cloud temporarily visible on " + frame);
            }
            CheckAccumulatedCircle(root, chart);
        }

        private static void CheckAccumulatedCircle(string root, OrderFlowResearchChart chart)
        {
            OrderFlowResearchRequest request = CloudRequest(root, Row(Start, 100, 10, Side.Buy), Row(Start, 100, 10, Side.Buy));
            chart.SetResult(Replay(request)); chart.SetCloudFilters(null, null); chart.SetLayers(false, true, false);
            RenderDrawingChart(chart);
            (Point center, double radius, OrderFlowCloud cloud) = CloudHits(chart, false).Single();
            AssertEqual(cloud.CloudId, chart.CloudAt(center)?.CloudId, "Accumulated circle center is interactive");
            AssertTrue(chart.CloudAt(new Point(center.X + radius * 0.75, center.Y + radius * 0.75)) == null, "Accumulated circle excludes square corners");
        }

        private static void TestCloudExplorerMarkupAndHelp(string root)
        {
            XDocument ui;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Research.Ui.xaml")) { ui = XDocument.Load(stream); }
            string[] inputs = { "TextBox", "ComboBox", "CheckBox", "Button", "Slider", "DatePicker", "ScrollBar" };
            foreach (bool russian in new[] { false, true })
            {
                Dictionary<string, string> fields = OrderFlowResearchHelp.Fields(russian);
                Dictionary<string, string> columns = OrderFlowResearchHelp.Columns(russian);
                foreach (XElement element in ui.Descendants().Where(element => inputs.Contains(element.Name.LocalName)))
                {
                    string name = (string)element.Attribute("Name");
                    AssertTrue((name != null && fields.TryGetValue(name, out string help) && help.Length > 20) || element.Attribute("ToolTip") != null,
                        "Explanatory owned input help " + name + " / " + russian);
                }
                foreach (XElement element in ui.Descendants().Where(element => element.Name.LocalName == "DataGridTextColumn" || element.Name.LocalName == "DataGridCheckBoxColumn"))
                {
                    string path = Regex.Match((string)element.Attribute("Binding"), @"\{Binding (?:Path=)?([^,}]+)").Groups[1].Value;
                    AssertTrue(columns.TryGetValue(path, out string help) && help.Length > 20, "Column header/cell help " + path);
                }
            }
            foreach (string prefix in new[] { "Cloud", "Cloud2" })
            {
                XElement header = ui.Descendants().Single(element => (string)element.Attribute("Name") == "TextBlock" + prefix + "FilterHeader");
                AssertEqual("{DynamicResource ControlForegroundWhite}", (string)header.Attribute("Foreground"), "Readable theme-bound header");
                XElement apply = ui.Descendants().Single(element => (string)element.Attribute("Name") == "Button" + prefix + "FilterApply");
                AssertFalse(apply.Ancestors().Any(element => (string)element.Attribute("Name") == "Grid" + prefix + "Settings"), "Display filter stays outside disabled formation panel");
                XElement context = ui.Descendants().Single(element => (string)element.Attribute("Name") == "TextBox" + prefix + "ContextSeconds");
                AssertFalse(context.Ancestors().Any(element => (string)element.Attribute("Name") == "Expander" + prefix + "Filter"), "Context duration belongs to calculation");
            }
            AssertEqual(1, ui.Descendants().Count(element => (string)element.Attribute("Name") == "TabItemStatistics"), "Separate statistics tab");
            XElement summary = ui.Descendants().Single(element => (string)element.Attribute("Name") == "TextBoxStatisticsSummary");
            AssertTrue(int.Parse((string)summary.Attribute("MinHeight")) >= 60, "Statistics summary reserves multiple visible lines");
        }

        #endregion
    }
}
