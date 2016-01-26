using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
namespace SoupMix.Modules
{
    public class HttpModule : BackendModule
    {
        public Dictionary<string,string[]> acceptMethods;
        public Dictionary<string,string[]> acceptHeaders;
        public bool shouldRun = true;
        protected TimeSpan sleepFor = Timeout.InfiniteTimeSpan;
        private string prefix;
        public HttpModule(string name,string prefix) : base(name)
        {
            acceptMethods = new Dictionary<string, string[]>();
            acceptHeaders = new Dictionary<string, string[]>();
            this.prefix = prefix;
        }

        public override void Load()
        {
            Program.httpModule.prefixModList.Add(prefix, this);
            backendThread = new Thread(Update);
            backendThread.Start();
            base.Load();
        }

        public override void Unload()
        {
            shouldRun = false;
            if (CanInterrupt)
            {
                backendThread.Interrupt();
            }
            base.Unload();
        }

        public virtual void UpdateProcess(){

        }

        protected virtual bool HandleRequest(HttpListenerContext context,string action,  out byte[] message){
            message = new byte[0];
            return true;
        }

        public void Update()
        {
            HttpListenerContext con;
            string requestURL;
            byte[] message = new byte[0];
            while (shouldRun)
            {
                UpdateProcess();
                while (requests.TryDequeue(out con))
                {
                    bool okay = true;
                    bool close = true;
                    requestURL = con.Request.Url.Segments[con.Request.Url.Segments.Length-1];
                    if(requestURL.Contains("?")){
                        requestURL = requestURL.Substring(0,requestURL.IndexOf('?'));
                    }
                    string origin = con.Request.Headers.Get("Origin");
                    if (origin == null && (con.Request.LocalEndPoint.Address.ToString() != "127.0.0.1"))//Allow local testing
                    {
                        okay = false;
                    }
                    if (Program.Domains.Contains(origin))
                    {
                        con.Response.AddHeader("Access-Control-Allow-Origin", origin);
                    }
                    else
                    {
                        con.Response.AddHeader("Access-Control-Allow-Origin", Program.Domains[0]);
                    }
                    con.Response.ContentType = "application/json";


                    if (con.Request.HttpMethod == "OPTIONS")
                    {
                        if (acceptMethods.ContainsKey(requestURL))
                        {
                            con.Response.AddHeader("Access-Control-Allow-Methods", String.Join(",", acceptMethods[requestURL]));
                            con.Response.AddHeader("Access-Control-Max-Age", "60");
                            con.Response.AddHeader("Access-Control-Allow-Headers", String.Join(",", acceptHeaders[requestURL]));
                            con.Response.Close();
                            continue;
                        }
                        else
                        {
                            Program.debugMsgs.Enqueue("Request was made to " + requestURL + " but no method is defined");
                        }
                    }
                    if(okay){
                        close = HandleRequest(con,requestURL,out message);
                    }
                    if (close)
                    {
                        try
                        {
                            con.Response.OutputStream.Write(message, 0, message.Length);
                            con.Response.OutputStream.Close();
                        }
                        catch (Exception e)
                        {
                            Program.debugMsgs.Enqueue("An exception occured when trying to write to the client:" + e.Message);
                        }
                    }
                }
                try
                {
                    CanInterrupt = true;
                    #if TRACETHREADS
                    if(sleepFor.TotalSeconds >= 5){
                        Program.debugMsgs.Enqueue("[thread]" + this.MODNAME + " is going to sleep.");
                    }
                    #endif
                    Thread.Sleep(sleepFor);
                }
                catch (ThreadInterruptedException)
                {
                    #if TRACETHREADS
                    if(sleepFor.TotalSeconds >= 5){
                        Program.debugMsgs.Enqueue("[thread]" + this.MODNAME + " woke up.");
                    }
                    #endif
                    CanInterrupt = false;
                }
            }
        }
    }
}

