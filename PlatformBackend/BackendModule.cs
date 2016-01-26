using System;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;

namespace SoupMix
{
    public class BackendModule
    {
        public Thread backendThread;
        public ConcurrentQueue<HttpListenerContext> requests;
        public string MODNAME = "NoModName";
        public bool CanInterrupt = true;
        public bool IsRunning = false;
        public bool RequiresDatabase = false;
        public string[] Dependencies = new string[0];

        public BackendModule(string name)
        {
            MODNAME = name;
            requests = new ConcurrentQueue<HttpListenerContext>();
        }

        public virtual void Load(){
            IsRunning = true;
        }

        //public virtual void Update(){} 

        public virtual void Unload(){
            IsRunning = false;
        }

        public int CurrentLoad(){
            return requests.Count;
        }
    }
}

