using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Text;
using System.Reflection;
namespace SoupMix.Modules
{

	public struct TcpState{
		public byte[] buffer;
		public NetworkStream stream;
	}

	public class TcpConsole : BackendModule
	{
		TcpListener listener;
		const int DEFAULT_PORT = 5512;
		const int MAX_CONNECTIONS = 512;
		const int BUFFERSIZE = 512;
		const int ERROR_TOOMANYCONNECTIONS = 2;
		public DateTime lastLogin;
		public string lastIP;


		public TcpConsole () : base("TcpConsole")
		{

		}

		public override void Load ()
		{
			string address;
			int port;
			Settings.Get<string>("TcpConsole.address",out address);
			Settings.Get<int>("TcpConsole.port",out port);

			if(address == null)
				address = "127.0.0.1";

			if(port == 0)
				port = DEFAULT_PORT;

			listener = new TcpListener(System.Net.IPAddress.Parse(address),port);
			listener.Start();
			listener.BeginAcceptTcpClient(NewClient,null);

			base.Load ();
		}

		public override void Unload ()
		{
			listener.Stop();
		}

		public void NewClient (IAsyncResult e)
		{
			TcpClient c = listener.EndAcceptTcpClient (e);
			NetworkStream s = c.GetStream ();
			int ecode = 0;

			byte[] msg = BuildPacket (new { errorCode = ecode });
			if (s.CanWrite) {
				s.Write (msg, 0, msg.Length);
				s.Flush ();
			} else {// Well this sucks
				s.Close();
				return;
			}
			if (ecode == 0) {
				lastLogin = DateTime.Now;
				lastIP = ((IPEndPoint)c.Client.RemoteEndPoint).Address.ToString();
				TcpState state = new TcpState();
				state.stream = s;
				state.buffer = new byte[BUFFERSIZE];
				s.BeginRead(state.buffer,0,BUFFERSIZE,ClientMessage,state);
			} else {
				s.Close();
			}
			


			listener.BeginAcceptTcpClient(NewClient,null);
		}


		public void ClientMessage (IAsyncResult e)
		{
			int read = 0;
			TcpState state = (TcpState)e.AsyncState;
			try {
				read = state.stream.EndRead (e);
			} catch (ObjectDisposedException) {
				//Client quit. That's all folks.
				return;
			}
			bool closing = false;
			JObject jobj = null;
			Console.WriteLine ("Got a " + read + " byte packet");
			string sdata = Encoding.UTF8.GetString (state.buffer);

			try {
				jobj = JObject.Parse (sdata);
			} catch (JsonException) {
				Console.WriteLine ("Data is bogus, kill the client");
				closing = true;
			}

			if (!closing) {
				Task.Run(() => ProcessCommand(jobj,state.stream));
				state.buffer = new byte[BUFFERSIZE];
				state.stream.BeginRead (state.buffer, 0, BUFFERSIZE, ClientMessage, state);
			} else {
				state.stream.Close();
			}

		}

		private static byte[] BuildPacket(Object o){
			return Encoding.UTF8.GetBytes(JObject.FromObject(o).ToString(Newtonsoft.Json.Formatting.None));
		}


		public void ProcessCommand (JObject cmdobj, NetworkStream stream)
		{
			JToken jcmd;
			int error = -51;
			Dictionary<string,object> response = new Dictionary<string, object> ();
			if (cmdobj.TryGetValue ("cmd", out jcmd)) {
				error = 0;
				string[] cmd = jcmd.ToObject<string[]> ();

				if (cmd [0] == "lastlogin") {
					response.Add("r", lastIP + " at " + lastLogin);
					error = 0;
				} else {
					Type shell = typeof(TcpShell);
					MethodInfo method = shell.GetMethod (cmd[0],BindingFlags.Public | BindingFlags.Static);

					if (method != null) {
						error = (int)(method.Invoke (null, new object[]{ cmd, response }));
					} else {
						error = -81;
					}
				}

			}//Missing required command.
			response.Add("error",error);
			if(stream.CanWrite)
			{
				byte[] pack = BuildPacket(response);
				stream.Write(pack,0,pack.Length);
				stream.Flush ();
			}
			stream.Close();

		}



	}

	public class TcpShell
	{

		[System.ComponentModel.DescriptionAttribute("List's avaliable commands and how to use them")]
		public static int help (string[] cmd, Dictionary<string,object> response)
		{
			Type shell = typeof(TcpShell);
			MethodInfo[] methods = shell.GetMethods (BindingFlags.Public | BindingFlags.Static);
			SortedDictionary<string,string>  helpstrings = new SortedDictionary<string,string>();
			foreach (MethodInfo method in methods) {
				string helptext = ((System.ComponentModel.DescriptionAttribute)method.GetCustomAttribute(typeof(System.ComponentModel.DescriptionAttribute))).Description;
				helpstrings.Add(method.Name,helptext);
			}
			response.Add("r",helpstrings);
			return 0;
		}

		[System.ComponentModel.DescriptionAttribute("Echos this command and anything after it back to you")]
		public static int echo (string[] cmd, Dictionary<string,object> response)
		{
			response.Add ("r", string.Join(" ",cmd));
			return 0;
		}

		[System.ComponentModel.DescriptionAttribute("States the uptime of the server")]
		public static int uptime (string[] cmd, Dictionary<string,object> response)
		{
			response["r"] = ( DateTime.Now - Program.StartedAt ).TotalSeconds;
			return 0;
		}

		[System.ComponentModel.DescriptionAttribute("States the load of each module")]
		public static int load(string[] cmd, Dictionary<string,object> response){
			Dictionary<string,int> data = Program.ReportLoad ();
			if (cmd.Length > 1) {
				KeyValuePair<string,int> val = data.FirstOrDefault (x => x.Key == cmd [1]);
				if (val.Key == null) {
					return -50;
				}
				Dictionary<string,int> dict = new Dictionary<string, int>();
				dict.Add(val.Key,val.Value);
				response.Add ("r", dict);
			} else {
				response.Add("r",data);
			}
			return 0;
		}

		[System.ComponentModel.DescriptionAttribute("Kill the server")]
		public static int kill(string[] cmd, Dictionary<string,object> response){
			Program.KillServer();
			return 0;
		}

		[System.ComponentModel.DescriptionAttribute("Restart the server")]
		public static int restart(string[] cmd, Dictionary<string,object> response){
			Program.RestartServer();
			return 0;
		}

		[System.ComponentModel.DescriptionAttribute("Which IP last logged in")]
		public static int lastlogin(string[] cmd, Dictionary<string,object> response){
			//Dummy command, we actually handle this in the module itself.
			return 0;
		}

		[System.ComponentModel.DescriptionAttribute("Version of this server")]
		public static int version(string[] cmd, Dictionary<string,object> response){
			response.Add("r",Program.PROGRAMNAME + ",version " + Program.VERSION);
			return 0;
		}
	}

}

