using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
namespace SoupMix.Modules
{
    public class HttpMonitor : BackendModule
    {
        private bool shouldRun = false;
        const int PORT = 8049;
        public Dictionary<string,HttpModule> prefixModList;
        private HttpListener listen;
        public HttpMonitor() : base("HttpMonitor")
        {
            
        }

        public override void Load()
        {
            listen = new HttpListener();
			prefixModList = new Dictionary<string, HttpModule>(5000);
            shouldRun = true;
            base.Load();
        }

		public void HttpStart(){
            listen.Prefixes.Add("http://*:"+PORT+"/");
            listen.IgnoreWriteExceptions = true;//Don't error if the client fucks with us.
            listen.Start();
            listen.BeginGetContext(HTTPResponse,null);
            Console.WriteLine("Starting HTTP listener on port " + PORT);
        }

        public override void Unload()
        {
            shouldRun = false;
            listen.Stop();
            base.Unload();
        }

        public void HTTPResponse (IAsyncResult res)
		{
			HttpListenerContext con = listen.EndGetContext (res);
			List<string> keyList = new List<string> (prefixModList.Keys);
			string requestURL = "";
			if (con.Request.Url.Segments.Length > 1) {
				requestURL = con.Request.Url.Segments[1].Replace("/","");
			}
			
            string[] modKeys = keyList.Where(str => requestURL.StartsWith(str)).ToArray();
            if (modKeys.Length > 0)
            {
                prefixModList[modKeys[0]].requests.Enqueue(con);
				prefixModList[modKeys[0]].Interrupt();
            }
            else
            {
                con.Response.StatusCode = 404;
                con.Response.Close();
            }

            if (shouldRun)
            {
                listen.BeginGetContext(HTTPResponse, null);
            }
        }
    }
}

