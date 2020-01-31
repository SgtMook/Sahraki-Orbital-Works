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
			MiningExtend,
			MiningRetract,
			MiningPause,
			MiningDown,
		}


		// Setup and utility
		bool isSetup = false;
		StringBuilder setupBuilder = new StringBuilder();
		StringBuilder echoBuilder = new StringBuilder();
		ScriptState currentState = ScriptState.Standby;
		ScriptState nextState = ScriptState.Standby;
		ScriptState pauseStateCache = ScriptState.Standby;
		MyCommandLine commandLine = new MyCommandLine();
		Dictionary<string, Action> commands = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);
		Dictionary<ScriptState, Action> updates = new Dictionary<ScriptState, Action>();

		// Mining Mode
		// Mine [startstep = 0]
		const string miningPrefix = "[M]";
		const string miningReversePrefix = "[MR]";
		const float miningSpeed = 0.04f;
		const float extendSpeed = 1f;
		const float miningRPM = 0.5f;
		const float extendClearance = 6*2.5f + 0.5f;

		List<IMyPistonBase> forwardPistons = new List<IMyPistonBase>();
		List<IMyPistonBase> backwardPistons = new List<IMyPistonBase>();
		int pistonCount = 0;
		List<IMyShipDrill> drills = new List<IMyShipDrill>();
		IMyMotorAdvancedStator drillRotor = null;
		float pistonMaxExtend = 0;


		public Program()
		{
			Runtime.UpdateFrequency = UpdateFrequency.Once;
			ChangeState(ScriptState.Standby);

			commands["setstate"] = SetState;
			commands["mine"] = Mine;

			updates[ScriptState.MiningExtend] = MiningExtend;
			updates[ScriptState.MiningRetract] = MiningRetract;
			updates[ScriptState.MiningDown] = MiningDown;
			updates[ScriptState.MiningPause] = MiningPause;

			Runtime.UpdateFrequency = UpdateFrequency.Update10;
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
			echoBuilder.AppendLine($"Next state >> {nextState.ToString()}");

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

			echoBuilder.AppendLine($"== Current Position {GetPistonTotalPosition().ToString("0.##")} m");
			echoBuilder.AppendLine($"== Current Angle {(drillRotor.Angle * 180 / (float)Math.PI).ToString("0.##")} d");

			base.Echo(echoBuilder.ToString());
		}

		void ChangeState(ScriptState newState)
		{
			if ((newState == ScriptState.MiningDown)
				&& GetPistonTotalPosition() < extendClearance)
				return;

			if (newState == ScriptState.Standby)
				SetAllDrills(false);

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

			nextState = ScriptState.Standby;
		}

		void Mine()
		{
			ChangeState(ScriptState.MiningExtend);
			nextState = ScriptState.MiningDown;
		}

		// Update functions
		void MiningExtend()
		{
			SetPistonMaxExtend(extendClearance + 0.1f);
			SetPistonTotalSpeed(extendSpeed);
			SetAllDrills(false);

			if (GetPistonTotalPosition() >= extendClearance)
				ChangeState(nextState);
		}

		void MiningRetract()
		{
			SetPistonMaxExtend(extendClearance);
			SetAllDrills(false);

			drillRotor.LowerLimitRad = 0;
			drillRotor.TargetVelocityRPM = -miningRPM*4;

			SetPistonTotalSpeed(Math.Abs(AngleSubtract(drillRotor.Angle, 0)) < 0.001 ? - extendSpeed : 0);

			if (GetPistonTotalPosition() <= 0)
			{
				drillRotor.LowerLimitRad = float.MinValue;
				drillRotor.TargetVelocityRPM = 0;
				ChangeState(ScriptState.Standby);
			}
		}

		void MiningDown()
		{
			SetPistonMaxExtend(pistonCount * 10f);
			SetPistonTotalSpeed(miningSpeed);
			SetAllDrills(true);
			drillRotor.TargetVelocityRPM = miningRPM;

			if (GetIsAnyDrillFull())
			{
				ChangeState(ScriptState.MiningPause);
				nextState = ScriptState.MiningDown;
				pauseStateCache = nextState;
			}

			if (GetPistonTotalPosition() >= pistonCount * 10f)
				ChangeState(ScriptState.MiningRetract);
		}

		void MiningPause()
		{
			drillRotor.TargetVelocityRPM = 0;
			SetPistonTotalSpeed(0);
			if (GetIsAllDrillEmpty())
			{
				ChangeState(nextState);
				nextState = pauseStateCache;
			}
		}

		// Setup functions
		bool GetBlocks()
		{
			bool AOK = true;

			drillRotor = null;
			forwardPistons.Clear();
			backwardPistons.Clear();
			drills.Clear();

			GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, BlockCollect);

			pistonCount = forwardPistons.Count + backwardPistons.Count;

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

			if (drillRotor == null)
			{
				setupBuilder.AppendLine(">> Error: No controller");
				AOK = false;
			}

			return AOK;
		}

		bool BlockCollect(IMyTerminalBlock block)
		{
			if (block is IMyPistonBase && block.CustomName != null && block.CustomName.StartsWith(miningPrefix))
				forwardPistons.Add((IMyPistonBase)block);

			if (block is IMyPistonBase && block.CustomName != null && block.CustomName.StartsWith(miningReversePrefix))
				backwardPistons.Add((IMyPistonBase)block);

			if (block is IMyShipDrill && block.CustomName != null && block.CustomName.StartsWith(miningPrefix))
				drills.Add((IMyShipDrill)block);

			if (block is IMyMotorAdvancedStator && block.CustomName != null && block.CustomName.StartsWith(miningPrefix))
				drillRotor= (IMyMotorAdvancedStator)block;

			return false;
		}

		// Helper functions
		float AngleSubtract(float s, float t)
		{
			float deltaA = t - s;
			deltaA = CustomMod((deltaA + (float)Math.PI), ((float)Math.PI * 2)) - (float)Math.PI;
			return deltaA;
		}

		float CustomMod(float n, float d)
		{
			return (n % d + d) % d;
		}

		void SetPistonTotalSpeed(float n)
		{
			for (int i = 0; i < forwardPistons.Count; i++)
			{
				forwardPistons[i].Velocity = n / pistonCount;
			}
			for (int i = 0; i < backwardPistons.Count; i++)
			{
				backwardPistons[i].Velocity = -n / pistonCount;
			}
		}

		float GetPistonTotalPosition()
		{
			float position = 0f;
			for (int i = 0; i < forwardPistons.Count; i++)
			{
				position += forwardPistons[i].CurrentPosition;
			}
			for (int i = 0; i < backwardPistons.Count; i++)
			{
				position += backwardPistons[i].MaxLimit - backwardPistons[i].CurrentPosition;
			}
			return position;
		}

		void SetPistonMaxExtend(float n)
		{
			for (int i = 0; i < forwardPistons.Count; i++)
			{
				forwardPistons[i].MaxLimit = n / pistonCount;
			}
			for (int i = 0; i < backwardPistons.Count; i++)
			{
				backwardPistons[i].MinLimit = backwardPistons[i].HighestPosition - n / pistonCount;
			}

			pistonMaxExtend = n;
		}

		void SetAllDrills(bool on = true)
		{
			for (int i = 0; i < drills.Count; i++)
				drills[i].Enabled = on;
		}

		bool GetIsAnyDrillFull()
		{
			for (int i = 0; i < drills.Count; i++)
				if (drills[i].GetInventory().CurrentVolume > drills[i].GetInventory().MaxVolume * 0.9f)
					return true;

			return false;
		}

		bool GetIsAllDrillEmpty()
		{
			for (int i = 0; i < drills.Count; i++)
				if (drills[i].GetInventory().CurrentVolume > 0)
					return false;

			return true;
		}
	}
}
