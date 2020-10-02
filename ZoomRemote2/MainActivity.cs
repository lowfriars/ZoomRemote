#region Copyright
/*
Copyright(c) 2020 Tim Dixon

        Permission is hereby granted, free of charge, to any person obtaining a 
        copy of this software and associated documentation files (the "Software"), 
        to deal in the Software without restriction, including without limitation 
        the rights to use, copy, modify, merge, publish, distribute, sublicense, 
        and/or sell copies of the Software, and to permit persons to whom the 
        Software is furnished to do so, subject to the following conditions:

        The above copyright notice and this permission notice shall be included in 
        all copies or substantial portions of the Software.

        THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
        OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
        FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. 
        IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, 
        DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR 
        OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE 
        USE OR OTHER DEALINGS IN THE SOFTWARE.
 */
#endregion

#region Using
using System;
using Android.App;
using Android.Content;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.OS;
using Android.Bluetooth;
using Android.Preferences;
using System.Collections.Generic;
#endregion

namespace ZoomRemote
{
    [Activity(Label = "ZoomRemote", MainLauncher = true, Icon = "@drawable/icon")]
    public class MainActivity : Activity
    {
        private static readonly byte[] keyUp = new byte[] { 0x80, 0x00 };
        private static BluetoothAdapter bluetoothAdapter = null;
        private static AppConst.ActivityState activityState = AppConst.ActivityState.idle;
        private static int connectionErrors = 0;

        private const int REQUEST_ENABLE_BT = 1;
        private const int REQUEST_CONNECT_DEVICE = 2;
        private const float enabledAlpha = 1.0f, disabledAlpha = 0.5f; 

        private static readonly int[] ButtonResources = { Resource.Id.buttonMarkIcon, Resource.Id.buttonRecordIcon, Resource.Id.buttonRecPauseIcon, Resource.Id.buttonNextIcon, 
                                                            Resource.Id.buttonPlayIcon, Resource.Id.buttonPrevIcon, Resource.Id.buttonVolDownIcon, Resource.Id.buttonVolUpIcon};

        private static readonly Dictionary<int, byte[]> keySequence = new Dictionary<int, byte[]>()
            {
                { Resource.Id.buttonNextIcon, new byte[] { 0x88, 0x00 } },          // Data sent when NEXT button pushed
                { Resource.Id.buttonPrevIcon, new byte[] { 0x90, 0x00 } },          // .. PREV button
                { Resource.Id.buttonMarkIcon, new byte[] { 0x82, 0x00 } },          // .. MARK button (same as Play)
                { Resource.Id.buttonPlayIcon, new byte[] { 0x82, 0x00 } },          // .. PLAY/PAUSE button
                { Resource.Id.buttonRecordIcon, new byte[] { 0x81, 0x00 } },        // .. RECORD button
                { Resource.Id.buttonRecPauseIcon, new byte[] { 0x80, 0x02 } },      // .. PAUSE RECORD button
                { Resource.Id.buttonVolDownIcon, new byte[] { 0x80, 0x10 } },       // .. VOLUME DOWN button
                { Resource.Id.buttonVolUpIcon,new byte[] { 0x80, 0x08 } }           // .. VOLUME UP button
            };



        private static string deviceAddress, deviceName;
        private static bool isRecording = false;

        private DataReceiver receiverInstance;

        /// <summary>
        ///     This is the broadcast receiver which is created to handle data/status reports from the remote device.
        ///     It runs on the UI thread, so we can safely call functions in the UI Activity, saving its instance in the main constructor.
        /// </summary>
        class DataReceiver : BroadcastReceiver 
        {
            private readonly MainActivity mainActivity;
            public DataReceiver (MainActivity mainActivity)
            {
                this.mainActivity = mainActivity;
            }
            public override void OnReceive(Context context, Android.Content.Intent intent)
            {
                if (intent.Action == AppConst.rxIntentName)                                                   // We've received data
                {
                    string s = intent.GetStringExtra(BundleConst.bundleCmdTag);
                    if (s == BundleConst.bundleDataTag)
                    {
                        int i = intent.GetIntExtra(BundleConst.bundleValTag, 0);

                        mainActivity.UpdateUI(i);
                    }
                    else if (s == BundleConst.bundleStatusTag)                                                    // We've received a status update
                    {
                        activityState = (AppConst.ActivityState) intent.GetIntExtra(BundleConst.bundleValTag, 0);
                        switch (activityState)
                        {
                            case AppConst.ActivityState.idle:
                                mainActivity.DisableButtons();
                                break;

                            case AppConst.ActivityState.connected:
                                mainActivity.DisableButtons();
                                break;

                            case AppConst.ActivityState.synced:
                                mainActivity.EnableButtons();
                                connectionErrors = 0;
                                break;

                            case AppConst.ActivityState.error:
                                mainActivity.DisableButtons();
                                connectionErrors += 1;
                                mainActivity.ScanOrConnect();                                                    // Attempt reconnection
                                break;
                        }
                        mainActivity.CommunicateState(activityState);
                    }
                }
            }
        }

        /// <summary>
        /// When activity is created:
        ///     - Create a broadcast receiver for information sent by the service that talks to the bluetooth device
        ///     - Set the initial content view
        ///     - Check bluetooth is available
        ///     - Retrieve preferences for remote device name/address
        ///     - Set up the buttons to their initial state
        /// </summary>
        /// <param name="bundle">Saved bundle state</param>
        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);

            SetContentView(Resource.Layout.DeviceControl);

            // Get local Bluetooth adapter
            bluetoothAdapter = BluetoothAdapter.DefaultAdapter;

            // If the adapter is null, then Bluetooth is not supported
            if (bluetoothAdapter == null)
            {
                Toast.MakeText(this, "Bluetooth is not available", ToastLength.Long).Show();
                Finish();
                return;
            }

            IntentFilter intentFilter = new IntentFilter();
            intentFilter.AddAction(AppConst.rxIntentName);
            receiverInstance = new DataReceiver(this);
            RegisterReceiver(receiverInstance, intentFilter);

            ISharedPreferences sharedPref = PreferenceManager.GetDefaultSharedPreferences(this);
            deviceAddress = sharedPref.GetString(BundleConst.bundleAddrTag, "");
            deviceName = sharedPref.GetString(BundleConst.bundleDeviceTag, "");

            InitialiseButtons();
        }

        /// <summary>
        /// When activity is started
        ///     - Check bluetooth is turned on
        ///     - If not, request it be enabled
        ///     - If so, initiate connection process
        /// </summary>
        protected override void OnStart()
        {
            base.OnStart();

            // If Bluetooth is not enabled, make a system request to turn it on
            if (!bluetoothAdapter.IsEnabled)
            {
                Intent enableIntent = new Intent(BluetoothAdapter.ActionRequestEnable);
                StartActivityForResult(enableIntent, REQUEST_ENABLE_BT);
            }
            else
            {
                if (deviceAddress != "")
                    ConnectDevice(deviceAddress, deviceName);
            }

        }
        /// <summary>
        /// When the activity is about to be destroyed
        ///     - Remove the broadcast receiver
        /// </summary>
        protected override void OnDestroy()
        {
            base.OnDestroy();
            UnregisterReceiver(receiverInstance);
        }

        /// <summary>
        /// The activity is no longer visible: tell the bluetooth communication service to stop
        /// </summary>
        protected override void OnStop()
        {
            base.OnStop();
            return;
        }
        /// <summary>
        /// When an activity we initiated returns a result
        /// 
        ///     - If it's a request to turn on bluetooth and that failed, warn and exit
        ///     - If it's a request to select a device, save the selected information and start the connection
        /// </summary>
        /// <param name="requestCode">Either REQUEST_ENABLE_BT (to enable bluetooth) or REQUEST_CONNECT_DEVICE to select and pair a bluetooth device</param>
        /// <param name="resultCode">Result of operation</param>
        /// <param name="data">In the case of device selection, Extras contain device name and address</param>
        protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            switch (requestCode)
            {
                // DeviceListActitity has returned a device to use
                case REQUEST_CONNECT_DEVICE:
                    if (resultCode == Result.Ok)
                    {
                        ISharedPreferences prefs = PreferenceManager.GetDefaultSharedPreferences(this);
                        ISharedPreferencesEditor editor = prefs.Edit();
                        editor.PutString(BundleConst.bundleAddrTag, data.Extras.GetString(BundleConst.bundleAddrTag));
                        editor.PutString(BundleConst.bundleDeviceTag, data.Extras.GetString(BundleConst.bundleDeviceTag));
                        editor.Apply();
                        ConnectDevice(data.Extras.GetString(BundleConst.bundleAddrTag), data.Extras.GetString(BundleConst.bundleDeviceTag));
                    }
                    else
                    {
                        if (deviceAddress != "")
                            ConnectDevice(deviceAddress, deviceName);
                    }
                    break;

                // System request to enable Bluetooth is complete
                case REQUEST_ENABLE_BT:
                    if (resultCode == Result.Ok)
                    {
                        Intent serverIntent = new Intent(this, typeof(DeviceListActivity));
                        StartActivityForResult(serverIntent, REQUEST_CONNECT_DEVICE);
                    }
                    else
                    {
                        // The request to enable Bluetooth failed or was cancelled
                        Toast.MakeText(this, "Bluetooth was not enabled", ToastLength.Short).Show();
                        Finish();
                    }
                    break;
            }
        }

        /// <summary>
        /// Handle touch event on scan button
        /// 
        /// Disconnect present connection as it will otherwise not show up in scan
        /// </summary>
        /// <param name="sender">UI element source of event</param>
        /// <param name="touchEventArgs">Determines whether it's a button down or button up event</param>
        public void OnScanClick(object sender, EventArgs e)
        {
            Intent serverIntent = new Intent(this, typeof(DeviceListActivity));
            Intent connectIntent = new Intent(this, typeof(RemoteCommunicationService));

            connectIntent.PutExtra(BundleConst.bundleCmdTag, BundleConst.cmdDisconnect);

            StartService(connectIntent);
            StartActivityForResult(serverIntent, REQUEST_CONNECT_DEVICE);
        }

  
        /// <summary>
        /// Handle touch event on virtual remote control buttons
        /// </summary>
        /// <param name="sender">UI element source of event</param>
        /// <param name="touchEventArgs">Determines whether it's a button down or button up event</param>
        private void OnButtonTouch(object sender, View.TouchEventArgs touchEventArgs)
        {
            ImageView iv = (ImageView)sender;                                       // Get the button's ImageView

            if (activityState != AppConst.ActivityState.synced)
            {
                iv.SetBackgroundResource(Resource.Drawable.buttonbgnormal);         // Simply restore background if handshake with device incomplete
                return;
            }


            switch (touchEventArgs.Event.Action)
            {
                case MotionEventActions.Down:                                       // For button down, flip background for UI feedback and send key press sequence to device

                    SendButtonDown(iv.Id);
                    iv.SetBackgroundResource(Resource.Drawable.buttonbgdown);
                    break;

                case MotionEventActions.Outside:
                case MotionEventActions.Up:
                    SendButtonUp(iv.Id);
                    iv.SetBackgroundResource(Resource.Drawable.buttonbgnormal);
                    break;

                default:
                    break;
            }
        }

        /// <summary>
        /// If the device has been selected previously so that we have its name and address available, try to connect.
        /// If we don't ask the user to select one
        /// </summary>
        void ScanOrConnect()
        {
            if (connectionErrors > AppConst.maxConnectionErrors)
            {
                Toast.MakeText(this, "Device connection failed", ToastLength.Long).Show();
                return;
            }

            if (deviceAddress == "")
            {
                Intent serverIntent = new Intent(this, typeof(DeviceListActivity));
                StartActivityForResult(serverIntent, REQUEST_CONNECT_DEVICE);
            }
            else
            {
                ConnectDevice(deviceAddress, deviceName);
            }
        }

        /// <summary>
        /// Send a connection request to the bluetooth communications service
        /// </summary>
        /// <param name="deviceAddress">Bluetooth MAC address</param>
        /// <param name="deviceName">Device name</param>
        private void ConnectDevice(string deviceAddress, string deviceName)
        {
            Intent connectIntent = new Intent(this, typeof(RemoteCommunicationService));

            connectIntent.PutExtra(BundleConst.bundleCmdTag, BundleConst.cmdConnect);
            connectIntent.PutExtra(BundleConst.bundleAddrTag, deviceAddress);

            TextView tv = (TextView)FindViewById(Resource.Id.deviceName);
            tv.Text = deviceName;

            StartService(connectIntent);
        }
        /// <summary>
        /// Set the initial button UI state indicating the buttons are unavailable owing to lack of connection
        /// </summary>
        void InitialiseButtons()
        {
            for (int i = 0; i < ButtonResources.Length; i++)
            {
                ImageView iv = FindViewById<ImageView>(ButtonResources[i]);
                iv.Enabled = false;
                iv.Alpha = disabledAlpha;
                iv.Touch += OnButtonTouch;
            }

            FindViewById<TextView>(Resource.Id.deviceName).SetTextColor(Android.Graphics.Color.DimGray);
            Button b = FindViewById<Button>(Resource.Id.button_launch_scan);
            b.Click += OnScanClick;
            DisableButtons();
        }

        /// <summary>
        /// Make the buttons usable in the UI once connected, highlighting them to confirm
        /// </summary>
        void EnableButtons()
        {
            ImageView iv;
            for (int i = 0; i < ButtonResources.Length; i++)
            {
                iv = FindViewById<ImageView>(ButtonResources[i]);
                iv.Enabled = true;
                iv.Alpha = enabledAlpha;
            }

            FindViewById<ImageView>(Resource.Id.buttonRecordIcon).SetImageResource(Resource.Drawable.ButtonRecord);
            FindViewById<ImageView>(Resource.Id.buttonRecPauseIcon).Enabled = false;
            FindViewById<ImageView>(Resource.Id.buttonMarkIcon).Enabled = false;
            FindViewById<ImageView>(Resource.Id.buttonPlayIcon).Enabled = true;

            FindViewById<ImageView>(Resource.Id.buttonRecPauseIcon).Alpha = disabledAlpha;
            FindViewById<ImageView>(Resource.Id.buttonMarkIcon).Alpha = disabledAlpha;
            FindViewById<ImageView>(Resource.Id.buttonPlayIcon).Alpha = enabledAlpha;


        }
        void CommunicateState (AppConst.ActivityState state)
        {
            switch (state)
            {
                case AppConst.ActivityState.connected:
                    FindViewById<TextView>(Resource.Id.deviceName).SetTextColor(Android.Graphics.Color.LightGoldenrodYellow);
                    break;

                case AppConst.ActivityState.synced:
                    FindViewById<TextView>(Resource.Id.deviceName).SetTextColor(Android.Graphics.Color.LightGreen);
                    break;

                default:
                    FindViewById<TextView>(Resource.Id.deviceName).SetTextColor(Android.Graphics.Color.DimGray);
                    break;
            }
        }

        /// <summary>
        /// Make the buttons unusable in the UI once disconnected
        /// </summary>
        void DisableButtons()
        {
            for (int i = 0; i < ButtonResources.Length; i++)
            {
                ImageView iv = FindViewById<ImageView>(ButtonResources[i]);
                iv.Enabled = false;
                iv.Alpha = disabledAlpha;
            }

            FindViewById<ImageView>(Resource.Id.buttonRecordIcon).SetImageResource(Resource.Drawable.ButtonRecord);
        }

        /// <summary>
        /// Update the UI in response to a status update from the recorder
        /// 
        /// The status update is a single byte value with bits coded as follows:
        /// 
        /// Bit 7   -   0
        /// Bit 6   -   Green status for recorder channel 3 (not used for H2n)
        /// Bit 5   -   Green status for recorder channel 1
        /// Bit 4   -   Greet status for recorder channel 2
        /// Bit 3   -   Red status for recorder channel 3 (not used for H2n)
        /// Bit 2   -   Red status for recorder channel 1
        /// Bit 1   -   Red status for recorder channel 2
        /// Bit 0   -   1 if recording in progress (alternates 0 and 1 when recording paused)

        /// </summary>
        /// <param name="i">Status value</param>
        void UpdateUI(int i)
        {
            int recording = i & 1;

            // If the recording state changes, update the UI as follows:
            //      If recording, make the record button display red and enable the "record pause" and "mark" buttons
            //      Otherwise, the record button is black and "record pause" and "mark" are disabled

            if (isRecording) 
            {
                if (recording == 0)
                {
                    FindViewById<ImageView>(Resource.Id.buttonRecordIcon).SetImageResource(Resource.Drawable.ButtonRecord);
                    FindViewById<ImageView>(Resource.Id.buttonRecPauseIcon).Enabled = false;
                    FindViewById<ImageView>(Resource.Id.buttonMarkIcon).Enabled = false;
                    FindViewById<ImageView>(Resource.Id.buttonPlayIcon).Enabled = true;

                    FindViewById<ImageView>(Resource.Id.buttonRecPauseIcon).Alpha = disabledAlpha;
                    FindViewById<ImageView>(Resource.Id.buttonMarkIcon).Alpha = disabledAlpha;
                    FindViewById<ImageView>(Resource.Id.buttonPlayIcon).Alpha = enabledAlpha;
                    isRecording = false;
                }
            }
            else
            {
                if (recording != 0)
                {
                    FindViewById<ImageView>(Resource.Id.buttonRecordIcon).SetImageResource(Resource.Drawable.ButtonRecordRed);
                    FindViewById<ImageView>(Resource.Id.buttonRecPauseIcon).Enabled = true;
                    FindViewById<ImageView>(Resource.Id.buttonMarkIcon).Enabled = true;
                    FindViewById<ImageView>(Resource.Id.buttonPlayIcon).Enabled = false;

                    FindViewById<ImageView>(Resource.Id.buttonRecPauseIcon).Alpha = enabledAlpha;
                    FindViewById<ImageView>(Resource.Id.buttonMarkIcon).Alpha = enabledAlpha;
                    FindViewById<ImageView>(Resource.Id.buttonPlayIcon).Alpha = disabledAlpha;
                    isRecording = true;
                }
            }

            // Set the "VU" meters
            SetMicLevel(Resource.Id.level1, (i & 0x24) >> 2);
            SetMicLevel(Resource.Id.level2, (i & 0x12) >> 1);
        }

        /// <summary>
        /// Set a VU meter bar to the appropriate RED/AMBER/GREEN state depending on value reported by recorder
        /// 
        /// The reported value is a four-bit field in which the top bit is 1 for "green" the bottom bit is 1 for "red" and both bits together indicate "amber"
        /// </summary>
        /// <param name="id">Resource ID of the relevant ImageView</param>
        /// <param name="val">Value reported by recorder</param>
        void SetMicLevel (int id, int val)
        {
            ImageView iv = FindViewById<ImageView>(id);
            switch (val)
            {
                case 8:
                    iv.SetImageResource(Resource.Drawable.GreenBar);
                    break;
                case 9:
                    iv.SetImageResource(Resource.Drawable.AmberBar);
                    break;
                case 1:
                    iv.SetImageResource(Resource.Drawable.RedBar);
                    break;
                default:
                    iv.SetImageResource(Resource.Drawable.GreyBar);
                    break;
            }
        }

        /// <summary>
        /// Send the key sequence date for a "button down" event to the recorder
        /// 
        /// Looks up the appropriate data for the identified button in "keySequence" and issues a "SEND" command to the communications service
        /// </summary>
        /// <param name="buttonId">Id of the button pressed</param>
        void SendButtonDown (int buttonId)
        {
            if (activityState != AppConst.ActivityState.synced)
                return; 

            Intent sendIntent = new Intent(this, typeof(RemoteCommunicationService));
            sendIntent.PutExtra(BundleConst.bundleCmdTag, BundleConst.cmdSend);
            sendIntent.PutExtra(BundleConst.bundleDataTag, keySequence[buttonId]);
            StartService(sendIntent);
            return;
        }
        /// <summary>
        /// Sends a "button up" event to the recorder
        /// </summary>
        /// <param name="buttonId">Not used</param>
        void SendButtonUp (int buttonId)
        {
            if (activityState != AppConst.ActivityState.synced)
                return;

            Intent sendIntent = new Intent(this, typeof(RemoteCommunicationService));
            sendIntent.PutExtra(BundleConst.bundleCmdTag, BundleConst.cmdSend);
            sendIntent.PutExtra(BundleConst.bundleDataTag, keyUp);
            StartService(sendIntent);
            return;
        }
    }
}

