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

			// Tracking
			Tracking
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

		// Tracking
		Dictionary<int, IMyMotorStator> rotors = new Dictionary<int, IMyMotorStator>();
		float[] lastOutputs = { 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
		float[] lastLastOutputs = { 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
		float[] lastAngles = { 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
		float[] lastLastAngles = { 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
		float[] targetAngles = { 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
		Dictionary<int, List<IMySolarPanel>> panelGroups = new Dictionary<int, List<IMySolarPanel>>();

		// Name format [S]-1NAME where 1 is group number
		const string solarPrefix = "[S]";
		const int groupIndex = 4; // Max of 10 groups supported
		const float moveStep = 10f * (float)Math.PI / 180f;
		const float spazThreshold = 0.2f;
		const float scanThreshold = 0.9f;

		public Program()
		{
			Runtime.UpdateFrequency = UpdateFrequency.Once;
			ChangeState(ScriptState.Tracking);

			commands["setstate"] = SetState;

			updates[ScriptState.Tracking] = Tracking;
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
			Runtime.UpdateFrequency = UpdateFrequency.Once;
		}

		// Update functions
		void Tracking()
		{
			Runtime.UpdateFrequency = UpdateFrequency.Update100;
			float[] newLastOutputs = { 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
			float[] newLastAngles = { 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f };
			for (int i = 0; i < 10; i++)
			{
				if (panelGroups.ContainsKey(i) && rotors.ContainsKey(i))
				{
					float output = GetGroupOutputHelper(i);
					float maxOutput = GetGroupOutputHelper(i, true);
					float targetAngle = 0;

					newLastOutputs[i] = output * 1.01f;
					newLastAngles[i] = rotors[i].Angle;

					// Get desired angle
					float deltaLast = AngleSubtract(newLastAngles[i], lastAngles[i]);
					float deltaLastLast = AngleSubtract(newLastAngles[i], lastLastAngles[i]);

					float leftAngle, leftOutput, midAngle, midOutput, rightAngle, rightOutput;

					if (0 < deltaLast && deltaLast < deltaLastLast)
					{
						leftAngle = newLastAngles[i];
						leftOutput = output*1.01f;
						midAngle = lastAngles[i];
						midOutput = lastOutputs[i];
						rightAngle = lastLastAngles[i];
						rightOutput = lastLastOutputs[i];
					}
					else if (0 < deltaLastLast && deltaLastLast < deltaLast)
					{
						leftAngle = newLastAngles[i];
						leftOutput = output;
						midAngle = lastLastAngles[i];
						midOutput = lastLastOutputs[i];
						rightAngle = lastAngles[i];
						rightOutput = lastOutputs[i];
					}
					else if (deltaLast < 0 && 0 < deltaLastLast)
					{
						leftAngle = lastAngles[i];
						leftOutput = lastOutputs[i];
						midAngle = newLastAngles[i] ;
						midOutput = output;
						rightAngle = lastLastAngles[i];
						rightOutput = lastLastOutputs[i];
					}
					else if (deltaLastLast < 0 && 0 < deltaLast)
					{
						leftAngle = lastLastAngles[i];
						leftOutput = lastLastOutputs[i];
						midAngle = newLastAngles[i];
						midOutput = output;
						rightAngle = lastAngles[i];
						rightOutput = lastOutputs[i];
					}
					else if (deltaLast < deltaLastLast && deltaLastLast < 0)
					{
						leftAngle = lastAngles[i];
						leftOutput = lastOutputs[i];
						midAngle = lastLastAngles[i];
						midOutput = lastLastOutputs[i];
						rightAngle = newLastAngles[i];
						rightOutput = output;
					}
					else
					{
						leftAngle = lastLastAngles[i];
						leftOutput = lastLastOutputs[i];
						midAngle = lastAngles[i];
						midOutput = lastOutputs[i];
						rightAngle = newLastAngles[i];
						rightOutput = output;
					}

					if (leftOutput > midOutput && midOutput > rightOutput)
						targetAngle = leftAngle - moveStep;
					else if (leftOutput > rightOutput && rightOutput > midOutput)
						targetAngle = midAngle + AngleSubtract(midAngle, leftAngle) / 2f;
					else if (rightOutput > midOutput && midOutput > leftOutput)
						targetAngle = rightAngle + moveStep;
					else if (rightOutput > leftOutput && leftOutput > midOutput)
						targetAngle = midAngle + AngleSubtract(midAngle, rightAngle) / 2f;
					else if (midOutput > rightOutput && midOutput > leftOutput)
						targetAngle = midAngle;

					if (output < maxOutput * scanThreshold && Math.Abs(AngleSubtract(lastAngles[i], lastLastAngles[i])) < (float)Math.PI / 18000f)
						targetAngle += moveStep;
					
					// Move towards desired angle

					rotors[i].TargetVelocityRad = AngleSubtract(newLastAngles[i], targetAngle) * 0.4f / (float)Math.PI * 2;
					echoBuilder.AppendLine($"Group {i} output {(output * 1000).ToString("0")}/{(maxOutput * 1000).ToString("0")}");
					echoBuilder.Append("[");

					for (int j = 0; j < 20; j++)
						if (output >= maxOutput * (j + 1) / 20f)
							echoBuilder.Append("=");
						else
							echoBuilder.Append("-");

					echoBuilder.AppendLine("]");
					echoBuilder.AppendLine($"Target angle = {targetAngle * 180 / (float)Math.PI}");
					echoBuilder.AppendLine($"Current angle = {rotors[i].Angle * 180 / (float)Math.PI}");
					echoBuilder.AppendLine($"{leftAngle * 180 / (float)Math.PI} | {midAngle * 180 / (float)Math.PI} | {rightAngle * 180 / (float)Math.PI}");
					echoBuilder.AppendLine($"{leftOutput} | {midOutput} | {rightOutput}");
					echoBuilder.AppendLine($"Delta A = {AngleSubtract(newLastAngles[i], targetAngle) * 180 / (float)Math.PI}");
					echoBuilder.AppendLine($"Last angle = {lastAngles[i] * 180 / (float)Math.PI}");
					echoBuilder.AppendLine($"Lastlast angle = {lastLastAngles[i] * 180 / (float)Math.PI}");
					echoBuilder.AppendLine($"Speed = {rotors[i].TargetVelocityRad * 180 / (float)Math.PI}/s");
				}
			}

			lastLastOutputs = lastOutputs;
			lastOutputs = newLastOutputs;
			lastLastAngles = lastAngles;
			lastAngles = newLastAngles;
		}

		// Setup functions
		bool GetBlocks()
		{
			bool AOK = true;
			shipController = null;
			rotors.Clear();
			panelGroups.Clear();

			GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, BlockCollect);

			if (shipController == null)
			{
				setupBuilder.AppendLine(">> Error: No controller");
				AOK = false;
			}

			for (int i = 0; i < 10; i++)
			{
				if (rotors.ContainsKey(i))
					setupBuilder.AppendLine($">>>> Group {i} Rotor Found");
				if (panelGroups.ContainsKey(i))
					setupBuilder.AppendLine($">>>> Group {i} Panels Found {panelGroups[i].Count}");
			}
			return AOK;
		}

		bool BlockCollect(IMyTerminalBlock block)
		{
			if (block is IMyShipController && ((IMyShipController)block).IsUnderControl && ((IMyShipController)block).CanControlShip)
				shipController = (IMyShipController)block;

			if (block is IMySolarPanel && block.CustomName != null && block.CustomName.StartsWith(solarPrefix))
			{
				int group = 0;
				int.TryParse(block.CustomName[groupIndex].ToString(), out group);
				if (!panelGroups.ContainsKey(group))
					panelGroups[group] = new List<IMySolarPanel>();
				panelGroups[group].Add(((IMySolarPanel)block));
			}

			if (block is IMyMotorStator && block.CustomName != null && block.CustomName.StartsWith(solarPrefix))
			{
				int group = 0;
				int.TryParse(block.CustomName[groupIndex].ToString(), out group);
				rotors[group] = (IMyMotorStator)block;
			}

			return false;
		}

		// Helper functions
		float GetGroupOutputHelper(int group, bool max = false)
		{
			if (!panelGroups.ContainsKey(group))
				return 0f;
			float total = 0;
			for (int i = 0; i < panelGroups[group].Count; i++)
				total += max ? 0.16f : panelGroups[group][i].CurrentOutput;

			return total;
		}

		float AngleSubtract(float s, float t)
		{
			float deltaA = t - s;
			deltaA = CustomMod(deltaA + (float)Math.PI, (float)Math.PI * 2) - (float)Math.PI;
			return deltaA;
		}

		float CustomMod(float n, float d)
		{
			return (n % d + d) % d;
		}
	}
}
