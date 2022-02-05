using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Text;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace IngameScript
{
    public class HeliDriveSubsystem : ISubsystem
    {
        #region ISubsystem
        public UpdateFrequency UpdateFrequency => UpdateFrequency.Update1;

        public void Command(TimeSpan timestamp, string command, object argument)
        {
        }

        public void CommandV2(TimeSpan timestamp, CommandLine command)
        {
            if (command.Argument(0) == "toggle_manual") Drive.SwitchToMode(Drive.mode == "manual" ? "flight" : "manual");
            else if (command.Argument(0) == "toggle_landing") Drive.SwitchToMode(Drive.mode == "landing" ? "flight" : "landing");
            else if (command.Argument(0) == "toggle_shutdown") Drive.SwitchToMode(Drive.mode == "shutdown" ? "flight" : "shutdown");
            else if (command.Argument(0) == "toggle_standby") Drive.SwitchToMode(Drive.mode == "standby" ? "flight" : "standby");
            else if (command.Argument(0) == "toggle_precision") enablePrecisionAim = !enablePrecisionAim;
            else if (command.Argument(0) == "toggle_lateral_dampening") Drive.enableLateralOverride = !Drive.enableLateralOverride;
            else if (command.Argument(0) == "toggle_lateral_override") Drive.enableLateralOverride = !Drive.enableLateralOverride;
        }

        public void DeserializeSubsystem(string serialized)
        {
            Drive.SwitchToMode(serialized);
        }

        public string GetStatus()
        {
            return Drive.mode;
        }

        public string SerializeSubsystem()
        {
            return Drive.mode;
        }

        // [HeliDrive]
        // block_group_name = "Heli Assist"
        // start_mode = flight
        // remember_mode = true
        // max_pitch = 40
        // max_roll = 40
        // max_landing_pitch = 15
        // max_landing_roll = 15
        // precision = 16
        // mouse_speed = 0.5
        void ParseConfigs()
        {
            iniParser.Clear();
            MyIniParseResult result;
            if (!iniParser.TryParse(Context.Reference.CustomData, out result))
                return;

            blockGroupName = iniParser.Get(kHeliDriveConfigSection, "block_group_name").ToString("Heli Assist");

            start_mode = iniParser.Get(kHeliDriveConfigSection, "start_mode").ToString("flight");
//            rememberLastMode = iniParser.Get(kHeliDriveConfigSection, "remember_mode").ToBoolean(true);

            Drive.maxFlightPitch = iniParser.Get(kHeliDriveConfigSection, "max_pitch").ToSingle(40.0f);
            Drive.maxFlightRoll = iniParser.Get(kHeliDriveConfigSection, "max_roll").ToSingle(40.0f);

            Drive.maxLandingPitch = iniParser.Get(kHeliDriveConfigSection, "max_landing_pitch").ToSingle(15.0f);
            Drive.maxLandingRoll = iniParser.Get(kHeliDriveConfigSection, "max_landing_roll").ToSingle(15.0f);

            precisionAimFactor = iniParser.Get(kHeliDriveConfigSection, "precision").ToSingle(16.0f);
            mouseSpeed = iniParser.Get(kHeliDriveConfigSection, "mouse_speed").ToSingle(0.5f);
        }

        public void Setup(ExecutionContext context, string name)
        {
            Context = context;
            ParseConfigs();

            var blockGroup = Context.Terminal.GetBlockGroupWithName(blockGroupName);
            if (blockGroup == null) throw new Exception("Could not find block group with name '" + blockGroupName + "'");

            controllerCache.Clear();
            blockGroup.GetBlocksOfType<IMyShipController>(controllerCache);
            if (controllerCache.Count == 0) throw new Exception("Ship must have at least one ship controller");
            controller = null;
            foreach (var controller in controllerCache)
            {
                if (controller.IsUnderControl || (controller.IsMainCockpit && this.controller == null))
                    this.controller = controller;
            }
            if (this.controller == null) this.controller = controllerCache[0];

            gyroCache.Clear();
            blockGroup.GetBlocksOfType<IMyGyro>(gyroCache);
            if (gyroCache.Count == 0) throw new Exception("Ship must have atleast one gyroscope");

            thrustCache.Clear();
            blockGroup.GetBlocksOfType<IMyThrust>(thrustCache);
            if (thrustCache.Count == 0) throw new Exception("Ship must have atleast one thruster");

            Drive.Setup(controller);

            Drive.thrustController.Update(controller, thrustCache);

            Drive.gyroController.Update(controller, gyroCache);

            Drive.SwitchToMode(start_mode);
        }

        

        public void Update(TimeSpan timestamp, UpdateFrequency updateFlags)
        {
            runs++;
            if (runs% 10 == 0)
            {
                Drive.MoveIndicators = controller.MoveIndicator;
                Drive.RotationIndicators = new Vector3(controller.RotationIndicator, controller.RollIndicator * 9);
                Drive.autoStop = controller.DampenersOverride;

                if (enablePrecisionAim) Drive.RotationIndicators *= 1 / precisionAimFactor;
                else Drive.RotationIndicators *= mouseSpeed;

                Drive.enableLateralOverride = enableLateralOverride;

                Drive.Drive();
            }
        }
        #endregion

        //Config Variables
        string blockGroupName;

        string start_mode;
//        string last_mode = "";
//        bool rememberLastMode;


        MyIni iniParser = new MyIni();
        public const string kHeliDriveConfigSection = "HeliDrive";


        ExecutionContext Context;

        int runs = 0;

        HeliDrive Drive = new HeliDrive();
        IMyShipController controller;

        public float precisionAimFactor;
        public float mouseSpeed;

        public bool enableLateralOverride;
        public bool enablePrecisionAim;

        //Cache Variables
        public List<IMyShipController> controllerCache = new List<IMyShipController>();
        public List<IMyGyro> gyroCache = new List<IMyGyro>();
        public List<IMyThrust> thrustCache = new List<IMyThrust>();

    }
}
