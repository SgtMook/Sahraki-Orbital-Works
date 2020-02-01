using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Ingame;

namespace SharedProjects
{
    public interface ISubsystem
    {
        void Setup(MyGridProgram program);
        void Update();
        void Command(string command, object argument);
        string GetStatus();

        string SerializeSubsystem();
        void DeserializeSubsystem(string serialized);

        int UpdateFrequency { get; }
    }
}
