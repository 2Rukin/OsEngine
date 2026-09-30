using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OsEngine.Entity;

namespace OsEngine.OsTrader.Grids.Futures2
{
    /// <summary>Structural and economic validation of the local ownership recovery ledger.</summary>
    internal static class Futures2InventoryValidation
    {
        internal static void Validate(Futures2Checkpoint data)
        {
            List<Futures2InventoryOperation> operations = data.InventoryOperations;
            if (operations == null || data.Book.InventoryRevision < 0 || data.Schema == 1 && operations.Count > 0
                || operations.Any(o => o == null || o.Request == null)
                || operations.Select(o => o.Request.Id).Distinct().Count() != operations.Count
                || operations.Count(o => !o.Committed) > 1 || operations.Any(o => !o.Committed && o != operations.Last())) Fail();
            long revision = -1;
            HashSet<string> executions = new HashSet<string>();
            foreach (Futures2InventoryOperation operation in operations)
            {
                Futures2InventoryRequest request = operation.Request;
                if (string.IsNullOrWhiteSpace(request.Id) || request.Endpoint < 0 || request.Endpoint > 1
                    || !Enum.IsDefined(request.Kind) || !data.Plans.ContainsKey(request.PlanId)
                    || operation.EndpointIdentity.Length == 0 || operation.EndpointIdentity != data.Plans[request.PlanId].EndpointIdentity
                    || operation.At == default || operation.Revision <= revision || operation.Revision < 0 || operation.Revision == long.MaxValue
                    || operation.Committed && operation.Revision >= data.Book.InventoryRevision
                    || !operation.Committed && operation.Revision != data.Book.InventoryRevision
                    || operation.Before == null || operation.After == null || operation.Native == null || operation.Native.Count == 0) Fail();
                revision = operation.Revision;
                bool execution = request.Kind == Futures2InventoryKind.ExternalIncrease || request.Kind == Futures2InventoryKind.ExternalDecrease;
                if (execution && (string.IsNullOrWhiteSpace(request.ExecutionReference) || !executions.Add(request.ExecutionReference)
                    || !request.Price.HasValue || request.Quantity <= 0 || request.Fee < 0)) Fail();
                if (!execution && (operation.RealizedBefore != operation.RealizedAfter || operation.DayBefore != operation.DayAfter
                    || operation.DaySpentBefore != operation.DaySpentAfter || request.Fee != 0 || request.ExecutionReference.Length > 0)) Fail();
                ValidateLots(operation.Before, data); ValidateLots(operation.After, data);
                HashSet<int> positions = new HashSet<int>();
                foreach (Futures2NativeInventoryChange native in operation.Native)
                {
                    if (native == null || native.Adjustment == null) Fail();
                    Position position = new Position(); position.SetDealFromString(native.Snapshot);
                    if (position.Inventory == null || !positions.Add(position.Number)
                        || !position.SignalTypeOpen.StartsWith("F2:" + data.Campaign + ":", StringComparison.Ordinal)
                        || native.Adjustment.Id != request.Id + ":" + position.Number
                        || position.OpenVolume != operation.Before.Where(l => l.Endpoint == request.Endpoint && l.PositionNumber == position.Number).Sum(l => l.Quantity)) Fail();
                    position.ApplyInventory(native.Adjustment);
                    if (position.OpenVolume != operation.After.Where(l => l.Endpoint == request.Endpoint && l.PositionNumber == position.Number).Sum(l => l.Quantity)) Fail();
                }
                foreach (Futures2Lot lot in operation.After.Where(l => l.PlanId != request.PlanId || l.Endpoint != request.Endpoint
                    || !request.Levels.Contains(l.LevelId)))
                    if (JsonSerializer.Serialize(lot) != JsonSerializer.Serialize(operation.Before.Find(l => l.Id == lot.Id))) Fail();
                foreach (Futures2Lot lot in operation.Before)
                    if (!operation.After.Any(l => l.Id == lot.Id)) Fail();
            }
        }

        private static void ValidateLots(List<Futures2Lot> lots, Futures2Checkpoint data)
        {
            if (lots.Select(l => l.Id).Distinct().Count() != lots.Count) Fail();
            foreach (Futures2Lot lot in lots)
                if (string.IsNullOrWhiteSpace(lot.Id) || !data.Plans.ContainsKey(lot.PlanId) || lot.PositionNumber <= 0
                    || lot.Endpoint < 0 || lot.Endpoint > 1 || lot.Quantity < 0 || lot.Quantity == 0 && lot.Cost != 0
                    || !data.Plans[lot.PlanId].Levels.Any(l => l.Id == lot.LevelId)) Fail();
        }

        private static void Fail() => throw new InvalidDataException("Invalid inventory recovery ledger.");
    }
}
