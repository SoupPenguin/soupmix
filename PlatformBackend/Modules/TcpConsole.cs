using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Text;
namespace SoupMix
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
			s.Write (msg, 0, msg.Length);
			s.Flush ();

			if (ecode == 0) {
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
			JToken cmd;
			if (cmdobj.TryGetValue ("cmd", out cmd)) {
				byte[] msg = BuildPacket (new { response = "Hi!" });
				stream.Write (msg, 0, msg.Length);
				stream.Flush ();
				if (cmdobj.GetValue ("keep-alive") == null) {
					stream.Close ();
				}
			} else {
				stream.Close();//Missing required command.
			}

		}
	}

}

