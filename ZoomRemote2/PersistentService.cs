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


namespace ZoomRemote
{
    public abstract class PersistentService : Service
    {
        private volatile Looper threadLooper;
        private volatile PersistentServiceHandler serviceHandler;
        private string serviceName;

        private sealed class PersistentServiceHandler : Handler
        {
            private PersistentService p;
            public PersistentServiceHandler(Looper looper, PersistentService p) : base(looper)
            {
                this.p = p;
            }

            public override void HandleMessage(Message msg)
            {
                p.OnHandleIntent((Intent)msg.Obj);
            }
        }

        public PersistentService(string serviceName) : base()
        {
            this.serviceName = serviceName;
        }

        sealed public override void OnCreate()
        {
            base.OnCreate();
            HandlerThread thread = new HandlerThread(serviceName);
            thread.Start();

            threadLooper = thread.Looper;
            serviceHandler = new PersistentServiceHandler(threadLooper, this);
        }

      
        /**
         * You should not override this method for your IntentService. Instead,
         * override {@link #onHandleIntent}, which the system calls when the IntentService
         * receives a start request.
         * @see android.app.Service#onStartCommand
         */

        sealed public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            Message msg = serviceHandler.ObtainMessage();
            msg.Arg1 = startId;
            msg.Obj = intent;
            serviceHandler.SendMessage(msg);
            return StartCommandResult.Sticky;
        }

        sealed public override void OnDestroy()
        {
            threadLooper.Quit();
        }

        public override IBinder OnBind(Intent intent)
        {
            return null;
        }

        /**
         * Override this method to be called on worker thread in response to Intent
         */
        protected abstract void OnHandleIntent(Intent intent);


    }
}