//
//  Settings.cs
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
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace SoupMix
{
    public static class Settings
    {
        private static JObject config = new JObject();
        public static bool LoadFile(string file,bool create = false){
            Console.WriteLine("[Config] Loading " + file);
            if (!File.Exists(file))
            {
                if (create)
                {
                    CreateDefault(file);
                }
                else
                {
                    return false;
                }
            }
            string data = File.ReadAllText(file);
            try
            {
                JObject newdata = JObject.Parse(data);
                config.Merge(newdata);
            }
            catch(JsonException e){
                Console.WriteLine("[Config] Errors in JSON, not loading!");
                Console.WriteLine("[Config] " + e.Message);
                return false;
            }
            return true;
        }

        public static bool Get<T>(string name,out T data){
            string[] segments = name.Split(new char[1]{'.'},StringSplitOptions.RemoveEmptyEntries);
            JObject finalobj = config;
            bool found = (segments.Length == 1);
            JToken token;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                try
                {
                    if(finalobj.TryGetValue(segments[i],out token)){
                        finalobj = (JObject)token;
                        found=(i == segments.Length - 2);
                    }
                    else
                    {
                        break;
                    }
                }
                catch(InvalidCastException){
                    break;
                }
            }


            data = default(T);

            if (found)
            {
                if (finalobj.TryGetValue(segments[segments.Length - 1], out token))
                {
                    data = (T)token.ToObject(typeof(T));
                }
                else
                {
                    found = false;
                }
            }
            
            return found;
        }

        private static void CreateDefault(string file){
            JObject token = new JObject();
            System.Net.IPAddress[] addresses = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList;
            string[] saddrs = new string[addresses.Length];
            for(int i =0;i<addresses.Length;i++){
                saddrs[i] = addresses[i].ToString();
            }
            token.Add("hosts", JToken.FromObject(saddrs));

            //Mysql Stuff
            JObject mysql = new JObject();
            mysql.Add("host",(JToken)"localhost");
            mysql.Add("db",(JToken)"webPlatform");
            mysql.Add("username",(JToken)"webplat");
            mysql.Add("password",(JToken)"");
            token.Add("mysql",mysql);

            new FileInfo(file).Directory.Create();
            File.WriteAllText(file, token.ToString(Formatting.Indented));
        }
    }
}

