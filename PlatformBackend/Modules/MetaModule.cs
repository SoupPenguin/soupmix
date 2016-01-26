//
//  MetaModule.cs
//
//  Author:
//       Will Hunt <william@molrams.com>
//
//  Copyright (c) 2015 
//
//  This program is free software; you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation; either version 2 of the License, or
//  (at your option) any later version.
//
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//  GNU General Public License for more details.
//
//  You should have received a copy of the GNU General Public License
//  along with this program; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
//
using System;
using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json.Linq;
namespace SoupMix.Modules
{
    public class MetaModule : HttpModule
    {
        public MetaModule() : base("Meta","api")
        {
            
        }
        const int LOADINTERVAL = 250;

        Dictionary<string,float[]> modLoad = new Dictionary<string, float[]>();
        int refreshTime = 0;
        Timer loadTimer;

        protected override bool HandleRequest(System.Net.HttpListenerContext context, string action, out byte[] message)
        {
            context.Response.ContentType = "application/json";
            string json;
            switch (action)
            {
                case "status":
                    json = JObject.FromObject(Program.ReportStatus()).ToString(Newtonsoft.Json.Formatting.None);
                    message = System.Text.Encoding.UTF8.GetBytes(json);
                    break;
                case "version":
                    json = JObject.FromObject(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version).ToString(Newtonsoft.Json.Formatting.None);
                    message = System.Text.Encoding.UTF8.GetBytes(json);
                    break;
                case "load":
                    json = JObject.FromObject(modLoad).ToString(Newtonsoft.Json.Formatting.None);
                    message = System.Text.Encoding.UTF8.GetBytes(json);
                    break;
                default:
                    message =System.Text.Encoding.UTF8.GetBytes("{\"error\":\"This request was not understood\"}");
                break;

            }
            return true;
        }

        public override void Load()
        {
            loadTimer = new Timer(LoadUpdate, null, LOADINTERVAL, LOADINTERVAL);
            base.Load();
        }

        public override void Unload()
        {
            loadTimer.Dispose();
            base.Unload();
        }

        public void LoadUpdate (object state)
		{
			Dictionary<string,int> load = Program.ReportLoad();
			if (modLoad.Count == 0) {
				load = Program.ReportLoad();
	            foreach(string key in load.Keys){
	                modLoad.Add(key, new float[3]);
	            }
			}


            
            if (refreshTime % 60000 == 0)
            {
                foreach (string key in modLoad.Keys)
                {
                    modLoad[key][0] = 0;
                }

            }

            if (refreshTime % 300000 == 0)
            {
                foreach (string key in modLoad.Keys)
                {
                    modLoad[key][1] = 0;
                }
            }

            if (refreshTime % 900000 == 0)
            {
                foreach (string key in modLoad.Keys)
                {
                    modLoad[key][2] = 0;
                }
            }
            float readsMade = ((refreshTime / LOADINTERVAL) + 1);
            foreach(string key in load.Keys){
                modLoad[key][0] = (float)Math.Round((modLoad[key][0] + load[key]) / readsMade,3);
                modLoad[key][1] = (float)Math.Round((modLoad[key][1] + load[key]) / readsMade,3);
                modLoad[key][2] = (float)Math.Round((modLoad[key][2] + load[key]) / readsMade,3);
            }
            refreshTime += LOADINTERVAL;
        }

    }
}

