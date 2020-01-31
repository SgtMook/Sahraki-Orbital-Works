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

		// Mining Mode
		// Command format:
		// mine [reps = 5]
		Vector3D adjustStartPosition = Vector3D.Zero;
		int repsRemaining = 0;
		
		const int repsDefault = 5;
		const string miningPrefix = "[M]";
		const string miningReversePrefix = "[MR]";
		const float miningSpeed = 0.4f;

		List<IMyPistonBase> forwardPistons = new List<IMyPistonBase>();
		List<IMyPistonBase> backwardPistons = new List<IMyPistonBase>();
		List<IMyShipDrill> drills = new List<IMyShipDrill>();

		List<IMyMotorSuspension> wheels = new List<IMyMotorSuspension>();

		const float adjustWheelSpeed = 0.4f;
		const float adjustDist = 2.4f;

		public Program()
		{
			Runtime.UpdateFrequency = UpdateFrequency.Once;
			ChangeState(ScriptState.Standby);

			commands["mine"] = Mine;
			commands["setstate"] = SetState;

			updates[ScriptState.MiningDown] = MiningDown;
			updates[ScriptState.MiningUp] = MiningUp;
			updates[ScriptState.MiningAdjust] = MiningAdjust;
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

		void Mine()
		{
			string argReps = commandLine.Argument(1);
			if (argReps == null || !int.TryParse(argReps, out repsRemaining))
				repsRemaining = repsDefault;

			ChangeState(ScriptState.MiningDown);
			Runtime.UpdateFrequency = UpdateFrequency.Update10;
		}

		// Update functions

		void MiningDown()
		{
			int pistonCount = forwardPistons.Count + backwardPistons.Count;
			bool finished = true;
			float position = 0;

			for (int i = 0; i < forwardPistons.Count; i++)
			{
				forwardPistons[i].Velocity = miningSpeed / pistonCount;
				finished &= (forwardPistons[i].CurrentPosition == forwardPistons[i].MaxLimit);
				position += forwardPistons[i].CurrentPosition;
			}
			for (int i = 0; i < backwardPistons.Count; i++)
			{
				backwardPistons[i].Velocity = -miningSpeed / pistonCount;
				finished &= (backwardPistons[i].CurrentPosition == backwardPistons[i].MinLimit);
				position += backwardPistons[i].MaxLimit - backwardPistons[i].CurrentPosition;
			}
			for (int i = 0; i < drills.Count; i++)
				drills[i].Enabled = true;

			for (int i = 0; i < wheels.Count; i++)
				wheels[i].Brake = true;

			echoBuilder.AppendLine($"MINING: Depth = {position} m");
			echoBuilder.AppendLine($"MINING: Reps = {repsRemaining}");

			if (finished)
				ChangeState(ScriptState.MiningUp);
		}

		void MiningUp()
		{
			bool finished = true;
			float position = 0;

			for (int i = 0; i < forwardPistons.Count; i++)
			{
				forwardPistons[i].Velocity = -3*miningSpeed;
				finished &= (forwardPistons[i].CurrentPosition == forwardPistons[i].MinLimit);
				position += forwardPistons[i].CurrentPosition;
			}
			for (int i = 0; i < backwardPistons.Count; i++)
			{
				backwardPistons[i].Velocity = 3*miningSpeed;
				finished &= (backwardPistons[i].CurrentPosition == backwardPistons[i].MaxLimit);
				position += backwardPistons[i].MaxLimit - backwardPistons[i].CurrentPosition;
			}
			for (int i = 0; i < drills.Count; i++)
				drills[i].Enabled = false;

			for (int i = 0; i < wheels.Count; i++)
				wheels[i].Brake = true;

			echoBuilder.AppendLine($"RETRACTING: Depth = {position} m");
			echoBuilder.AppendLine($"MINING: Reps = {repsRemaining}");

			if (finished)
			{
				repsRemaining -= 1;
				if (repsRemaining == 0)
					ChangeState(ScriptState.Standby);
				else
					ChangeState(ScriptState.MiningAdjust);
			}
		}

		void MiningAdjust()
		{
			if (adjustStartPosition == Vector3D.Zero)
				adjustStartPosition = Me.GetPosition();

			for (int i = 0; i < wheels.Count; i++)
			{
				var propulsionMult = -Math.Sign(Math.Round(Vector3D.Dot(wheels[i].WorldMatrix.Up, shipController.WorldMatrix.Right), 2));
				TerminalPropertiesHelper.SetValue(wheels[i], "Propulsion override", adjustWheelSpeed*propulsionMult);
				wheels[i].Brake = false;
			}
			double dist = Vector3D.Distance(Me.GetPosition(), adjustStartPosition);

			echoBuilder.AppendLine($"Adjusting: Distance = {dist}/{adjustDist} m");
			echoBuilder.AppendLine($"MINING: Reps = {repsRemaining}");

			if (adjustDist < dist)
			{
				for (int i = 0; i < wheels.Count; i++)
				{
					TerminalPropertiesHelper.SetValue(wheels[i], "Propulsion override", 0f);
					wheels[i].Brake = true;
				}
				if (repsRemaining == 0)
					ChangeState(ScriptState.Standby);
				else
					ChangeState(ScriptState.MiningDown);
				adjustStartPosition = Vector3D.Zero;
			}
		}

		bool GetBlocks()
		{
			bool AOK = true;
			wheels.Clear();
			drills.Clear();
			forwardPistons.Clear();
			backwardPistons.Clear();
			shipController = null;

			GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, BlockCollect);

			if (wheels.Count == 0)
			{
				setupBuilder.AppendLine(">> Error: No wheels");
				AOK = false;
			}
			else
			{
				setupBuilder.AppendFormat("{0} Wheels found.\n", wheels.Count);
			}

			if (forwardPistons.Count + backwardPistons.Count == 0)
			{
				setupBuilder.AppendLine(">> Error: No pistons");
				AOK = false;
			}
			else
			{
				setupBuilder.AppendFormat("{0} Forward pistons found.\n", forwardPistons.Count);
				setupBuilder.AppendFormat("{0} Backward pistons found.\n", backwardPistons.Count);
			}

			if (drills.Count == 0)
			{
				setupBuilder.AppendLine(">> Error: No drills");
				AOK = false;
			}
			else
			{
				setupBuilder.AppendFormat("{0} Drills found.\n", drills.Count);
			}

			if (shipController == null)
			{
				setupBuilder.AppendLine(">> Error: No controller");
				AOK = false;
			}

			return AOK;
		}

		bool BlockCollect(IMyTerminalBlock block)
		{
			if (block is IMyMotorSuspension)
				wheels.Add((IMyMotorSuspension)block);

			if (block is IMyPistonBase && block.CustomName != null && block.CustomName.StartsWith(miningPrefix))
				forwardPistons.Add((IMyPistonBase)block);

			if (block is IMyPistonBase && block.CustomName != null && block.CustomName.StartsWith(miningReversePrefix))
				backwardPistons.Add((IMyPistonBase)block);

			if (block is IMyShipDrill && block.CustomName != null && block.CustomName.StartsWith(miningPrefix))
				drills.Add((IMyShipDrill)block);

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
