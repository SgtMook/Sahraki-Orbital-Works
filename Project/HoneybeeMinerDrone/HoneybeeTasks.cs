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
    public class HoneybeeMiningTaskGenerator : ITaskGenerator
    {
        #region ITaskGenerator
        public TaskType AcceptedTypes => TaskType.Mine;

        public ITask GenerateTask(TaskType type, MyTuple<IntelItemType, long> intelKey, Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, long myID)
        {
            if (type != TaskType.Mine) return new NullTask();
            if (!IntelItems.ContainsKey(intelKey)) return new NullTask();
            if (intelKey.Item1 != IntelItemType.Waypoint) return new NullTask();

            var target = (Waypoint)IntelItems[intelKey];

            // Make sure we actually have the asteroid we are supposed to be mining
            AsteroidIntel host = null;
            foreach (var kvp in IntelItems)
            {
                if (kvp.Key.Item1 != IntelItemType.Asteroid) continue;
                var dist = (kvp.Value.GetPositionFromCanonicalTime(canonicalTime) - target.GetPositionFromCanonicalTime(canonicalTime)).Length();
                if (dist > kvp.Value.Radius) continue;
                host = (AsteroidIntel)kvp.Value;
                break;
            }

            if (host == null) return new NullTask();

            return new HoneybeeMiningTask(Program, MiningSystem, Autopilot, AgentSubsystem, target, host, IntelProvider, MonitorSubsystem, DockingSubsystem, DockTaskGenerator, UndockTaskGenerator);
        }
        #endregion

        MyGridProgram Program;
        HoneybeeMiningSystem MiningSystem;
        IAutopilot Autopilot;
        IAgentSubsystem AgentSubsystem;
        IDockingSubsystem DockingSubsystem;
        DockTaskGenerator DockTaskGenerator;
        UndockFirstTaskGenerator UndockTaskGenerator;
        IIntelProvider IntelProvider;
        IMonitorSubsystem MonitorSubsystem;

        public HoneybeeMiningTaskGenerator(MyGridProgram program, HoneybeeMiningSystem miningSystem, IAutopilot autopilot, IAgentSubsystem agentSubsystem, IDockingSubsystem dockingSubsystem, DockTaskGenerator dockTaskGenerator, UndockFirstTaskGenerator undockTaskGenerator, IIntelProvider intelProvder, IMonitorSubsystem monitorSubsystem)
        {
            Program = program;
            MiningSystem = miningSystem;
            Autopilot = autopilot;
            AgentSubsystem = agentSubsystem;
            DockTaskGenerator = dockTaskGenerator;
            UndockTaskGenerator = undockTaskGenerator;
            IntelProvider = intelProvder;
            MonitorSubsystem = monitorSubsystem;
            DockingSubsystem = dockingSubsystem;
        }
    }


    public class HoneybeeMiningTask : ITask
    {
        #region ITask
        public TaskStatus Status { get; private set; }


        StringBuilder debugBuilder = new StringBuilder();

        public void Do(Dictionary<MyTuple<IntelItemType, long>, IFleetIntelligence> IntelItems, TimeSpan canonicalTime, Profiler profiler)
        {
            // DEBUG
            //debugBuilder.Clear();
            //for (int i = 0; i < 11; i++)
            //{
            //    for (int j = 0; j < 11; j++)
            //    {
            //        debugBuilder.Append(miningMatrix[i, j]);
            //    }
            //    debugBuilder.AppendLine();
            //}
            //
            //foreach (var percentage in LastSampleCargoPercentages)
            //{
            //    debugBuilder.AppendLine(percentage.ToString());
            //}
            //
            //AgentSubsystem.Status = debugBuilder.ToString();
            // DEBUG
            if (MiningSystem.Recalling > 0 || currentPosition > 120)
            {
                Recalling = true;
            }
            if (Recalling && state < 3) state = 3;
            if (state == 1) // Diving to surface of asteroid
            {
                MineTask.Do(IntelItems, canonicalTime, profiler);
                MineTask.Destination.MaxSpeed = Autopilot.GetMaxSpeedFromBrakingDistance(kFarSensorDist);

                if (MineTask.Status == TaskStatus.Complete || !MiningSystem.SensorsFarClear())
                {
                    EntryPoint = Autopilot.Reference.WorldMatrix.Translation + MineTask.Destination.Direction * (kFarSensorDist - 10);
                    MineTask.Destination.MaxSpeed = 1f;
                    state = 2;
                }
            }
            else if (state == 2) // Boring tunnel
            {
                bool sampleHome = false;
                double distToMiningEnd = (Autopilot.Reference.WorldMatrix.Translation - MiningEnd).Length();
                if (MiningSystem.SensorsClear())
                {
                    MineTask.Destination.Position = MiningEnd;
                }
                else if (MiningSystem.SensorsBack())
                {
                    MineTask.Destination.Position = EntryPoint;
                }
                else
                {
                    MineTask.Destination.Position = Vector3D.Zero;
                    if (SampleCount <= 0)
                    {
                        SampleCount = kSampleFrequency;
                        var cargoPercentage = MonitorSubsystem.GetPercentage(MonitorOptions.Cargo);
                        if (LastSampleCargoPercentages.Count >= kMaxSampleCount)
                        {
                            LastSampleCargoPercentages.Enqueue(cargoPercentage);

                            var sampleGreater = 0;
                            var sampleLesser = 0;
                            var comparePercentage = LastSampleCargoPercentages.Dequeue() + 0.00001;
                            foreach (var percentage in LastSampleCargoPercentages)
                            {
                                if (percentage > comparePercentage) sampleGreater++;
                                else sampleLesser++;
                            }

                            if (sampleGreater > sampleLesser)
                            {
                                if (!HitOre)
                                {
                                    HitOre = true;
                                    var currentCoords = GetMiningPosition(currentPosition);
                                    miningMatrix[currentCoords.X + 5, currentCoords.Y + 5] = 1;
                                }
                                if (LowestExpectedOreDist == -1)
                                    LowestExpectedOreDist = (float)distToMiningEnd - 5;
                            }
                            else
                            {
                                if (HitOre || distToMiningEnd < LowestExpectedOreDist)
                                {
                                    sampleHome = true;

                                    if (!HitOre)
                                    {
                                        var currentCoords = GetMiningPosition(currentPosition);
                                        miningMatrix[currentCoords.X + 5, currentCoords.Y + 5] = 2;
                                    }
                                }
                            }
                        }
                        else
                        {
                            LastSampleCargoPercentages.Enqueue(cargoPercentage);
                        }
                    }
                    else
                    {
                        SampleCount--;
                    }
                }

                MiningSystem.Drill();
                MineTask.Do(IntelItems, canonicalTime, profiler);

                if (GoHomeCheck() || MiningSystem.SensorsFarClear() || distToMiningEnd < 4 || sampleHome)
                {
                    if (MiningSystem.SensorsFarClear() || distToMiningEnd < 4 || sampleHome)
                    {
                        UpdateMiningMatrix(currentPosition);
                        IncrementCurrentPosition();
                        HitOre = false;
                    }
                    state = 3;
                    MineTask.Destination.MaxSpeed = 1;
                    LastSampleCargoPercentages.Clear();
                }
            }
            else if (state == 3) // Exiting tunnel
            {
                MiningSystem.StopDrill();
                if (MineTask.Destination.Position != ExitPoint) MineTask.Destination.Position = EntryPoint;
                MineTask.Do(IntelItems, canonicalTime, profiler);
                if (MineTask.Status == TaskStatus.Complete)
                {
                    if (MineTask.Destination.Position == EntryPoint)
                    {
                        MineTask.Destination.Position = ExitPoint;
                        MineTask.Destination.MaxSpeed = 100;
                    }
                    else
                    {
                        state = 10;
                    }
                }
            }
            else if (state == 10) // Resuming to approach point
            {
                if (GoHomeCheck() || Recalling) state = 4;
                else
                {
                    LeadTask.Destination.Position = ApproachPoint;
                    LeadTask.Do(IntelItems, canonicalTime, profiler);
                    if (LeadTask.Status == TaskStatus.Complete)
                    {
                        var position = GetMiningPosition(currentPosition);
                        LeadTask.Destination.Position = ApproachPoint + (Perpendicular * position.X * MiningSystem.OffsetDist + Perpendicular.Cross(MineTask.Destination.Direction) * position.Y * MiningSystem.OffsetDist);
                        LeadTask.Destination.MaxSpeed = 10;
                        ExitPoint = LeadTask.Destination.Position;
                        state = 11;
                    }
                }
            }
            else if (state == 11) // Search for the digging spot
            {
                if (GoHomeCheck() || Recalling) state = 4;
                else
                {
                    LeadTask.Do(IntelItems, canonicalTime, profiler);
                    if (LeadTask.Status == TaskStatus.Complete)
                    {
                        state = 1;
                        MiningSystem.SensorsOn();
                        var position = GetMiningPosition(currentPosition);
                        MineTask.Destination.Position = SurfacePoint + (Perpendicular * position.X * MiningSystem.OffsetDist + Perpendicular.Cross(MineTask.Destination.Direction) * position.Y * MiningSystem.OffsetDist) - MineTask.Destination.Direction * MiningSystem.CloseDist;
                        MiningEnd = SurfacePoint + (Perpendicular * position.X * MiningSystem.OffsetDist + Perpendicular.Cross(MineTask.Destination.Direction) * position.Y * MiningSystem.OffsetDist) + MineTask.Destination.Direction * MiningDepth;
                    }
                }
            }
            else if (state == 4) // Going home
            {
                if (DockingSubsystem.HomeID == -1)
                {
                    state = 9999;
                }
                else
                {
                    if (HomeTask == null)
                    {
                        HomeTask = DockTaskGenerator.GenerateMoveToAndDockTask(MyTuple.Create(IntelItemType.NONE, (long)0), IntelItems, 40);
                    }
                    HomeTask.Do(IntelItems, canonicalTime, profiler);
                    if (HomeTask.Status != TaskStatus.Incomplete)
                    {
                        HomeTask = null;
                        homeCheck = false;
                        state = 5;
                    }
                }
            }
            else if (state == 5) // Waiting for refuel/unload
            {
                if (Recalling) state = 9999;
                if ((Program.Me.WorldMatrix.Translation - EntryPoint).LengthSquared() > MiningSystem.CancelDist * MiningSystem.CancelDist) state = 9999;
                if (LeaveHomeCheck()) state = 6;
            }
            else if (state == 6) // Undocking
            { 
                if (DockingSubsystem.Connector.Status == MyShipConnectorStatus.Connected)
                {
                    if (UndockTask == null)
                    {
                        UndockTask = UndockTaskGenerator.GenerateUndockTask(canonicalTime);
                    }
                }

                if (UndockTask != null)
                {
                    UndockTask.Do(IntelItems, canonicalTime, profiler);
                    if (UndockTask.Status != TaskStatus.Incomplete)
                    {
                        UndockTask = null;
                        state = 10;
                    }
                }
                else
                {
                    state = 10;
                }
            }
            else if (state == 9999)
            {
                Status = TaskStatus.Complete;
            }
        }

        public string Name => "HoneybeeMiningTask";
        #endregion

        WaypointTask LeadTask;
        WaypointTask MineTask;
        MyGridProgram Program;
        HoneybeeMiningSystem MiningSystem;
        IAutopilot Autopilot;
        IAgentSubsystem AgentSubsystem;
        IMonitorSubsystem MonitorSubsystem;
        IDockingSubsystem DockingSubsystem;
        MyTuple<IntelItemType, long> IntelKey;
        AsteroidIntel Host;
        Vector3D EntryPoint;
        Vector3D ExitPoint;
        Vector3D ApproachPoint;
        Vector3D MiningEnd;
        Vector3D Perpendicular;
        Vector3D SurfacePoint;
        ITask HomeTask = null;
        ITask UndockTask = null;

        DockTaskGenerator DockTaskGenerator;
        UndockFirstTaskGenerator UndockTaskGenerator;

        int currentPosition = 0;
        
        double MiningDepth;
        double SurfaceDist;

        int state = 6;

        bool Recalling = false;

        const int kFarSensorDist = 40;

        const int kMaxSampleCount = 5;
        const int kSampleFrequency = 1;
        int SampleCount = 0;

        Queue<float> LastSampleCargoPercentages = new Queue<float>(4);
        bool HitOre = false;
        float LowestExpectedOreDist = -1;

        Vector2I[] Orientations = { new Vector2I(1, 0), new Vector2I(0, 1), new Vector2I(-1, 0), new Vector2I(0, -1) };
        Vector2I[] OrientationsUp = { new Vector2I(0, 1), new Vector2I(-1, 0), new Vector2I(0, -1), new Vector2I(1, 0) };

        bool homeCheck = false;

        public HoneybeeMiningTask(MyGridProgram program, HoneybeeMiningSystem miningSystem, IAutopilot autopilot, IAgentSubsystem agentSubsystem, Waypoint target, AsteroidIntel host, IIntelProvider intelProvider, IMonitorSubsystem monitorSubsystem, IDockingSubsystem dockingSubsystem, DockTaskGenerator dockTaskGenerator, UndockFirstTaskGenerator undockTaskGenerator)
        {
            Program = program;
            MiningSystem = miningSystem;
            Autopilot = autopilot;
            AgentSubsystem = agentSubsystem;
            MonitorSubsystem = monitorSubsystem;
            Host = host;
            MiningDepth = MiningSystem.MineDepth;
            LowestExpectedOreDist = (float)MiningDepth;
            DockingSubsystem = dockingSubsystem;

            Status = TaskStatus.Incomplete;

            double lDoc, det;
            GetSphereLineIntersects(host.Position, host.Radius, target.Position, target.Direction, out lDoc, out det);
            Perpendicular = GetPerpendicular(target.Direction);

            if (det < 0)
            {
                Status = TaskStatus.Aborted;
                state = -1;
                return;
            }

            SurfaceDist = -lDoc + Math.Sqrt(det);

            ApproachPoint = target.Position + target.Direction * SurfaceDist;
            ExitPoint = ApproachPoint;

            EntryPoint = target.Position + target.Direction * miningSystem.CloseDist;
            MiningEnd = target.Position - target.Direction * MiningDepth;

            SurfacePoint = target.Position;

            LeadTask = new WaypointTask(Program, Autopilot, new Waypoint(), WaypointTask.AvoidObstacleMode.SmartEnter);
            MineTask = new WaypointTask(Program, Autopilot, new Waypoint(), WaypointTask.AvoidObstacleMode.DoNotAvoid);

            LeadTask.Destination.Position = ApproachPoint;
            LeadTask.Destination.Direction = target.Direction * -1;
            LeadTask.Destination.DirectionUp = Perpendicular;
            intelProvider.ReportFleetIntelligence(LeadTask.Destination, TimeSpan.FromSeconds(1));
            MineTask.Destination.Direction = target.Direction * -1;
            MineTask.Destination.DirectionUp = Perpendicular;
            MineTask.Destination.Position = EntryPoint;

            DockTaskGenerator = dockTaskGenerator;
            UndockTaskGenerator = undockTaskGenerator;
        }

        // https://en.wikipedia.org/wiki/Line%E2%80%93sphere_intersection
        private void GetSphereLineIntersects(Vector3D center, double radius, Vector3D lineStart, Vector3D lineDirection, out double lDoc, out double det)
        {
            lDoc = Vector3.Dot(lineDirection, lineStart - center);
            det = lDoc * lDoc - ((lineStart - center).LengthSquared() - radius * radius);
        }

        private bool GoHomeCheck()
        {
            if (homeCheck) return true;
            if (MonitorSubsystem.GetPercentage(MonitorOptions.Cargo) > 0.96 ||
                MonitorSubsystem.GetPercentage(MonitorOptions.Hydrogen) < 0.2 ||
                MonitorSubsystem.GetPercentage(MonitorOptions.Power) < 0.2)
            {
                homeCheck = true;
                return true;
            }
            return false;
        }

        private bool LeaveHomeCheck()
        {
            return MonitorSubsystem.GetPercentage(MonitorOptions.Cargo) < 0.01 &&
                   MonitorSubsystem.GetPercentage(MonitorOptions.Hydrogen) > 0.9 &&
                   MonitorSubsystem.GetPercentage(MonitorOptions.Power) > 0.4;
        }

        private Vector3D GetPerpendicular(Vector3D vector)
        {
            Vector3D result = new Vector3D(1, 1, -(vector.X + vector.Y) / vector.Z);
            result.Normalize();
            return result;
        }

        private Vector2I GetMiningPosition(int index)
        {
            if (index == 0) return Vector2I.Zero;
            index -= 1;
            int rem = index % 4;
            int subIndex = index / 4;

            int col = 1;
            int space = 0;
            for (int i = 0; i < subIndex; i++)
            {
                space++;
                if (space > col)
                {
                    col++;
                    space = 1 - col;
                }
            }
            return Orientations[rem] * col + OrientationsUp[rem] * space;
        }

        // 0 = Not to mine
        // 1 = Mined - Has Ore
        // 2 = Mined - No Ore
        // 3 = To mine
        int[,] miningMatrix = new int[11, 11];

        private void UpdateMiningMatrix(int currentPosition)
        {
            var currentCoords = GetMiningPosition(currentPosition) + 5;
            if (miningMatrix[currentCoords.X, currentCoords.Y] == 1)
            {
                for (int i = 0; i < 4; i++)
                {
                    var offsetCoords = Orientations[i] + currentCoords;
                    if (offsetCoords.X >= 0 && offsetCoords.X < 11 && offsetCoords.Y >= 0 && offsetCoords.Y < 11 && miningMatrix[offsetCoords.X, offsetCoords.Y] == 0)
                    {
                        miningMatrix[offsetCoords.X, offsetCoords.Y] = 3;
                    }
                }
            }
            //if (miningMatrix[currentCoords.X, currentCoords.Y] == 2)
            //{
            //    for (int i = 0; i < 4; i++)
            //    {
            //        var offsetCoords = Orientations[i] + currentCoords;
            //        if (miningMatrix[offsetCoords.X, offsetCoords.Y] == 1)
            //        {
            //            // Check in line
            //            for (int j = 0; j < 11; j++)
            //            {
            //                var setCoords = -Orientations[i] * j + currentCoords;
            //                if (setCoords.X >= 0 && setCoords.X < 11 && setCoords.Y >= 0 && setCoords.Y < 11)
            //                {
            //                    miningMatrix[setCoords.X, setCoords.Y] = 3;
            //                }
            //                else
            //                {
            //                    break;
            //                }
            //            }
            //
            //            // Check upper corner
            //            offsetCoords = Orientations[i] + OrientationsUp[i] + currentCoords;
            //
            //            if (miningMatrix[offsetCoords.X, offsetCoords.Y] == 2)
            //            {
            //                for (int j = 0; j < 11; j++)
            //                {
            //                    for (int k = 0; k < 11; k++)
            //                    {
            //                        var setCoords = -Orientations[i] * j + OrientationsUp[i] * k + currentCoords;
            //                        if (setCoords.X >= 0 && setCoords.X < 11 && setCoords.Y >= 0 && setCoords.Y < 11)
            //                        {
            //                            miningMatrix[setCoords.X, setCoords.Y] = 3;
            //                        }
            //                        else
            //                        {
            //                            break;
            //                        }
            //                    }
            //                }
            //            }
            //
            //            // Check lower corner
            //            offsetCoords = Orientations[i] - OrientationsUp[i] + currentCoords;
            //
            //            if (miningMatrix[offsetCoords.X, offsetCoords.Y] == 2)
            //            {
            //                for (int j = 0; j < 11; j++)
            //                {
            //                    for (int k = 0; k < 11; k++)
            //                    {
            //                        var setCoords = -Orientations[i] * j - OrientationsUp[i] * k + currentCoords;
            //                        if (setCoords.X >= 0 && setCoords.X < 11 && setCoords.Y >= 0 && setCoords.Y < 11)
            //                        {
            //                            miningMatrix[setCoords.X, setCoords.Y] = 3;
            //                        }
            //                        else
            //                        {
            //                            break;
            //                        }
            //                    }
            //                }
            //            }
            //        }
            //    }
            //}
        }

        private void IncrementCurrentPosition()
        {
            while (currentPosition <= 120)
            {
                currentPosition++;
                var currentCoords = GetMiningPosition(currentPosition) + 5;

                if (currentCoords.X >= 0 && currentCoords.X < 11 && currentCoords.Y >= 0 && currentCoords.Y < 11 && miningMatrix[currentCoords.X, currentCoords.Y] == 3) return;

                //for (int i = 0; i < 4; i++)
                //{
                //    var offsetCoords = Orientations[i] + currentCoords;
                //    if (offsetCoords.X >= 0 && offsetCoords.X < 11 && offsetCoords.Y >= 0 && offsetCoords.Y < 11 && miningMatrix[offsetCoords.X, offsetCoords.Y] == 3)
                //        return;
                //}

                //if (currentCoords.X >= 0 && currentCoords.X < 11 && currentCoords.Y >= 0 && currentCoords.Y < 11 &&
                //    miningMatrix[currentCoords.X, currentCoords.Y] == 0) return;
            }
        }
    }
}
