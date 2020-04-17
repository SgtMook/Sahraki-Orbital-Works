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
    public static class LicenseHasher
    {
        public static long hash1(long a, long b)
        {
            return (a + 2461) * b + 4663;
        }

        public static long hash2(long a, long b)
        {
            return (a + 6154) * b + 2451;
        }

        public static long hash3(long a, long b)
        {
            return (a + 572) * b + 2143;
        }

        public static long hash4(long a, long b)
        {
            return (a + 9683) * b + 5243;
        }

        public static long hash5(long a, long b)
        {
            return (a + 2646) * b + 4613;
        }

        public static long hash6(long a, long b)
        {
            return (a + 1126) * b + 3512;
        }

        public static long hash7(long a, long b)
        {
            return (a + 6524) * b + 1125;
        }

        public static string GetWrongKeyError()
        {
            return "Your license key is incorrect.";
        }

        public static string GetElapsedError()
        {
            return "Your license has elapsed.";
        }

        public static string GetWrongOwnerError()
        {
            return "The license you are trying to use does not belong to you or your faction.";
        }

        public static string GetExceedCombatDroneError()
        {
            return "You have exceeded the maximum number of combat drones your license allows.";
        }

        public static string GetLicenseField()
        {
            return "License";
        }

        public static string GetLicenseString()
        {
            return "LicenseString";
        }

        public static string GetLicenseKeyString()
        {
            return "Key";
        }
    }
}
