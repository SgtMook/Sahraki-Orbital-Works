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
    // Missile200mm
    // NATO_5p56x45mm
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

    // Northwind
    // R75ammo          - Railgun 75mm)
    // R150ammo         - Railgun 150mm)
    // R250ammo         - Railgun 250mm)
    // H203Ammo         - 203mm HE)
    // H203AmmoAP       - 203mm AP)
    // C30Ammo          - 30mm Standard)
    // C30DUammo        - 30mm Dep. Uranium)
    // CRAM30mmAmmo     - C-RAM (CIWS?))
    // C100mmAmmo       - 100mm HE)
    // C300AmmoAP       - 300mm AP)
    // C300AmmoHE       - 300mm HE)
    // C300AmmoG        - 300mm Guided)
    // C400AmmoAP       - 400mm AP)
    // C400AmmoHE       - 400mm HE)
    // C400AmmoCluster  - 400mm Cluster)

    // MWI Homing 
    // TorpedoMk1           - M-1 Launcher
    // DestroyerMissileX    - M-8 Launcher


    public class CombatLoaderSubsystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update1;
        
        public void Command(TimeSpan timestamp, string command, object argument)
        {
            if (command == "reload") Reload();
            if (command == "unload") Unload();
        }

        public void CommandV2(TimeSpan timestamp, CommandLine command)
        {

        }

        public void DeserializeSubsystem(string serialized)
        {
        }
        
        public string GetStatus()
        {
            debugBuilder.Clear();

            debugBuilder.AppendLine(CargoGroupName);
            debugBuilder.AppendLine(StoreGroupName);
            
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

        public void Setup(ExecutionContext context, string name )
        {
            Context = context;

            ParseConfigs();
            GetParts();
            SortInventory(null);
        }
        
        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            runs++;

            if (QueueReload > 0)
            {
                QueueReload--;
                if (QueueReload == 0)
                    Reload();
            }

            if (runs % 10 == 0)
            {
                UpdateInventories();
            }
        }
        #endregion
        string InventoryRequestSection = "InventoryRequest";
        const string kLoaderSection = "Loader";
        ExecutionContext Context;

        List<IMyTerminalBlock> StoreInventoryOwners = new List<IMyTerminalBlock>();
        Dictionary<long, Dictionary<MyItemType, int>> InventoryRequests = new Dictionary<long, Dictionary<MyItemType, int>>();
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
        public int QueueReload = 0;
        bool UnloadingInventory = false;

        StringBuilder debugBuilder = new StringBuilder();
        StringBuilder reportBuilder = new StringBuilder();

        string CargoGroupName;
        string StoreGroupName;
        string ReportOutputName;
        int ReportOutputIndex;

        IMyTextSurface TextSurface;

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

            Context.Terminal.GetBlocksOfType<IMyTerminalBlock>(null, CollectBlocks);

            var cargoGroup = Context.Terminal.GetBlockGroupWithName(CargoGroupName);
            if (cargoGroup == null) return;
            cargoGroup.GetBlocks(null, CollectInventoryOwners);
        }
        
        bool CollectInventoryOwners(IMyTerminalBlock block)
        {
            if (block.CubeGrid.EntityId != Context.Reference.CubeGrid.EntityId) return false;
            if (block.HasInventory)
            {
                GetBlockRequestSettings(block);
                InventoryOwners.Add(block);
            }

            return false;
        }

        bool CollectBlocks(IMyTerminalBlock block)
        {
            if (block.CubeGrid.EntityId != Context.Reference.CubeGrid.EntityId) return false;
            if (block.CustomName == ReportOutputName)
            {
                if (block is IMyTextSurface)
                    TextSurface = (IMyTextSurface)block;
                else if (block is IMyTextSurfaceProvider)
                    TextSurface = ((IMyTextSurfaceProvider)block).GetSurface(ReportOutputIndex);

                return false;
            }
            return false;
        }

        // [Loader]
        // CargoGroupName = Cargo
        // StoreGroupName = Store
        // ReportOutputName = ""
        // ReportOutputIndex = 0
        // Loadout = InventoryRequest
        void ParseConfigs()
        {
            MyIni Parser = new MyIni();
            MyIniParseResult result;
            if (!Parser.TryParse(Context.Reference.CustomData, out result))
                return;

            CargoGroupName = Parser.Get(kLoaderSection, "CargoGroupName").ToString(CargoGroupName);
            StoreGroupName = Parser.Get(kLoaderSection, "StoreGroupName").ToString(StoreGroupName);
            ReportOutputName = Parser.Get(kLoaderSection, "ReportOutputName").ToString(ReportOutputName);
            ReportOutputIndex = Parser.Get(kLoaderSection, "ReportOutputIndex").ToInt32(ReportOutputIndex);
            InventoryRequestSection = Parser.Get(kLoaderSection, "Loadout").ToString(InventoryRequestSection);
        }

        void GetBlockRequestSettings(IMyTerminalBlock block)
        {
            InventoryRequests[block.EntityId] = new Dictionary<MyItemType, int>();

            if (iniParser.TryParse(block.CustomData) && iniParser.ContainsSection(InventoryRequestSection))
            {
                // TODO: Replace with
                //         public void GetKeys(string section, List<MyIniKey> keys);
                iniParser.GetKeys(iniKeyScratchpad);
                foreach (var key in iniKeyScratchpad)
                {
                    if (key.Section != InventoryRequestSection) continue;
                    var count = iniParser.Get(key).ToInt32();
                    if (count == 0) continue;
                    var type = MyItemType.Parse(key.Name);

                    var inventory = block.GetInventory(block.InventoryCount - 1);
                    itemTypeScratchpad.Clear();
                    inventory.GetAcceptedItems(itemTypeScratchpad);
                    if (!itemTypeScratchpad.Contains(type)) continue;

                    InventoryRequests[block.EntityId][type] = count;

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
                        if (TextSurface != null)
                            TextSurface.WriteText($"LOADING - {i} / {InventoryOwners.Count()}");
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
                        if (TextSurface != null)
                        {
                            reportBuilder.Clear();
                            reportBuilder.AppendLine("LOADER REPORT");
                            reportBuilder.AppendLine($"LOADOUT: {InventoryRequestSection}");
                            reportBuilder.AppendLine(DateTime.Now.ToShortTimeString());
                            reportBuilder.AppendLine("========");

                            foreach (var kvp in TotalInventoryRequests)
                            {
                                var request = kvp.Value;
                                int got = TotalInventory.GetValueOrDefault(kvp.Key);
                                reportBuilder.AppendLine($"{(got == request ? "AOK" : (got > request ? "OVR" : "MIS"))} - {kvp.Key.SubtypeId} - {got} / {request}");
                            }

                            TextSurface.WriteText(reportBuilder.ToString());
                        }

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
            if (!InventoryRequests.ContainsKey(inventoryOwner.EntityId)) return;

            inventoryItemsScratchpad.Clear();
            inventory.GetItems(inventoryItemsScratchpad);

            foreach (var inventoryItem in inventoryItemsScratchpad)
            {
                int desiredAmount = 0;
                if (!UnloadingInventory && InventoryRequests[inventoryOwner.EntityId].ContainsKey(inventoryItem.Type))
                    desiredAmount = InventoryRequests[inventoryOwner.EntityId][inventoryItem.Type];

                var amountDiff = inventoryItem.Amount - desiredAmount;

                if (amountDiff > 0)
                {
                    foreach (var store in StoreInventoryOwners)
                    {
                        var destInventory = store.GetInventory(store.InventoryCount - 1);
                        amountDiff = InventoryHelpers.TransferAsMuchAsPossible(inventory, destInventory, inventoryItem, amountDiff);
                        if (amountDiff <= 0) break;
                    }
                }

                inventoryRequestAmountsCache[inventoryItem.Type] = -amountDiff;
            }

            if (!UnloadingInventory)
            {
                // Transfer in
                foreach (var kvp in InventoryRequests[inventoryOwner.EntityId])
                {
                    MyFixedPoint requestAmount = kvp.Value;
                    if (inventoryRequestAmountsCache.ContainsKey(kvp.Key))
                        requestAmount = inventoryRequestAmountsCache[kvp.Key];

                    if (requestAmount > 0)
                    {
                        foreach (var store in StoreInventoryOwners)
                        {
                            inventoryItemsScratchpad.Clear();
                            var storeInventory = store.GetInventory(store.InventoryCount - 1);
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

        public void Unload()
        {
            UnloadingInventory = true;
            Reload();
        }

        public void Reload()
        {
            UpdateNum++;
            StoreInventoryOwners.Clear();
            var storeGroup = Context.Terminal.GetBlockGroupWithName(StoreGroupName);
            if (storeGroup != null)
            {
                storeGroup.GetBlocksOfType<IMyTerminalBlock>(null, CollectStores);
            }

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
