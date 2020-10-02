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
using Android.App;
using Android.Content;
using Android.OS;
using Android.Bluetooth;
using Java.Util;
using System.IO;
using Java.Lang;
#endregion

namespace ZoomRemote
{
    /// <summary>
    /// This module provides an Android "started service" for communicating with the remote bluetooth device.
    /// 
    /// It receives commands by means of intents from the hosting activity and returns information by way of broadcasts.
    /// 
    /// The commands it provides are:
    ///     CONNECT - to connect a device
    ///     SEND - to send data to a device
    ///     DISCONNECT to disconnect a device
    ///     STOP - When the service is no longer required
    ///     
    ///     Parameters for the commands are stored in Intent Extras.
    ///     
    /// The reponses it provides are
    ///     STATUS - when the status of the device changes
    ///     DATA    - When data is received from the device
    ///     
    ///     Again, the parameters are stored in Intent Extras
    ///     
    /// In order to minimise communication with the host activity, this service understands some details of the remote control protocol.
    /// 
    /// Initially, the service is in the "Idle" state. On receipt of a CONNECT command (specifying the MAC address of the target device), 
    /// a bluetooth connection is established to the device, streams to read and write are opened and a thread is started to read data as
    /// it becomes available. The service transitions to "Connected" state and issues a status update.
    /// 
    /// Initially, the Zoom device sends a sequence of two bytes: for the H2n these are 0x80 and 0x81, for other devices they may be 
    /// different, but the high bit of 0x80 appears to be intended to signify that it's a two-byte sequence. The service sends "key up"
    /// messages to the recorder. At some point, the recorder recognises the existence of the remote controller and switches to one-byte
    /// responses with the top bit clear. The contents of each byte represents the status of the recorder as follows:
    /// 
    /// Bit 7   -   1 for handshake phase, 0 once remote control has been acknowledged
    /// Bit 6   -   Green status for recorder channel 3
    /// Bit 5   -   Green status for recorder channel 1
    /// Bit 4   -   Greet status for recorder channel 2
    /// Bit 3   -   Red status for recorder channel 3
    /// Bit 2   -   Red status for recorder channel 1
    /// Bit 1   -   Red status for recorder channel 2
    /// Bit 0   -   1 if recording in progress (alternates 0 and 1 when recording paused)
    /// 
    /// NB: The H2n doesn't have a "channel 3". Red and Green refer to crude level indications: if both are set it is equivalent to "Amber".
    /// 
    /// Once the handshake is complete, the service transitions to the "Synced" state and issues a status update. It will also send the data received
    /// from the recorder to the hosting activity whenever the data changes (not every time it is received).
    /// 
    /// The hosting activity sends "DATA" commands indicating the key codes to be sent to the remote device in response to button presses.
    /// 
    /// At some point, the hosting activity may send a "DISCONNECT" command.
    /// 
    /// </summary>
    /// 
    [Service]
    class RemoteCommunicationService : Service
    {
        public static byte[] promptSequence = new byte[] { 0x80, 0x00, 0x00 };

        private AppConst.ActivityState serviceState = AppConst.ActivityState.idle;
        private HandlerThread handlerThread;
        private Looper looper;
        private ServiceHandler handler;

        private int lastReport;                                // The last status value from the remote device


        /// <summary>
        /// To avoid doing too much work on the UI thread, we have a Looper on a background thread to which we queue messages which are processed by a Handler.
        /// </summary>
        private class ServiceHandler : Handler
        {
            private System.Threading.Thread readThread;                       
            private ReadThread readThreadObject;
            private readonly RemoteCommunicationService serviceContext;
            private BluetoothDevice btDevice;
            private BluetoothSocket btSocket;
            private Stream inputStream, outputStream;

            /// <summary>
            /// Constructor for ServiceHandler
            /// </summary>
            /// <param name="looper">The associated looper which the base class needs</param>
            /// <param name="serviceContext">The instance for the service so we can access non-static members</param>
            public ServiceHandler(Looper looper, RemoteCommunicationService serviceContext) : base(looper)
            {
                this.serviceContext = serviceContext;
            }
 
            /// <summary>
            /// Close a connection (designed to be safely called even if no connection exists or the close operations fault)
            /// </summary>
            private void CloseConnection(int startId)
            {
                try
                {
                    btSocket?.Close();                   // This will fault the input stream causing the read thread to exit
                }
                catch (Exception)
                {
                }

                readThread?.Join();

                try
                {
                    outputStream?.Close();
                }
                catch (Throwable e)
                {
                    AppLog.Log("RCS: CloseConnection; close output stream exception: " + e.ToString());
                }

                outputStream = null;
                inputStream = null;
                readThread = null;
                btSocket = null;
                serviceContext.serviceState = AppConst.ActivityState.idle;
            }
            /// <summary>
            /// Send received status data to the hosting activity
            /// </summary>
            /// <param name="i">The new status value</param>
            private void SendData(int i)
            {
                try
                {
                    Intent readIntent = new Intent(ZoomRemote.AppConst.rxIntentName);
                    readIntent.PutExtra(ZoomRemote.BundleConst.bundleCmdTag, BundleConst.bundleDataTag);
                    readIntent.PutExtra(ZoomRemote.BundleConst.bundleValTag, i);
                    serviceContext.SendBroadcast(readIntent);
                }
                catch (Throwable e)
                {
                    AppLog.Log("RCS: SendData; exception: " + e.ToString());
                }
            }

            /// <summary>
            /// Process a message from the queue (CONNECT, DISCONNECT, SEND or STOP)
            /// </summary>
            /// <param name="msg">The message being delivered by the Looper</param>
            public override void HandleMessage(Message msg)
            {
                string command = msg.Data.GetString(BundleConst.bundleCmdTag);
                Intent statusIntent;
                bool done = true;

                if (!done)
                    return;

                AppLog.Log ("RCS-HandleMessage: " + command);

                try
                {

                    switch (command)
                    {
                        case BundleConst.cmdConnect:                                                             // Connection request
                            if (serviceContext.serviceState != AppConst.ActivityState.idle && serviceContext.serviceState != AppConst.ActivityState.error)
                            {
                                // If we're already connected, prod the UI with the last reported status
                                serviceContext.serviceState = AppConst.ActivityState.connected;
                                outputStream.Write(promptSequence, 0, promptSequence.Length);                                                  // Try to elicit a response in case we were previously connected.
                                SendData(serviceContext.lastReport);
                                return;
                            }

                            try
                            {
                                string deviceAddr = msg.Data.GetString(BundleConst.bundleAddrTag);               // Get the Bluetooth device address

                                btDevice = BluetoothAdapter.DefaultAdapter.GetRemoteDevice(deviceAddr);
                                btSocket = btDevice.CreateInsecureRfcommSocketToServiceRecord(AppConst.uuid);
                                btSocket.Connect();                                                         // Connect the socket

                                AppLog.Log("RCS-HandleMessage: " + command + "; connected device " + deviceAddr);

                                inputStream = Stream.Synchronized(btSocket.InputStream);
                                outputStream = Stream.Synchronized(btSocket.OutputStream);
                                serviceContext.serviceState = AppConst.ActivityState.connected;
                                readThreadObject = new ReadThread(inputStream);
                                readThread = new System.Threading.Thread(readThreadObject.ReadProc);                         // Start the read thread
                                readThread.Start(serviceContext);
                                outputStream.Write(promptSequence, 0, promptSequence.Length);                                                  // Try to elicit a response in case we were previously connected.
                                AppLog.Log("RCS-HandleMessage: " + command + "; created threads");
                            }
                            catch (Throwable e)
                            {
                                try
                                {
                                    if (btSocket != null)
                                        btSocket.Close();
                                }
                                catch
                                {

                                }

                                AppLog.Log("RCS-HandleMessage: " + command + "; exception: " + e.ToString());
                                done = false;
                            }

                            statusIntent = new Intent(AppConst.rxIntentName);                                     // Send the status to the activity
                            statusIntent.PutExtra(BundleConst.bundleCmdTag, BundleConst.bundleStatusTag);
                            if (done)
                            {
                                serviceContext.serviceState = AppConst.ActivityState.connected;
                            }
                            else
                            {
                                serviceContext.serviceState = AppConst.ActivityState.error;
                            }
                            statusIntent.PutExtra(BundleConst.bundleValTag, (int)serviceContext.serviceState);
                            serviceContext.SendBroadcast(statusIntent);

                            if (!done)
                                CloseConnection(msg.Arg1);
                            break;

                        case BundleConst.cmdDisconnect:                                                                  // Disconnect request
                            CloseConnection(msg.Arg1);
                            serviceContext.serviceState = AppConst.ActivityState.idle;
                            statusIntent = new Intent(AppConst.rxIntentName);
                            statusIntent.PutExtra(BundleConst.bundleCmdTag, BundleConst.bundleStatusTag);
                            statusIntent.PutExtra(BundleConst.bundleValTag, (int)serviceContext.serviceState);
                            serviceContext.SendBroadcast(statusIntent);                                             // Update status
                            break;

                        case BundleConst.cmdSend:                                                                        // Send data
                            if (serviceContext.serviceState == AppConst.ActivityState.connected || serviceContext.serviceState == AppConst.ActivityState.synced)
                            {
                                try
                                {
                                    byte[] devCmd;

                                    devCmd = msg.Data.GetByteArray(BundleConst.bundleDataTag);
                                    outputStream.Write(devCmd, 0, devCmd.Length);
                                    AppLog.Log("RCS-HandleMessage: " + command + "; sent: " + devCmd.ToString());
                                }
                                catch (Throwable e)
                                {
                                    AppLog.Log("RCS-HandleMessage: " + command + "; exception: " + e.ToString());
                                    done = false;
                                }
                            }
                            else
                                done = false;

                            if (!done)                                                                                  // Update status on error
                            {
                                statusIntent = new Intent(AppConst.rxIntentName);
                                statusIntent.PutExtra(BundleConst.bundleCmdTag, BundleConst.bundleStatusTag);
                                statusIntent.PutExtra(BundleConst.bundleValTag, (int)AppConst.ActivityState.error);
                                serviceContext.SendBroadcast(statusIntent);
                                CloseConnection(msg.Arg1);
                            }
                            break;
                    }
                }
                catch (Throwable e)
                {
                    AppLog.Log("RCS-HandleMessage: " + command + "; exception: " + e.ToString());
                }
            }
        }
        /// <summary>
        /// Called once when service is created. 
        /// Set up a background thread with a looper and message handler.
        /// </summary>
        public override void OnCreate()
        {
            AppLog.Log("RCS-OnCreate");
            try
            {
                handlerThread = new HandlerThread(typeof(RemoteCommunicationService).Name, (int)Android.OS.ThreadPriority.Background);
                handlerThread.Start();
                looper = handlerThread.Looper;
                handler = new ServiceHandler(looper, this);
            }
            catch (Throwable e)
            {
                AppLog.Log("RCS-OnCreate; exception: " + e.ToString());
            }
        }

        /// <summary>
        /// Required override: returns null as we don't accept binding
        /// </summary>
        /// <param name="intent">Not used</param>
        /// <returns>null</returns>
        public override IBinder OnBind(Intent intent)
        {
            return null;
        }

        /// <summary>
        /// Called once when service about to be terminated 
        /// 
        /// This will attempt to post a "disconnect" message after removing other queued messages, unless we've already received a "STOP".
        /// Whether execution proceeds to that point remains to be seen.
        /// </summary>
        public override void OnDestroy()
        {
            AppLog.Log("RCS-OnDestroy");

            try
            {
                if (handler != null)
                {
                    handler.RemoveMessages((int)AppConst.MsgPrio.low);
                    Message msg = handler.ObtainMessage();
                    msg.What = (int)AppConst.MsgPrio.high;
                    msg.Data.PutString(BundleConst.bundleCmdTag, BundleConst.cmdDisconnect);
                    handler.SendMessage(msg);
                    handlerThread.QuitSafely();
                }
            }
            catch (Throwable e)
            {
                AppLog.Log("RCS-OnDestroy; exception: " + e.ToString());
            }
        }
        /// <summary>
        /// Called when an intent is received from "StartService" requesting some action.
        /// The command is put onto the message queue.
        /// </summary>
        /// <param name="intent">The received intent</param>
        /// <param name="flags">not used</param>
        /// <param name="startId">not used</param>
        /// <returns></returns>
        public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            string[] extras = new string[] { BundleConst.bundleCmdTag, BundleConst.bundleAddrTag,BundleConst.bundleValTag};
            AppLog.Log("RCS-StartCommandResult");
            if (intent == null)
            {
                AppLog.Log("RCS-StartCommandResult - Intent is null");
                return StartCommandResult.Sticky; ;
            }

            try
            {
                Message msg = handler.ObtainMessage();
                msg.Arg1 = startId;

                msg.What = (int)AppConst.MsgPrio.low;

                foreach (string s in extras)
                {
                    if (intent.Extras.ContainsKey(s))
                        msg.Data.PutString(s, intent.GetStringExtra(s));
                }
                if (intent.Extras.ContainsKey(BundleConst.bundleDataTag))
                    msg.Data.PutByteArray(BundleConst.bundleDataTag, intent.GetByteArrayExtra(BundleConst.bundleDataTag));

                handler.SendMessage(msg);
            }
            catch (Throwable e)
            {
                AppLog.Log("RCS-StartCommandResult - exception: " + e.ToString());
            }
            return StartCommandResult.Sticky;
        }

        /// <summary>
        ///     A class implementing the thread used to read data from the remote device and respond accordingly.
        /// </summary>
        private class ReadThread
        {
            private RemoteCommunicationService serviceContext;
            private readonly Stream inputStream;

            public ReadThread(Stream inputStream)
            {
                this.inputStream = inputStream;
            }

            /// <summary>
            /// Update the internal state and report it to the hosting activity
            /// </summary>
            /// <param name="newState">The new internal state</param>
            private void UpdateState(AppConst.ActivityState newState)
            {
                Intent statusIntent;
                try
                {
                    statusIntent = new Intent(ZoomRemote.AppConst.rxIntentName);
                    statusIntent.PutExtra(ZoomRemote.BundleConst.bundleCmdTag, BundleConst.bundleStatusTag);
                    statusIntent.PutExtra(ZoomRemote.BundleConst.bundleValTag, (int)newState);
                    serviceContext.SendBroadcast(statusIntent);
                    serviceContext.serviceState = newState;

                    AppLog.Log("RCS: New state " + newState.ToString());
                }
                catch (Throwable e)
                {
                    AppLog.Log("RCS: New state; exception: " + e.ToString());
                }
            }
            /// <summary>
            /// Send received status data to the hosting activity
            /// </summary>
            /// <param name="i">The new status value</param>
            private void SendData(int i)
            {
                try
                {
                    Intent readIntent = new Intent(ZoomRemote.AppConst.rxIntentName);
                    readIntent.PutExtra(ZoomRemote.BundleConst.bundleCmdTag, BundleConst.bundleDataTag);
                    readIntent.PutExtra(ZoomRemote.BundleConst.bundleValTag, i);
                    serviceContext.SendBroadcast(readIntent);
                }
                catch (Throwable e)
                {
                    AppLog.Log("RCS: SendData; exception: " + e.ToString());
                }
            }


            /// <summary>
            /// The actual read thread
            /// </summary>
            /// <param name="service">Service instance to access public members</param>
            public void ReadProc(object service)
            {
                int byteRead;                                   // We read one byte at a time
                bool twoByteFlag = false;                       // Set if we've read the first byte of a two-byte sequence
                int secondByte = 0;                             // The second byte of the sequence (may denoted device type)
                Message msg;

                serviceContext = (RemoteCommunicationService) service;

                try
                {
                    while ((byteRead = inputStream.ReadByte()) >= 0)
                    {
                        AppLog.Log("RCS-ReadProc; reading: " + byteRead.ToString());

                        switch (serviceContext.serviceState)
                        {
                            case AppConst.ActivityState.idle:     // We shouldn't actually get here as the read thread should only be started in connected state
                                serviceContext.lastReport = 0;
                                twoByteFlag = false;
                                break;

                            case AppConst.ActivityState.connected:    
                                if (twoByteFlag)                // We are connected, but the handshake is in progress. Check if we're reading the second byte of a sequence
                                {
                                    secondByte = byteRead;
                                    twoByteFlag = false;

                                    if (secondByte < 128)
                                    {
                                        UpdateState(AppConst.ActivityState.synced);
                                        serviceContext.lastReport = 0;
                                    }
                                    break;
                                }

                                if (byteRead < 127)             // As soon as we have a byte with the top bit clear, the handshake is complete (may occur immediately on reconnecting to device)
                                {
                                    UpdateState(AppConst.ActivityState.synced);
                                    serviceContext.lastReport = 0;
                                    twoByteFlag = false;
                                }
                                else if (byteRead == 128)       // If we've received a byte with the top bit set, send "key up" message to handshake the recorder
                                {
                                    twoByteFlag = true;
                                    msg = serviceContext.handler.ObtainMessage();
                                    msg.What = (int)AppConst.MsgPrio.low;
                                    msg.Data.PutString(BundleConst.bundleCmdTag, BundleConst.cmdSend);
                                    msg.Data.PutByteArray(BundleConst.bundleDataTag, promptSequence);
                                    serviceContext.handler.SendMessage(msg);
                                }

                                break;

                            case AppConst.ActivityState.synced:       // If top bit set, the handshake has been lost (eg bluetooth device has been unplugged from recorder)
                                if (byteRead > 127)
                                {
                                    UpdateState(AppConst.ActivityState.connected);
                                    twoByteFlag = true;
                                }
                                else
                                {
                                    twoByteFlag = false;
                                    if (serviceContext.lastReport != byteRead)
                                        SendData(byteRead);
                                    serviceContext.lastReport = byteRead;
                                }
                                break;
                        }
                    }
                }
                // Most likely called when the other thread closes the bluetooth socket on disconnecting
                catch (Throwable e)
                {
                    AppLog.Log("RCS-ReadProc; exception: " + e.ToString());
                    try
                    {
                        inputStream.Close();
                    }
                    catch (Throwable)
                    {
                        AppLog.Log("RCS-ReadProc; stream close exception: " + e.ToString());
                    }
                }
            }
        }
    }
}