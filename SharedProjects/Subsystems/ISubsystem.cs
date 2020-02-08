using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI.Ingame;

namespace IngameScript
{
    public interface ISubsystem
    {
        void Setup(MyGridProgram program, string name);
        void Update(TimeSpan timestamp, UpdateFrequency updateFlags);
        void Command(TimeSpan timestamp, string command, object argument);
        string GetStatus();

        string SerializeSubsystem();
        void DeserializeSubsystem(string serialized);

        UpdateFrequency UpdateFrequency { get; }
    }

    public interface IControlIntercepter
    {
        bool InterceptControls { get; }
        IMyShipController Controller { get; }
    }
}
