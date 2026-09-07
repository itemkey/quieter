using System;
using System.Collections.Generic;

namespace Quieter.Inventory
{
    public sealed class InventoryModel
    {
        private readonly ItemCatalog catalog;
        private readonly ItemStackState[] inventory = new ItemStackState[InventoryLayout.InventorySlotCount];
        private readonly ItemStackState[] workbench = new ItemStackState[InventoryLayout.WorkbenchSlotCount];
        private ItemStackState cursor;

        public InventoryModel(ItemCatalog itemCatalog)
        {
            catalog = itemCatalog ?? throw new ArgumentNullException(nameof(itemCatalog));
        }

        public IReadOnlyList<ItemStackState> Inventory => inventory;
        public IReadOnlyList<ItemStackState> Workbench => workbench;

        public bool TryDrainLiquid(
            InventorySlotReference source,
            LiquidKind requiredKind,
            ushort maximumMilliliters,
            Func<ItemStackState, ushort, bool> acceptLiquid,
            out ushort drainedMilliliters)
        {
            drainedMilliliters = 0;
            var original = GetSlot(source);
            if (maximumMilliliters == 0 || acceptLiquid == null || original.IsEmpty
                || original.LiquidKind != requiredKind || original.LiquidMilliliters == 0)
                return false;
            var amount = (ushort)Math.Min(maximumMilliliters, original.LiquidMilliliters);
            var next = original;
            next.LiquidMilliliters -= amount;
            if (requiredKind == LiquidKind.Waste)
                next.Cleanliness = (ushort)Math.Min((int)next.Cleanliness, 1200);
            if (next.LiquidMilliliters == 0) next.LiquidKind = LiquidKind.None;
            if (!SetSlot(source, next)) return false;
            try
            {
                // The synchronous sink sees the already-debited source. No notification
                // or save is emitted until it has accepted the transfer.
                if (acceptLiquid(original, amount))
                {
                    drainedMilliliters = amount;
                    return true;
                }
            }
            catch
            {
                SetSlot(source, original);
                throw;
            }
            SetSlot(source, original);
            return false;
        }
        public ItemStackState Cursor => cursor;
        public InventorySlotReference CursorOrigin { get; private set; } = InventorySlotReference.Invalid;
        public byte SelectedHotbarIndex { get; private set; }

        public ItemStackState ActiveStack => inventory[InventoryLayout.FirstHotbarSlot + SelectedHotbarIndex];

        public List<ItemStackState> Load(
            IEnumerable<StoredInventorySlot> slots,
            byte selectedHotbarIndex,
            ulong sampleOwnerSalt = 0)
        {
            Array.Clear(inventory, 0, inventory.Length);
            Array.Clear(workbench, 0, workbench.Length);
            cursor = default;
            CursorOrigin = InventorySlotReference.Invalid;
            var extraSamples = new List<ItemStackState>();
            if (slots != null)
            {
                foreach (var stored in slots)
                {
                    if (stored == null || stored.SlotIndex >= inventory.Length)
                    {
                        continue;
                    }

                    var raw = new ItemStackState(
                            stored.ItemId,
                            stored.Quantity,
                            stored.Condition,
                            (ResourceQuality)stored.Quality,
                            stored.HiddenItemId,
                            ParseNodeId(stored.SourceNodeId),
                            stored.RevealAtPercent,
                            ParseNodeId(stored.SampleId),
                            ParseNodeId(stored.ItemInstanceId),
                            stored.Freshness,
                            stored.BiologicalContamination,
                            stored.ToxinContamination,
                            stored.Wetness,
                            stored.Cleanliness,
                            stored.LiquidMilliliters,
                            (LiquidKind)stored.LiquidKind,
                            stored.Equipped);
                    if (catalog.TryGetItem(raw.ItemId, out var rawDefinition)
                        && rawDefinition.RequiresInstanceId
                        && raw.ItemInstanceId == 0)
                    {
                        raw.ItemInstanceId = ItemInstanceIdFactory.Create();
                    }
                    if (catalog.TryGetItem(raw.ItemId, out var definition)
                        && definition.Kind == ItemKind.HiddenSample)
                    {
                        var quantity = Math.Max(1, (int)raw.Quantity);
                        for (var ordinal = 0; ordinal < quantity; ordinal++)
                        {
                            var sample = raw.WithQuantity(1);
                            sample.SampleId = raw.SampleId != 0 && ordinal == 0
                                ? raw.SampleId
                                : CreateLegacySampleId(sampleOwnerSalt, stored.SlotIndex, ordinal, sample);
                            if (ordinal == 0 && TryValidate(sample, out var first))
                            {
                                inventory[stored.SlotIndex] = first;
                            }
                            else if (TryValidate(sample, out var extra))
                            {
                                extraSamples.Add(extra);
                            }
                        }
                        continue;
                    }

                    if (TryValidate(raw, out var valid)) inventory[stored.SlotIndex] = valid;
                }
            }

            SelectedHotbarIndex = (byte)Math.Min((int)selectedHotbarIndex,
                InventoryLayout.HotbarSlotCount - 1);
            var pending = new List<ItemStackState>();
            foreach (var sample in extraSamples)
            {
                if (AutoInsert(sample, PickupPlacementPriority.InventoryFirst) > 0)
                {
                    pending.Add(sample);
                }
            }
            return pending;
        }

        public List<StoredInventorySlot> CreateStoredSlots()
        {
            var result = new List<StoredInventorySlot>();
            for (var index = 0; index < inventory.Length; index++)
            {
                var stack = inventory[index];
                if (!stack.IsEmpty)
                {
                    result.Add(new StoredInventorySlot
                    {
                        SlotIndex = (byte)index,
                        ItemId = stack.ItemId,
                        Quantity = stack.Quantity,
                        Condition = stack.Condition,
                        Quality = (byte)stack.Quality,
                        HiddenItemId = stack.HiddenItemId,
                        SourceNodeId = stack.SourceNodeId == 0 ? null : stack.SourceNodeId.ToString(),
                        RevealAtPercent = stack.RevealAtPercent,
                        SampleId = stack.SampleId == 0 ? null : stack.SampleId.ToString(),
                        ItemInstanceId = stack.ItemInstanceId == 0
                            ? null : stack.ItemInstanceId.ToString(),
                        Freshness = stack.Freshness,
                        BiologicalContamination = stack.BiologicalContamination,
                        ToxinContamination = stack.ToxinContamination,
                        Wetness = stack.Wetness,
                        Cleanliness = stack.Cleanliness,
                        LiquidMilliliters = stack.LiquidMilliliters,
                        LiquidKind = (byte)stack.LiquidKind,
                        Equipped = stack.Equipped,
                    });
                }
            }

            return result;
        }

        public bool TryCreateNormalizedStoredSlots(out List<StoredInventorySlot> slots)
        {
            var snapshot = new InventoryModel(catalog);
            Array.Copy(inventory, snapshot.inventory, inventory.Length);
            Array.Copy(workbench, snapshot.workbench, workbench.Length);
            snapshot.cursor = cursor;
            snapshot.CursorOrigin = CursorOrigin;
            snapshot.SelectedHotbarIndex = SelectedHotbarIndex;
            if (!snapshot.NormalizeTemporaryStorage())
            {
                slots = null;
                return false;
            }

            slots = snapshot.CreateStoredSlots();
            return true;
        }

        public void SetSelectedHotbar(byte index)
        {
            SelectedHotbarIndex = (byte)Math.Min((int)index, InventoryLayout.HotbarSlotCount - 1);
        }

        public bool SetSlot(InventorySlotReference reference, ItemStackState stack)
        {
            if (!TryGetArray(reference, out var slots) || reference.Index >= slots.Length
                || !TryValidate(stack, out var valid))
            {
                return false;
            }

            slots[reference.Index] = valid;
            return true;
        }

        public ItemStackState GetSlot(InventorySlotReference reference)
        {
            return TryGetArray(reference, out var slots) && reference.Index < slots.Length
                ? slots[reference.Index]
                : default;
        }

        public bool MoveStack(
            InventorySlotReference sourceReference,
            InventorySlotReference destinationReference,
            ushort expectedItemId,
            int quantity)
        {
            if (!cursor.IsEmpty || sourceReference.Equals(destinationReference)
                || !TryGetArray(sourceReference, out var sourceSlots)
                || !TryGetArray(destinationReference, out var destinationSlots)
                || sourceReference.Index >= sourceSlots.Length
                || destinationReference.Index >= destinationSlots.Length)
            {
                return false;
            }

            ref var source = ref sourceSlots[sourceReference.Index];
            ref var destination = ref destinationSlots[destinationReference.Index];
            var maximum = MaximumStack(expectedItemId);
            if (!CanMoveStack(source, destination, expectedItemId, quantity, maximum))
            {
                return false;
            }

            if (destination.IsEmpty)
            {
                destination = source.WithQuantity(quantity);
                source.Quantity -= (ushort)quantity;
                if (source.Quantity == 0) source.Clear();
                return true;
            }

            if (destination.CanStackWith(source))
            {
                var moved = Math.Min(quantity, maximum - destination.Quantity);
                destination.Quantity += (ushort)moved;
                source.Quantity -= (ushort)moved;
                if (source.Quantity == 0) source.Clear();
                return true;
            }

            (source, destination) = (destination, source);
            return true;
        }

        public bool RemoveStack(
            InventorySlotReference sourceReference,
            ushort expectedItemId,
            int quantity,
            out ItemStackState removed)
        {
            removed = default;
            if (!cursor.IsEmpty || !TryGetArray(sourceReference, out var sourceSlots)
                || sourceReference.Index >= sourceSlots.Length)
            {
                return false;
            }

            ref var source = ref sourceSlots[sourceReference.Index];
            if (source.IsEmpty || source.ItemId != expectedItemId
                || quantity <= 0 || quantity > source.Quantity)
            {
                return false;
            }

            removed = source.WithQuantity(quantity);
            source.Quantity -= (ushort)quantity;
            if (source.Quantity == 0) source.Clear();
            return true;
        }

        public bool CanRemoveStack(
            InventorySlotReference sourceReference,
            ushort expectedItemId,
            int quantity)
        {
            if (!cursor.IsEmpty || !TryGetArray(sourceReference, out var sourceSlots)
                || sourceReference.Index >= sourceSlots.Length)
            {
                return false;
            }

            var source = sourceSlots[sourceReference.Index];
            return !source.IsEmpty && source.ItemId == expectedItemId
                && quantity > 0 && quantity <= source.Quantity;
        }

        public static bool CanMoveStack(
            ItemStackState source,
            ItemStackState destination,
            ushort expectedItemId,
            int quantity,
            int maximumStack)
        {
            if (source.IsEmpty || source.ItemId != expectedItemId || quantity <= 0
                || quantity > source.Quantity || maximumStack <= 0)
            {
                return false;
            }

            if (destination.IsEmpty) return quantity <= maximumStack;
            if (destination.CanStackWith(source))
            {
                return destination.Quantity < maximumStack;
            }

            return quantity == source.Quantity;
        }

        public bool LeftClick(InventorySlotReference reference, int selectedQuantity = 0)
        {
            if (!TryGetArray(reference, out var slots) || reference.Index >= slots.Length)
            {
                return false;
            }

            ref var target = ref slots[reference.Index];
            if (cursor.IsEmpty)
            {
                if (target.IsEmpty)
                {
                    return false;
                }

                var amount = selectedQuantity <= 0
                    ? target.Quantity
                    : Math.Min(selectedQuantity, target.Quantity);
                cursor = target.WithQuantity(amount);
                target.Quantity -= (ushort)amount;
                if (target.Quantity == 0) target.Clear();
                CursorOrigin = reference;
                return true;
            }

            if (target.IsEmpty)
            {
                target = cursor;
                cursor = default;
                CursorOrigin = InventorySlotReference.Invalid;
                return true;
            }

            if (target.CanStackWith(cursor))
            {
                var maximum = MaximumStack(cursor.ItemId);
                var moved = Math.Min(cursor.Quantity, maximum - target.Quantity);
                if (moved <= 0) return false;
                target.Quantity += (ushort)moved;
                cursor.Quantity -= (ushort)moved;
                if (cursor.Quantity == 0)
                {
                    cursor.Clear();
                    CursorOrigin = InventorySlotReference.Invalid;
                }

                return true;
            }

            (target, cursor) = (cursor, target);
            CursorOrigin = InventorySlotReference.Invalid;
            return true;
        }

        public bool RightClick(InventorySlotReference reference)
        {
            if (!TryGetArray(reference, out var slots) || reference.Index >= slots.Length)
            {
                return false;
            }

            ref var target = ref slots[reference.Index];
            if (cursor.IsEmpty)
            {
                if (target.IsEmpty) return false;
                var amount = (target.Quantity + 1) / 2;
                cursor = target.WithQuantity(amount);
                target.Quantity -= (ushort)amount;
                if (target.Quantity == 0) target.Clear();
                CursorOrigin = reference;
                return true;
            }

            if (!target.IsEmpty && !target.CanStackWith(cursor))
            {
                return false;
            }

            if (!target.IsEmpty && target.Quantity >= MaximumStack(target.ItemId))
            {
                return false;
            }

            if (target.IsEmpty) target = cursor.WithQuantity(1);
            else target.Quantity++;
            cursor.Quantity--;
            if (cursor.Quantity == 0)
            {
                cursor.Clear();
                CursorOrigin = InventorySlotReference.Invalid;
            }

            return true;
        }

        public bool AdjustCursorFromOrigin(int delta)
        {
            if (delta == 0 || cursor.IsEmpty || !CursorOrigin.IsValid
                || !TryGetArray(CursorOrigin, out var slots)
                || CursorOrigin.Index >= slots.Length)
            {
                return false;
            }

            ref var origin = ref slots[CursorOrigin.Index];
            if (delta > 0)
            {
                if (origin.IsEmpty || !origin.CanStackWith(cursor)
                    || cursor.Quantity >= MaximumStack(cursor.ItemId))
                {
                    return false;
                }

                var moved = Math.Min(delta, Math.Min(origin.Quantity,
                    MaximumStack(cursor.ItemId) - cursor.Quantity));
                cursor.Quantity += (ushort)moved;
                origin.Quantity -= (ushort)moved;
                if (origin.Quantity == 0) origin.Clear();
                return moved > 0;
            }

            var giveBack = Math.Min(-delta, cursor.Quantity - 1);
            if (giveBack <= 0 || (!origin.IsEmpty && !origin.CanStackWith(cursor)))
            {
                return false;
            }

            var space = origin.IsEmpty ? MaximumStack(cursor.ItemId)
                : MaximumStack(cursor.ItemId) - origin.Quantity;
            giveBack = Math.Min(giveBack, space);
            if (giveBack <= 0) return false;
            if (origin.IsEmpty) origin = cursor.WithQuantity(giveBack);
            else origin.Quantity += (ushort)giveBack;
            cursor.Quantity -= (ushort)giveBack;
            return true;
        }

        public bool ShiftClick(InventorySlotReference source)
        {
            if (!cursor.IsEmpty || !TryGetArray(source, out var slots)
                || source.Index >= slots.Length || slots[source.Index].IsEmpty)
            {
                return false;
            }

            if (source.Area == InventorySlotArea.Workbench)
            {
                return MoveSourceIntoRanges(slots, source.Index,
                    (0, InventoryLayout.MainSlotCount),
                    (InventoryLayout.FirstHotbarSlot, InventoryLayout.HotbarSlotCount));
            }

            return InventoryLayout.IsMain(source.Index)
                ? MoveSourceIntoRanges(slots, source.Index,
                    (InventoryLayout.FirstHotbarSlot, InventoryLayout.HotbarSlotCount))
                : MoveSourceIntoRanges(slots, source.Index,
                    (0, InventoryLayout.MainSlotCount));
        }

        public int AutoInsert(ushort itemId, int quantity, PickupPlacementPriority priority)
        {
            if (quantity <= 0 || !catalog.TryGetItem(itemId, out var definition)) return quantity;
            var condition = definition.IsDurable ? definition.MaximumDurability : (ushort)0;
            return AutoInsert(new ItemStackState(itemId, quantity, condition), priority);
        }

        public int AutoInsert(ItemStackState stack, PickupPlacementPriority priority)
        {
            if (stack.IsEmpty || !catalog.TryGetItem(stack.ItemId, out var definition))
                return stack.Quantity;
            if (definition.RequiresInstanceId && stack.ItemInstanceId == 0)
            {
                var remainingUnique = (int)stack.Quantity;
                while (remainingUnique > 0)
                {
                    var unique = stack.WithQuantity(1);
                    unique.ItemInstanceId = ItemInstanceIdFactory.Create();
                    var remainder = priority == PickupPlacementPriority.HotbarFirst
                        ? InsertIntoRangeByPriority(
                            inventory,
                            unique,
                            1,
                            PickupPlacementPriority.HotbarFirst)
                        : InsertIntoRangeByPriority(
                            inventory,
                            unique,
                            1,
                            PickupPlacementPriority.InventoryFirst);
                    if (remainder > 0) break;
                    remainingUnique--;
                }
                return remainingUnique;
            }
            var quantity = (int)stack.Quantity;
            if (priority == PickupPlacementPriority.HotbarFirst)
            {
                quantity = InsertIntoRange(inventory, stack, quantity,
                    InventoryLayout.FirstHotbarSlot, InventoryLayout.HotbarSlotCount);
                return InsertIntoRange(inventory, stack, quantity, 0, InventoryLayout.MainSlotCount);
            }

            quantity = InsertIntoRange(inventory, stack, quantity, 0, InventoryLayout.MainSlotCount);
            return InsertIntoRange(inventory, stack, quantity,
                InventoryLayout.FirstHotbarSlot, InventoryLayout.HotbarSlotCount);
        }

        public bool TryTransferTo(
            InventorySlotReference sourceReference,
            int requestedQuantity,
            InventoryModel destination,
            PickupPlacementPriority priority,
            out ItemStackState transferred)
        {
            transferred = default;
            if (destination == null || ReferenceEquals(this, destination)
                || !cursor.IsEmpty || !destination.cursor.IsEmpty
                || !TryGetArray(sourceReference, out var sourceSlots)
                || sourceReference.Index >= sourceSlots.Length)
            {
                return false;
            }

            ref var source = ref sourceSlots[sourceReference.Index];
            if (source.IsEmpty || requestedQuantity <= 0)
            {
                return false;
            }

            var amount = Math.Min(requestedQuantity, source.Quantity);
            var candidate = source.WithQuantity(amount);
            candidate.Equipped = false;

            // Stage insertion on a copy. The source is changed only after we know the
            // exact destination state, so a full inventory cannot duplicate loot.
            var destinationCopy = new InventoryModel(destination.catalog);
            Array.Copy(destination.inventory, destinationCopy.inventory,
                destination.inventory.Length);
            destinationCopy.SelectedHotbarIndex = destination.SelectedHotbarIndex;
            var remainder = destinationCopy.AutoInsert(candidate, priority);
            var moved = amount - remainder;
            if (moved <= 0)
            {
                return false;
            }

            source.Quantity -= (ushort)moved;
            if (source.Quantity == 0) source.Clear();
            Array.Copy(destinationCopy.inventory, destination.inventory,
                destination.inventory.Length);
            transferred = candidate.WithQuantity(moved);
            return true;
        }

        public bool SimulatePerishables(
            float elapsedRealSeconds,
            float ambientTemperatureC,
            float humidity)
        {
            if (elapsedRealSeconds <= 0f) return false;
            var changed = SimulatePerishableArray(
                inventory, elapsedRealSeconds, ambientTemperatureC, humidity);
            changed |= SimulatePerishableArray(
                workbench, elapsedRealSeconds, ambientTemperatureC, humidity);
            if (!cursor.IsEmpty && catalog.TryGetItem(cursor.ItemId, out var cursorItem))
            {
                var previous = cursor;
                cursor = FoodDecayRules.Advance(
                    cursor, cursorItem, elapsedRealSeconds, ambientTemperatureC, humidity);
                changed |= !cursor.Equals(previous);
            }
            return changed;
        }

        public float CalculateCarriedMassKg()
        {
            var mass = 0f;
            foreach (var stack in inventory)
            {
                if (stack.IsEmpty || !catalog.TryGetItem(stack.ItemId, out var definition))
                    continue;
                mass += definition.UnitMassKg * stack.Quantity
                    + stack.LiquidMilliliters * 0.001f;
            }
            return mass;
        }

        private bool SimulatePerishableArray(
            ItemStackState[] slots,
            float elapsedRealSeconds,
            float ambientTemperatureC,
            float humidity)
        {
            var changed = false;
            for (var index = 0; index < slots.Length; index++)
            {
                var previous = slots[index];
                if (previous.IsEmpty || !catalog.TryGetItem(previous.ItemId, out var item))
                    continue;
                var current = FoodDecayRules.Advance(
                    previous, item, elapsedRealSeconds, ambientTemperatureC, humidity);
                if (current.Equals(previous)) continue;
                slots[index] = current;
                changed = true;
            }
            return changed;
        }

        public float CalculateUsedVolumeLiters()
        {
            var volume = 0f;
            foreach (var stack in inventory)
            {
                if (stack.IsEmpty || !catalog.TryGetItem(stack.ItemId, out var definition))
                    continue;
                volume += definition.UnitVolumeLiters * stack.Quantity;
            }
            return volume;
        }

        public bool MatchesExactly(CraftingRecipe recipe)
        {
            if (recipe == null || recipe.Output == null) return false;
            var required = new Dictionary<ushort, int>();
            foreach (var ingredient in recipe.Ingredients)
            {
                if (ingredient.Item == null || ingredient.Quantity == 0) return false;
                required.TryGetValue(ingredient.Item.ItemId, out var current);
                required[ingredient.Item.ItemId] = current + ingredient.Quantity;
            }

            var actual = new Dictionary<ushort, int>();
            foreach (var stack in workbench)
            {
                if (stack.IsEmpty) continue;
                actual.TryGetValue(stack.ItemId, out var current);
                actual[stack.ItemId] = current + stack.Quantity;
            }

            if (required.Count != actual.Count) return false;
            foreach (var pair in required)
            {
                if (!actual.TryGetValue(pair.Key, out var amount) || amount != pair.Value)
                {
                    return false;
                }
            }

            return true;
        }

        public bool TryCraft(CraftingRecipe recipe)
        {
            if (!cursor.IsEmpty || !MatchesExactly(recipe)) return false;
            var copy = (ItemStackState[])inventory.Clone();
            var outputCondition = recipe.Output.IsDurable
                ? recipe.Output.MaximumDurability
                : (ushort)0;
            var output = new ItemStackState(
                recipe.Output.ItemId,
                recipe.OutputQuantity,
                outputCondition);
            var remainder = InsertIntoRangeByPriority(
                copy,
                output,
                recipe.OutputQuantity,
                recipe.Output.PickupPriority);
            if (remainder > 0) return false;
            Array.Copy(copy, inventory, inventory.Length);
            Array.Clear(workbench, 0, workbench.Length);
            return true;
        }

        public bool NormalizeTemporaryStorage()
        {
            if (!cursor.IsEmpty)
            {
                if (CursorOrigin.IsValid && TryGetArray(CursorOrigin, out var originSlots)
                    && CursorOrigin.Index < originSlots.Length)
                {
                    ref var origin = ref originSlots[CursorOrigin.Index];
                    MergeInto(ref origin, ref cursor);
                }

                if (!cursor.IsEmpty)
                {
                    var remaining = InsertIntoRange(inventory, cursor, cursor.Quantity,
                        0, inventory.Length);
                    cursor.Quantity = (ushort)remaining;
                    if (remaining == 0) cursor.Clear();
                }
            }

            for (var index = 0; index < workbench.Length; index++)
            {
                var stack = workbench[index];
                if (stack.IsEmpty) continue;
                var remaining = InsertIntoRange(inventory, stack, stack.Quantity,
                    0, inventory.Length);
                workbench[index] = remaining == 0
                    ? default
                    : stack.WithQuantity(remaining);
            }

            if (!cursor.IsEmpty) return false;
            foreach (var stack in workbench)
            {
                if (!stack.IsEmpty) return false;
            }

            CursorOrigin = InventorySlotReference.Invalid;
            return true;
        }

        private int InsertIntoRangeByPriority(
            ItemStackState[] target,
            ItemStackState stack,
            int quantity,
            PickupPlacementPriority priority)
        {
            if (priority == PickupPlacementPriority.HotbarFirst)
            {
                quantity = InsertIntoRange(target, stack, quantity,
                    InventoryLayout.FirstHotbarSlot, InventoryLayout.HotbarSlotCount);
                return InsertIntoRange(target, stack, quantity, 0, InventoryLayout.MainSlotCount);
            }

            quantity = InsertIntoRange(target, stack, quantity, 0, InventoryLayout.MainSlotCount);
            return InsertIntoRange(target, stack, quantity,
                InventoryLayout.FirstHotbarSlot, InventoryLayout.HotbarSlotCount);
        }

        private bool MoveSourceIntoRanges(
            ItemStackState[] sourceSlots,
            int sourceIndex,
            params (int Start, int Count)[] ranges)
        {
            var stack = sourceSlots[sourceIndex];
            var remaining = (int)stack.Quantity;
            foreach (var range in ranges)
            {
                remaining = InsertIntoRange(inventory, stack, remaining, range.Start, range.Count);
            }

            if (remaining == stack.Quantity) return false;
            sourceSlots[sourceIndex] = remaining == 0
                ? default
                : stack.WithQuantity(remaining);
            return true;
        }

        private int InsertIntoRange(
            ItemStackState[] target,
            ItemStackState stack,
            int quantity,
            int start,
            int count)
        {
            if (quantity <= 0) return 0;
            var maximum = MaximumStack(stack.ItemId);
            var end = Math.Min(target.Length, start + count);
            for (var index = start; index < end && quantity > 0; index++)
            {
                if (target[index].IsEmpty || !target[index].CanStackWith(stack)
                    || target[index].Quantity >= maximum) continue;
                var moved = Math.Min(quantity, maximum - target[index].Quantity);
                target[index].Quantity += (ushort)moved;
                quantity -= moved;
            }

            for (var index = start; index < end && quantity > 0; index++)
            {
                if (!target[index].IsEmpty) continue;
                var moved = Math.Min(quantity, maximum);
                target[index] = stack.WithQuantity(moved);
                quantity -= moved;
            }

            return quantity;
        }

        private void MergeInto(ref ItemStackState target, ref ItemStackState source)
        {
            if (source.IsEmpty || (!target.IsEmpty && !target.CanStackWith(source))) return;
            var space = target.IsEmpty ? MaximumStack(source.ItemId)
                : MaximumStack(source.ItemId) - target.Quantity;
            var moved = Math.Min(source.Quantity, space);
            if (moved <= 0) return;
            if (target.IsEmpty) target = source.WithQuantity(moved);
            else target.Quantity += (ushort)moved;
            source.Quantity -= (ushort)moved;
            if (source.Quantity == 0) source.Clear();
        }

        private ushort MaximumStack(ushort itemId)
        {
            return catalog.TryGetItem(itemId, out var item) ? item.MaximumStack : (ushort)1;
        }

        private bool TryValidate(ItemStackState stack, out ItemStackState valid)
        {
            if (stack.IsEmpty)
            {
                valid = default;
                return true;
            }

            if (!catalog.TryGetItem(stack.ItemId, out var definition)
                || stack.Quantity > definition.MaximumStack
                || (byte)stack.Quality > (byte)ResourceQuality.Superior
                || (stack.HiddenItemId != 0 && !catalog.TryGetItem(stack.HiddenItemId, out _)))
            {
                valid = default;
                return false;
            }

            if (definition.IsDurable)
            {
                if (stack.Condition == 0)
                {
                    stack.Condition = definition.MaximumDurability;
                }
                else if (stack.Condition > definition.MaximumDurability)
                {
                    valid = default;
                    return false;
                }
            }
            else
            {
                stack.Condition = 0;
            }

            valid = stack;
            return true;
        }

        public bool DamageActiveTool(ToolKind requiredTool, int amount, out bool broke)
        {
            broke = false;
            if (amount <= 0) return false;
            var index = InventoryLayout.FirstHotbarSlot + SelectedHotbarIndex;
            ref var active = ref inventory[index];
            if (active.IsEmpty || !catalog.TryGetItem(active.ItemId, out var definition)
                || definition.Tool != requiredTool || !definition.IsDurable)
            {
                return false;
            }

            if (active.Condition <= amount)
            {
                active.Clear();
                broke = true;
            }
            else
            {
                active.Condition -= (ushort)amount;
            }

            return true;
        }

        public int RevealSamples(ulong sourceNodeId, int studyPercent)
        {
            if (sourceNodeId == 0 || studyPercent < 100) return 0;
            var revealed = 0;
            for (var index = 0; index < inventory.Length; index++)
            {
                var stack = inventory[index];
                if (stack.IsEmpty || stack.SourceNodeId != sourceNodeId
                    || stack.HiddenItemId == 0)
                {
                    continue;
                }

                inventory[index] = new ItemStackState(
                    stack.HiddenItemId,
                    stack.Quantity,
                    0,
                    stack.Quality);
                revealed += stack.Quantity;
            }

            if (revealed > 0)
            {
                ConsolidateInventory();
            }

            return revealed;
        }

        private void ConsolidateInventory()
        {
            for (var sourceIndex = inventory.Length - 1; sourceIndex >= 0; sourceIndex--)
            {
                var source = inventory[sourceIndex];
                if (source.IsEmpty) continue;
                for (var targetIndex = 0; targetIndex < sourceIndex && !source.IsEmpty; targetIndex++)
                {
                    ref var target = ref inventory[targetIndex];
                    if (target.IsEmpty || !target.CanStackWith(source)) continue;
                    var maximum = MaximumStack(source.ItemId);
                    var moved = Math.Min(source.Quantity, maximum - target.Quantity);
                    if (moved <= 0) continue;
                    target.Quantity += (ushort)moved;
                    source.Quantity -= (ushort)moved;
                    if (source.Quantity == 0) source.Clear();
                }

                inventory[sourceIndex] = source;
            }
        }

        private static ulong ParseNodeId(string value) =>
            ulong.TryParse(value, out var parsed) ? parsed : 0UL;

        private static ulong CreateLegacySampleId(
            ulong ownerSalt,
            int slotIndex,
            int ordinal,
            ItemStackState sample)
        {
            unchecked
            {
                var value = 14695981039346656037UL;
                value = (value ^ ownerSalt) * 1099511628211UL;
                value = (value ^ sample.SourceNodeId) * 1099511628211UL;
                value = (value ^ sample.HiddenItemId) * 1099511628211UL;
                value = (value ^ (uint)slotIndex) * 1099511628211UL;
                value = (value ^ (uint)ordinal) * 1099511628211UL;
                value = (value ^ (byte)sample.Quality) * 1099511628211UL;
                return value == 0 ? 1UL : value;
            }
        }

        private bool TryGetArray(InventorySlotReference reference, out ItemStackState[] slots)
        {
            slots = reference.Area switch
            {
                InventorySlotArea.Inventory => inventory,
                InventorySlotArea.Workbench => workbench,
                _ => null,
            };
            return slots != null;
        }
    }
}
