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
    public class HoverEngineDriver
    {
        // Override
        // altitudemin_slider_S
        // altituderange_slider_S
        // altituderegdist_slider_S
        // altitudemin_slider_L
        // altituderange_slider_L
        // altituderegdist_slider_L
        // aligntogravity_checkbox
        // force_to_centerofmass_checkbox
        // push_only_checkbox

        public float AltitudeMin { 
            get
            {
                return TerminalPropertiesHelper.GetValue<float>(Block, _altitudeMinName);
            }
            set
            {
                if (value != _altitudeMin) TerminalPropertiesHelper.SetValue(Block, _altitudeMinName, value);
                _altitudeMin = value;
            }
        }
        public float AltitudeRange
        {
            get
            {
                return TerminalPropertiesHelper.GetValue<float>(Block, _altitudeRangeName);
            }
            set
            {
                if (value != _altitudeRange) TerminalPropertiesHelper.SetValue(Block, _altitudeRangeName, value);
                _altitudeRange = value;
            }
        }
        public float AltitudeRegDist
        {
            get
            {
                return TerminalPropertiesHelper.GetValue<float>(Block, _altitudeRegDistName);
            }
            set
            {
                if (value != _altitudeRegDist) TerminalPropertiesHelper.SetValue(Block, _altitudeRegDistName, value);
                _altitudeRegDist = value;
            }
        }
        public float Override
        {
            get
            {
                return TerminalPropertiesHelper.GetValue<float>(Block, "Override");
            }
            set
            {
                if (value != _override) TerminalPropertiesHelper.SetValue(Block, "Override", value);
                _override = value;
            }
        }
        public float OverridePercentage
        {
            get
            {
                return Override / MaxThrust;
            }
            set
            {
                Override = value *MaxThrust;
            }
        }
        public bool ForceToCOM
        {
            get 
            {
                if (_forceToCOM == 0) return TerminalPropertiesHelper.GetValue<bool>(Block, "force_to_centerofmass_checkbox");
                return _forceToCOM == 2;
            }
            set
            {
                if (value && _forceToCOM != 2)
                {
                    TerminalPropertiesHelper.SetValue(Block, "force_to_centerofmass_checkbox", true);
                }
                else if (!value && _forceToCOM != 1)
                {
                    TerminalPropertiesHelper.SetValue(Block, "force_to_centerofmass_checkbox", false);
                }
                _forceToCOM = value ? 2 : 1;
            }
        }
        public bool PushOnly
        {
            get
            {
                if (_pushOnly == 0) return TerminalPropertiesHelper.GetValue<bool>(Block, "push_only_checkbox");
                return _pushOnly == 2;
            }
            set
            {
                if (value && _pushOnly != 2)
                {
                    TerminalPropertiesHelper.ApplyAction(Block, "push_only_btn_on_Action");
                }
                else if (!value && _pushOnly != 1)
                {
                    TerminalPropertiesHelper.ApplyAction(Block, "push_only_btn_off_Action");
                }
                _pushOnly = value ? 2 : 1;
            }
        }

        float _altitudeMin = -1;
        float _altitudeRange = -1;
        float _altitudeRegDist = -1;
        float _override = -1;
        int _forceToCOM = 0; // 0 is unknown, 1 is false, 2 is true
        int _pushOnly = 0;

        string _altitudeMinName = "altitudemin_slider";
        string _altitudeRangeName = "altituderange_slider";
        string _altitudeRegDistName = "altituderegdist_slider";

        public float MaxThrust = 714000;

        public IMyThrust Block;
        public HoverEngineDriver(IMyThrust hoverDriveTerminalBlock)
        {
            Block = hoverDriveTerminalBlock;

            var sliderSuffix = Block.CubeGrid.GridSizeEnum == MyCubeSize.Large ? "_L" : "_S";
            _altitudeMinName += sliderSuffix;
            _altitudeRangeName += sliderSuffix;
            _altitudeRegDistName += sliderSuffix;

            MaxThrust = Block.MaxThrust;
        }
    }

}
