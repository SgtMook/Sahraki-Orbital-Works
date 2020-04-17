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
    public class DRMSubsystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update100;

        public void Command(TimeSpan timestamp, string command, object argument)
        {
        }

        public void DeserializeSubsystem(string serialized)
        {
        }

        public string GetStatus()
        {
            return string.Empty;
        }

        public string SerializeSubsystem()
        {
            return string.Empty;
        }

        public void Setup(MyGridProgram program, string name)
        {
            Program = program;
            ParseConfigs();
        }

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            CheckLicenseDisable(timestamp);
        }
        #endregion

        MyGridProgram Program;
        IIntelProvider IntelProvider;

        int NumCombatDrones = 0;

        string ErrorMsg = string.Empty;
        StringBuilder stringBuilder = new StringBuilder();

        public DRMSubsystem(IIntelProvider intelProvider)
        {
            IntelProvider = intelProvider;
        }

        // [License]
        // LicenseString = AKI-20200501-02
        // Key = 9189310709010477879
        private void ParseConfigs()
        {
            ErrorMsg = "";
            MyIni Parser = new MyIni();
            MyIniParseResult result;
            if (!Parser.TryParse(Program.Me.CustomData, out result))
                return;

            var LicenseString = Parser.Get(LicenseHasher.GetLicenseField(), LicenseHasher.GetLicenseString()).ToString();
            var Key = Parser.Get(LicenseHasher.GetLicenseField(), LicenseHasher.GetLicenseKeyString()).ToInt64();

            if (StringToNumber(LicenseString) != Key)
            {
                Crash(LicenseHasher.GetWrongKeyError());
                EchoError();
                Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, DisableParts);
                return;
            }

            string[] fields = LicenseString.Split('-');
            if (fields[0] != GetTagFunction(Program.Me)())
            {
                Crash(LicenseHasher.GetWrongOwnerError());
                EchoError();
                Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, DisableParts);
                return;
            }

            var year = int.Parse(fields[1].Substring(0, 4));
            var month = int.Parse(fields[1].Substring(4, 2));
            var day = int.Parse(fields[1].Substring(6, 2));

            if (new DateTime(year, month, day) < DateTime.Today)
            {
                Crash(LicenseHasher.GetElapsedError());
                EchoError();
                Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, DisableParts);
                return;
            }

            NumCombatDrones = int.Parse(fields[2]);

            if (ErrorMsg == string.Empty)
            {
                stringBuilder.Clear();
                stringBuilder.AppendLine("<< Project Looking Glass >>");
                stringBuilder.AppendLine("<< V 0.9.0 >>");
                stringBuilder.AppendLine();
                stringBuilder.AppendLine("<< License Accepted >>");
                stringBuilder.AppendLine($"<< License Holder - {fields[0]} >>");
                stringBuilder.AppendLine($"<< License Valid Until - {year}/{month}/{day} >>");
                stringBuilder.AppendLine($"<< License Includes: >>");
                stringBuilder.AppendLine($"<< Command Units - Unlimited >>");
                stringBuilder.AppendLine($"<< Combat Drones - {NumCombatDrones} Units >> ");
                stringBuilder.AppendLine();
                stringBuilder.AppendLine("<< AKI thanks you for your business. >>");
                Program.Echo(stringBuilder.ToString());
            }
        }

        public long StringToNumber(string input)
        {
            long value = 7;
            long index = 0;
            foreach (var c in input)
            {
                value = hashFs[index % 7](value, Convert.ToByte(c));
                index = hashFs[index % 7](index, Convert.ToByte(c));
                if (index < 0) index *= -1;
            }
            return value;
        }

        public Func<long, long, long>[] hashFs = new Func<long, long, long>[7]
        {
            LicenseHasher.hash1, LicenseHasher.hash2, LicenseHasher.hash3, LicenseHasher.hash4, LicenseHasher.hash5, LicenseHasher.hash6, LicenseHasher.hash7
        };

        private void Crash(string error)
        {
            ErrorMsg = error;
        }

        public Func<string> GetTagFunction(IMyCubeBlock block)
        {
            return block.GetOwnerFactionTag;
        }

        private void CheckLicenseDisable(TimeSpan localTime)
        {
            if (NumCombatDrones < 99)
            {
                var currentCombatDrones = 0;
                var intels = IntelProvider.GetFleetIntelligences(localTime);
                foreach (var kvp in intels)
                {
                    if (kvp.Value.IntelItemType == IntelItemType.Friendly)
                    {
                        var fsi = (FriendlyShipIntel)kvp.Value;
                        if (fsi.AgentClass == AgentClass.Fighter) currentCombatDrones++;
                        if (currentCombatDrones > NumCombatDrones) break;
                    }
                }

                if (currentCombatDrones > NumCombatDrones)
                {
                    ErrorMsg = LicenseHasher.GetExceedCombatDroneError();
                    EchoError();
                    Program.GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, DisableParts);
                    return;
                }
            }

            if (ErrorMsg != "")
            {
                ParseConfigs();
            }
        }

        private bool DisableParts(IMyTerminalBlock arg)
        {
            if (arg.CubeGrid.EntityId == Program.Me.CubeGrid.EntityId && arg is IMyFunctionalBlock
                && !(arg is IMyShipConnector) && !(arg is IMyBatteryBlock) && !(arg is IMyGasTank))
                Disable((IMyFunctionalBlock)arg);
            return false;
        }

        private void Disable(IMyFunctionalBlock block)
        {
            block.Enabled = false;
        }

        private void EchoError()
        {
            stringBuilder.Clear();
            stringBuilder.AppendLine("<< Project Looking Glass >>");
            stringBuilder.AppendLine("<< V 0.9.0 >>");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("<< License Declined >>");
            stringBuilder.AppendLine($"<< Error - {ErrorMsg} >>");
            stringBuilder.AppendLine();
            stringBuilder.AppendLine("<< All blocks on this grid has been disabled. >>");
            stringBuilder.AppendLine("<< Please contact AKI for assistance. >>");
            Program.Echo(stringBuilder.ToString());
        }
    }
}
