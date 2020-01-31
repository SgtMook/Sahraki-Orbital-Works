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

			// Mining
			MiningDown,
			MiningUp,
			MiningAdjust,
		}

		// Setup and utility
		bool isSetup = false;
		StringBuilder setupBuilder = new StringBuilder();
		StringBuilder echoBuilder = new StringBuilder();
		ScriptState currentState = ScriptState.Standby;
		MyCommandLine commandLine = new MyCommandLine();
		Dictionary<string, Action> commands = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);
		Dictionary<ScriptState, Action> updates = new Dictionary<ScriptState, Action>();
		IMyShipController shipController;

		public Program()
		{
			Runtime.UpdateFrequency = UpdateFrequency.Once;
			ChangeState(ScriptState.Standby);

			commands["setstate"] = SetState;

			// updates[ScriptState.MiningDown] = MiningDown;
			// updates[ScriptState.MiningUp] = MiningUp;
			// updates[ScriptState.MiningAdjust] = MiningAdjust;
		}

		public void Save()
		{
		}

		public void Main(string argument, UpdateType updateSource)
		{
			echoBuilder.Clear();

			if (!isSetup)
				SetUp();

			echoBuilder.Append(setupBuilder.ToString());

			echoBuilder.AppendLine($"Current state >> {currentState.ToString()}");

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
					ChangeState(ScriptState.Standby);
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

			base.Echo(echoBuilder.ToString());
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

			AOK &= GetBlocks();

			isSetup = AOK;

			if (!AOK)
			{
				Runtime.UpdateFrequency = UpdateFrequency.None;
			}
		}

		// Command functions
		void SetState()
		{
			string argReps = commandLine.Argument(1);
			ScriptState newState = ScriptState.Standby;
			if (argReps != null && Enum.TryParse<ScriptState>(argReps, true, out newState))
				ChangeState(newState);
			Runtime.UpdateFrequency = UpdateFrequency.Update10;
		}

		// Update functions


		// Setup functions
		bool GetBlocks()
		{
			bool AOK = true;
			shipController = null;

			GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, BlockCollect);

			if (shipController == null)
			{
				setupBuilder.AppendLine(">> Error: No controller");
				AOK = false;
			}

			return AOK;
		}

		bool BlockCollect(IMyTerminalBlock block)
		{
			if (block is IMyShipController && ((IMyShipController)block).IsUnderControl && ((IMyShipController)block).CanControlShip)
				shipController = (IMyShipController)block;

			return false;
		}

		public class TerminalPropertiesHelper
		{
			static Dictionary<string, ITerminalAction> _terminalActionDict = new Dictionary<string, ITerminalAction>();
			static Dictionary<string, ITerminalProperty> _terminalPropertyDict = new Dictionary<string, ITerminalProperty>();

			public static void ApplyAction(IMyTerminalBlock block, string actionName)
			{
				ITerminalAction act;
				if (_terminalActionDict.TryGetValue(actionName, out act))
				{
					act.Apply(block);
					return;
				}

				act = block.GetActionWithName(actionName);
				_terminalActionDict[actionName] = act;
				act.Apply(block);
			}

			public static void SetValue<T>(IMyTerminalBlock block, string propertyName, T value)
			{
				ITerminalProperty prop;
				if (_terminalPropertyDict.TryGetValue(propertyName, out prop))
				{
					prop.Cast<T>().SetValue(block, value);
					return;
				}

				prop = block.GetProperty(propertyName);
				_terminalPropertyDict[propertyName] = prop;
				prop.Cast<T>().SetValue(block, value);
			}

			public static T GetValue<T>(IMyTerminalBlock block, string propertyName)
			{
				ITerminalProperty prop;
				if (_terminalPropertyDict.TryGetValue(propertyName, out prop))
				{
					return prop.Cast<T>().GetValue(block);
				}

				prop = block.GetProperty(propertyName);
				_terminalPropertyDict[propertyName] = prop;
				return prop.Cast<T>().GetValue(block);
			}
		}

	}
}
