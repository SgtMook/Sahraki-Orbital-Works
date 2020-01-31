using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System.IO;
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
        // Setup
        long controllerId = -1;
        StringBuilder setupBuilder = new StringBuilder();
        StringBuilder echoBuilder = new StringBuilder();
        MyCommandLine commandLine = new MyCommandLine();
        Dictionary<IMyTextSurface, StringBuilder> outputBuilders = new Dictionary<IMyTextSurface, StringBuilder>();
        bool isSetup = false;

        // States
        enum ScriptState
        {
            // Basic
            Standby,
        }
        ScriptState currentState = ScriptState.Standby;

        // Commands
        Dictionary<string, Action> commands = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);
        Dictionary<ScriptState, Action> updates = new Dictionary<ScriptState, Action>();

        // Controller
        IMyCockpit cockpit;
        List<IMyCockpit> potentialControllers = new List<IMyCockpit>();
        bool interceptInput = false;

        // Inputs
        bool lastADown = false;
        bool lastSDown = false;
        bool lastDDown = false;
        bool lastWDown = false;
        bool lastCDown = false;
        bool lastSpaceDown = false;
        List<Action> AListeners = new List<Action>();
        List<Action> SListeners = new List<Action>();
        List<Action> DListeners = new List<Action>();
        List<Action> WListeners = new List<Action>();
        List<Action> CListeners = new List<Action>();
        List<Action> SpaceListeners = new List<Action>();
        //StringBuilder inputTester = new StringBuilder();

        // Data comms
        List<IMyProgrammableBlock> RemoteTerminals = new List<IMyProgrammableBlock>();

        // Menues
        MenuItem mainMenu;
        MenuItem currentMenu;
        MenuItem remoteMenu;

        class MenuItem
        {
            public string name;
            public List<MenuItem> subMenues = new List<MenuItem>();
            public Action action;
            public MenuItem parent;
            public int currentIndex = 0;

            public MenuItem(string name, Action action)
            {
                this.name = name;
                this.action = action;
            }

            public void AddSubItem(MenuItem sub)
            {
                subMenues.Add(sub);
                sub.parent = this;
            }
        }


        public Program()
        {
            commands["setcontroller"] = SetController;
            commands["toggle"] = ToggleInput;
            commands["toggleinput"] = ToggleInput;
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
            Load();

            AListeners.Add(MenuExit);
            SListeners.Add(MenuDown);
            DListeners.Add(MenuEnter);
            WListeners.Add(MenuUp);
            CListeners.Add(MenuExit);
            SpaceListeners.Add(MenuEnter);

            // Setup menu structure
            mainMenu = new MenuItem("Root", NoOp);

            var fontSize = new MenuItem("Font Size", NoOp);

            mainMenu.AddSubItem(fontSize);
            fontSize.AddSubItem(new MenuItem("++", () => ChangeFontSize(0, 0.05f)));
            fontSize.AddSubItem(new MenuItem("--", () => ChangeFontSize(0, -0.05f)));

            remoteMenu = new MenuItem("Remotes", GetRemotes);
            mainMenu.AddSubItem(remoteMenu);

            currentMenu = mainMenu;
        }

        #region Save and Load
        int _currentLine = 0;
        string[] loadArray;
        public void Save()
        {
            StringBuilder storageBuilder = new StringBuilder();

            storageBuilder.AppendLine(controllerId.ToString());

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

            // First line is the target controller ID
            long.TryParse(NextStorageLine(), out controllerId);
        }

        private string NextStorageLine()
        {
            _currentLine += 1;
            if (loadArray.Length >= _currentLine)
                return loadArray[_currentLine - 1];
            return String.Empty;
        }
        #endregion

        private void Setup()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update100;
            setupBuilder.Clear();
            outputBuilders.Clear();
            // setupBuilder.Append(controllerId.ToString());
            // setupBuilder.AppendLine(Storage);
            cockpit = (IMyCockpit)GridTerminalSystem.GetBlockWithId(controllerId);
            if (cockpit == null)
            {
                PromptSetupController();
            }
            else
            {
                setupBuilder.AppendLine($"Controller is {cockpit.CustomName}");

                for (int i = 0; i < cockpit.SurfaceCount; i++)
                {
                    var surface = cockpit.GetSurface(i);
                    outputBuilders[surface] = new StringBuilder();
                    surface.FontSize = 0.8f;
                }

                Runtime.UpdateFrequency = UpdateFrequency.Update100;
                DebugFlush();
                DebugLog("Debugging here");
                cockpit.CustomData = Me.EntityId.ToString();
            }
            isSetup = true;
        }

        // Controller
        private void PromptSetupController()
        {
            GridTerminalSystem.GetBlocksOfType<IMyCockpit>(potentialControllers, SameConstructAsMe);

            echoBuilder.AppendLine($"Set new controller with 'setcontroller #`");
            echoBuilder.AppendLine($"{potentialControllers.Count} controllers found:");

            for (int i = 0; i < potentialControllers.Count; i++)
                echoBuilder.AppendLine($"{i}: {potentialControllers[i].CustomName}");
            Runtime.UpdateFrequency = UpdateFrequency.None;
        }

        public void Main(string argument, UpdateType updateSource)
        {
            echoBuilder.Clear();

            if (!isSetup) Setup();

            echoBuilder.Append(setupBuilder.ToString());

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

            if (interceptInput) TriggerInputs();

            WriteMainDisplayOutput();

            base.Echo(echoBuilder.ToString());
            FlushOutput();
        }

        // Command functions
        void SetController()
        {
            if (cockpit != null) return;
            int newControllerIndex;
            if (commandLine.ArgumentCount >= 2 && int.TryParse(commandLine.Argument(1), out newControllerIndex))
            {
                if (potentialControllers.Count > newControllerIndex)
                {
                    isSetup = false;
                    controllerId = potentialControllers[newControllerIndex].EntityId;
                    Runtime.UpdateFrequency = UpdateFrequency.Once;
                }
            }
            else
            {
                echoBuilder.AppendLine("Invalid arguments for setcontroller");
            }
        }

        void ToggleInput()
        {
            if (cockpit == null) return;
            interceptInput = !interceptInput;
            cockpit.ControlThrusters = !interceptInput;
            cockpit.ControlWheels = !interceptInput;
            Runtime.UpdateFrequency = interceptInput ? UpdateFrequency.Update1 : UpdateFrequency.Update100;
            if (!interceptInput) currentMenu = mainMenu;
        }

        string RequestData(long targetId, string command)
        {
            if (((IMyProgrammableBlock)GridTerminalSystem.GetBlockWithId(targetId)).TryRun(command))
                return GetCustomData();
            return String.Empty;
        }
        // Helpers
        private void TriggerInputs()
        {
            if (cockpit == null) return;
            var inputVecs = cockpit.MoveIndicator;
            if (!lastADown && inputVecs.X < 0) TriggerListOfActions(AListeners);
            lastADown = inputVecs.X < 0;
            if (!lastSDown && inputVecs.Z > 0) TriggerListOfActions(SListeners);
            lastSDown = inputVecs.Z > 0;
            if (!lastDDown && inputVecs.X > 0) TriggerListOfActions(DListeners);
            lastDDown = inputVecs.X > 0;
            if (!lastWDown && inputVecs.Z < 0) TriggerListOfActions(WListeners);
            lastWDown = inputVecs.Z < 0;
            if (!lastCDown && inputVecs.Y < 0) TriggerListOfActions(CListeners);
            lastCDown = inputVecs.Y < 0;
            if (!lastSpaceDown && inputVecs.Y > 0) TriggerListOfActions(SpaceListeners);
            lastSpaceDown = inputVecs.Y > 0;
        }

        private void TriggerListOfActions(List<Action> actions)
        {
            for (int i = 0; i < actions.Count; i++)
                actions[i]();
        }

        private bool SameConstructAsMe(IMyTerminalBlock block)
        {
            return block.IsSameConstructAs(Me);
        }

        private void FlushOutput()
        {
            if (cockpit != null)
            {
                for (int i = 0; i < cockpit.SurfaceCount; i++)
                {
                    var surface = cockpit.GetSurface(i);
                    if (outputBuilders[surface].Length > 0)
                    {
                        surface.WriteText(outputBuilders[surface]);
                        outputBuilders[surface].Clear();
                    }
                }
            }
        }

        private void WriteToCockpit(int screen, string text, bool newLine = true, bool append = true)
        {
            if (cockpit == null) return;
            StringBuilder builder = outputBuilders[cockpit.GetSurface(screen)];
            if (!append) builder.Clear();
            if (newLine) builder.AppendLine(text);
            else builder.Append(text);
        }

        private void WriteMainDisplayOutput()
        {
            WriteToCockpit(0, $"This cockpit is under the control of {Me.CustomName}");
            if (interceptInput)
            {
                WriteToCockpit(0, "Control ACTIVE");
                WriteToCockpit(0, "Use 'toggle' to disable controls");
                WriteToCockpit(0, "");
                BuildMenu(0);
            }
            else
            {
                WriteToCockpit(0, "Control DISABLED");
                WriteToCockpit(0, "Use 'toggle' to enable controller");
            }
        }

        private void BuildMenu(int screen)
        {
            if (currentMenu.parent != null)
                WriteToCockpit(screen, currentMenu.parent.name);

            WriteToCockpit(screen, $" - {currentMenu.name}");

            for (int i = 0; i < currentMenu.subMenues.Count; i++)
            {
                var prepend = i == currentMenu.currentIndex ? "   >> " : "   --- ";
                WriteToCockpit(screen, $"{prepend} {currentMenu.subMenues[i].name}");
            }
        }

        private void MenuUp()
        {
            currentMenu.currentIndex--;
            if (currentMenu.currentIndex < 0) currentMenu.currentIndex = currentMenu.subMenues.Count - 1;
        }

        private void MenuDown()
        {
            currentMenu.currentIndex++;
            if (currentMenu.currentIndex > currentMenu.subMenues.Count - 1) currentMenu.currentIndex = 0;
        }

        private void MenuEnter()
        {
            currentMenu.subMenues[currentMenu.currentIndex].action();
            if (currentMenu.subMenues[currentMenu.currentIndex].subMenues.Count > 0)
                currentMenu = currentMenu.subMenues[currentMenu.currentIndex];
        }

        private void MenuExit()
        {
            if (currentMenu.parent != null)
                currentMenu = currentMenu.parent;
        }

        private MenuItem DeserializeMenuFromRemote(string serialized, long remoteId)
        {
            int pos = 0;
            int ifirst = serialized.IndexOf('{');
            string itemName = serialized.Substring(0, ifirst);
            int ilast = serialized.LastIndexOf('}');
            var item = new MenuItem(itemName, NoOp);
            if (ilast < serialized.Length - 1)
                item.action = () => SendRemote(serialized.Substring(ilast + 1), remoteId);
            if (ilast > ifirst + 1)
            {
                var subMenues = serialized.Substring(ifirst + 1, ilast - ifirst);
                while (subMenues.Length > 1)
                {
                    int jfirst = subMenues.IndexOf('{') + 1;
                    int jsecond = subMenues.IndexOf('}');
                    int nextLength = int.Parse(subMenues.Substring(jfirst, jsecond - jfirst));
                    item.AddSubItem(DeserializeMenuFromRemote(subMenues.Substring(jsecond + 1, nextLength), remoteId));
                    subMenues = subMenues.Substring(jsecond + nextLength + 1);
                }
            }
            return item;
        }

        // Listeners

        private void NoOp() { }

        private void ChangeFontSize(int screen, float delta)
        {
            if (cockpit == null) return;
            cockpit.GetSurface(screen).FontSize += delta;
        }

        private void GetRemotes()
        {
            RemoteTerminals.Clear();
            GridTerminalSystem.GetBlocksOfType<IMyProgrammableBlock>(RemoteTerminals, SameConstructAsMe);

            remoteMenu.subMenues.Clear();

            for (int i = 0; i < RemoteTerminals.Count; i++)
            {
                if (RemoteTerminals[i].TryRun($"getremotecommands {Me.EntityId}"))
                {
                    try
                    {
                        var data = GetCustomData();
                        if (data.Length > 0)
                        {
                            MenuItem newRemote = DeserializeMenuFromRemote(data, RemoteTerminals[i].EntityId);
                            newRemote.name = RemoteTerminals[i].CustomName;
                            remoteMenu.AddSubItem(newRemote);
                        }
                    }
                    catch
                    {
                        DebugLog($"Error getting remote from {RemoteTerminals[i].CustomName}");
                    }
                }
            }
        }

        private void SendRemote(string command, long targetId)
        {
            ((IMyProgrammableBlock)GridTerminalSystem.GetBlockWithId(targetId)).TryRun($"{command} {Me.EntityId}");
        }

        private string GetCustomData()
        {
            var data = Me.CustomData;
            Me.CustomData = string.Empty;
            return data;
        }

        // Debug
        private void DebugLog(string log)
        {
            cockpit.GetSurface(1).WriteText(log + "\n", true);
        }
        private void DebugFlush()
        {
            cockpit.GetSurface(1).WriteText("");
        }
    }
}
