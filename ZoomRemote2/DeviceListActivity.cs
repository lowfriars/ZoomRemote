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
using System.Collections.Generic;
using System.Linq;
using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Java.Util;
#endregion

namespace ZoomRemote
{
    [Activity(Label = "Zoom Remote: Pick Device",
        Theme = "@android:style/Theme.Dialog",
        ConfigurationChanges=Android.Content.PM.ConfigChanges.KeyboardHidden | Android.Content.PM.ConfigChanges.Orientation)]
    
    ///
    /// Dialog to pick the bluetooth device with which to communicate. 
    ///
    public class DeviceListActivity : ListActivity
    {
        /// <summary>
        /// Represents name, address and connection status of available bluetooth devices
        /// </summary>
        public class BluetoothSummaryInfo
        {
            public string Name;
            public string Address;
        }

        // Member fields
        private static BluetoothAdapter btAdapter;
        private static Receiver receiver;
        private static DeviceListAdapter listAdapter;

        /// <summary>
        /// List in which discovered devices are stored
        /// </summary>
        private static readonly List<BluetoothSummaryInfo> AvailableDevices = new List<BluetoothSummaryInfo>();

        /// <summary>
        /// Called when activity is created
        /// 
        /// Formerly we scanned for all devices, including those not yet paired.
        /// However, this returns a lot of devices that aren't usable and we don't have access
        /// to the service information until the device is paired, so we now just look at the bonded devices.
        /// The code infrastructure is still present to go back to scanning for all devices presenetly discoverable.
        /// </summary>
        /// <param name="bundle"></param>
        protected override void OnCreate(Bundle bundle)
        {
            base.OnCreate(bundle);

            // Setup the window
            RequestWindowFeature(WindowFeatures.NoTitle);
            SetContentView(Resource.Layout.DeviceListLayout);
           
            // Set result CANCELED incase the user backs out
            SetResult(Result.Canceled);

            // Set up button event handlers and initial states
            /*
             * Button scanButton = FindViewById<Button>(Resource.Id.button_scan);
                scanButton.Enabled = true;
                scanButton.Click += OnScanClick;
            */

            FindViewById<Button>(Resource.Id.button_scan).Visibility = ViewStates.Gone;

            Button endScanButton = FindViewById<Button>(Resource.Id.button_endscan);
            endScanButton.Click += OnEndScanClick;

            //endScanButton.Enabled = false;

            // Set up the list adapter and event handler for clicking on items in the device list
            listAdapter = new DeviceListAdapter(this, AvailableDevices);
            ListAdapter = listAdapter;
            ListView availableDeviceView = FindViewById<ListView>(Android.Resource.Id.List);
            availableDeviceView.ItemClick += DeviceListClick;

            FindViewById<ProgressBar>(Resource.Id.indeterminateBar).Visibility = ViewStates.Invisible;

            // Register for broadcasts when a device is discovered
            RegisterFilters();
            InitialiseDiscovery();
        }

        /// <summary>
        ///     In case we get destroyed in the midst of Bluetooth discovery, clean up
        /// </summary>
        protected override void OnDestroy()
        {
            CancelDiscovery();
            base.OnDestroy();
        }

        /// <summary>
        /// Add a device to the list if it supports a service with the required UUID
        /// </summary>
        /// <param name="device">The Bluetooth Device</param>
        public static void AddDevice(BluetoothDevice device)
        {
            ParcelUuid[] supportedUuids;
            bool supported = false;

            BluetoothSummaryInfo info = new BluetoothSummaryInfo
            {
                Address = device.Address,
                Name = device.Name ?? "Unknown",
            };

            if (AvailableDevices.Contains(info))
                return;

            supportedUuids = device.GetUuids();
            if (supportedUuids != null)
                foreach (ParcelUuid id in supportedUuids)
                {
                    if (id.ToString() == AppConst.uuid.ToString())
                    {
                        supported = true;
                        break;
                    }
                }

            if (supported)
            {
                AvailableDevices.Add(info);
                listAdapter.NotifyDataSetChanged();
            }
        }


        /// <summary>
        ///     Prepare fo start device discovery
        /// </summary>
        private void InitialiseDiscovery()
        {
            // Get the local Bluetooth adapter
            btAdapter = BluetoothAdapter.DefaultAdapter;

            AvailableDevices.Clear();
            var pairedDevices = btAdapter.BondedDevices;

            // If there are paired devices, add each one to the ArrayAdapter
            if (pairedDevices.Count > 0)
            {
                foreach (var device in pairedDevices)
                {
                    AddDevice(device);
                }
            }

            // We no longer start discovery as only already bonded devices have their UUIDs available for us to filter by device type
            // btAdapter.StartDiscovery();

            listAdapter.NotifyDataSetInvalidated();
        }

        /// <summary>
        ///     Cancel Bluetooth discovery - and stop receiving Bluetooth events
        /// </summary>
        private void CancelDiscovery()
        {
            if (receiver != null)
                UnregisterReceiver(receiver);

            receiver = null;

            if (btAdapter != null)
            {
                //btAdapter.CancelDiscovery();
                btAdapter = null;
            }
        }
        /// <summary>
        ///     Set up Broadcast Receiver for receiving Bluetooth events
        /// </summary>
        private void RegisterFilters()
        {
            // In case we're called from OnResume
            if (receiver != null)
                return;

            receiver = new Receiver(this);
            IntentFilter filter = new IntentFilter(BluetoothDevice.ActionFound);
            RegisterReceiver(receiver, filter);

            // Register for broadcasts when discovery has finished
            filter = new IntentFilter(BluetoothAdapter.ActionDiscoveryFinished);
            RegisterReceiver(receiver, filter);

        }

        /// <summary>
        ///     Called in response to user starting scan for a Bluetooth device
        /// </summary>
        /// <param name="sender">Object raising the event</param>
        /// <param name="args">Event arguuments</param>
        private void OnScanClick(object sender, EventArgs args)
        {
            //DoDiscovery();
            (sender as View).Enabled = false;
            Button endScanButton = FindViewById<Button>(Resource.Id.button_endscan);
            endScanButton.Enabled = true;

        }

        /// <summary>
        ///     Called in response to user cancelling Bluetooth device discovery
        /// </summary>
        /// <param name="sender">Object raising the event</param>
        /// <param name="args">Event arguuments</param>
        private void OnEndScanClick(object sender, EventArgs args)
        {
            (sender as View).Enabled = false;

            CancelDiscovery();
            SetResult(Result.Canceled);
            Finish();

        }

    /// <summary>
    ///     The adapter class that allows the ListView to get the information about discovered devices
    /// </summary>
        public class DeviceListAdapter : BaseAdapter<BluetoothSummaryInfo>
        {
            private readonly List<BluetoothSummaryInfo> _items;
            private readonly Activity _context;

            public DeviceListAdapter(Activity context, List<BluetoothSummaryInfo> items) : base()
            {
                _context = context;
                _items = items;
            }

            public override BluetoothSummaryInfo this[int position]
            {
                get { return _items[position]; }
            }

            public override int Count
            {
                get { return _items.Count(); }
            }

            public override long GetItemId(int position)
            {
                return position;
            }

            public override View GetView(int position, View convertView, ViewGroup parent)
            {
                View view = convertView;
                if (view == null)
                {
                    view = _context.LayoutInflater.Inflate(Android.Resource.Layout.SimpleListItem2, null);
                }

                view.FindViewById<TextView>(Android.Resource.Id.Text1).Text = _items[position].Name;
                view.FindViewById<TextView>(Android.Resource.Id.Text2).Text = _items[position].Address;
                return view;
            }
        }

        /// <summary>
        /// Start device discover with the BluetoothAdapter
        /// </summary>
        private void DoDiscovery()
        {
            // Indicate scanning in the title
            FindViewById<ProgressBar>(Resource.Id.indeterminateBar).Visibility = ViewStates.Visible;

            // If we're already discovering, stop it
            if (btAdapter.IsDiscovering)
            {
                btAdapter.CancelDiscovery();
            }
        }


        /// <summary>
        /// The on-click listener when a device is selected from those available.
        /// Return the selected device information to the parent activity.
        /// </summary>
        void DeviceListClick(object sender, AdapterView.ItemClickEventArgs e)
        {
            // Cancel discovery because it's costly and we're about to connect
            btAdapter.CancelDiscovery();

            // Create the result Intent and include the MAC address
            Intent intent = new Intent();
            intent.PutExtra(BundleConst.bundleAddrTag, AvailableDevices[e.Position].Address);
            intent.PutExtra(BundleConst.bundleDeviceTag, AvailableDevices[e.Position].Name);

            // Set result and finish this Activity
            SetResult(Result.Ok, intent);
            Finish();
        }

        /// <summary>
        ///     This class receives the Bluetooth notifications of discovered devices (and discovery end).
        /// </summary>
        public class Receiver : BroadcastReceiver
        {
            readonly Activity _chat;

            /// <summary>
            ///     Constructor: save important references
            /// </summary>
            /// <param name="chat">The related Activity, so we can update progress</param>
            public Receiver(Activity chat)
            {
                _chat = chat;
            }

            /// <summary>
            ///     Event receiver for Bluetooth events
            /// </summary>
            /// <param name="context">Event context</param>
            /// <param name="intent">Intent with related data</param>
            public override void OnReceive(Context context, Intent intent)
            {
                string action = intent.Action;

                // When discovery finds a device
                if (action == BluetoothDevice.ActionFound)
                {
                    // Get the BluetoothDevice object from the Intent
                    BluetoothDevice device = (BluetoothDevice)intent.GetParcelableExtra(BluetoothDevice.ExtraDevice);
                    AddDevice(device);

                }
                else if (action == BluetoothAdapter.ActionDiscoveryFinished)
                {
                    // When discovery is finished, turn off progress indicator
                    _chat.FindViewById<ProgressBar>(Resource.Id.indeterminateBar).Visibility = ViewStates.Invisible;
                }
            }
        } 
    }
}