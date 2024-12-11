using GameReaderCommon;
using SimHub.Plugins;
using System;
using System.Text;  //For File Encoding
using System.Windows.Forms;
using System.Windows.Controls;
//using System.Linq; // Needed for Properties().OrderBy
using Newtonsoft.Json.Linq; // Needed for JObject
using System.IO;    // Need for read/write JSON settings file
using SimHub;
//using SimHub.Plugins.InputPlugins;
using System.Net;
using System.Collections.Generic;
using SimHub.Plugins.InputPlugins;   // Needed for Logging

using System.Collections.ObjectModel;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Newtonsoft.Json;
using SimHub.Plugins.Resources;
using WoteverCommon;
using WoteverCommon.Extensions;
using WoteverLocalization;
using static WoteverCommon.JsonExtensions;
using static System.Net.Mime.MediaTypeNames;
using SimHub.Plugins.OutputPlugins.Dash.WPFUI;
using System.Xml.Linq;
using SimHub.Plugins.Devices.DevicesExtensionsDummy;
using System.Windows.Markup;
using SimHub.Plugins.DataPlugins.DataCore;
using System.Linq.Expressions;
using System.Windows.Documents;
using SimHub.Plugins.OutputPlugins.Dash.GLCDTemplating;
using SimHub.Plugins.Devices.UI;

namespace Redadeg.lmuDataPlugin
{
    [PluginName("Redadeg LMU Data plugin")]
    [PluginDescription("Plugin for Redadeg Dashboards \nWorks for LMU")]
    [PluginAuthor("Bobokhidze T.B.")]

    //the class name is used as the property headline name in SimHub "Available Properties"
    public class lmuDataPlugin : IPlugin, IDataPlugin, IWPFSettings
    {

        private Thread lmu_extendedThread;

        private SettingsControl settingsControlwpf;

        private CancellationTokenSource cts = new CancellationTokenSource();
        private CancellationTokenSource ctsExt = new CancellationTokenSource();

        public bool IsEnded { get; private set; }

        public PluginManager PluginManager { get; set; }

        public bool StopUpdate;
        public int Priority => 1;

        //input variables
        private string curGame;
        private bool GameInMenu = true;
        private bool GameRunning = true;
        private bool GamePaused = false;
        //private JoystickManagerSlimDX gamepadinput;
        //private string CarModel = "";

        //private float[] TyreRPS = new float[] { 0f, 0f, 0f, 0f };
        int[] lapsForCalculate = new int[] { };
        //private JObject JSONdata_diameters;
        private bool isHybrid = false;
        private bool isDamaged = false;
        private bool isStopAndGo = false;
        private bool haveDriverMenu = false;
        private Guid SessionId;
        //output variables
        private float[] TyreDiameter = new float[] { 0f, 0f, 0f, 0f };   // in meter - FL,FR,RL,RR
        private float[] LngWheelSlip = new float[] { 0f, 0f, 0f, 0f }; // Longitudinal Wheel Slip values FL,FR,RL,RR
        
        private List<double> LapTimes = new List<double>();
        private List<int> EnergyConsuptions = new List<int>();

        //private double energy_AverageConsumptionPer5Lap;
        private int energy_LastLapEnergy = 0;
        private int energy_CurrentIndex = 0;
        private int IsInPit = -1;
        private Guid LastLapId = new Guid();
        
        private int energyPerLastLapRealTime = 0;
        private TimeSpan outFromPitTime = TimeSpan.FromSeconds(0);
        private bool OutFromPitFlag = false;
        private TimeSpan InToPitTime = TimeSpan.FromSeconds(0);
        private bool InToPitFlag = false;
        private int pitStopUpdatePause = -1;
        JObject pitMenuH;
        JObject JSONdata;

        MappedBuffer<LMU_Extended> extendedBuffer = new MappedBuffer<LMU_Extended>(LMU_Constants.MM_EXTENDED_FILE_NAME, false /*partial*/, true /*skipUnchanged*/);
        MappedBuffer<rF2Scoring> scoringBuffer = new MappedBuffer<rF2Scoring>(LMU_Constants.MM_SCORING_FILE_NAME, true /*partial*/, true /*skipUnchanged*/);
        MappedBuffer<rF2Rules> rulesBuffer = new MappedBuffer<rF2Rules>(LMU_Constants.MM_RULES_FILE_NAME, true /*partial*/, true /*skipUnchanged*/);

        LMU_Extended lmu_extended;
        rF2Scoring scoring;
        rF2Rules rules;

        bool lmu_extended_connected = false;
        bool rf2_score_connected = false;
            

        private void ComputeEnergyData(int CurrentLap, double CurrentLapTime, int pitState ,bool IsLapValid, PluginManager pluginManager)
        {
           // pluginManager.SetPropertyValue("georace.lmu.NewLap", this.GetType(), CurrentLap + " - PitState " + pitState);
           

            //if (pitState > 0)
            //{
            //    energy_LastLapEnergy = currentVirtualEnergy;
            // pluginManager.SetPropertyValue("georace.lmu.CurrentLapTimeDifOldNew", this.GetType(), energy_LastLapEnergy + " - 112" + LMURepairAndRefuelData.currentVirtualEnergy);
            //}
             
            if (energy_LastLapEnergy > LMURepairAndRefuelData.currentVirtualEnergy)
            {
                int energyPerLastLapRaw = energy_LastLapEnergy - LMURepairAndRefuelData.currentVirtualEnergy;
               
                if (OutFromPitFlag) energyPerLastLapRaw = energyPerLastLapRealTime;
;

                //pluginManager.SetPropertyValue("georace.lmu.CurrentLapTimeDifOldNew", this.GetType(),  energyPerLastLapRaw);

                if ((pitState != CurrentLap && IsLapValid) || OutFromPitFlag || InToPitFlag)
                {
                    IsInPit = -1;
                    if (LapTimes.Count < 5)
                    {
                        energy_CurrentIndex++;
                        LapTimes.Add(CurrentLapTime);
                        EnergyConsuptions.Add(energyPerLastLapRaw);
    
                    }
                    else if (LapTimes.Count == 5)
                    {
                        energy_CurrentIndex++;
                        if (energy_CurrentIndex > 4) energy_CurrentIndex = 0;
                        LapTimes[energy_CurrentIndex] = CurrentLapTime;
                        EnergyConsuptions[energy_CurrentIndex] = energyPerLastLapRaw;
                    }
                }
                LMURepairAndRefuelData.energyPerLastLap = (double)(energyPerLastLapRaw);
                LMURepairAndRefuelData.energyPerLast5Lap = EnergyConsuptions.Average() / LMURepairAndRefuelData.maxVirtualEnergy;
                    
                    //pluginManager.SetPropertyValue("georace.lmu.CurrentLapTimeDifOldNew", this.GetType(), LapTimes.Average() + " - " + EnergyConsuptions.Average() / LMURepairAndRefuelData.maxVirtualEnergy);
                }
         

            energy_LastLapEnergy = LMURepairAndRefuelData.currentVirtualEnergy;
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            //curGame = pluginManager.GetPropertyValue("DataCorePlugin.CurrentGame").ToString();
            curGame = data.GameName;
            GameInMenu = data.GameInMenu;
            GameRunning = data.GameRunning;
            GamePaused = data.GamePaused;

            if (data.GameRunning && !data.GameInMenu && !data.GamePaused && !StopUpdate)
            {
                
                if ( curGame == "LMU")   //TODO: check a record where the game was captured from startup on
                {

                   
                    
                    try
                    {
                        WebClient wc = new WebClient();

                        JSONdata = JObject.Parse(wc.DownloadString("http://localhost:6397/rest/garage/UIScreen/RepairAndRefuel"));
                        JObject TireMagagementJSONdata = JObject.Parse(wc.DownloadString("http://localhost:6397/rest/garage/UIScreen/TireManagement"));
                        JObject GameStateJSONdata = JObject.Parse(wc.DownloadString("http://localhost:6397/rest/sessions/GetGameState"));
                      //  JObject StandingsJSONdata = JObject.Parse(wc.DownloadString(" http://localhost:6397/rest/garage/UIScreen/Standings"));
                   




                        JObject fuelInfo = JObject.Parse(JSONdata["fuelInfo"].ToString());
                        JObject pitStopLength = JObject.Parse(JSONdata["pitStopLength"].ToString());

                        if (pitStopUpdatePause == -1)
                        {
                            pitMenuH = JObject.Parse(JSONdata["pitMenu"].ToString());
                        }
                        else
                        {
                            if (pitStopUpdatePause == 0) // Update pit data if pitStopUpdatePauseCounter is 0
                            {
                                //wc.Headers[HttpRequestHeader.ContentType] = "application/json";
                                //string HtmlResult = wc.UploadString("http://localhost:6397/rest/garage/PitMenu/loadPitMenu", pitMenuH["pitMenu"].ToString());
                                pitStopUpdatePause = -1;
                            }
                            pitStopUpdatePause--;
                        }


                        JObject tireInventory = JObject.Parse(TireMagagementJSONdata["tireInventory"].ToString());
                       // JObject Standings = JObject.Parse(StandingsJSONdata["standings"].ToString());

                        LMURepairAndRefuelData.maxAvailableTires = tireInventory["maxAvailableTires"] != null ?(int)tireInventory["maxAvailableTires"]:0;
                        LMURepairAndRefuelData.newTires = tireInventory["newTires"] != null ? (int)tireInventory["newTires"]: 0;

                        LMURepairAndRefuelData.currentBattery = fuelInfo["currentBattery"] != null ? (int)fuelInfo["currentBattery"]: 0;
                        LMURepairAndRefuelData.currentFuel = fuelInfo["currentFuel"] != null ? (int)fuelInfo["currentFuel"] : 0;
                        LMURepairAndRefuelData.timeOfDay = GameStateJSONdata["timeOfDay"] != null ? (double)GameStateJSONdata["timeOfDay"]: 0;
                        try
                        {
                            JObject InfoForEventJSONdata = JObject.Parse(wc.DownloadString("http://localhost:6397/rest/sessions/GetSessionsInfoForEvent"));
                            JObject scheduledSessions = JObject.Parse(InfoForEventJSONdata.ToString());

                            foreach (JObject Sesstions in scheduledSessions["scheduledSessions"])
                            {
                                if (Sesstions["name"].ToString().ToUpper().Equals(data.NewData.SessionTypeName.ToUpper())) LMURepairAndRefuelData.rainChance = Sesstions["rainChance"] != null ? (int)Sesstions["rainChance"] : 0;

                            }
                        }
                        catch
                        { 
                        }


                        try
                        {
                            LMURepairAndRefuelData.currentVirtualEnergy = (int)fuelInfo["currentVirtualEnergy"];
                            LMURepairAndRefuelData.maxVirtualEnergy = (int)fuelInfo["maxVirtualEnergy"];
                        }
                        catch
                        {
                            LMURepairAndRefuelData.currentVirtualEnergy = 0;
                            LMURepairAndRefuelData.maxVirtualEnergy = 0;
                        }
                       
                        LMURepairAndRefuelData.maxBattery = (int)fuelInfo["maxBattery"];
                        LMURepairAndRefuelData.maxFuel = (int)fuelInfo["maxFuel"];
                       
                        LMURepairAndRefuelData.pitStopLength = (int)pitStopLength["timeInSeconds"];
                        haveDriverMenu = false;
                        isStopAndGo = false;
                        isDamaged = false;

                        //pitStopUpdatePause area
                        int idx = 0;
                        foreach (JObject PMCs in pitMenuH["pitMenu"])
                        {



                            if ((int)PMCs["PMC Value"] == 0)
                            {
                                LMURepairAndRefuelData.passStopAndGo = PMCs["settings"][(int)PMCs["currentSetting"]]["text"].ToString();
                                isStopAndGo = true;
                            }

                            if ((int)PMCs["PMC Value"] == 1)
                            {
                                if (idx == 0)
                                {
                                    isStopAndGo = false;
                                    LMURepairAndRefuelData.passStopAndGo = "";
                                }
                                LMURepairAndRefuelData.RepairDamage = PMCs["settings"][(int)PMCs["currentSetting"]]["text"].ToString();
                                if (LMURepairAndRefuelData.RepairDamage.Equals("N/A"))
                                { isDamaged = false; }
                                else
                                {
                                    isDamaged = true;
                                }

                            }
                            if ((int)PMCs["PMC Value"] == 4)
                            {
                                LMURepairAndRefuelData.Driver = PMCs["settings"][(int)PMCs["currentSetting"]]["text"].ToString();
                                haveDriverMenu = true;
                            }
                            int Virtual_Energy = 0;
                            try
                           
                            {


                                if ((int)PMCs["PMC Value"] == 5)
                                {
                                    LMURepairAndRefuelData.addVirtualEnergy = PMCs["settings"][(int)PMCs["currentSetting"]]["text"].ToString();
                                    Virtual_Energy = (int)PMCs["currentSetting"];
                                    pluginManager.SetPropertyValue("georace.lmu.Virtual_Energy", this.GetType(), Virtual_Energy);
                                         }
                                if ((int)PMCs["PMC Value"] == 6)
                                {
                                    if (PMCs["name"].ToString().Equals("FUEL:"))
                                    {
                                        LMURepairAndRefuelData.addFuel = PMCs["settings"][(int)PMCs["currentSetting"]]["text"].ToString();
                                        isHybrid = false;
                                    }
                                    else
                                    {
                                        LMURepairAndRefuelData.FuelRatio = (double)PMCs["settings"][(int)PMCs["currentSetting"]]["text"];
                                        LMURepairAndRefuelData.addFuel = string.Format("{0:f1}", LMURepairAndRefuelData.FuelRatio * Virtual_Energy) + "L" + LMURepairAndRefuelData.addVirtualEnergy.Split('%')[1];
                                        isHybrid = true;
                                    }
                                }
                            }
                            catch
                            {
                                LMURepairAndRefuelData.FuelRatio = 0;
                            }


                            try
                            {
                                if ((int)PMCs["PMC Value"] == 32)
                                {
                                    LMURepairAndRefuelData.Grille = PMCs["settings"][(int)PMCs["currentSetting"]]["text"].ToString();
                                }
                                if ((int)PMCs["PMC Value"] == 30)
                                {
                                    LMURepairAndRefuelData.Wing = PMCs["settings"][(int)PMCs["currentSetting"]]["text"].ToString();

                                }
                            }
                            catch
                            {
                            }

                            if ((int)PMCs["PMC Value"] == 12)
                            {
                                LMURepairAndRefuelData.fl_TyreChange = PMCs["settings"][(int)PMCs["currentSetting"]]["text"].ToString();

                            }
                            if ((int)PMCs["PMC Value"] == 13)
                            {
                                LMURepairAndRefuelData.fr_TyreChange = PMCs["settings"][(int)PMCs["currentSetting"]]["text"].ToString();

                            }
                            if ((int)PMCs["PMC Value"] == 14)
                            {
                                LMURepairAndRefuelData.rl_TyreChange = PMCs["settings"][(int)PMCs["currentSetting"]]["text"].ToString();
          
                            }
                            if ((int)PMCs["PMC Value"] == 15)
                            {
                                LMURepairAndRefuelData.rr_TyreChange = PMCs["settings"][(int)PMCs["currentSetting"]]["text"].ToString();

                            }

                            if ((int)PMCs["PMC Value"] == 35)
                            {
                                LMURepairAndRefuelData.fl_TyrePressure = PMCs["settings"][(int)PMCs["currentSetting"]]["text"].ToString();
      
                            }
                            if ((int)PMCs["PMC Value"] == 36)
                            {
                                LMURepairAndRefuelData.fr_TyrePressure = PMCs["settings"][(int)PMCs["currentSetting"]]["text"].ToString();

                            }
                            if ((int)PMCs["PMC Value"] == 37)
                            {
                                LMURepairAndRefuelData.rl_TyrePressure = PMCs["settings"][(int)PMCs["currentSetting"]]["text"].ToString();
      
                            }
                            if ((int)PMCs["PMC Value"] == 38)
                            {
                                LMURepairAndRefuelData.rr_TyrePressure = PMCs["settings"][(int)PMCs["currentSetting"]]["text"].ToString();

                            }

                            if ((int)PMCs["PMC Value"] == 43)
                            {
                                LMURepairAndRefuelData.replaceBrakes = PMCs["settings"][(int)PMCs["currentSetting"]]["text"].ToString();

                            }
                            idx++;
                        }


                        if (isStopAndGo)
                        {
                            pluginManager.AddProperty("Redadeg.lmu.isStopAndGo", this.GetType(), 1);
                        }
                        else
                        {
                            pluginManager.AddProperty("Redadeg.lmu.isStopAndGo", this.GetType(), 0);
                        }
                       
                        if (isHybrid)
                        {
                            pluginManager.SetPropertyValue("Redadeg.lmu.isHyper", this.GetType(), 1);
                        }
                        else
                        {
                            pluginManager.SetPropertyValue("Redadeg.lmu.isHyper", this.GetType(), 0);
                        }

                        if (isDamaged)
                        {
                            pluginManager.SetPropertyValue("Redadeg.lmu.isDamage", this.GetType(), 1);
                        }
                        else
                        {
                            pluginManager.SetPropertyValue("Redadeg.lmu.isDamage", this.GetType(), 0);
                        }
                        if (haveDriverMenu)
                        {
                            pluginManager.SetPropertyValue("Redadeg.lmu.haveDriverMenu", this.GetType(), 1);
                        }
                        else
                        {
                            pluginManager.SetPropertyValue("Redadeg.lmu.haveDriverMenu", this.GetType(), 0);
                        }
                        

                        //data.NewData.SessionOdo < 50 || 
                        if (data.OldData.SessionTypeName != data.NewData.SessionTypeName || data.OldData.IsSessionRestart != data.NewData.IsSessionRestart || !data.SessionId.Equals(SessionId))
                        {
                            SessionId = data.SessionId;
                            Logging.Current.Info("SectorChange: " + data.OldData.IsSessionRestart.ToString() + " - " + data.NewData.IsSessionRestart.ToString());
                            LapTimes.Clear();
                            EnergyConsuptions.Clear();
                            energy_LastLapEnergy = 0;
                            energy_CurrentIndex = 0;
                            LMURepairAndRefuelData.energyPerLast5Lap = 0;
                            LMURepairAndRefuelData.energyPerLastLap = 0;
                            LMURepairAndRefuelData.energyTimeElapsed = 0;


                            LMURepairAndRefuelData.mPlayerBestSector1 = 0;
                            LMURepairAndRefuelData.mPlayerBestSector2 = 0;
                            LMURepairAndRefuelData.mPlayerBestSector3 = 0;

                            LMURepairAndRefuelData.mPlayerCurSector1 = 0;
                            LMURepairAndRefuelData.mPlayerCurSector2 = 0;
                            LMURepairAndRefuelData.mPlayerCurSector3 = 0;

                            LMURepairAndRefuelData.mSessionBestSector1 = 0;
                            LMURepairAndRefuelData.mSessionBestSector2 = 0;
                            LMURepairAndRefuelData.mSessionBestSector3 = 0;

                            LMURepairAndRefuelData.mPlayerBestLapTime = 0;
                            LMURepairAndRefuelData.mPlayerBestLapSector1 = 0;
                            LMURepairAndRefuelData.mPlayerBestLapSector2 = 0;
                            LMURepairAndRefuelData.mPlayerBestLapSector3 = 0;

                            scoringBuffer.ClearStats();
                        }
                     

                        if (isHybrid)
                        {

                            if (energy_LastLapEnergy == 0)
                            {
                                energy_LastLapEnergy = LMURepairAndRefuelData.currentVirtualEnergy;
                            }
                            string mPitStatus = "0";
                            try
                            {
                                mPitStatus = pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.CurrentPlayer.mPitState").ToString(); }
                            catch { }

                            pluginManager.SetPropertyValue("Redadeg.lmu.CurrentLapTimeDifOldNew", this.GetType(), mPitStatus + " SetPit Int " + data.NewData.IsInPitSince);

                            if (!mPitStatus.Contains("4") && !mPitStatus.Contains("5")) energyPerLastLapRealTime = energy_LastLapEnergy - LMURepairAndRefuelData.currentVirtualEnergy;
                            pluginManager.SetPropertyValue("Redadeg.lmu.energyPerLastLapRealTime", this.GetType(), energyPerLastLapRealTime);


                            if (mPitStatus.Contains("4") || mPitStatus.Contains("5"))
                            {
                                pluginManager.SetPropertyValue("Redadeg.lmu.CurrentLapTimeDifOldNew", this.GetType(), mPitStatus + " In Pit Lane Remot " + data.NewData.IsInPitSince);
                                energy_LastLapEnergy = LMURepairAndRefuelData.currentVirtualEnergy;
                            }

                            if (data.OldData.IsInPit > 0) IsInPit = data.OldData.CurrentLap;
                           
                          

                         
                            if (data.OldData.IsInPit > data.NewData.IsInPit)
                            {
                                OutFromPitFlag = true;
                                outFromPitTime = data.NewData.CurrentLapTime;
                                //   pluginManager.SetPropertyValue("Redadeg.lmu.CurrentLapTimeDifOldNew", this.GetType(), outFromPitTime.ToString() + " SetPit Out " + data.NewData.IsInPit.ToString());
                            }


                            if (data.OldData.IsInPit < data.NewData.IsInPit)
                            {
                                InToPitFlag = true;
                                InToPitTime = data.NewData.CurrentLapTime;
                                //  pluginManager.SetPropertyValue("Redadeg.lmu.CurrentLapTimeDifOldNew", this.GetType(), InToPitTime + " SetPit Int " + data.NewData.IsInPit.ToString());
                            }
                            if (data.OldData.CurrentLap < data.NewData.CurrentLap)
                            {
                                if (OutFromPitFlag && InToPitFlag)
                                {
                                    ComputeEnergyData(data.OldData.CurrentLap, InToPitTime.TotalSeconds - outFromPitTime.TotalSeconds, IsInPit, data.OldData.IsLapValid, pluginManager);
                                    // pluginManager.SetPropertyValue("Redadeg.lmu.CurrentLapTimeDifOldNew", this.GetType(), data.OldData.IsInPit.ToString() + " OutFromPitFlag_1 " + data.NewData.IsInPit.ToString());
                                    OutFromPitFlag = false;
                                    InToPitFlag = false;
                                }
                                else if (OutFromPitFlag)
                                {
                                    ComputeEnergyData(data.OldData.CurrentLap, data.OldData.CurrentLapTime.TotalSeconds - outFromPitTime.TotalSeconds, IsInPit, data.OldData.IsLapValid, pluginManager);
                                    //pluginManager.SetPropertyValue("Redadeg.lmu.CurrentLapTimeDifOldNew", this.GetType(), data.OldData.IsInPit.ToString() + " OutFromPitFlag_2 " + data.NewData.IsInPit.ToString());

                                }
                                else
                                {
                                    ComputeEnergyData(data.OldData.CurrentLap, InToPitFlag ? InToPitTime.TotalSeconds : data.OldData.CurrentLapTime.TotalSeconds, IsInPit, data.OldData.IsLapValid, pluginManager);
                                    // pluginManager.SetPropertyValue("Redadeg.lmu.CurrentLapTimeDifOldNew", this.GetType(), data.OldData.IsInPit.ToString() + " cLEARlAP " + data.NewData.IsInPit.ToString());
                                }
                                OutFromPitFlag = false;
                                InToPitFlag = false;
                                outFromPitTime = TimeSpan.FromSeconds(0);
                                InToPitTime = TimeSpan.FromSeconds(0);
                                if (mPitStatus.Contains("4") || mPitStatus.Contains("5")) energy_LastLapEnergy = LMURepairAndRefuelData.currentVirtualEnergy;
                            }
                            else if (LastLapId != data.LapId)
                            {
                                if (OutFromPitFlag && InToPitFlag)
                                {
                                    ComputeEnergyData(data.OldData.CurrentLap, InToPitTime.TotalSeconds - outFromPitTime.TotalSeconds, IsInPit, data.OldData.IsLapValid, pluginManager);
                                    // pluginManager.SetPropertyValue("Redadeg.lmu.CurrentLapTimeDifOldNew", this.GetType(), data.OldData.IsInPit.ToString() + " OutFromPitFlag_1 " + data.NewData.IsInPit.ToString());
                                    OutFromPitFlag = false;
                                    InToPitFlag = false;
                                    outFromPitTime = TimeSpan.FromSeconds(0);
                                    InToPitTime = TimeSpan.FromSeconds(0);
                                } else if (OutFromPitFlag)
                                {
                                    ComputeEnergyData(data.OldData.CurrentLap, data.OldData.CurrentLapTime.TotalSeconds - outFromPitTime.TotalSeconds, IsInPit, data.OldData.IsLapValid, pluginManager);
                                    // pluginManager.SetPropertyValue("Redadeg.lmu.CurrentLapTimeDifOldNew", this.GetType(), data.OldData.IsInPit.ToString() + " OutFromPitFlag_1 " + data.NewData.IsInPit.ToString());
                                    OutFromPitFlag = false;
                                    InToPitFlag = false;
                                    outFromPitTime = TimeSpan.FromSeconds(0);
                                    InToPitTime = TimeSpan.FromSeconds(0);
                                }
                              
                                energy_LastLapEnergy = LMURepairAndRefuelData.currentVirtualEnergy;
                                //ComputeEnergyData(data.OldData.CurrentLap, data.OldData.CurrentLapTime.TotalSeconds, IsInPit, LapOldNew, data.OldData.IsLapValid, pluginManager);

                            }
                           

                            LastLapId = data.LapId;

                            pluginManager.SetPropertyValue("Redadeg.lmu.energyPerLast5Lap", this.GetType(), LMURepairAndRefuelData.energyPerLast5Lap);
                            pluginManager.SetPropertyValue("Redadeg.lmu.energyPerLastLap", this.GetType(), LMURepairAndRefuelData.energyPerLastLap);

                            if (OutFromPitFlag)
                            {
                                
                            }

                            if (EnergyConsuptions.Count() > 0 && LapTimes.Count() > 0)
                            {
                                LMURepairAndRefuelData.energyTimeElapsed = LapTimes.Average() * LMURepairAndRefuelData.currentVirtualEnergy / EnergyConsuptions.Average(); 
                            }
                            else
                            {
                                //real time calculation
                                double energyLapsRealTimeElapsed = data.OldData.TrackPositionPercent * (double)LMURepairAndRefuelData.currentVirtualEnergy / (double)energyPerLastLapRealTime;
                                LMURepairAndRefuelData.energyTimeElapsed = (double)LMURepairAndRefuelData.currentVirtualEnergy * (double)(data.OldData.CurrentLapTime.TotalSeconds - outFromPitTime.TotalSeconds) / (double)energyPerLastLapRealTime;
                                pluginManager.SetPropertyValue("Redadeg.lmu.energyLapsRealTimeElapsed", this.GetType(), energyLapsRealTimeElapsed);
                            }
                            pluginManager.SetPropertyValue("Redadeg.lmu.energyTimeElapsed", this.GetType(), LMURepairAndRefuelData.energyTimeElapsed);
                          


                        }

                        if (!this.rf2_score_connected)
                        {
                            try
                            {
                                // Extended buffer is the last one constructed, so it is an indicator RF2SM is ready.
                                this.scoringBuffer.Connect();
                                this.rf2_score_connected = true;
                            }
                            catch (Exception)
                            {

                                this.rf2_score_connected = false;
                                LMURepairAndRefuelData.mPlayerBestSector1 = 0;
                                LMURepairAndRefuelData.mPlayerBestSector2 = 0;
                                LMURepairAndRefuelData.mPlayerBestSector3 = 0;

                                LMURepairAndRefuelData.mPlayerCurSector1 = 0;
                                LMURepairAndRefuelData.mPlayerCurSector2 = 0;
                                LMURepairAndRefuelData.mPlayerCurSector3 = 0;

                                LMURepairAndRefuelData.mSessionBestSector1 = 0;
                                LMURepairAndRefuelData.mSessionBestSector2 = 0;
                                LMURepairAndRefuelData.mSessionBestSector3 = 0;

                                LMURepairAndRefuelData.mPlayerBestLapTime = 0;
                                LMURepairAndRefuelData.mPlayerBestLapSector1 = 0;
                                LMURepairAndRefuelData.mPlayerBestLapSector2 = 0;
                                LMURepairAndRefuelData.mPlayerBestLapSector3 = 0;
                                //Logging.Current.Info("Extended data update service not connectded.");
                            }
                        }
                        //Calc current times
                        if (data.OldData.CurrentSectorIndex != data.NewData.CurrentSectorIndex)
                        {


                            if (this.rf2_score_connected)
                            {
                                scoringBuffer.GetMappedData(ref scoring);
                                rF2VehicleScoring playerScoring = GetPlayerScoring(ref this.scoring);

                                //double mPlayerLastSector1 = 0.0;
                                //double mPlayerLastSector2 = 0.0;
                                //double mPlayerLastSector3 = 0.0;

                                double mSessionBestSector1 = 0.0;
                                double mSessionBestSector2 = 0.0;
                                double mSessionBestSector3 = 0.0;

                                List<rF2VehicleScoring> OpenentsScoring = GetOpenentsScoring(ref this.scoring);
                                foreach (rF2VehicleScoring OpenentScore in OpenentsScoring)
                                {
                                    if (GetStringFromBytes(playerScoring.mVehicleClass).Equals(GetStringFromBytes(OpenentScore.mVehicleClass)))
                                    {

                                        if (OpenentScore.mCurSector1 > 0) mSessionBestSector1 = OpenentScore.mCurSector1;

                                        if (LMURepairAndRefuelData.mSessionBestSector1 == 0 && OpenentScore.mBestLapSector1 > 0)
                                        {
                                            LMURepairAndRefuelData.mSessionBestSector1 = OpenentScore.mBestLapSector1;
                                        }

                                        if (OpenentScore.mCurSector1 > 0 && OpenentScore.mCurSector2 > 0) mSessionBestSector2 = OpenentScore.mCurSector2 - OpenentScore.mCurSector1;

                                        if (LMURepairAndRefuelData.mSessionBestSector2 == 0 && OpenentScore.mBestLapSector2 > 0 && OpenentScore.mBestLapSector1 > 0)
                                        {
                                            LMURepairAndRefuelData.mSessionBestSector2 = OpenentScore.mBestLapSector2 - OpenentScore.mBestLapSector1;
                                        }

                                        if (OpenentScore.mCurSector2 > 0 && OpenentScore.mLastLapTime > 0) mSessionBestSector3 = OpenentScore.mLastLapTime - OpenentScore.mCurSector2;

                                        if (LMURepairAndRefuelData.mSessionBestSector3 == 0 && OpenentScore.mBestLapTime > 0 && OpenentScore.mBestLapSector2 > 0)
                                        {
                                            LMURepairAndRefuelData.mSessionBestSector3 = OpenentScore.mBestLapTime - OpenentScore.mBestLapSector2;
                                        }


                                        if (LMURepairAndRefuelData.mSessionBestSector1 > mSessionBestSector1 && mSessionBestSector1 > 0) LMURepairAndRefuelData.mSessionBestSector1 = mSessionBestSector1;
                                        if (LMURepairAndRefuelData.mSessionBestSector2 > mSessionBestSector2 && mSessionBestSector2 > 0) LMURepairAndRefuelData.mSessionBestSector2 = mSessionBestSector2;
                                        if (LMURepairAndRefuelData.mSessionBestSector3 > mSessionBestSector3 && mSessionBestSector3 > 0) LMURepairAndRefuelData.mSessionBestSector3 = mSessionBestSector3;

                                    }
                                }
                            }
                            //Logging.Current.Info("SectorChange: " + data.OldData.CurrentSectorIndex.ToString() + " - " + data.NewData.CurrentSectorIndex.ToString());

                            if (data.NewData.Sector1Time.HasValue) LMURepairAndRefuelData.mPlayerCurSector1 = data.NewData.Sector1Time.Value.TotalSeconds;
                            //Logging.Current.Info("Print sector 1: " + data.OldData.Sector1Time.Value.TotalSeconds.ToString() + " - " + data.NewData.Sector1Time.Value.TotalSeconds.ToString());

                        if (data.NewData.Sector2Time.HasValue) LMURepairAndRefuelData.mPlayerCurSector2 = data.NewData.Sector2Time.Value.TotalSeconds;
                        if (data.NewData.Sector3LastLapTime.HasValue) LMURepairAndRefuelData.mPlayerCurSector3 = data.NewData.Sector3LastLapTime.Value.TotalSeconds;

                        if ((LMURepairAndRefuelData.mPlayerBestSector1 > LMURepairAndRefuelData.mPlayerCurSector1 || LMURepairAndRefuelData.mPlayerBestSector1 == 0) && LMURepairAndRefuelData.mPlayerCurSector1 > 0.0) LMURepairAndRefuelData.mPlayerBestSector1 = LMURepairAndRefuelData.mPlayerCurSector1;
                        if ((LMURepairAndRefuelData.mPlayerBestSector2 > LMURepairAndRefuelData.mPlayerCurSector2 || LMURepairAndRefuelData.mPlayerBestSector2 == 0) && LMURepairAndRefuelData.mPlayerCurSector2 > 0.0) LMURepairAndRefuelData.mPlayerBestSector2 = LMURepairAndRefuelData.mPlayerCurSector2;
                        if ((LMURepairAndRefuelData.mPlayerBestSector3 > LMURepairAndRefuelData.mPlayerCurSector3 || LMURepairAndRefuelData.mPlayerBestSector3 == 0) && LMURepairAndRefuelData.mPlayerCurSector3 > 0.0) LMURepairAndRefuelData.mPlayerBestSector3 = LMURepairAndRefuelData.mPlayerCurSector3;

                        LMURepairAndRefuelData.mPlayerBestLapTime = data.NewData.BestLapTime.TotalSeconds > 0 ? data.NewData.BestLapTime.TotalSeconds : 0;
                        LMURepairAndRefuelData.mPlayerBestLapSector1 = data.NewData.Sector1BestLapTime.Value.TotalSeconds > 0 ? data.NewData.Sector1BestLapTime.Value.TotalSeconds : 0;
                        LMURepairAndRefuelData.mPlayerBestLapSector2 = data.NewData.Sector2BestLapTime.Value.TotalSeconds > 0 ? data.NewData.Sector2BestLapTime.Value.TotalSeconds : 0;
                        LMURepairAndRefuelData.mPlayerBestLapSector3 = data.NewData.Sector3BestLapTime.Value.TotalSeconds > 0 ? data.NewData.Sector3BestLapTime.Value.TotalSeconds : 0;
                        }
                        //Calc current times end

                        pluginManager.SetPropertyValue("Redadeg.lmu.passStopAndGo", this.GetType(), LMURepairAndRefuelData.passStopAndGo);
                        pluginManager.SetPropertyValue("Redadeg.lmu.RepairDamage", this.GetType(), LMURepairAndRefuelData.RepairDamage);
                        pluginManager.SetPropertyValue("Redadeg.lmu.Driver", this.GetType(), LMURepairAndRefuelData.Driver);
                        pluginManager.SetPropertyValue("Redadeg.lmu.rainChance", this.GetType(), LMURepairAndRefuelData.rainChance);
                        pluginManager.SetPropertyValue("Redadeg.lmu.timeOfDay", this.GetType(), LMURepairAndRefuelData.timeOfDay);
                        pluginManager.SetPropertyValue("Redadeg.lmu.FuelRatio", this.GetType(), LMURepairAndRefuelData.FuelRatio);
                        pluginManager.SetPropertyValue("Redadeg.lmu.currentFuel", this.GetType(), LMURepairAndRefuelData.currentFuel);
                        pluginManager.SetPropertyValue("Redadeg.lmu.addFuel", this.GetType(), LMURepairAndRefuelData.addFuel);
                        pluginManager.SetPropertyValue("Redadeg.lmu.addVirtualEnergy", this.GetType(), LMURepairAndRefuelData.addVirtualEnergy);
                        pluginManager.SetPropertyValue("Redadeg.lmu.Wing", this.GetType(), LMURepairAndRefuelData.Wing);
                        pluginManager.SetPropertyValue("Redadeg.lmu.Grille", this.GetType(), LMURepairAndRefuelData.Grille);

                        pluginManager.SetPropertyValue("Redadeg.lmu.maxAvailableTires", this.GetType(), LMURepairAndRefuelData.maxAvailableTires);
                        pluginManager.SetPropertyValue("Redadeg.lmu.newTires", this.GetType(), LMURepairAndRefuelData.newTires);

                        pluginManager.SetPropertyValue("Redadeg.lmu.currentBattery", this.GetType(), LMURepairAndRefuelData.currentBattery);
                        pluginManager.SetPropertyValue("Redadeg.lmu.currentVirtualEnergy", this.GetType(), LMURepairAndRefuelData.currentVirtualEnergy);
                        pluginManager.SetPropertyValue("Redadeg.lmu.pitStopLength", this.GetType(), LMURepairAndRefuelData.pitStopLength);

                        pluginManager.SetPropertyValue("Redadeg.lmu.fl_TyreChange", this.GetType(), LMURepairAndRefuelData.fl_TyreChange);
                        pluginManager.SetPropertyValue("Redadeg.lmu.fr_TyreChange", this.GetType(), LMURepairAndRefuelData.fr_TyreChange);
                        pluginManager.SetPropertyValue("Redadeg.lmu.rl_TyreChange", this.GetType(), LMURepairAndRefuelData.rl_TyreChange);
                        pluginManager.SetPropertyValue("Redadeg.lmu.rr_TyreChange", this.GetType(), LMURepairAndRefuelData.rr_TyreChange);

                        pluginManager.SetPropertyValue("Redadeg.lmu.fl_TyrePressure", this.GetType(), LMURepairAndRefuelData.fl_TyrePressure);
                        pluginManager.SetPropertyValue("Redadeg.lmu.fr_TyrePressure", this.GetType(), LMURepairAndRefuelData.fr_TyrePressure);
                        pluginManager.SetPropertyValue("Redadeg.lmu.rl_TyrePressure", this.GetType(), LMURepairAndRefuelData.rl_TyrePressure);
                        pluginManager.SetPropertyValue("Redadeg.lmu.rr_TyrePressure", this.GetType(), LMURepairAndRefuelData.rr_TyrePressure);
                        pluginManager.SetPropertyValue("Redadeg.lmu.replaceBrakes", this.GetType(), LMURepairAndRefuelData.replaceBrakes);

                        pluginManager.SetPropertyValue("Redadeg.lmu.maxBattery", this.GetType(), LMURepairAndRefuelData.maxBattery);
                        pluginManager.SetPropertyValue("Redadeg.lmu.maxFuel", this.GetType(), LMURepairAndRefuelData.maxFuel);
                        pluginManager.SetPropertyValue("Redadeg.lmu.maxVirtualEnergy", this.GetType(), LMURepairAndRefuelData.maxVirtualEnergy);


                        pluginManager.SetPropertyValue("Redadeg.lmu.Extended.Cuts", this.GetType(), LMURepairAndRefuelData.Cuts);
                        pluginManager.SetPropertyValue("Redadeg.lmu.Extended.CutsMax", this.GetType(), LMURepairAndRefuelData.CutsMax);
                        pluginManager.SetPropertyValue("Redadeg.lmu.Extended.PenaltyLeftLaps", this.GetType(), LMURepairAndRefuelData.PenaltyLeftLaps);
                        pluginManager.SetPropertyValue("Redadeg.lmu.Extended.PenaltyType", this.GetType(), LMURepairAndRefuelData.PenaltyType);
                        pluginManager.SetPropertyValue("Redadeg.lmu.Extended.PenaltyCount", this.GetType(), LMURepairAndRefuelData.PenaltyCount);
                        
                        pluginManager.SetPropertyValue("Redadeg.lmu.Extended.PendingPenaltyType1", this.GetType(), LMURepairAndRefuelData.mPendingPenaltyType1);
                        pluginManager.SetPropertyValue("Redadeg.lmu.Extended.PendingPenaltyType2", this.GetType(), LMURepairAndRefuelData.mPendingPenaltyType2);
                        pluginManager.SetPropertyValue("Redadeg.lmu.Extended.PendingPenaltyType3", this.GetType(), LMURepairAndRefuelData.mPendingPenaltyType3);

                        pluginManager.SetPropertyValue("Redadeg.lmu.Extended.TractionControl", this.GetType(), LMURepairAndRefuelData.mpTractionControl);
                        pluginManager.SetPropertyValue("Redadeg.lmu.Extended.BrakeMigration", this.GetType(), LMURepairAndRefuelData.mpBrakeMigration);
                        pluginManager.SetPropertyValue("Redadeg.lmu.Extended.BrakeMigrationMax", this.GetType(), LMURepairAndRefuelData.mpBrakeMigrationMax);

                        pluginManager.SetPropertyValue("Redadeg.lmu.mPlayerBestSector1", this.GetType(), LMURepairAndRefuelData.mPlayerBestSector1);
                        pluginManager.SetPropertyValue("Redadeg.lmu.mPlayerBestSector2", this.GetType(), LMURepairAndRefuelData.mPlayerBestSector2);
                        pluginManager.SetPropertyValue("Redadeg.lmu.mPlayerBestSector3", this.GetType(), LMURepairAndRefuelData.mPlayerBestSector3);

                        pluginManager.SetPropertyValue("Redadeg.lmu.mPlayerCurSector1", this.GetType(), LMURepairAndRefuelData.mPlayerCurSector1);
                        pluginManager.SetPropertyValue("Redadeg.lmu.mPlayerCurSector2", this.GetType(), LMURepairAndRefuelData.mPlayerCurSector2);
                        pluginManager.SetPropertyValue("Redadeg.lmu.mPlayerCurSector3", this.GetType(), LMURepairAndRefuelData.mPlayerCurSector3);

                        pluginManager.SetPropertyValue("Redadeg.lmu.mSessionBestSector1", this.GetType(), LMURepairAndRefuelData.mSessionBestSector1);
                        pluginManager.SetPropertyValue("Redadeg.lmu.mSessionBestSector2", this.GetType(), LMURepairAndRefuelData.mSessionBestSector2);
                        pluginManager.SetPropertyValue("Redadeg.lmu.mSessionBestSector3", this.GetType(), LMURepairAndRefuelData.mSessionBestSector3);

                        pluginManager.SetPropertyValue("Redadeg.lmu.mPlayerBestLapTime", this.GetType(), LMURepairAndRefuelData.mPlayerBestLapTime);
                        pluginManager.SetPropertyValue("Redadeg.lmu.mPlayerBestLapSector1", this.GetType(), LMURepairAndRefuelData.mPlayerBestLapSector1);
                        pluginManager.SetPropertyValue("Redadeg.lmu.mPlayerBestLapSector2", this.GetType(), LMURepairAndRefuelData.mPlayerBestLapSector2);
                        pluginManager.SetPropertyValue("Redadeg.lmu.mPlayerBestLapSector3", this.GetType(), LMURepairAndRefuelData.mPlayerBestLapSector3);

                        pluginManager.SetPropertyValue("Redadeg.lmu.Clock_Format24", this.GetType(), ButtonBindSettings.Clock_Format24);
                        pluginManager.SetPropertyValue("Redadeg.lmu.RealTimeClock", this.GetType(), ButtonBindSettings.RealTimeClock);

                        pluginManager.SetPropertyValue("Redadeg.lmu.Extended.MotorMap", this.GetType(),LMURepairAndRefuelData.mpMotorMap);
                        pluginManager.SetPropertyValue("Redadeg.lmu.Extended.RegenLevel", this.GetType(), LMURepairAndRefuelData.mpRegenLevel);
                        //DataCorePlugin.GameRawData.CurrentPlayerTelemetry.mRearBrakeBias
                        double mRearBrakeBias = 0.0;
                        try
                        { 
                            mRearBrakeBias = (double)pluginManager.GetPropertyValue("DataCorePlugin.GameRawData.CurrentPlayerTelemetry.mRearBrakeBias"); 
                        }
                        catch
                        { }

                        //if (lmu_extended_connected)
                        //{
                        //    pluginManager.SetPropertyValue("Redadeg.lmu.mMessage", this.GetType(), GetStringFromBytes( rules.mTrackRules.mMessage));

                        //}

                        isStopAndGo = false;
                        LMURepairAndRefuelData.passStopAndGo = "";
                    }
                    // if there is no settings file, use the following defaults
                    catch (Exception ex)
                    {
                        LMURepairAndRefuelData.currentBattery = 50;
                        LMURepairAndRefuelData.currentFuel = 0;
                        LMURepairAndRefuelData.currentVirtualEnergy = 5;
                        LMURepairAndRefuelData.maxBattery = 486000000;
                        LMURepairAndRefuelData.maxVirtualEnergy = 920000000;
                        LMURepairAndRefuelData.pitStopLength = 0;
                        Logging.Current.Info("Plugin Redadeg.lmuDataPlugin: " + ex.ToString());
                        
                    }
                 }
                StopUpdate = false;
            }
            
        }





        /// <summary>
        /// Called at plugin manager stop, close/displose anything needed here !
        /// </summary>
        /// <param name="pluginManager"></param>
        public void End(PluginManager pluginManager)
        {
            IsEnded = true;
            cts.Cancel();
            lmu_extendedThread.Join();
               // try to read complete data file from disk, compare file data with new data and write new file if there are diffs
            try
            {
                if (rf2_score_connected) this.scoringBuffer.Disconnect();
                if(lmu_extended_connected) this.extendedBuffer.Disconnect();
                if (lmu_extended_connected) this.rulesBuffer.Disconnect();
               
                //WebClient wc = new WebClient();
                //JObject JSONcurGameData = JObject.Parse(wc.DownloadString("http://localhost:6397/rest/garage/UIScreen/RepairAndRefuel"));

            }
            // if there is not already a settings file on disk, create new one and write data for current game
            catch (FileNotFoundException)
            {
                // try to write data file
               
            }
            // other errors like Syntax error on JSON parsing, data file will not be saved
            catch (Exception ex)
            {
                Logging.Current.Info("Plugin Redadeg.lmuDataPlugin - data file not saved. The following error occured: " + ex.Message);
            }
        }

        /// <summary>
        /// Return you winform settings control here, return null if no settings control
        /// 
        /// </summary>
        /// <param name="pluginManager"></param>
        /// <returns></returns>
        public System.Windows.Forms.Control GetSettingsControl(PluginManager pluginManager)
        {
            return null;
        }

        public  System.Windows.Controls.Control GetWPFSettingsControl(PluginManager pluginManager)
        {
            if (settingsControlwpf == null)
            {
                settingsControlwpf = new SettingsControl();
            }

            return settingsControlwpf;
        }

        private void LoadSettings(PluginManager pluginManager)
        {
            //IL_006a: Unknown result type (might be due to invalid IL or missing references)
            //IL_006f: Unknown result type (might be due to invalid IL or missing references)
            //IL_007c: Unknown result type (might be due to invalid IL or missing references)
            //IL_008e: Expected O, but got Unknown
           
        }


        private void lmu_extendedReadThread()
        {
            try
            {
                Task.Delay(500, cts.Token).Wait();
            
                while (!IsEnded)
                {
                    if (!this.lmu_extended_connected)
                    {
                        try
                        {
                            // Extended buffer is the last one constructed, so it is an indicator RF2SM is ready.
                            this.extendedBuffer.Connect();
                            this.rulesBuffer.Connect();
                            
                            this.lmu_extended_connected = true; 
                        }
                        catch (Exception)
                        {
                            LMURepairAndRefuelData.Cuts = 0;
                            LMURepairAndRefuelData.CutsMax = 0;
                            LMURepairAndRefuelData.PenaltyLeftLaps = 0;
                            LMURepairAndRefuelData.PenaltyType = 0;
                            LMURepairAndRefuelData.PenaltyCount = 0;
                            LMURepairAndRefuelData.mPendingPenaltyType1 = 0;
                            LMURepairAndRefuelData.mPendingPenaltyType2 = 0;
                            LMURepairAndRefuelData.mPendingPenaltyType3 = 0;
                            LMURepairAndRefuelData.mpBrakeMigration = 0;
                            LMURepairAndRefuelData.mpBrakeMigrationMax = 0;
                            LMURepairAndRefuelData.mpTractionControl = 0;
                            LMURepairAndRefuelData.mpMotorMap = "None";
                            LMURepairAndRefuelData.mpRegenLevel = "None";
                            this.lmu_extended_connected = false;
                           // Logging.Current.Info("Extended data update service not connectded.");
                        }
                    }
                    else
                    {
                        extendedBuffer.GetMappedData(ref lmu_extended);
                        rulesBuffer.GetMappedData(ref rules);
                        LMURepairAndRefuelData.Cuts = lmu_extended.mCuts;
                        LMURepairAndRefuelData.CutsMax = lmu_extended.mCutsPoints;
                        LMURepairAndRefuelData.PenaltyLeftLaps  = lmu_extended.mPenaltyLeftLaps;
                        LMURepairAndRefuelData.PenaltyType = lmu_extended.mPenaltyType;
                        LMURepairAndRefuelData.PenaltyCount = lmu_extended.mPenaltyCount;
                        LMURepairAndRefuelData.mPendingPenaltyType1 = lmu_extended.mPendingPenaltyType1;
                        LMURepairAndRefuelData.mPendingPenaltyType2 = lmu_extended.mPendingPenaltyType2;
                        LMURepairAndRefuelData.mPendingPenaltyType3 = lmu_extended.mPendingPenaltyType3;
                        LMURepairAndRefuelData.mpBrakeMigration = lmu_extended.mpBrakeMigration;
                        LMURepairAndRefuelData.mpBrakeMigrationMax = lmu_extended.mpBrakeMigrationMax;
                        LMURepairAndRefuelData.mpTractionControl = lmu_extended.mpTractionControl;
                        LMURepairAndRefuelData.mpMotorMap = GetStringFromBytes(lmu_extended.mpMotorMap);
                        LMURepairAndRefuelData.mpRegenLevel = GetStringFromBytes(lmu_extended.mpRegenLevel);



                       // Logging.Current.Info(("Extended data update service connectded. " +  lmu_extended.mCutsPoints.ToString() + " Penalty laps" + lmu_extended.mPenaltyLeftLaps).ToString());
                    }





                 Thread.Sleep(50);

                
                }

            }
            catch (AggregateException)
            {
                Logging.Current.Info(("AggregateException"));
            }
            catch (TaskCanceledException)
            {
                Logging.Current.Info(("TaskCanceledException"));
            }
        }

        private static string GetStringFromBytes(byte[] bytes)
        {
            if (bytes == null)
                return "null";

            var nullIdx = Array.IndexOf(bytes, (byte)0);

            return nullIdx >= 0
              ? Encoding.Default.GetString(bytes, 0, nullIdx)
              : Encoding.Default.GetString(bytes);


        }

        public static rF2VehicleScoring GetPlayerScoring(ref rF2Scoring scoring)
        {
            var playerVehScoring = new rF2VehicleScoring();
            for (int i = 0; i < scoring.mScoringInfo.mNumVehicles; ++i)
            {
                var vehicle = scoring.mVehicles[i];
                switch ((LMU_Constants.rF2Control)vehicle.mControl)
                {
                    case LMU_Constants.rF2Control.AI:
                    case LMU_Constants.rF2Control.Player:
                    case LMU_Constants.rF2Control.Remote:
                        if (vehicle.mIsPlayer == 1)
                            playerVehScoring = vehicle;

                        break;

                    default:
                        continue;
                }

                if (playerVehScoring.mIsPlayer == 1)
                    break;
            }

            return playerVehScoring;
        }

        public static List<rF2VehicleScoring> GetOpenentsScoring(ref rF2Scoring scoring)
        {
            List<rF2VehicleScoring> playersVehScoring  = new List<rF2VehicleScoring>();
            for (int i = 0; i < scoring.mScoringInfo.mNumVehicles; ++i)
            {
                var vehicle = scoring.mVehicles[i];
                switch ((LMU_Constants.rF2Control)vehicle.mControl)
                {
                    case LMU_Constants.rF2Control.AI:
                        //if (vehicle.mIsPlayer != 1)
                            playersVehScoring.Add(vehicle);
                        break;
                    case LMU_Constants.rF2Control.Player:
                    case LMU_Constants.rF2Control.Remote:
                        //if (vehicle.mIsPlayer != 1)
                            playersVehScoring.Add(vehicle);

                        break;

                    default:
                        continue;
                }

             }

            return playersVehScoring;
        }

        private void SaveJSonSetting()
        {
            JObject JSONdata = new JObject(
                   new JProperty("Clock_Format24", ButtonBindSettings.Clock_Format24),
                   new JProperty("RealTimeClock", ButtonBindSettings.RealTimeClock));
            //string settings_path = AccData.path;
            try
            {
                // create/write settings file
                File.WriteAllText(LMURepairAndRefuelData.path, JSONdata.ToString());
                //Logging.Current.Info("Plugin Viper.PluginCalcLngWheelSlip - Settings file saved to : " + System.Environment.CurrentDirectory + "\\" + LMURepairAndRefuelData.path);
            }
            catch
            {
                //A MessageBox creates graphical glitches after closing it. Search another way, maybe using the Standard Log in SimHub\Logs
                //MessageBox.Show("Cannot create or write the following file: \n" + System.Environment.CurrentDirectory + "\\" + AccData.path, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                //Logging.Current.Error("Plugin Viper.PluginCalcLngWheelSlip - Cannot create or write settings file: " + System.Environment.CurrentDirectory + "\\" + LMURepairAndRefuelData.path);


            }
        }
      
        public void Init(PluginManager pluginManager)
        {
            // set path/filename for settings file
            LMURepairAndRefuelData.path = PluginManager.GetCommonStoragePath("Redadeg.lmuDataPlugin.json");
            string path_data = PluginManager.GetCommonStoragePath("Redadeg.lmuDataPlugin.data.json");
            //List<PitStopDataIndexesClass> PitStopDataIndexes = new List<PitStopDataIndexesClass>();
            // try to read settings file




            LoadSettings(pluginManager);
            lmu_extendedThread = new Thread(lmu_extendedReadThread)
            {
                Name = "ExtendedDataUpdateThread"
            };
            lmu_extendedThread.Start();

            Logging.Current.Info("Plugin Redadeg.lmuDataPlugin - try devices update.");

            try
            {
                JObject JSONdata = JObject.Parse(File.ReadAllText(LMURepairAndRefuelData.path));
                ButtonBindSettings.Clock_Format24 = JSONdata["Clock_Format24"] != null ? (bool)JSONdata["Clock_Format24"] : false;
                ButtonBindSettings.RealTimeClock = JSONdata["RealTimeClock"] != null ? (bool)JSONdata["RealTimeClock"] : false;
            }
            catch { }

            //var joystickDevices = GetDevices();
            //if (joystickDevices != null)
            //{ 
            //    foreach (JoystickDevice joy in GetDevices())
            //    {
            //        Logging.Current.Info("Joystic: " + joy.Name);
            //    }
            //}

            pluginManager.AddProperty("Redadeg.lmu.energyPerLast5Lap", this.GetType(), LMURepairAndRefuelData.energyPerLast5Lap);
            pluginManager.AddProperty("Redadeg.lmu.energyPerLastLap", this.GetType(), LMURepairAndRefuelData.energyPerLastLap);
            pluginManager.AddProperty("Redadeg.lmu.energyPerLastLapRealTime", this.GetType(), 0);
            pluginManager.AddProperty("Redadeg.lmu.energyLapsRealTimeElapsed", this.GetType(), 0);

            pluginManager.AddProperty("Redadeg.lmu.energyTimeElapsed", this.GetType(), LMURepairAndRefuelData.energyTimeElapsed);

            pluginManager.AddProperty("Redadeg.lmu.NewLap", this.GetType(), 0);
            pluginManager.AddProperty("Redadeg.lmu.CurrentLapTimeDifOldNew", this.GetType(), 0);
            pluginManager.AddProperty("Redadeg.lmu.rainChance", this.GetType(), LMURepairAndRefuelData.rainChance);
            pluginManager.AddProperty("Redadeg.lmu.timeOfDay", this.GetType(), LMURepairAndRefuelData.timeOfDay);
            pluginManager.AddProperty("Redadeg.lmu.passStopAndGo", this.GetType(), LMURepairAndRefuelData.passStopAndGo);
            pluginManager.AddProperty("Redadeg.lmu.RepairDamage", this.GetType(), LMURepairAndRefuelData.RepairDamage);
            pluginManager.AddProperty("Redadeg.lmu.Driver", this.GetType(), LMURepairAndRefuelData.Driver);
            pluginManager.AddProperty("Redadeg.lmu.FuelRatio", this.GetType(), LMURepairAndRefuelData.FuelRatio);
            pluginManager.AddProperty("Redadeg.lmu.currentFuel", this.GetType(), LMURepairAndRefuelData.currentFuel);
            pluginManager.AddProperty("Redadeg.lmu.addFuel", this.GetType(), LMURepairAndRefuelData.addFuel);
            pluginManager.AddProperty("Redadeg.lmu.addVirtualEnergy", this.GetType(), LMURepairAndRefuelData.addVirtualEnergy);
            pluginManager.AddProperty("Redadeg.lmu.Wing", this.GetType(), LMURepairAndRefuelData.Wing);
            pluginManager.AddProperty("Redadeg.lmu.Grille", this.GetType(), LMURepairAndRefuelData.Grille);
            pluginManager.AddProperty("Redadeg.lmu.Virtual_Energy", this.GetType(), 0);
            pluginManager.AddProperty("Redadeg.lmu.currentBattery", this.GetType(), LMURepairAndRefuelData.currentBattery);
            pluginManager.AddProperty("Redadeg.lmu.currentVirtualEnergy", this.GetType(), LMURepairAndRefuelData.currentVirtualEnergy);
            pluginManager.AddProperty("Redadeg.lmu.pitStopLength", this.GetType(), LMURepairAndRefuelData.pitStopLength);

            pluginManager.AddProperty("Redadeg.lmu.maxAvailableTires", this.GetType(), LMURepairAndRefuelData.maxAvailableTires);
            pluginManager.AddProperty("Redadeg.lmu.newTires", this.GetType(), LMURepairAndRefuelData.newTires);

            pluginManager.AddProperty("Redadeg.lmu.fl_TyreChange", this.GetType(), LMURepairAndRefuelData.fl_TyreChange);
            pluginManager.AddProperty("Redadeg.lmu.fr_TyreChange", this.GetType(), LMURepairAndRefuelData.fr_TyreChange);
            pluginManager.AddProperty("Redadeg.lmu.rl_TyreChange", this.GetType(), LMURepairAndRefuelData.rl_TyreChange);
            pluginManager.AddProperty("Redadeg.lmu.rr_TyreChange", this.GetType(), LMURepairAndRefuelData.rr_TyreChange);

            pluginManager.AddProperty("Redadeg.lmu.fl_TyrePressure", this.GetType(), LMURepairAndRefuelData.fl_TyrePressure);
            pluginManager.AddProperty("Redadeg.lmu.fr_TyrePressure", this.GetType(), LMURepairAndRefuelData.fr_TyrePressure);
            pluginManager.AddProperty("Redadeg.lmu.rl_TyrePressure", this.GetType(), LMURepairAndRefuelData.rl_TyrePressure);
            pluginManager.AddProperty("Redadeg.lmu.rr_TyrePressure", this.GetType(), LMURepairAndRefuelData.rr_TyrePressure);
            pluginManager.AddProperty("Redadeg.lmu.replaceBrakes", this.GetType(), LMURepairAndRefuelData.replaceBrakes);

            pluginManager.AddProperty("Redadeg.lmu.maxBattery", this.GetType(), LMURepairAndRefuelData.maxBattery);
            pluginManager.AddProperty("Redadeg.lmu.selectedMenuIndex", this.GetType(), 0);
            pluginManager.AddProperty("Redadeg.lmu.maxFuel", this.GetType(), LMURepairAndRefuelData.maxFuel);
            pluginManager.AddProperty("Redadeg.lmu.maxVirtualEnergy", this.GetType(), LMURepairAndRefuelData.maxVirtualEnergy);

            pluginManager.AddProperty("Redadeg.lmu.isStopAndGo", this.GetType(), 0);
            pluginManager.AddProperty("Redadeg.lmu.isDamage", this.GetType(), 0);
            pluginManager.AddProperty("Redadeg.lmu.haveDriverMenu", this.GetType(), 0);
            pluginManager.AddProperty("Redadeg.lmu.isHyper", this.GetType(), 0);

            pluginManager.AddProperty("Redadeg.lmu.Extended.Cuts", this.GetType(), 0);
            pluginManager.AddProperty("Redadeg.lmu.Extended.CutsMax", this.GetType(), 0);
            pluginManager.AddProperty("Redadeg.lmu.Extended.PenaltyLeftLaps", this.GetType(), 0);
            pluginManager.AddProperty("Redadeg.lmu.Extended.PenaltyType", this.GetType(), 0);
            pluginManager.AddProperty("Redadeg.lmu.Extended.PenaltyCount", this.GetType(), 0);
            pluginManager.AddProperty("Redadeg.lmu.Extended.PendingPenaltyType1", this.GetType(), 0);
            pluginManager.AddProperty("Redadeg.lmu.Extended.PendingPenaltyType2", this.GetType(), 0);
            pluginManager.AddProperty("Redadeg.lmu.Extended.PendingPenaltyType3", this.GetType(), 0);
            pluginManager.AddProperty("Redadeg.lmu.Extended.TractionControl", this.GetType(),0);
            pluginManager.AddProperty("Redadeg.lmu.Extended.MotorMap", this.GetType(), "None");
            pluginManager.AddProperty("Redadeg.lmu.Extended.RegenLevel", this.GetType(), "None");
            pluginManager.AddProperty("Redadeg.lmu.Extended.BrakeMigration", this.GetType(),0);
            pluginManager.AddProperty("Redadeg.lmu.Extended.BrakeMigrationMax", this.GetType(), 0);

            pluginManager.AddProperty("Redadeg.lmu.mPlayerBestSector1", this.GetType(),0);
            pluginManager.AddProperty("Redadeg.lmu.mPlayerBestSector2", this.GetType(), 0);
            pluginManager.AddProperty("Redadeg.lmu.mPlayerBestSector3", this.GetType(), 0);
            pluginManager.AddProperty("Redadeg.lmu.mSessionBestSector1", this.GetType(), 0);
            pluginManager.AddProperty("Redadeg.lmu.mSessionBestSector2", this.GetType(), 0);
            pluginManager.AddProperty("Redadeg.lmu.mSessionBestSector3", this.GetType(), 0);

            pluginManager.AddProperty("Redadeg.lmu.mPlayerCurSector1", this.GetType(), 0);
            pluginManager.AddProperty("Redadeg.lmu.mPlayerCurSector2", this.GetType(), 0);
            pluginManager.AddProperty("Redadeg.lmu.mPlayerCurSector3", this.GetType(), 0);

            pluginManager.AddProperty("Redadeg.lmu.mPlayerBestLapTime", this.GetType(), 0);
            pluginManager.AddProperty("Redadeg.lmu.mPlayerBestLapSector1", this.GetType(),0);
            pluginManager.AddProperty("Redadeg.lmu.mPlayerBestLapSector2", this.GetType(), 0);
            pluginManager.AddProperty("Redadeg.lmu.mPlayerBestLapSector3", this.GetType(), 0);

            pluginManager.AddProperty("Redadeg.lmu.Clock_Format24", this.GetType(), ButtonBindSettings.Clock_Format24);
            pluginManager.AddProperty("Redadeg.lmu.RealTimeClock", this.GetType(), ButtonBindSettings.RealTimeClock);

            pluginManager.AddProperty("Redadeg.lmu.mMessage", this.GetType(), "");


           
        }
    }


}
