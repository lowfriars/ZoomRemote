#region Copyright
/*
 Copyright (c) 2020 Tim Dixon

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
using System.Diagnostics;
using Android.OS;
using Java.Util;

#endregion
namespace ZoomRemote
{    class AppConst
    {
        public const string appName = "uk.webwork.zoomremote";
        public const string rxIntentName = appName;
        public enum ActivityState { idle, connected, synced, error };
        public const int maxConnectionErrors = 3;
        public const int disconnectTimeout = 100;
        public enum MsgPrio { low, high }

        public static readonly UUID uuid = UUID.FromString("00001101-0000-1000-8000-00805F9B34FB");     // This is the UUID of the Bluetooth service endpoint for modem communication
        public static readonly ParcelUuid uuidp = new ParcelUuid(uuid);

    }
    class AppLog
    {
        public const string logTag = AppConst.appName;

        [Conditional("DEBUG")]
        public static void Log(string s)
        {
            Android.Util.Log.Debug(logTag, s);
        }
    }
    class BundleConst
    {
        public const string bundleValTag = AppConst.appName + ".VALUE";
        public const string bundleCmdTag = AppConst.appName + ".COMMAND";
        public const string bundleDataTag = AppConst.appName + ".DATA";
        public const string bundleStatusTag = AppConst.appName + ".STATUS";
        public const string bundleAddrTag = AppConst.appName + ".device_address";
        public const string bundleDeviceTag = AppConst.appName + ".device_name";

        public const string cmdConnect = "CONNECT";
        public const string cmdSend = "SEND";
        public const string cmdDisconnect = "DISCONNECT";

    }
}