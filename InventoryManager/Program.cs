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
	partial class Program : MyGridProgram
	{
		enum ScriptState
		{
			// Basic
			Standby,

			// Sorting
			Sorting,
		}

		enum Modes
		{
			Sort,
			Request,
		}

		// Setup and utility
		bool isSetup = false;
		StringBuilder setupBuilder = new StringBuilder();
		StringBuilder echoBuilder = new StringBuilder();
		ScriptState currentState = ScriptState.Standby;
		MyCommandLine commandLine = new MyCommandLine();
		Dictionary<string, Action> commands = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);
		Dictionary<ScriptState, Action> updates = new Dictionary<ScriptState, Action>();

		MyFixedPoint MyFixedPointOne = MyFixedPoint.Ceiling(MyFixedPoint.AddSafe(MyFixedPoint.Zero, MyFixedPoint.SmallestPossibleValue));

		List<MyInventoryItem> itemsScratchPad = new List<MyInventoryItem>();
		List<IMyTerminalBlock> getBlocksScratchPad = new List<IMyTerminalBlock>();

		Modes opsMode = Modes.Sort;

		const string sortMode = "[S]";
		const string requestMode = "[R]";

		// Sorting
		List<IMyEntity> inventoryOwners = new List<IMyEntity>();
		const string oreFlag = "o";
		const string ingotFlag = "i";
		const string compFlag = "c";
		const string toolFlag = "t";
		const string ammoFlag = "a";
		const string wasteFlag = "w";
		const string inventoryPrefix = "[I>";
		const string ignoreFlag = "x";
		const string requestFlag = "R";
		Dictionary<string, HashSet<IMyEntity>> flaggedContainers = new Dictionary<string, HashSet<IMyEntity>>();
		List<IMyTerminalBlock> myContainerSet = new List<IMyTerminalBlock>();
		List<IMyTerminalBlock> myRequesterSet = new List<IMyTerminalBlock>();

		// Displayer
		List<IMyTextPanel> displays = new List<IMyTextPanel>();
		List<IMyTerminalBlock> surfaceProviders = new List<IMyTerminalBlock>();
		List<int> surfaceIndices = new List<int>();

		// Requester
		Dictionary<MyItemType, int> requestDict = new Dictionary<MyItemType, int>();
		Dictionary<MyItemType, MyFixedPoint> hasDict = new Dictionary<MyItemType, MyFixedPoint>();
		List<MyItemType> keys = new List<MyItemType>();

		// Key definitions
		Dictionary<string, MyItemType> KeyToType = new Dictionary<string, MyItemType>();
		Dictionary<MyItemType, string> TypeToKey = new Dictionary<MyItemType, string>();

		// Remote
		struct MenuRemote
		{
			public string name;
			public List<MenuRemote> subMenues;
			public string remoteCommand;

			public MenuRemote(string name, string command)
			{
				this.name = name;
				this.remoteCommand = command;
				this.subMenues = new List<MenuRemote>();
			}

			public string Serialize()
			{
				StringBuilder builder = new StringBuilder();
				builder.Append($"{name}{{");
				for (int i = 0; i < subMenues.Count; i++)
				{
					string v = subMenues[i].Serialize();
					builder.Append($"{{{v.Length.ToString()}}}{v}");
				}
				builder.Append($"}}{remoteCommand}");
				return builder.ToString();
			}
		}


		public Program()
		{
			Runtime.UpdateFrequency = UpdateFrequency.Update100;

			if (Me.CustomName.Contains(requestMode))
				opsMode = Modes.Request;

			if (opsMode == Modes.Sort)
				ChangeState(ScriptState.Sorting);

			commands["connectdisplay"] = ConnectDisplay;
			commands["disconnectdisplay"] = DisonnectDisplay;
			commands["send"] = SendData;
			commands["getremotecommands"] = RemoteSendCommands;

			commands["setstate"] = SetState;
			commands["requestall"] = MakeRequest;
			commands["request"] = RequestItem;
			commands["get"] = RequestItem;
			commands["purge"] = PurgeRequestContainers;
			commands["flush"] = PurgeRequestContainers;
			commands["deltarequest"] = DeltaRequest;

			updates[ScriptState.Sorting] = Sorting;

			flaggedContainers[oreFlag] = new HashSet<IMyEntity>();
			flaggedContainers[ingotFlag] = new HashSet<IMyEntity>();
			flaggedContainers[compFlag] = new HashSet<IMyEntity>();
			flaggedContainers[toolFlag] = new HashSet<IMyEntity>();
			flaggedContainers[ammoFlag] = new HashSet<IMyEntity>();
			flaggedContainers[wasteFlag] = new HashSet<IMyEntity>();

			AddKey("CNCP", new MyItemType("MyObjectBuilder_Component", "Construction"));
			AddKey("MTGD", new MyItemType("MyObjectBuilder_Component", "MetalGrid"));
			AddKey("INTP", new MyItemType("MyObjectBuilder_Component", "InteriorPlate"));
			AddKey("STLP", new MyItemType("MyObjectBuilder_Component", "SteelPlate"));
			AddKey("GIRD", new MyItemType("MyObjectBuilder_Component", "Girder"));
			AddKey("SMTB", new MyItemType("MyObjectBuilder_Component", "SmallTube"));
			AddKey("LGTB", new MyItemType("MyObjectBuilder_Component", "LargeTube"));
			AddKey("MOTR", new MyItemType("MyObjectBuilder_Component", "Motor"));
			AddKey("DISP", new MyItemType("MyObjectBuilder_Component", "Display"));
			AddKey("CMPT", new MyItemType("MyObjectBuilder_Component", "Computer"));

			requestDict.Add(KeyToType["CNCP"], 500);
			requestDict.Add(KeyToType["MTGD"], 200);
			requestDict.Add(KeyToType["INTP"], 500);
			requestDict.Add(KeyToType["STLP"], 1000);
			requestDict.Add(KeyToType["GIRD"], 200);
			requestDict.Add(KeyToType["SMTB"], 400);
			requestDict.Add(KeyToType["LGTB"], 200);
			requestDict.Add(KeyToType["MOTR"], 400);
			requestDict.Add(KeyToType["DISP"], 40);
			requestDict.Add(KeyToType["CMPT"], 600);

			hasDict.Add(KeyToType["CNCP"], 0);
			hasDict.Add(KeyToType["MTGD"], 0);
			hasDict.Add(KeyToType["INTP"], 0);
			hasDict.Add(KeyToType["STLP"], 0);
			hasDict.Add(KeyToType["GIRD"], 0);
			hasDict.Add(KeyToType["SMTB"], 0);
			hasDict.Add(KeyToType["LGTB"], 0);
			hasDict.Add(KeyToType["MOTR"], 0);
			hasDict.Add(KeyToType["DISP"], 0);
			hasDict.Add(KeyToType["CMPT"], 0);

			var enumerator = hasDict.GetEnumerator();
			while (enumerator.MoveNext())
			{
				keys.Add(enumerator.Current.Key);
			}

			Load();
		}

		#region Save and Load
		int _currentLine = 0;
		string[] loadArray;
		public void Save()
		{
			StringBuilder storageBuilder = new StringBuilder();

			storageBuilder.AppendLine(displays.Count.ToString());

			for (int i = 0; i < displays.Count; i++)
				storageBuilder.AppendLine(displays[i].EntityId.ToString());
			
			storageBuilder.AppendLine(surfaceProviders.Count.ToString());

			for (int i = 0; i < surfaceProviders.Count; i++)
				storageBuilder.AppendLine($"{surfaceProviders[i].EntityId.ToString()} {surfaceIndices[i]}");

			storageBuilder.AppendLine(requestDict.Keys.Count.ToString());
			IEnumerator<MyItemType> enumerator = requestDict.Keys.GetEnumerator();
			while (enumerator.MoveNext())
				storageBuilder.AppendLine($"{TypeToKey[enumerator.Current]} {requestDict[enumerator.Current]}");

			Me.GetSurface(1).WriteText(storageBuilder.ToString());
		}

		public void Load()
		{
			_currentLine = 0;
			var loadBuilder = new StringBuilder();
			Me.GetSurface(1).ReadText(loadBuilder);
			loadArray = loadBuilder.ToString().Split(
				new[] { "\r\n", "\r", "\n" },
				StringSplitOptions.None
			);

			// First line is the number of hooked displays
			int count = 0;
			int.TryParse(NextStorageLine(), out count);
			for (int i = 0; i < count; i++)
				ConnectDisplay(long.Parse(NextStorageLine()), 0);

			// Number of hooked surface providers
			count = 0;
			int.TryParse(NextStorageLine(), out count);
			for (int i = 0; i < count; i++)
			{
				string[] split = NextStorageLine().Split(' ');
				ConnectDisplay(long.Parse(split[0]), int.Parse(split[1]));
			}

			// Request numbers
			count = 0;
			int.TryParse(NextStorageLine(), out count);
			for (int i = 0; i < count; i++)
			{
				string[] split = NextStorageLine().Split(' ');
				requestDict[KeyToType[split[0]]] = int.Parse(split[1]);
			}
		}

		string NextStorageLine()
		{
			_currentLine += 1;
			if (loadArray.Length >= _currentLine)
				return loadArray[_currentLine - 1];
			return String.Empty;
		}
        #endregion

        public void Main(string argument, UpdateType updateSource)
		{
			echoBuilder.Clear();

			if (!isSetup)
				SetUp();

			echoBuilder.Append(setupBuilder.ToString());

			echoBuilder.AppendLine($"Current state >> {currentState.ToString()}");

			GetBlocks();

			if (commandLine.TryParse(argument))
			{
				Action commandAction;

				// Retrieve the first argument. Switches are ignored.
				string command = commandLine.Argument(0);

				// Now we must validate that the first argument is actually specified, 
				// then attempt to find the matching command delegate.
				if (command == null)
				{
					Echo("No command specified");
				}
				else if (commands.TryGetValue(command, out commandAction))
				{
					// We have found a command. Invoke it.
					commandAction();
				}
				else
				{
					echoBuilder.AppendLine($"Unknown command {command}");
				}
			}
			else
			{
				Action updateAction;
				if (updates.TryGetValue(currentState, out updateAction))
				{
					updateAction();
				}
			}

			doDisplay();
		}

		void ChangeState(ScriptState newState)
		{
			currentState = newState;
		}

		void SetUp()
		{
			bool AOK = true;
			setupBuilder.Clear();
			echoBuilder.Clear();
		}

		// Command functions
		void SetState()
		{
			string argReps = commandLine.Argument(1);
			ScriptState newState = ScriptState.Standby;
			if (argReps != null && Enum.TryParse<ScriptState>(argReps, true, out newState))
				ChangeState(newState);
			Runtime.UpdateFrequency = UpdateFrequency.Update100;
		}

		void MakeRequest()
		{
			PurgeRequestContainers();
			CountInventory();
			var enumerator = requestDict.GetEnumerator();
			while (enumerator.MoveNext())
			{
				var current = enumerator.Current;
				RequestItem(current.Key, current.Value - hasDict[current.Key].ToIntSafe());
			}
		}

		void PurgeRequestContainers()
		{
			for (int i = 0; i < myRequesterSet.Count; i++)
			{
				IMyInventory inv = myRequesterSet[i].GetInventory();
				inv.GetItems(itemsScratchPad);
				for (int j = 0; j < itemsScratchPad.Count; j++)
				{
					AutoputItem(itemsScratchPad[j], inv);
				}
				itemsScratchPad.Clear();
			}
		}

		void RequestItem()
		{
			if (commandLine.ArgumentCount < 3) return;
			string argTag = commandLine.Argument(1);
			string argNum = commandLine.Argument(2);

			float amount = 0;
			if (!float.TryParse(argNum, out amount)) return;

			MyItemType type = KeyToType[argTag];

			RequestItem(type, amount);
		}

		void RequestItem(MyItemType type, float amount)
		{
			if (amount <= 0) return;
			var fixedAmt = amount * MyFixedPointOne;

			string targetFlag = compFlag;

			if (type.TypeId == "MyObjectBuilder_Ammo") targetFlag = ammoFlag;
			else if (type.TypeId == "MyObjectBuilder_Component") targetFlag = compFlag;

			var enumerator = flaggedContainers[targetFlag].GetEnumerator();
			while (enumerator.MoveNext())
			{
				var current = enumerator.Current;
				IMyInventory inv = current.GetInventory();

				inv.GetItems(itemsScratchPad);

				for (int i = 0; i < itemsScratchPad.Count; i++)
				{
					if (itemsScratchPad[i].Type == type)
					{
						for (int j = 0; j < myRequesterSet.Count; j++)
						{
							fixedAmt = TransferAsMuchAsPossible(inv, myRequesterSet[j].GetInventory(), 
										itemsScratchPad[i], fixedAmt);
						}
					}
				}

				itemsScratchPad.Clear();
			}
		}
		
		void RemoteSendCommands()
		{
			long targetId;
			long.TryParse(commandLine.Argument(1), out targetId);

			MenuRemote remoteMenuRoot;

			// Setup remote commands
			remoteMenuRoot = new MenuRemote("root", "");

			// Setup display
			var displaysMenu = new MenuRemote($"Displays ({displays.Count + surfaceProviders.Count})", "");
			displaysMenu.subMenues.Add(GetConnectDisplayRemote());
			displaysMenu.subMenues.Add(GetDisconnectDisplayRemote());

			remoteMenuRoot.subMenues.Add(displaysMenu);

			remoteMenuRoot.subMenues.Add(GetDeltaRequestRemote());

			SendData(targetId, remoteMenuRoot.Serialize());
		}

        #region Remote Display Setup
        MenuRemote GetConnectDisplayRemote()
		{
			MenuRemote connectDisplayRemote = new MenuRemote("Connect >", "");

			// Get surface providers
			GridTerminalSystem.GetBlocksOfType<IMyTextSurfaceProvider>(getBlocksScratchPad, SameConstructAsMe);
			for (int i = 0; i < getBlocksScratchPad.Count; i++)
			{
				var provider = (IMyTextSurfaceProvider)getBlocksScratchPad[i];
				var subItem = new MenuRemote(getBlocksScratchPad[i].CustomName, "");

				for (int j = 0; j < provider.SurfaceCount; j++)
				{
					subItem.subMenues.Add(new MenuRemote($"{provider.GetSurface(j).DisplayName} >", $"connectdisplay {getBlocksScratchPad[i].EntityId} {j}"));
				}

				connectDisplayRemote.subMenues.Add(subItem);
			}
			getBlocksScratchPad.Clear();

			// Get panels
			GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(getBlocksScratchPad, SameConstructAsMe);
			for (int i = 0; i < getBlocksScratchPad.Count; i++)
			{
				var panel = (IMyTextPanel)getBlocksScratchPad[i];
				var subItem = new MenuRemote(getBlocksScratchPad[i].CustomName, "");
				subItem.subMenues.Add(new MenuRemote($"{getBlocksScratchPad[i].DisplayName}", $"connectdisplay {getBlocksScratchPad[i].EntityId} 0"));

				connectDisplayRemote.subMenues.Add(subItem);
			}

			getBlocksScratchPad.Clear();
			return connectDisplayRemote;
		}

		MenuRemote GetDisconnectDisplayRemote()
		{
			MenuRemote disconnectDisplayRemote = new MenuRemote("Disconnect >", "");

			for (int i = 0; i < surfaceProviders.Count; i++)
				disconnectDisplayRemote.subMenues.Add(new MenuRemote(surfaceProviders[i].CustomName, $"disconnectdisplay {surfaceProviders[i].EntityId}"));
			for (int i = 0; i < displays.Count; i++)
				disconnectDisplayRemote.subMenues.Add(new MenuRemote(((IMyTerminalBlock)displays[i]).CustomName, $"disconnectdisplay {displays[i].EntityId}"));

			return disconnectDisplayRemote;
		}

		void ConnectDisplay()
		{
			ConnectDisplay(long.Parse(commandLine.Argument(1)), int.Parse(commandLine.Argument(2)));
		}

		void ConnectDisplay(long id, int subId)
		{
			try
			{
				var block = GridTerminalSystem.GetBlockWithId(id);
				if (block is IMyTextPanel) displays.Add((IMyTextPanel)block);
				else
				{
					setupBuilder.AppendLine("Hi");
					surfaceProviders.Add(block);
					surfaceIndices.Add(subId);
				}
			}
			catch(Exception e)
			{
				setupBuilder.AppendLine(e.ToString());
			}
		}

		void DisonnectDisplay()
		{
			DisconnectDisplay(long.Parse(commandLine.Argument(1)));
		}
		
		void DisconnectDisplay(long id)
		{
			for (int i = 0; i < surfaceProviders.Count; i++)
			{
				if (surfaceProviders[i].EntityId == id)
				{
					((IMyTextSurfaceProvider)surfaceProviders[i]).GetSurface(surfaceIndices[i]).WriteText("");
					surfaceProviders.RemoveAt(i);
					surfaceIndices.RemoveAt(i);
					return;
				}
			}

			for (int i = 0; i < displays.Count; i++)
			{
				if (displays[i].EntityId == id)
				{
					displays[i].WriteText("");
					displays.RemoveAt(i);
					return;
				}
			}
		}
        #endregion

        MenuRemote GetDeltaRequestRemote()
		{
			MenuRemote remote = new MenuRemote("Requests >", "");
			IEnumerator<MyItemType> i = requestDict.Keys.GetEnumerator();
			while (i.MoveNext())
			{
				MenuRemote itemRemote = new MenuRemote($"{TypeToKey[i.Current]}>", "");
				remote.subMenues.Add(itemRemote);
				itemRemote.subMenues.Add(new MenuRemote("+ 1000", $"deltarequest {TypeToKey[i.Current]} 1000"));
				itemRemote.subMenues.Add(new MenuRemote("+ 100", $"deltarequest {TypeToKey[i.Current]} 100"));
				itemRemote.subMenues.Add(new MenuRemote("+ 10", $"deltarequest {TypeToKey[i.Current]} 10"));
				itemRemote.subMenues.Add(new MenuRemote("- 1000", $"deltarequest {TypeToKey[i.Current]} -s 1000"));
				itemRemote.subMenues.Add(new MenuRemote("- 100", $"deltarequest {TypeToKey[i.Current]} -s 100"));
				itemRemote.subMenues.Add(new MenuRemote("- 10", $"deltarequest {TypeToKey[i.Current]} -s 10"));
			}
			return remote;
		}

		void DeltaRequest()
		{
			DeltaRequest(commandLine.Argument(1), int.Parse(commandLine.Argument(2)) * (commandLine.Switch("s") ? -1 : 1));
		}

		void DeltaRequest(string key, int count)
		{
			requestDict[KeyToType[key]] += count;
		}

		void SendData()
		{
			long targetId;
			string data = commandLine.Argument(1);
			long.TryParse(commandLine.Argument(2), out targetId);
			SendData(targetId, data);
		}

		void SendData(long targetId, string data)
		{
			((IMyProgrammableBlock)GridTerminalSystem.GetBlockWithId(targetId)).CustomData = data;
		}


		// Update functions

		void Sorting()
		{
			Runtime.UpdateFrequency = UpdateFrequency.Update100;

			for (int i = 0; i < inventoryOwners.Count; i++)
			{
				var inv = inventoryOwners[i].GetInventory(inventoryOwners[i].InventoryCount - 1);
				var list = new List<MyInventoryItem>();
				inv.GetItems(list);

				for (int j = 0; j < list.Count; j++)
				{
					AutoputItem(list[j], inv);
				}
			}
		}

		void doDisplay()
		{
			if (opsMode == Modes.Sort)
			{
				for (int i = 0; i < myContainerSet.Count; i++)
				{
					echoBuilder.AppendLine("");
					var inv = myContainerSet[i].GetInventory(myContainerSet[i].InventoryCount - 1);
					echoBuilder.AppendLine($"== {myContainerSet[i].CustomName}: {inv.CurrentVolume}/{inv.MaxVolume}");

					MyFixedPoint[] volumes = { 0, 0, 0, 0, 0 };
					var list = itemsScratchPad;
					inv.GetItems(list);

					for (int j = 0; j < list.Count; j++)
					{
						var info = list[j].Type.GetItemInfo();
						var vol = list[j].Amount * info.Volume;
						// if (!info.IsComponent) echoBuilder.AppendLine(list[j].Type.ToString());
						if (info.IsOre) volumes[0] += vol;
						else if (info.IsIngot) volumes[1] += vol;
						else if (info.IsComponent) volumes[2] += vol;
						else if (info.IsTool) volumes[3] += vol;
						else if (info.IsAmmo) volumes[4] += vol;
					}

					itemsScratchPad.Clear();
					echoBuilder.Append("[");

					for (int j = 0; j < 20; j++)
					{
						if (volumes[0] > (j + 0.5f) * inv.MaxVolume * 0.05f) echoBuilder.Append("O");
						else if (volumes[0] + volumes[1] > (j + 0.5f) * inv.MaxVolume * 0.05f) echoBuilder.Append("I");
						else if (volumes[0] + volumes[1] + volumes[2] > (j + 0.5f) * inv.MaxVolume * 0.05f) echoBuilder.Append("C");
						else if (volumes[0] + volumes[1] + volumes[2] + volumes[3] > (j + 0.5f) * inv.MaxVolume * 0.05f) echoBuilder.Append("T");
						else if (volumes[0] + volumes[1] + volumes[2] + volumes[3] + volumes[4] > (j + 0.5f) * inv.MaxVolume * 0.05f) echoBuilder.Append("A");
						else echoBuilder.Append("-");
					}
					echoBuilder.AppendLine("]");
				}
			}

			if (opsMode == Modes.Request)
			{
				CountInventory();

				var enumerator = requestDict.GetEnumerator();
				while (enumerator.MoveNext())
				{
					var current = enumerator.Current;
					int want = current.Value;
					if (want > 0)
					{
						MyFixedPoint has = hasDict[current.Key];
						int barSize = 20;
						float multi = 1f / barSize;
						MyFixedPoint n = MyFixedPoint.Zero;
						echoBuilder.Append("[");
						for (int j = 0; j < barSize; j++)
						{
							n += MyFixedPointOne * multi * want;
							echoBuilder.Append(has >= n ? "|" : "'");
						}
						echoBuilder.Append("]");
						hasDict[current.Key] = MyFixedPoint.Zero;
						echoBuilder.AppendLine($"{TypeToKey[current.Key]} : {has.ToString()} / {want}");
					}
				}
			}

			for (int i = 0; i < displays.Count; i++)
				displays[i].WriteText(echoBuilder.ToString());
			for (int i = 0; i < surfaceProviders.Count; i++)
				((IMyTextSurfaceProvider)surfaceProviders[i]).GetSurface(surfaceIndices[i]).WriteText(echoBuilder.ToString());

			base.Echo(echoBuilder.ToString());
		}

		void CountInventory()
		{
			for (int i = 0; i < keys.Count; i++)
			{
				hasDict[keys[i]] = 0;
			}
			for (int i = 0; i < myRequesterSet.Count; i++)
			{
				var inv = myRequesterSet[i].GetInventory();
				inv.GetItems(itemsScratchPad);

				for (int j = 0; j < itemsScratchPad.Count; j++)
				{
					var type = itemsScratchPad[j].Type;
					if (hasDict.ContainsKey(type))
						hasDict[type] += itemsScratchPad[j].Amount;
				}
				itemsScratchPad.Clear();
			}
		}


		// Setup functions
		bool GetBlocks()
		{
			bool AOK = true;
			inventoryOwners.Clear();
			flaggedContainers[oreFlag].Clear();
			flaggedContainers[ingotFlag].Clear();
			flaggedContainers[compFlag].Clear();
			flaggedContainers[toolFlag].Clear();
			flaggedContainers[ammoFlag].Clear();
			flaggedContainers[wasteFlag].Clear();
			myContainerSet.Clear();
			myRequesterSet.Clear();

			GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, BlockCollect);

			// echoBuilder.AppendLine($">> {inventoryOwners.Count} blocks with inventory found");

			// echoBuilder.AppendLine($">> {flaggedContainers[oreFlag].Count} \\ " +
			//	$"{flaggedContainers[ingotFlag].Count} \\ " +
			//	$"{flaggedContainers[compFlag].Count} \\ " +
			//	$"{flaggedContainers[toolFlag].Count} \\ " +
			//	$"{flaggedContainers[ammoFlag].Count}");

			return AOK;
		}

		bool BlockCollect(IMyTerminalBlock block)
		{
			if (!(block is IMyGasGenerator) && !(block is IMyLargeGatlingTurret) && !(block is IMySmallGatlingGun) && (block).HasInventory)
			{

				if (block.CustomName.StartsWith(inventoryPrefix) && block.CustomName.Contains("]"))
				{
					var substring = block.CustomName.Substring(inventoryPrefix.Length, block.CustomName.IndexOf("]") - inventoryPrefix.Length);
					if (substring.Contains(requestFlag)) 
					{
						if (block.IsSameConstructAs(Me)) { myRequesterSet.Add(block); myContainerSet.Add(block); }
						return false;
					}
					
					if (substring.Contains(ignoreFlag)) return false;
					if (substring.Contains(oreFlag)) flaggedContainers[oreFlag].Add(block);
					if (substring.Contains(ingotFlag)) flaggedContainers[ingotFlag].Add(block);
					if (substring.Contains(compFlag)) flaggedContainers[compFlag].Add(block);
					if (substring.Contains(toolFlag)) flaggedContainers[toolFlag].Add(block);
					if (substring.Contains(ammoFlag)) flaggedContainers[ammoFlag].Add(block);
					if (substring.Contains(wasteFlag)) flaggedContainers[wasteFlag].Add(block);
					if (block.IsSameConstructAs(Me)) myContainerSet.Add(block);
				}
				inventoryOwners.Add(block);
			}

			return false;
		}

		// Helper functions
		bool AutoputItem(MyInventoryItem item, IMyInventory source)
		{
			var targetFlag = oreFlag;
			var itemInfo = item.Type.GetItemInfo();
			var runningAmt = item.Amount;

			if (itemInfo.IsIngot) targetFlag = ingotFlag;
			else if (itemInfo.IsComponent) targetFlag = compFlag;
			else if (itemInfo.IsTool) targetFlag = toolFlag;
			else if (itemInfo.IsAmmo) targetFlag = ammoFlag;

			// 
			if (item.Type.TypeId == "MyObjectBuilder_Ingot" && item.Type.SubtypeId == "Stone") targetFlag = wasteFlag;

			if (flaggedContainers[targetFlag].Contains(source.Owner)) return true;

			var enumerator = flaggedContainers[targetFlag].GetEnumerator();
			while (enumerator.MoveNext())
			{
				var current = enumerator.Current;
				IMyInventory inv = current.GetInventory();

				TransferAsMuchAsPossible(source, inv, item, item.Amount);
			}
			return false;
		}



		void AddKey(string key, MyItemType type)
		{
			KeyToType.Add(key, type);
			TypeToKey.Add(type, key);
		}

		bool SameConstructAsMe(IMyTerminalBlock block)
		{
			return block.IsSameConstructAs(Me);
		}
	}
}
