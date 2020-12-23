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
    public class SmartThrustManager
    {
        public float MaxThrust { get { return CumulativeThrust[OrderedThrusters.Count]; }}

        // For example, if we have an atmo thruster with 20 thrust and a hydrogen thruster with 30 thrust:
        // OrderedThrusters should be [AtmoThruster, HydrogenThruster]
        // CumulativeThrust should be [0, 20, 50]
        List<IMyThrust> OrderedThrusters = new List<IMyThrust>();
        List<float> CumulativeThrust = new List<float>();

        int currentLastThruster = 0;
        float currentThrust = 0;

        public SmartThrustManager(List<IMyThrust> Thrusters)
        {
            // Insert thrusters in the order atmo > ion > hydrogen
            // We want to use atmo thrusters before hydrogen.
            float cumThrust = 0;
            CumulativeThrust.Add(0);
            foreach (var Thruster in Thrusters)
            {
                Thruster.ThrustOverride = 0;
                if (Thruster.BlockDefinition.SubtypeId.Contains("Atmospheric"))
                {
                    OrderedThrusters.Add(Thruster);
                    cumThrust += Thruster.MaxEffectiveThrust;
                    CumulativeThrust.Add(cumThrust);
                }
            }

            foreach (var Thruster in Thrusters)
            {
                if (!Thruster.BlockDefinition.SubtypeId.Contains("Atmospheric") && !Thruster.BlockDefinition.SubtypeId.Contains("Hydrogen"))
                {
                    OrderedThrusters.Add(Thruster);
                    cumThrust += Thruster.MaxEffectiveThrust;
                    CumulativeThrust.Add(cumThrust);
                }
            }

            foreach (var Thruster in Thrusters)
            {
                if (Thruster.BlockDefinition.SubtypeId.Contains("Hydrogen"))
                {
                    OrderedThrusters.Add(Thruster);
                    cumThrust += Thruster.MaxEffectiveThrust;
                    CumulativeThrust.Add(cumThrust);
                }
            }
        }

        public void SetThrust(float TargetThrust)
        {
            if (TargetThrust < 0) TargetThrust = 0;
            if (currentThrust == TargetThrust) return;
            int newLastThruster = 0;
            if (TargetThrust > CumulativeThrust[OrderedThrusters.Count])
                TargetThrust = CumulativeThrust[OrderedThrusters.Count];
            for (int i = 0; i < OrderedThrusters.Count; i++)
            {
                if (TargetThrust > CumulativeThrust[i + 1] && i >= currentLastThruster)
                {
                    OrderedThrusters[i].ThrustOverridePercentage = 1;
                }
                else if (TargetThrust < CumulativeThrust[i] && i <= currentLastThruster)
                {
                    OrderedThrusters[i].ThrustOverride = 0;
                }
                else
                {
                    OrderedThrusters[i].ThrustOverride = TargetThrust - CumulativeThrust[i];
                    newLastThruster = i;
                }
            }

            currentLastThruster = newLastThruster;
            currentThrust = TargetThrust;
        }

        public void RecalculateThrust()
        {
            for (int i = 0; i < OrderedThrusters.Count; i++)
            {
                CumulativeThrust[i + 1] = CumulativeThrust[i] + OrderedThrusters[i].MaxEffectiveThrust;
            }
        }
    }

    public class ThrusterManager
    {
        // indices line up with Base6Directions.Direction values
        Vector3D[] DirectionToVectorTable = new Vector3D[] 
        { 
            Vector3D.Forward, 
            Vector3D.Backward, 
            Vector3D.Left, 
            Vector3D.Right, 
            Vector3D.Up, 
            Vector3D.Down 
        };

        // indices line up with Base6Directions.Direction values
        List<IMyThrust>[] DirectionToThrusterList = new List<IMyThrust>[]
        {
            new List<IMyThrust>(),
            new List<IMyThrust>(),
            new List<IMyThrust>(),
            new List<IMyThrust>(),
            new List<IMyThrust>(),
            new List<IMyThrust>(),
        };

        Dictionary<Base6Directions.Direction, Vector3D> DirectionMap = new Dictionary<Base6Directions.Direction, Vector3D>()
        {
            { Base6Directions.Direction.Up, Vector3D.Up },
            { Base6Directions.Direction.Down, Vector3D.Down },
            { Base6Directions.Direction.Left, Vector3D.Left },
            { Base6Directions.Direction.Right, Vector3D.Right },
            { Base6Directions.Direction.Forward, Vector3D.Forward },
            { Base6Directions.Direction.Backward, Vector3D.Backward },
        };
        Dictionary<Base6Directions.Direction, List<IMyThrust>> Thrusters = new Dictionary<Base6Directions.Direction, List<IMyThrust>>()
        {
            { Base6Directions.Direction.Up, new List<IMyThrust>()},
            { Base6Directions.Direction.Down, new List<IMyThrust>()},
            { Base6Directions.Direction.Left, new List<IMyThrust>()},
            { Base6Directions.Direction.Right, new List<IMyThrust>()},
            { Base6Directions.Direction.Forward, new List<IMyThrust>()},
            { Base6Directions.Direction.Backward, new List<IMyThrust>()},
        };

        Base6Directions.Direction[] directions = new Base6Directions.Direction[6] {
            Base6Directions.Direction.Up,
            Base6Directions.Direction.Down,
            Base6Directions.Direction.Left,
            Base6Directions.Direction.Right,
            Base6Directions.Direction.Forward,
            Base6Directions.Direction.Backward
        };

        Dictionary<Base6Directions.Direction, double> prevPowers = new Dictionary<Base6Directions.Direction, double>();

        public ThrusterManager()
        {

        }

        public void Clear()
        {
            foreach (var direction in directions)
            {
                Thrusters[direction].Clear();
            }
            prevPowers.Clear();
        }

        public void AddThruster(IMyThrust thruster)
        {
            Thrusters[thruster.Orientation.Forward].Add(thruster);
        }

        public void SmartSetThrust(Vector3D GridMoveIndicator)
        {
            foreach (var K in directions)
            {
                List<IMyThrust> list = Thrusters[K];
                if (Thrusters[K].Count == 0) return;
                if (!prevPowers.ContainsKey(K))
                {
                    foreach (var thruster in list) thruster.ThrustOverridePercentage = 0;
                    prevPowers[K] = 0;
                }
                var power = Math.Max(0, Math.Min((DirectionMap[K] * -1f).Dot(GridMoveIndicator), 0.999999));

                if (power == prevPowers[K]) continue;

                var finalThrusterIndex = (int)(power * list.Count);
                var prevFinalThrusterIndex = (int)(prevPowers[K] * list.Count);
                if (finalThrusterIndex > prevFinalThrusterIndex)
                {
                    for (int i = prevFinalThrusterIndex; i < finalThrusterIndex; i++)
                        SetThruster(K, i, 1);
                }
                else if (finalThrusterIndex < prevFinalThrusterIndex)
                {
                    for (int i = finalThrusterIndex + 1; i <= prevFinalThrusterIndex; i++)
                        SetThruster(K, i, 0);
                }
                SetThruster(K, finalThrusterIndex, (float)((power * list.Count) % 1));
                prevPowers[K] = power;
            }
        }

        void SetThruster(Base6Directions.Direction K, int i, float val)
        {
            Thrusters[K][i].ThrustOverridePercentage = val;
        }
    }
}
