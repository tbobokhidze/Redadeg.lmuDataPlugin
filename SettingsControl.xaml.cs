﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Newtonsoft.Json.Linq; // Needed for JObject 
using System.IO;    // Needed for read/write JSON settings file
using SimHub;   // Needed for Logging
using System.Net;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Threading;
using MahApps.Metro.Controls;   // Needed for Logging
using System.Windows.Markup;
using SimHub.Plugins.OutputPlugins.Dash.WPFUI;
using System.Diagnostics.Eventing.Reader;

namespace Redadeg.lmuDataPlugin
{
    /// <summary>
    /// Logique d'interaction pour SettingsControlDemo.xaml
    /// </summary>

    public partial class SettingsControl : UserControl, IComponentConnector
    {


        //public void InitializeComponent()
        //{
        //    if (!_contentLoaded)
        //    {
        //        _contentLoaded = true;
        //        Uri resourceLocator = new Uri("/SimHub.Plugins;component/inputplugins/joystick/joystickpluginsettingscontrolwpf.xaml", UriKind.Relative);
        //        Application.LoadComponent(this, resourceLocator);
        //    }
        //}

        internal Delegate _CreateDelegate(Type delegateType, string handler)
        {
            return Delegate.CreateDelegate(delegateType, this, handler);
        }

        //void IComponentConnector.Connect(int connectionId, object target)
        //{
        //    if (connectionId == 1)
        //    {
        //        ((Button)target).Click += clearLogging_Click;
        //    }
        //    else
        //    {
        //        _contentLoaded = true;
        //    }
        //}
        public SettingsControl()
        {
            InitializeComponent();


        }

        //private bool value_changed = false;

        //private delegate void UpdateDataThreadSafeDelegate<TResult>(void Refresh);

        //public static void UpdateDataThreadSafe<TResult>(this Control @this)
        //{
        //   @this.Update;
        //}


        void OnLoad(object sender, RoutedEventArgs e)
        {
    
            clock_format24.IsChecked = ButtonBindSettings.Clock_Format24;
            RealTimeClock.IsChecked = ButtonBindSettings.RealTimeClock;
        }

   
        public  void Refresh(string _Key)
        {
            bool changedBind = false;
            string MessageText = "";
           
            if (changedBind)
            {
                
                SaveSetting();
            }

            base.Dispatcher.InvokeAsync(delegate
            {
                
               
                lock (clock_format24)
                {
                    clock_format24.IsChecked = ButtonBindSettings.Clock_Format24;

                }
                lock (RealTimeClock)
                {
                    RealTimeClock.IsChecked = ButtonBindSettings.RealTimeClock;

                }
                
              
                lock (message_text)
                {
                    message_text.Text = MessageText;

                }
            }
        );
        }

        private void SHSection_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            //Trigger for saving JSON file. Event is fired if you enter or leave the Plugin Settings View or if you close SimHub

            //Saving on leaving Settings View only
            if (!SHSectionPluginOptions.IsVisible)
            {
                try
                {
           
                  
                }
                catch (Exception ext)
                {
                    Logging.Current.Info("INNIT ERROR: " + ext.ToString());
                }


            }
        }

       

        private void refresh_button_Click(object sender, RoutedEventArgs e)
        {

            clock_format24.IsChecked = ButtonBindSettings.Clock_Format24;
            RealTimeClock.IsChecked = ButtonBindSettings.RealTimeClock;
            message_text.Text = "";
        }

        private void SaveSetting()
         {
            JObject JSONdata = new JObject(
                   new JProperty("Clock_Format24", ButtonBindSettings.Clock_Format24),
                   new JProperty("RealTimeClock", ButtonBindSettings.RealTimeClock));
            //string settings_path = AccData.path;
            try
            {
                // create/write settings file
                File.WriteAllText(LMURepairAndRefuelData.path, JSONdata.ToString());
                Logging.Current.Info("Plugin georace.lmuDataPlugin - Settings file saved to : " + System.Environment.CurrentDirectory + "\\" + LMURepairAndRefuelData.path);
            }
            catch
            {
                //A MessageBox creates graphical glitches after closing it. Search another way, maybe using the Standard Log in SimHub\Logs
                //MessageBox.Show("Cannot create or write the following file: \n" + System.Environment.CurrentDirectory + "\\" + AccData.path, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Logging.Current.Error("Plugin georace.lmuDataPlugin - Cannot create or write settings file: " + System.Environment.CurrentDirectory + "\\" + LMURepairAndRefuelData.path);


            }
        }
       
      

        private void clock_format24_Checked(object sender, RoutedEventArgs e)
        {
            ButtonBindSettings.Clock_Format24 = true;
            message_text.Text = "";
            SaveSetting();
        }
        private void clock_format24_unChecked(object sender, RoutedEventArgs e)
        {
            ButtonBindSettings.Clock_Format24 = false;
            message_text.Text = "";
            SaveSetting();
        }

        private void RealTimeClock_Checked(object sender, RoutedEventArgs e)
        {
            ButtonBindSettings.RealTimeClock = true;
            message_text.Text = "";
            SaveSetting();
        }
        private void RealTimeClock_unChecked(object sender, RoutedEventArgs e)
        {
            ButtonBindSettings.RealTimeClock = false;
            message_text.Text = "";
            SaveSetting();
        }



      
    }

    
    //public class for exchanging the data with the main cs file (Init and DataUpdate function)
    public class LMURepairAndRefuelData
    {
        public static double mPlayerBestLapTime { get; set; }
        public static double mPlayerBestLapSector1 { get; set; }
        public static double mPlayerBestLapSector2 { get; set; }
        public static double mPlayerBestLapSector3 { get; set; }

        public static double mPlayerBestSector1 { get; set; }
        public static double mPlayerBestSector2 { get; set; }
        public static double mPlayerBestSector3 { get; set; }

        public static double mPlayerCurSector1 { get; set; }
        public static double mPlayerCurSector2 { get; set; }
        public static double mPlayerCurSector3 { get; set; }

        public static double mSessionBestSector1 { get; set; }
        public static double mSessionBestSector2 { get; set; }
        public static double mSessionBestSector3 { get; set; }


        public static int mpBrakeMigration { get; set; }
        public static int mpBrakeMigrationMax { get; set; }
        public static int mpTractionControl { get; set; }
        public static string mpMotorMap { get; set; }
        public static string mpRegenLevel { get; set; }

        public static float Cuts { get; set; }
        public static int CutsMax { get; set; }
        public static int PenaltyLeftLaps { get; set; }
        public static int PenaltyType { get; set; }
        public static int PenaltyCount { get; set; }
        public static int mPendingPenaltyType1 { get; set; }
        public static int mPendingPenaltyType2 { get; set; }
        public static int mPendingPenaltyType3 { get; set; }
        public static double energyTimeElapsed { get; set; }
        public static double energyPerLastLap { get; set; }
        public static double energyPerLast5Lap { get; set; }
        public static double currentFuel { get; set; }
        public static int currentVirtualEnergy { get; set; }
        public static int currentBattery { get; set; }
        public static int maxBattery { get; set; }
        public static int maxFuel { get; set; }
        public static int maxVirtualEnergy { get; set; }
        public static string RepairDamage { get; set; }
        public static string passStopAndGo { get; set; }
        public static string Driver { get; set; }
        public static int VirtualEnergy { get; set; }

        public static string addVirtualEnergy { get; set; }
        public static string addFuel { get; set; }

        public static string Wing { get; set; }
        public static string Grille { get; set; }

        public static int maxAvailableTires { get; set; }
        public static int newTires { get; set; }
        public static string fl_TyreChange { get; set; }
        public static string fr_TyreChange { get; set; }
        public static string rl_TyreChange { get; set; }
        public static string rr_TyreChange { get; set; }

        public static string fl_TyrePressure { get; set; }
        public static string fr_TyrePressure { get; set; }
        public static string rl_TyrePressure { get; set; }
        public static string rr_TyrePressure { get; set; }
        public static string replaceBrakes { get; set; }
        public static double FuelRatio { get; set; }
        public static double pitStopLength { get; set; }
        public static string path { get; set; }
        public static double timeOfDay { get; set; }
        public static int rainChance { get; set; }
        
    }



    public class LMU_EnegryAndFuelCalculation
    {
        public static double lastLapEnegry { get; set; }
        public static int lapIndex = 0;
        public static bool runned = false;
        public static double LastLapUsed = 0;
        public static bool inPit = true;
        public static double AvgOfFive = 0;

    }
    public class ButtonKeyValues
    {
         string _key { get; set; }
         string _value { get; set; }
    }


        public class ButtonBindSettings
    {
        public static bool RealTimeClock { get; set; }
        public static bool Clock_Format24 { get; set; }

    }


    /*public class AccSpeed - old way
    {*/
    /*private static int Speed = 20;
    public static int Value
    {
        get { return Speed; }
        set { Speed = value; }
    }*/
    /*public static int Value { get; set; }
}*/
}