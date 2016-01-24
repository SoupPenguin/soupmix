using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using Switch.Structs;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
namespace Switch.Modules
{
    struct PeerRequest{
        public JObject json;
        public int senderHash;
        public Socket sender;
    }

    public class PeerJSModule : WebsocketModule
    {
        ConcurrentDictionary<string,Socket> users;
        ConcurrentDictionary<string,Socket> descSocket;
        public PeerJSModule() : base("PeerJS",8048)
        {
            this.Dependencies = new string[]{"User"};
        }

        public override void Load(){
            users = new ConcurrentDictionary<string, Socket>();
            descSocket = new ConcurrentDictionary<string, Socket>();
            base.Load();
        }

        public override void HandlePacket(WSPacket packet)
        {
            if (packet.opcode == WebsocketModule.OPCODE_TEXT)
            {
                PeerRequest req = new PeerRequest();
                JObject obj =  JObject.Parse(packet.textData);
                if ((string)obj["type"] == "register")
                {
                    string token = (string)obj["token"];
                    if (users.ContainsKey(token))
                    {
                        CloseSocket(users[token]);
                    }
                    users[token] = packet.sender;
                }
                else
                {
                    req.json = obj;
                    req.sender = packet.sender;
                    backendThread.Interrupt();
                    Program.debugMsgs.Enqueue(req.json.ToString());
                    if ((string)req.json["type"] == "offer") //Trying to create a room.
                    {
                        int[] uids = req.json["uids"].ToObject<int[]>();
                        string[] tokens = new string[uids.Length];
                        bool roomFailure = false;
                        for (int i = 0; i < uids.Length; i++)
                        {
                            SessionObject o = Program.userModule.RetriveSessionInfo((uint)uids[i]);
                            if (o.Equals(default(SessionObject)))
                            {
                                roomFailure = true;
                                break;
                            }
                            else
                            {
                                //Create a room.
                                tokens[i] = o.token;
                            }
                        }
                        if (roomFailure)
                        {
                            SendData(req.sender, "false");
                        }
                        else
                        {
                            string hash = Util.CalculateMD5Hash(req.json.GetValue("desc").ToString());
                            descSocket.GetOrAdd(hash, req.sender);
                            req.json.Add("senderid", hash);
                            foreach (string token in tokens)
                            {
                                try
                                {
                                    SendData(users[token], req.json.ToString());
                                }
                                catch(SocketException){
                                    SendData(req.sender, "false");
                                    break;
                                }
                            }
                        }
                    }
                    else if ((string)req.json["type"] == "answer")
                    {
                        Socket sock;
                        string desc = (string)req.json.GetValue("senderid");
                        descSocket.TryRemove(desc,out sock);
                        SendData(sock, req.json.ToString());
                    }
                }
            }
        }
    }
}

