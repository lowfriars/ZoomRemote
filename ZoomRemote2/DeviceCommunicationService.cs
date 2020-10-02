using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.Bluetooth;
using Java.Util;
using System.IO;
using System.Threading;

namespace ZoomRemote
{
    /// <summary>
    ///     The process of communicating with the Bluetooth device can be slow and cumbersome and is decoupled from the UI
    ///     by operating as a service.
    ///     
    ///     It receives intents from the foreground process to CONNECT to a particular device, to SEND data and to DISCONNECT from the device.
    ///     It starts a separate thread to monitor communication from the device and sends intents to the foreground process to indicate received DATA or STATUS changes.
    ///     
    /// </summary>
    [Service]
    public class DeviceCommunicationService : PersistentService
    {
        public const string cmdConnect = "CONNECT";
        public const string cmdSend = "SEND";
        public const string cmdDisconnect = "DISCONNECT";
        public const string extraNameCmd = "COMMAND";
        public const string extraNameDev = "DEVICE";
        public const string extraNameData = "DATA";

        public static byte[] keyUp = new byte[] { 0x80, 0x00 };

        private readonly BluetoothAdapter btAdapter;
        private Thread readThread;
        private ReadThread readThreadObject;
        private BluetoothDevice btDevice;
        private BluetoothSocket btSocket;
        private Stream inputStream, outputStream;
        private readonly static UUID uuid = UUID.FromString("00001101-0000-1000-8000-00805F9B34FB");     // This is the UUID of the Bluetooth service endpoint for modem communication

        public enum SvcState { idle, syncing, synced }
        public static SvcState serviceState = SvcState.idle;


        public static Context serviceContext;

        private class ReadThread
        {
            private readonly Stream inputStream;
            private byte lastReport;

            public ReadThread(Stream inputStream)
            {
                this.inputStream = inputStream;
            }

            public void ReadProc()
            {
                byte[] inBuf = new byte[1];
                Intent statusIntent, sendIntent;
                //***** 
                while (inputStream.Read(inBuf, 0, 1) > 0)
                {
                    switch (serviceState)
                    {
                        case SvcState.idle:
                            lastReport = 0;
                            break;

                        case SvcState.syncing:
                            if (inBuf[0] < 127)
                            {
                                serviceState = SvcState.synced;

                                statusIntent = new Intent(ZoomRemote.MainActivity.rxIntentName);
                                statusIntent.PutExtra(ZoomRemote.MainActivity.rxIntentExtraCmd, MainActivity.rxIntentCmdStatus);
                                statusIntent.PutExtra(ZoomRemote.MainActivity.rxIntentExtraVal, (int)MainActivity.ActivityState.synced);
                                serviceContext.SendBroadcast(statusIntent);
                                lastReport = 0;
                            }
                            else
                            {
                                sendIntent = new Intent(serviceContext, typeof(DeviceCommunicationService));
                                sendIntent.PutExtra(DeviceCommunicationService.extraNameCmd, DeviceCommunicationService.cmdSend);
                                sendIntent.PutExtra(DeviceCommunicationService.extraNameData, keyUp);
                                serviceContext.StartService(sendIntent);
                            }

                            break;

                        case SvcState.synced:
                            if (inBuf[0] > 127)
                            {
                                serviceState = SvcState.syncing;
                                statusIntent = new Intent(ZoomRemote.MainActivity.rxIntentName);
                                statusIntent.PutExtra(ZoomRemote.MainActivity.rxIntentExtraCmd, MainActivity.rxIntentCmdStatus);
                                statusIntent.PutExtra(ZoomRemote.MainActivity.rxIntentExtraVal, (int)MainActivity.ActivityState.connected);
                                serviceContext.SendBroadcast(statusIntent);
                                sendIntent = new Intent(serviceContext, typeof(DeviceCommunicationService));
                                sendIntent.PutExtra(DeviceCommunicationService.extraNameCmd, DeviceCommunicationService.cmdSend);
                                sendIntent.PutExtra(DeviceCommunicationService.extraNameData, keyUp);
                                serviceContext.StartService(sendIntent);
                            }
                            break;

                    }

                    if (serviceState == SvcState.synced)
                    {
                        if (inBuf[0] != lastReport) // Only report changes in the status
                        {
                            Intent readIntent = new Intent(ZoomRemote.MainActivity.rxIntentName);
                            readIntent.PutExtra(ZoomRemote.MainActivity.rxIntentExtraCmd, MainActivity.rxIntentCmdData);
                            readIntent.PutExtra(ZoomRemote.MainActivity.rxIntentExtraVal, (int)inBuf[0]);
                            serviceContext.SendBroadcast(readIntent);
                            lastReport = inBuf[0];
                        }
                    }
                }
            }
        }

        public DeviceCommunicationService() : base("DeviceCommunicationService")
        {
            btAdapter = BluetoothAdapter.DefaultAdapter;
            serviceContext = this;
        }

        private void CloseConnection()
        {
            try
            {
                if (readThread != null)
                {
                    readThread.Abort();
                }
            }
            finally
            {
                readThread = null;
            }

            try
            {
                if (inputStream != null)
                    inputStream.Close();

                if (outputStream != null)
                    outputStream.Close();
            }
            finally
            {
                inputStream = null;
                outputStream = null;
                serviceState = SvcState.idle;
            }

            try
            {
                if (btSocket != null)
                    btSocket.Close();
            }
            finally
            {
                btSocket = null;
            }
        }

        /// <summary>
        ///     Handle requests from the foreground process
        /// </summary>
        /// <param name="intent">Intent containing the request</param>
        protected override void OnHandleIntent(Intent intent)
        {
            string command = intent.GetStringExtra(extraNameCmd);
            string deviceAddr;
            Intent statusIntent;

            switch (command)
            {
                case cmdConnect: // Make a new connection 
                    CloseConnection();

                    try
                    {
                        deviceAddr = intent.GetStringExtra(extraNameDev);                           // Get the Bluetooth device address
                        

                        btDevice = btAdapter.GetRemoteDevice(deviceAddr);
                        btSocket = btDevice.CreateRfcommSocketToServiceRecord(uuid);
                        btSocket.Connect();                                                         // Connect the socket

                        inputStream = btSocket.InputStream;
                        outputStream = btSocket.OutputStream;
                        serviceState = SvcState.syncing;
                        readThreadObject = new ReadThread(inputStream);
                        readThread = new Thread(new ThreadStart(readThreadObject.ReadProc));        // Start the read thread
                        readThread.Start();
                    }
                    catch 
                    {
                        try
                        {
                            if (btSocket != null)
                                btSocket.Close();
                        }
                        catch
                        {

                        }
                        statusIntent = new Intent(ZoomRemote.MainActivity.rxIntentName);
                        statusIntent.PutExtra(ZoomRemote.MainActivity.rxIntentExtraCmd, MainActivity.rxIntentCmdStatus);
                        statusIntent.PutExtra(ZoomRemote.MainActivity.rxIntentExtraVal, (int) MainActivity.ActivityState.error);
                        serviceContext.SendBroadcast(statusIntent);
                        CloseConnection();
                    }

                    statusIntent = new Intent(ZoomRemote.MainActivity.rxIntentName);
                    statusIntent.PutExtra(ZoomRemote.MainActivity.rxIntentExtraCmd, MainActivity.rxIntentCmdStatus);
                    statusIntent.PutExtra(ZoomRemote.MainActivity.rxIntentExtraVal, (int)MainActivity.ActivityState.connected);
                    serviceContext.SendBroadcast(statusIntent);
                    break;

                case cmdSend:
                    try
                    {
                        byte[] devCmd;

                        devCmd = intent.GetByteArrayExtra(extraNameData);
                        outputStream.Write(devCmd, 0, devCmd.Length);
                    }
                    catch 
                    {
                        statusIntent = new Intent(ZoomRemote.MainActivity.rxIntentName);
                        statusIntent.PutExtra(ZoomRemote.MainActivity.rxIntentExtraCmd, MainActivity.rxIntentCmdStatus);
                        statusIntent.PutExtra(ZoomRemote.MainActivity.rxIntentExtraVal, (int)MainActivity.ActivityState.error);
                        serviceContext.SendBroadcast(statusIntent);
                        CloseConnection();
                    }
                    break;

                case cmdDisconnect:
                    CloseConnection();
                    statusIntent = new Intent(ZoomRemote.MainActivity.rxIntentName);
                    statusIntent.PutExtra(ZoomRemote.MainActivity.rxIntentExtraCmd, MainActivity.rxIntentCmdStatus);
                    statusIntent.PutExtra(ZoomRemote.MainActivity.rxIntentExtraVal, (int)MainActivity.ActivityState.idle);
                    serviceContext.SendBroadcast(statusIntent);
                    CloseConnection();
                    break;

                default:
                    break;

            }
        }
    }
}