using System;
using System.Net;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using MySql.Data.MySqlClient;
using SoupMix.Modules;
namespace SoupMix
{
    class Program
    {

        const float API_VERSION = 1;
        #if DEBUG
        const int MYSQLWAITTIME = 10000;
        #else
        const int MYSQLWAITTIME = 5000;
        #endif

        private static  char[] splitBy = { ' ' };
        private static string MYSQLHOST;
        private static string MYSQLDB;
        private static string MYSQLUNAME;
        private static string MYSQLPASSWORD;
        public static bool IsRoot = false;
        public static bool DatabaseAvaliable = false;
        public static HttpMonitor httpModule;
        public static UserModule userModule;
        public static List<string> Domains;
        static List<BackendModule> modList;
        static bool shouldRun = true;
        public static Queue<string> debugMsgs;
        public static void Main(string[] args)
        {
            debugMsgs = new Queue<string>();
            string rootconfig = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            //Check root
            if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                try
                {
                    System.IO.File.Create("/root/switchd").Close();
                    IsRoot = true;
                    rootconfig = "/etc";
                }
                catch(System.UnauthorizedAccessException){
                    IsRoot = false;
                }
            }

            Settings.LoadFile(rootconfig + "/switchd/config.json",true);
            //Load Directorys
            string configdir = rootconfig + "/switchd/config.d";
            System.IO.Directory.CreateDirectory(configdir);

            foreach (string dir in System.IO.Directory.EnumerateFiles(configdir))
            {
                Settings.LoadFile(dir);
            }

            string[] ips;
            if (!Settings.Get<string[]>("hosts", out ips))
            {
                throw new Exception("Config doesn't have a hosts list, so switchd cannot start");
            }

            //Set up mysql
            Settings.Get<string>("mysql.host",out MYSQLHOST);
            Settings.Get<string>("mysql.db",out MYSQLDB);
            Settings.Get<string>("mysql.username",out MYSQLUNAME);
            Settings.Get<string>("mysql.password",out MYSQLPASSWORD);

            Domains = new List<string>(ips.Length);
            for (int i =0; i<ips.Length;i++)
            {
                Domains.Add("http://"+ips[i]);
            }


            //Load MYSQL Connection
            Console.WriteLine("MYSQL Connection Connection Attempt.");
            MySqlConnection contest = new MySqlConnection(string.Format("Server={0};Database={1};User ID={2};Password={3}",MYSQLHOST,MYSQLDB,MYSQLUNAME,MYSQLPASSWORD));
            try
            {
                contest.Open();
            }
            catch(Exception e){
                Console.WriteLine("[E] Connection to DB failed!\n    Server will be in low functionality until this is fixed!");
                Console.WriteLine("[E] Exception Details:" + e.Message);
                DatabaseAvaliable = false;
            }
            if (contest.State == System.Data.ConnectionState.Open)
            {
                Console.WriteLine("MYSQL Connection Connection Succeded.");
                DatabaseAvaliable = true;
                contest.Close();
            }

            LoadModules();

            while (shouldRun)
            {
                while (debugMsgs.Count > 1)
                {
                    Console.WriteLine(debugMsgs.Dequeue());
                }
                Thread.Sleep(5);
            }

            foreach(BackendModule mod in modList){
                Console.WriteLine("Trying Unload: " + mod.MODNAME);
                mod.Unload();
                Console.WriteLine("Unloaded: " + mod.MODNAME);
            }
            Console.WriteLine("Closing Backend.");

        }

        private static void GetCompatibleMods (List<BackendModule> modsToCheck, List<BackendModule> modsLoaded, ref Queue<BackendModule> queue)
		{
			List<string> smodList = new List<string> ();
			foreach (BackendModule m in modsLoaded) {
				smodList.Add (m.MODNAME);
			}


			foreach (BackendModule mod in modsToCheck) {
				if (mod.RequiresDatabase && !DatabaseAvaliable) {
					Console.WriteLine("[W] Disabled " + mod.MODNAME + " Reason: No database"); 
					continue;
				}
				if (mod.Dependencies.Except(smodList).Count () > 0) {
					continue;
				}
				queue.Enqueue(mod);
			}
        }

        private static void LoadModules ()
		{
			modList = new List<BackendModule> ();
			List<BackendModule> loadQueue = new List<BackendModule> (); 
			Queue<BackendModule> processingQueue = new Queue<BackendModule> (); 

			httpModule = new HttpMonitor ();
			userModule = new UserModule ();

			loadQueue.Add (httpModule);
			loadQueue.Add (userModule);
			loadQueue.Add (new TcpConsole ());
			loadQueue.Add (new MetaModule ());
			loadQueue.Add (new HelloWorld ());

			GetCompatibleMods (loadQueue, modList, ref processingQueue);

			while (processingQueue.Count > 0) {
				while (processingQueue.Count > 0) {
					BackendModule mod = processingQueue.Dequeue ();
					mod.Load ();
					modList.Add (mod);
					loadQueue.Remove (mod);
					Console.WriteLine("[I] Enabled " + mod.MODNAME); 
				}
				GetCompatibleMods (loadQueue, modList, ref processingQueue);
			}
			if (loadQueue.Count > 0) {
				foreach (BackendModule mod in loadQueue) {
					Console.WriteLine("[W] Disabled " + mod.MODNAME + " Reason: Missing or disabled dependency"); 
				}
			}


            Console.WriteLine("Started");
            httpModule.HttpStart();
        }

        /// <summary>
        /// Reports absolute load for each module.
        /// </summary>
        /// <returns>Overload value.</returns>
        public static Dictionary<string,int> ReportLoad(){
            Dictionary<string,int> load = new Dictionary<string, int>();
            foreach (BackendModule module in modList)
            {
                load.Add(module.MODNAME, module.CurrentLoad());
            }
            return load;
        }

        public static Dictionary<string,string> ReportStatus(){
            Dictionary<string,string> status = new Dictionary<string, string>();
            status.Add("Database Connection",DatabaseAvaliable ? "Connected" : "No Connection");
            foreach (BackendModule module in modList)
            {
                status.Add(module.MODNAME, module.IsRunning ? "Running" : "Not Running");
            }
            return status;
        }

        public static MySqlConnection GetMysqlConnection(){
            MySqlConnection newcon = new MySqlConnection(string.Format("Server={0};Database={1};User ID={2};Password={3};Pooling=true",MYSQLHOST,MYSQLDB,MYSQLUNAME,MYSQLPASSWORD));
            try
            {
                newcon.Open();
                return newcon;
            }
            catch(Exception e)
            {
                #if TRACEMYSQL
                debugMsgs.Enqueue("[MYSQL] Could not create a connection.");
                #endif
                Console.WriteLine(e.ToString());
                throw new Exception("Could not get a mysql connection");
            }
        }

    }
     
}
