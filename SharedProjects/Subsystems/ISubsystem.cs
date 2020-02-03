using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Ingame;

namespace SharedProjects.Subsystems
{
    public interface ISubsystem
    {
        void Setup(MyGridProgram program, SubsystemManager manager);
        void Update(TimeSpan timestamp);
        void Command(string command, object argument);
        string GetStatus();

        string SerializeSubsystem();
        void DeserializeSubsystem(string serialized);

        int UpdateFrequency { get; }
    }
}
