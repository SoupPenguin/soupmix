using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Threading;
namespace SoupMix.Modules
{
    public class HttpModule : BackendModule
    {
    	/// <summary>
    	/// Accepted HTTP methods for each 'action'
    	/// </summary>
        public Dictionary<string,string[]> HttpMethods;

		/// <summary>
    	/// Accepted HTTP headers for each 'action'.
    	/// Browsers will block any headers not in this list.
    	/// </summary>
        public Dictionary<string,string[]> HttpAcceptHeaders;

        /// <summary>
        /// Should the HTTP thread run?
        /// </summary>
        public bool ThreadRunning = true;

        /// <summary>
        /// Time for the thread to sleep for after finishing requests.
       	/// Threads can and will be woken up early when needed.
       	/// This can usually be set to Infinite, unless you want to do some processing in between.
        /// </summary>
        protected TimeSpan ThreadSleepFor = Timeout.InfiniteTimeSpan;

        /// <summary>
        /// Prefix (after your domain) of the module.
        /// </summary>
        /// <example>helloworld</example>
        public string Prefix {
        	get;
        	private set;
        }


    	/// <summary>
    	/// List of requests waitint to be handled.
    	/// </summary>
        public ConcurrentQueue<HttpListenerContext> requests;

        /// <summary>
        /// Create a new HTTP Module.
        /// </summary>
        /// <param name="name">Internal name of the module</param>
        /// <param name="prefix">url prefix</param>
        public HttpModule(string name,string prefix) : base(name)
        {
            HttpMethods = new Dictionary<string, string[]>();
			HttpAcceptHeaders = new Dictionary<string, string[]>();
            requests = new ConcurrentQueue<HttpListenerContext>();
            this.Prefix = prefix;
        }

        public override void Load()
        {
            Program.httpModule.prefixModList.Add(Prefix, this);
            UpdateThread = new Thread(Update);
            UpdateThread.Start();
            base.Load();
        }

        public override void Unload()
        {
            ThreadRunning = false;
            if (CanInterrupt)
            {
                UpdateThread.Interrupt();
            }
            base.Unload();
        }

        /// <summary>
        /// Work to be done before the thread begins processing requests.
        /// </summary>
        public virtual void UpdateProcess(){

        }

        /// <summary>
        /// Handle any requests send to the prefix here.
        /// </summary>
        /// <returns><c>true</c>, if request was handled, <c>false</c> otherwise.</returns>
        /// <param name="context">HTTP Context object.</param>
        /// <param name="action">The 'action' after the prefix.</param>
        /// <param name="message">Response to be sent to the client.</param>
        protected virtual bool HandleRequest(HttpListenerContext context,string action,  out byte[] message){
            message = new byte[0];
            return true;
        }

        public override int CurrentLoad ()
		{

            return requests.Count;
		}

        /// <summary>
        /// Main thread update loop.
        /// </summary>
        private void Update()
        {
            HttpListenerContext con;
            string requestURL;
            byte[] message = new byte[0];
            while (ThreadRunning)
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
                        if (HttpMethods.ContainsKey(requestURL))
                        {
                            con.Response.AddHeader("Access-Control-Allow-Methods", String.Join(",", HttpMethods[requestURL]));
                            con.Response.AddHeader("Access-Control-Max-Age", "60");
                            con.Response.AddHeader("Access-Control-Allow-Headers", String.Join(",", HttpAcceptHeaders[requestURL]));
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
                    if(ThreadSleepFor.TotalSeconds >= 5){
                        Program.debugMsgs.Enqueue("[thread]" + this.MODNAME + " is going to sleep.");
                    }
                    #endif
                    Thread.Sleep(ThreadSleepFor);
                }
                catch (ThreadInterruptedException)
                {
                    #if TRACETHREADS
                    if(ThreadSleepFor.TotalSeconds >= 5){
                        Program.debugMsgs.Enqueue("[thread]" + this.MODNAME + " woke up.");
                    }
                    #endif
                    CanInterrupt = false;
                }
            }
        }
    }
}

