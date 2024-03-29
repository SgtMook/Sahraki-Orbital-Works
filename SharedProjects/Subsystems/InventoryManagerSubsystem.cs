﻿using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRage;
using VRageMath;

namespace IngameScript
{
    public interface IInventoryRefreshRequester
    {
        bool RequestingRefresh();
        void AcknowledgeRequest();
    }


    public class InventoryManagerSubsystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update10;
        
        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "refreshparts") GetParts();
        }

        public void CommandV2(TimeSpan timestamp, CommandLine command)
        {

        }

        public void DeserializeSubsystem(string serialized)
        {
        }
        
        public string GetStatus()
        {
            return "";// debugBuilder.ToString();
        }
        
        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void Setup(ExecutionContext context, string name)
        {
            Context = context;

            GetParts();
        }
        
        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            SortInventories();
            ProcessRefreshRequests();
        }
        #endregion
        const string kInventoryRequestSection = "InventoryRequest";
        ExecutionContext Context;

        List<IMyTerminalBlock> InventoryOwners = new List<IMyTerminalBlock>();
        List<IMyTerminalBlock> StoreInventoryOwners = new List<IMyTerminalBlock>();
        Dictionary<IMyTerminalBlock, Dictionary<MyItemType, int>> InventoryRequests = new Dictionary<IMyTerminalBlock, Dictionary<MyItemType, int>>();

        MyIni iniParser = new MyIni();
        List<MyIniKey> iniKeyScratchpad = new List<MyIniKey>();

        List<MyInventoryItem> inventoryItemsScratchpad = new List<MyInventoryItem>();
        HashSet<MyItemType> stackCombineScratchpad = new HashSet<MyItemType>();
        List<MyItemType> itemTypeScratchpad = new List<MyItemType>();
        Dictionary<MyItemType, MyFixedPoint> inventoryRequestAmountsCache = new Dictionary<MyItemType, MyFixedPoint>();

        List<IInventoryRefreshRequester> RefreshRequesters = new List<IInventoryRefreshRequester>();

        int LastCheckIndex = 0;
        int kMaxChecksPerRun = 1;

        void GetParts()
        {
            LastCheckIndex = 0;
            InventoryOwners.Clear();
            InventoryRequests.Clear();
            Context.Terminal.GetBlocksOfType<IMyTerminalBlock>(null, CollectParts);
        }
        
        bool CollectParts(IMyTerminalBlock block)
        {
            // if (!Program.Me.IsSameConstructAs(block)) return false;

            // Exclude types
            if (block is IMyLargeTurretBase) return false;
            if (block is IMyReactor) return false;
            if (block is IMySmallGatlingGun) return false;
            if (block is IMySmallGatlingGun) return false;
            if (block is IMyGasGenerator) return false;
            if (block.CustomName.Contains("[I-X]")) return false;
        
            if (block.HasInventory)
            {
                if (block.CustomName.Contains("[I-S]"))
                {
                    StoreInventoryOwners.Add(block);
                }
                else
                {
                    GetBlockRequestSettings(block);
                }
                InventoryOwners.Add(block);
            }

            return false;
        }

        public void RegisterRequester(IInventoryRefreshRequester requester)
        {
            RefreshRequesters.Add(requester);
        }

        void GetBlockRequestSettings(IMyTerminalBlock block)
        {
            InventoryRequests[block] = new Dictionary<MyItemType, int>();

            if (iniParser.TryParse(block.CustomData) && iniParser.ContainsSection(kInventoryRequestSection))
            {
                iniParser.GetKeys(iniKeyScratchpad);
                foreach (var key in iniKeyScratchpad)
                {
                    if (key.Section != kInventoryRequestSection) continue;
                    var count = iniParser.Get(key).ToInt32();
                    if (count == 0) continue;
                    var type = MyItemType.Parse(key.Name);

                    var inventory = block.GetInventory(block.InventoryCount - 1);
                    itemTypeScratchpad.Clear();
                    inventory.GetAcceptedItems(itemTypeScratchpad);
                    if (!itemTypeScratchpad.Contains(type)) continue;

                    InventoryRequests[block][type] = count;
                }
            }
        }

        void SortInventories()
        {
            for (int i = LastCheckIndex; i < LastCheckIndex + kMaxChecksPerRun; i++)
            {
                if (i < InventoryOwners.Count())
                {
                    SortInventory(InventoryOwners[i]);
                }
                else
                {
                    LastCheckIndex = 0;
                    return;
                }
            }
            LastCheckIndex += kMaxChecksPerRun;
        }

        void ProcessRefreshRequests()
        {
            bool refresh = false;

            foreach (var requester in RefreshRequesters)
            {
                if (requester.RequestingRefresh())
                {
                    refresh = true;
                    break;
                }
            }

            if (refresh)
            {
                GetParts();
                foreach (var requester in RefreshRequesters)
                    requester.AcknowledgeRequest();
            }
        }

        void SortInventory(IMyTerminalBlock inventoryOwner)
        {
            var inventory = inventoryOwner.GetInventory(inventoryOwner.InventoryCount - 1);
            inventoryRequestAmountsCache.Clear();

            if (inventory == null) return;

            // Combine stacks
            CombineStacks(inventory);

            // Transfer out
            if (!InventoryRequests.ContainsKey(inventoryOwner)) return;

            inventoryItemsScratchpad.Clear();
            inventory.GetItems(inventoryItemsScratchpad);

            foreach (var inventoryItem in inventoryItemsScratchpad)
            {
                int desiredAmount = 0;
                if (InventoryRequests[inventoryOwner].ContainsKey(inventoryItem.Type))
                    desiredAmount = InventoryRequests[inventoryOwner][inventoryItem.Type];

                var amountDiff = inventoryItem.Amount - desiredAmount;

                if (amountDiff > 0)
                {
                    foreach (var store in StoreInventoryOwners)
                    {
                        var destInventory = store.GetInventory(0);
                        amountDiff = InventoryHelpers.TransferAsMuchAsPossible(inventory, destInventory, inventoryItem, amountDiff);
                        if (amountDiff <= 0) break;
                    }
                }

                inventoryRequestAmountsCache[inventoryItem.Type] = -amountDiff;
            }

            // Transfer in
            foreach (var kvp in InventoryRequests[inventoryOwner])
            {
                MyFixedPoint requestAmount = kvp.Value;
                if (inventoryRequestAmountsCache.ContainsKey(kvp.Key))
                    requestAmount = inventoryRequestAmountsCache[kvp.Key];

                if (requestAmount > 0)
                {
                    foreach (var store in StoreInventoryOwners)
                    {
                        inventoryItemsScratchpad.Clear();
                        var storeInventory = store.GetInventory(0);
                        storeInventory.GetItems(inventoryItemsScratchpad);

                        foreach (var item in inventoryItemsScratchpad)
                        {
                            if (item.Type == kvp.Key)
                            {
                                requestAmount = InventoryHelpers.TransferAsMuchAsPossible(storeInventory, inventory, item, requestAmount);
                                break;
                            }
                        }

                        if (requestAmount <= 0) break;
                    }
                }
            }
        }

        void CombineStacks(IMyInventory inventory)
        {
            inventoryItemsScratchpad.Clear();
            inventory.GetItems(inventoryItemsScratchpad);
            stackCombineScratchpad.Clear();
            foreach (var inventoryItem in inventoryItemsScratchpad)
            {
                if (stackCombineScratchpad.Contains(inventoryItem.Type))
                    inventory.TransferItemTo(inventory, inventoryItem);
                else
                    stackCombineScratchpad.Add(inventoryItem.Type);
            }
        }


    }
}
