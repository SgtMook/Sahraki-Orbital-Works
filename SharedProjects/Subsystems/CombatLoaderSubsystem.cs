using Sandbox.Game.EntityComponents;
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
    // TypeIDs:
    // MyObjectBuilder_AmmoMagazine
    // MyObjectBuilder_Component
    //
    // SubtypeIDs:
    // NATO_25x184mm
    // Construction
    // MetalGrid
    // InteriorPlate
    // SteelPlate
    // Girder
    // SmallTube
    // LargeTube
    // Display
    // BulletproofGlass
    // Superconductor
    // Computer
    // Reactor
    // Thrust
    // GravityGenerator
    // Medical
    // RadioCommunication
    // Detector
    // Explosives
    // SolarCell
    // PowerCell
    // Canvas
    // Motor

    public class CombatLoaderSubsystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update1;
        
        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "reload") Reload();
            if (command == "unload") Unload();
        }
        
        public void DeserializeSubsystem(string serialized)
        {
        }
        
        public string GetStatus()
        {
            debugBuilder.Clear();

            debugBuilder.AppendLine(TotalInventory.Count.ToString());
            debugBuilder.AppendLine(InventoryOwners.Count.ToString());
            debugBuilder.AppendLine(LastCheckIndex.ToString());
            
            foreach (var kvp in TotalInventory)
            {
                debugBuilder.AppendLine($"{kvp.Key.SubtypeId} - {kvp.Value}");
            }

            return debugBuilder.ToString();
        }
        
        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        IMyTerminalBlock ProgramReference;
        public void Setup(MyGridProgram program, string name, IMyTerminalBlock programReference = null)
        {
            ProgramReference = programReference;
            if (ProgramReference == null) ProgramReference = program.Me;
            Program = program;
            GetParts();
            SortInventory(null);
        }
        
        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            runs++;

            if (runs % 20 == 0)
            {
                UpdateInventories();
            }
        }
        #endregion
        const string kInventoryRequestSection = "InventoryRequest";
        MyGridProgram Program;

        List<IMyTerminalBlock> StoreInventoryOwners = new List<IMyTerminalBlock>();
        Dictionary<IMyTerminalBlock, Dictionary<MyItemType, int>> InventoryRequests = new Dictionary<IMyTerminalBlock, Dictionary<MyItemType, int>>();
        public List<IMyTerminalBlock> InventoryOwners = new List<IMyTerminalBlock>();
        public Dictionary<MyItemType, int> TotalInventoryRequests = new Dictionary<MyItemType, int>();
        public Dictionary<MyItemType, int> TotalInventory = new Dictionary<MyItemType, int>();
        public Dictionary<MyItemType, int> NextTotalInventory = new Dictionary<MyItemType, int>();

        MyIni iniParser = new MyIni();
        List<MyIniKey> iniKeyScratchpad = new List<MyIniKey>();

        List<MyInventoryItem> inventoryItemsScratchpad = new List<MyInventoryItem>();
        HashSet<MyItemType> stackCombineScratchpad = new HashSet<MyItemType>();
        List<MyItemType> itemTypeScratchpad = new List<MyItemType>();
        Dictionary<MyItemType, MyFixedPoint> inventoryRequestAmountsCache = new Dictionary<MyItemType, MyFixedPoint>();

        public int LastCheckIndex = 0;
        public int UpdateNum = 0;
        int kMaxChecksPerRun = 1;
        int runs = 0;

        public bool LoadingInventory = false;
        bool UnloadingInventory = false;

        StringBuilder debugBuilder = new StringBuilder();

        string CargoGroupName;
        string StoreGroupName;

        public CombatLoaderSubsystem(string cargoGroupName = "Cargo", string storeGroupName = "Store")
        {
            CargoGroupName = cargoGroupName;
            StoreGroupName = storeGroupName;
        }

        void GetParts()
        {
            LastCheckIndex = 0;
            InventoryOwners.Clear();
            InventoryRequests.Clear();
            var cargoGroup = Program.GridTerminalSystem.GetBlockGroupWithName(CargoGroupName);
            if (cargoGroup == null) return;
            cargoGroup.GetBlocks(null, CollectInventoryOwners);
        }
        
        bool CollectInventoryOwners(IMyTerminalBlock block)
        {
            if (block.CubeGrid.EntityId != ProgramReference.CubeGrid.EntityId) return false;
            if (block.HasInventory)
            {
                GetBlockRequestSettings(block);
                InventoryOwners.Add(block);
            }

            return false;
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

                    if (!TotalInventoryRequests.ContainsKey(type)) TotalInventoryRequests[type] = 0;
                    TotalInventoryRequests[type] += count;
                }
            }
        }

        void UpdateInventories()
        {
            for (int i = LastCheckIndex; i < LastCheckIndex + kMaxChecksPerRun; i++)
            {
                if (i < InventoryOwners.Count())
                {
                    if (LoadingInventory)
                    {
                        SortInventory(InventoryOwners[i]);
                    }

                    inventoryItemsScratchpad.Clear();
                    InventoryOwners[i].GetInventory(0).GetItems(inventoryItemsScratchpad);

                    foreach (var item in inventoryItemsScratchpad)
                    {
                        if (!NextTotalInventory.ContainsKey(item.Type)) NextTotalInventory[item.Type] = 0;
                        NextTotalInventory[item.Type] += (int)item.Amount;
                    }
                }
                else
                {
                    var inventory = TotalInventory;
                    TotalInventory = NextTotalInventory;
                    inventory.Clear();
                    NextTotalInventory = inventory;
                    LastCheckIndex = 0;
                    if (LoadingInventory)
                    {
                        UnloadingInventory = false;
                        LoadingInventory = false;
                        StoreInventoryOwners.Clear();
                    }
                    UpdateNum++;
                    return;
                }
            }
            LastCheckIndex += kMaxChecksPerRun;
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

        void SortInventory(IMyTerminalBlock inventoryOwner)
        {
            if (inventoryOwner == null) return;

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
                if (!UnloadingInventory && InventoryRequests[inventoryOwner].ContainsKey(inventoryItem.Type))
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

            if (!UnloadingInventory)
            {
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

        void Unload()
        {
            UnloadingInventory = true;
            Reload();
        }

        void Reload()
        {
            UpdateNum++;
            StoreInventoryOwners.Clear();
            var storeGroup = Program.GridTerminalSystem.GetBlockGroupWithName(StoreGroupName);
            if (storeGroup == null)
            {
                return;
            }
            storeGroup.GetBlocksOfType<IMyTerminalBlock>(null, CollectStores);

            LoadingInventory = true;
            LastCheckIndex = 0;
            NextTotalInventory.Clear();
        }

        bool CollectStores(IMyTerminalBlock block)
        {
            if (block.HasInventory)
            {
                StoreInventoryOwners.Add(block);
            }

            return false;
        }
    }
}
