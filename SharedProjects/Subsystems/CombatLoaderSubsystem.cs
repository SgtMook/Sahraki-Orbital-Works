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

    // Kharak
    //
    // MyObjectBuilder_AmmoMagazine
    //
    // NATO_25x184mm
    // NATO_5p56x45mm
    // Ballistics_Flak
    // Ballistics_Cannon
    // Ballistics_Railgun
    // Ballistics_MAC
    // Missile200mm
    // Missiles_Missile
    // Missiles_Torpedo


    public class CombatLoaderSubsystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update1;
        
        public void Command(TimeSpan timestamp, string command, object argument)
        {
        }

        public void CommandV2(TimeSpan timestamp, CommandLine command)
        {
            if (command.Argument(0) == "reload") Reload();
            if (command.Argument(0) == "unload") Unload();
            if (command.Argument(0) == "toggleauto") AutoReload = !AutoReload;
        }

        public void DeserializeSubsystem(string serialized)
        {
        }
        
        public string GetStatus()
        {
            var debugBuilder = Context.SharedStringBuilder;
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

                if (!LoadingInventory && !UnloadingInventory && AutoReload && AutoReloadInterval > 0)
                {
                    if (AutoReloadTicker > 0)
                    {
                        AutoReloadTicker--;
                    }
                    else
                    {
                        AutoReloadTicker = AutoReloadInterval;
                        Reload();
                    }
                }
            }
        }
        #endregion
        string InventoryRequestSection = "InventoryRequest";
        const string kLoaderSection = "Loader";
        const string kLoaderDisplaySection = "LoaderDisplay";
        ExecutionContext Context;

        List<IMyTerminalBlock> StoreInventoryOwners = new List<IMyTerminalBlock>();
        Dictionary<long, Dictionary<MyItemType, int>> InventoryRequests = new Dictionary<long, Dictionary<MyItemType, int>>();
        public List<IMyTerminalBlock> InventoryOwners = new List<IMyTerminalBlock>();
        public Dictionary<MyItemType, int> TotalInventoryRequests = new Dictionary<MyItemType, int>();
        public Dictionary<MyItemType, int> TotalInventory = new Dictionary<MyItemType, int>();
        public Dictionary<MyItemType, int> NextTotalInventory = new Dictionary<MyItemType, int>();
        public List<MyItemType> SortedInventory = new List<MyItemType>();

        List<MyIniKey> iniKeyScratchpad = new List<MyIniKey>();

        List<MyInventoryItem> inventoryItemsScratchpad = new List<MyInventoryItem>();
        HashSet<MyItemType> stackCombineScratchpad = new HashSet<MyItemType>();
        List<MyItemType> itemTypeScratchpad = new List<MyItemType>();
        Dictionary<MyItemType, MyFixedPoint> inventoryRequestAmountsCache = new Dictionary<MyItemType, MyFixedPoint>();

        public int LastCheckIndex = 0;
        public int UpdateNum = 0;
        int LoadsPerRun = 1;
        int runs = 0;

        public bool LoadingInventory = false;
        public int QueueReload = 0;
        bool UnloadingInventory = false;

        string CargoGroupName;
        string StoreGroupName;
        string ReportOutputGroupName;
        string ReportOutputTag;

        int AutoReloadInterval = -1;
        int AutoReloadTicker = -1;
        bool AutoReload = true;

        Dictionary<IMyTextSurface, OutputSurfaceConfig> TextSurfaces = new Dictionary<IMyTextSurface, OutputSurfaceConfig>();

        struct OutputSurfaceConfig
        {
            public bool ShowLoading;
            public string Header;
            public int StartIndex;
            public int EndIndex;

            public OutputSurfaceConfig(bool showLoading = true, string header = "", int startIndex = -1, int endIndex = -1)
            {
                ShowLoading = showLoading;
                Header = header;
                StartIndex = startIndex;
                EndIndex = endIndex;
            }
        }

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
            var displayGroup = Context.Terminal.GetBlockGroupWithName(ReportOutputGroupName);
            if (displayGroup != null)
            {
                displayGroup.GetBlocksOfType<IMyTerminalBlock>(null, CollectDisplays);
            }

            var cargoGroup = Context.Terminal.GetBlockGroupWithName(CargoGroupName);
            if (cargoGroup == null) return;
            cargoGroup.GetBlocks(null, CollectInventoryOwners);

            InventoryOwners.Sort((a, b) => a.CustomName.CompareTo(b.CustomName));
        }
        
        bool CollectInventoryOwners(IMyTerminalBlock block)
        {
            if (block.CubeGrid.EntityId != Context.Reference.CubeGrid.EntityId) return false;
            if (block.HasInventory)
            {
                GetBlockRequestSettings(block);
                InventoryOwners.Add(block);
            }

            SortedInventory.Sort((a, b)=>a.ToString().CompareTo(b.ToString()));

            return false;
        }

        bool CollectBlocks(IMyTerminalBlock block)
        {
            if (block.CubeGrid.EntityId != Context.Reference.CubeGrid.EntityId) return false;
            if (block.CustomName.Contains(ReportOutputTag))
            {
                if (block is IMyTextSurface)
                    AddTextSurface(block);
                else if (block is IMyTextSurfaceProvider)
                    AddTextSurfaceProvider(block);
            }
            return false;
        }

        bool CollectDisplays(IMyTerminalBlock block)
        {
            if (!block.IsSameConstructAs(Context.Reference)) return false;
            if (block is IMyTextSurface)
                AddTextSurface(block);
            else if (block is IMyTextSurfaceProvider)
                AddTextSurfaceProvider(block);

            return false;
        }


        void AddTextSurface(IMyTerminalBlock block)
        {
            TextSurfaces.Add(block as IMyTextSurface, ReadConfigData(block.CustomData, kLoaderDisplaySection));
        }
        void AddTextSurfaceProvider(IMyTerminalBlock block)
        {
            var surfaceProvider = block as IMyTextSurfaceProvider;
            for (int i = 0; i < surfaceProvider.SurfaceCount; i++)
            {
                TextSurfaces.Add(surfaceProvider.GetSurface(i), ReadConfigData(block.CustomData, kLoaderDisplaySection + i.ToString()));
            }
        }

        OutputSurfaceConfig ReadConfigData(string data, string configTag)
        {
            MyIni Parser = new MyIni();
            if (!Parser.TryParse(data))
                return new OutputSurfaceConfig();

            var ShowLoading = Parser.Get(configTag, "ShowLoading").ToBoolean(true);
            var Header = Parser.Get(configTag, "Header").ToString("");
            var StartIndex = Parser.Get(configTag, "StartIndex").ToInt32(-1);
            var EndIndex = Parser.Get(configTag, "EndIndex").ToInt32(-1);

            return new OutputSurfaceConfig(ShowLoading, Header, StartIndex, EndIndex);
        }

        // [Loader]
        // CargoGroupName = Cargo
        // StoreGroupName = Store
        // ReportOutputGroupName = CargoReport
        // ReportOutputTag = [CargoReport]
        // Loadout = InventoryRequest
        // AutoReloadSeconds = -1
        // LoadsPerRun = 1
        void ParseConfigs()
        {
            var Parser = Context.IniParser;

            if (!Parser.TryParse(Context.Reference.CustomData))
                return;

            CargoGroupName = Parser.Get(kLoaderSection, "CargoGroupName").ToString(CargoGroupName);
            StoreGroupName = Parser.Get(kLoaderSection, "StoreGroupName").ToString(StoreGroupName);
            ReportOutputGroupName = Parser.Get(kLoaderSection, "ReportOutputGroupName").ToString(ReportOutputGroupName);
            ReportOutputTag = Parser.Get(kLoaderSection, "ReportOutputTag").ToString("[CargoReport]");
            InventoryRequestSection = Parser.Get(kLoaderSection, "Loadout").ToString(InventoryRequestSection);
            AutoReloadInterval = Parser.Get(kLoaderSection, "AutoReloadSeconds").ToInt32(AutoReloadInterval) * 6;
            AutoReloadTicker = AutoReloadInterval;
            LoadsPerRun = Parser.Get(kLoaderSection, "LoadsPerRun").ToInt32(LoadsPerRun);
        }

//         void BuildDictionaryFromCustomData<TKey,TValue>(ExecutionContext context, IMyTerminalBlock block, string section, Func<string, TKey> funcKeyParse, Func<MyIniValue, TValue> funcValueParse, Action<TKey,TValue> funcWrite) // Dictionary<MyIniValue, TValue> dictionary )
//         {
//             var Parser = context.IniParser;
//             if (Parser.TryParse(block.CustomData) && Parser.ContainsSection(section))
//             {
//                 var TODOiniKeyScratchpad = new List<MyIniKey>();
//                 Parser.GetKeys(TODOiniKeyScratchpad);
//                 foreach (var iniKey in iniKeyScratchpad)
//                 {
//                     if (iniKey.Section != section)
//                         continue;
// 
//                     var value = valueParse(Parser.Get(iniKey));
//                     if (value == null)
//                         continue;
//                     
//                     var key = keyParse(iniKey.Name);
//                     if (key == null)
//                         continue;
// 
//                 }
//             }
//         }

        void GetBlockRequestSettings(IMyTerminalBlock block)
        {
            InventoryRequests[block.EntityId] = new Dictionary<MyItemType, int>();

            var Parser = Context.IniParser;
            if (Parser.TryParse(block.CustomData) && Parser.ContainsSection(InventoryRequestSection))
            {
                // TODO: Replace with
                //         public void GetKeys(string section, List<MyIniKey> keys);
                Parser.GetKeys(iniKeyScratchpad);
                foreach (var key in iniKeyScratchpad)
                {
                    if (key.Section != InventoryRequestSection) continue;
                    var count = Parser.Get(key).ToInt32();
                    if (count == 0) continue;
                    var type = MyItemType.Parse(key.Name);

                    var inventory = block.GetInventory(block.InventoryCount - 1);
                    itemTypeScratchpad.Clear();
                    inventory.GetAcceptedItems(itemTypeScratchpad);
                    if (!itemTypeScratchpad.Contains(type)) continue;

                    InventoryRequests[block.EntityId][type] = count;

                    if (!TotalInventoryRequests.ContainsKey(type))
                        TotalInventoryRequests[type] = 0;
                    TotalInventoryRequests[type] += count;
                    if (!SortedInventory.Contains(type)) SortedInventory.Add(type);
                }
            }
        }

        void UpdateInventories()
        {
            for (int i = LastCheckIndex; i < LastCheckIndex + LoadsPerRun; i++)
            {
                if (i < InventoryOwners.Count())
                {
                    if (InventoryOwners[i].CubeGrid != Context.Reference.CubeGrid) continue;

                    if (LoadingInventory)
                    {
                        SortInventory(InventoryOwners[i]);

                        foreach (var kvp in TextSurfaces)
                        {
                            if (kvp.Value.ShowLoading)
                                kvp.Key.WriteText($"LOADING - {i} / {InventoryOwners.Count()}");
                        }
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
                        foreach (var kvp in TextSurfaces)
                        {
                            var reportBuilder = Context.SharedStringBuilder;

                            reportBuilder.Clear();
                            reportBuilder.AppendLine(kvp.Value.Header != "" ? kvp.Value.Header : "LOADER REPORT");
                            reportBuilder.AppendLine($"LOADOUT: {InventoryRequestSection}");
                            reportBuilder.AppendLine(DateTime.Now.ToShortTimeString());
                            reportBuilder.AppendLine("========");

                            int indexCounter = 0;

                            foreach (var key in SortedInventory)
                            {
                                if (indexCounter >= kvp.Value.EndIndex && kvp.Value.EndIndex > -1)
                                {
                                    break;
                                }

                                if (indexCounter > kvp.Value.StartIndex)
                                {
                                    var request = TotalInventoryRequests[key];
                                    int got = TotalInventory.GetValueOrDefault(key);
                                    reportBuilder.AppendLine($"{(got == request ? "AOK" : (got > request ? "OVR" : "MIS"))} - {key.SubtypeId} - {got} / {request}");
                                }

                                indexCounter++;
                            }

                            kvp.Key.WriteText(reportBuilder.ToString());
                        }

                        UnloadingInventory = false;
                        LoadingInventory = false;
                        StoreInventoryOwners.Clear();
                    }
                    UpdateNum++;
                    return;
                }
            }
            LastCheckIndex += LoadsPerRun;
        }

        void SortInventories()
        {
            for (int i = LastCheckIndex; i < LastCheckIndex + LoadsPerRun; i++)
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
            LastCheckIndex += LoadsPerRun;
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
            storeGroup?.GetBlocksOfType<IMyTerminalBlock>(null, CollectStores);

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
