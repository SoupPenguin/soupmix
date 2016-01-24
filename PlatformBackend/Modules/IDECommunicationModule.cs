using System;
using System.Net;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using System.Web;
using Switch.Structs;
namespace Switch.Modules
{
    public class IDECommunicationModule : WebsocketModule
    {
        Dictionary<string,string> users = new Dictionary<string, string>();
        const int TOKENSIZE = 16;
        public IDECommunicationModule() : base("IDE",8047)
        {
            this.Dependencies = new string[]{"User","Project"};
        }

        private string GenerateToken(){
            Random generator = new Random((int)DateTime.Now.Ticks);
            string token = "";
            for (uint i = 0; i < TOKENSIZE; i++)
            {
                token += (char)generator.Next(35, 126);
            }
            if(users.ContainsKey(token)){
                return GenerateToken();
            }
            return token;
        }


        public override void HandlePacket(WSPacket packet)
        {
            if (packet.opcode == WebsocketModule.OPCODE_TEXT)
            {
                JObject message = new JObject();
                try{
                     message = JObject.Parse(packet.textData);
                }
                catch(Newtonsoft.Json.JsonReaderException){
                    Console.WriteLine("[IDE] Packet failed to be read!");
                    Console.WriteLine(packet.textData);
                    CloseSocket(packet.sender);
                    sockets.Remove(packet.sender);
                }
                Program.debugMsgs.Enqueue(message.ToString());
                ProjectChange change;
                JObject response = new JObject();
                int type = 0;
                bool isAuth;
                string path;
                string token;
                type = message.GetValue("type").Value<int>();
                if (type == 0)
                {
                    //Authentication
                    try
                    {
                        token = message.GetValue("token").Value<string>();
                        string agent = message.GetValue("agent").Value<string>();
                        isAuth = Program.userModule.CheckAuthentication(token, agent, ((IPEndPoint)packet.sender.RemoteEndPoint).Address.ToString());
                        if(isAuth){
                            string wstoken = GenerateToken();
                            users.Add(wstoken,token);
                            response.Add("type",0);
                            response.Add("wstoken",wstoken);
                            SendData(packet.sender,response.ToString(Newtonsoft.Json.Formatting.None));
                            return;
                        }
                    }
                    catch(Exception){
                        //Bad object.
                    }
                }
               
                token = message.GetValue("token").Value<string>();
                isAuth = users.ContainsKey(token);
                if (!users.ContainsKey(token))
                {
                    sockets.Remove(packet.sender);
                    CloseSocket(packet.sender);
                    return;
                }
                UserProfile profile = Program.userModule.GetUserProfile(users[token]);
                int pid = message.GetValue("package").Value<int>();
                isAuth = Program.projectModule.DeveloperOwnsPackage((int)profile.developer,pid);
                if (!isAuth)
                {
                    sockets.Remove(packet.sender);
                    CloseSocket(packet.sender);
                    return;
                }
                switch(type)
                {
                    case 0:
                       

                    case 1:
                        //Request file
                        response.Add("type", 1);
                        path = message.GetValue("file").Value<string>();

                        string mime = MimeMapping.GetMimeMapping(path).Split(new char[]{ '/' }, 2)[0];

                        string data;

                        if (mime != "text" && mime != "application")
                        {
                            response.Add("data", "This type of file is not supported by the code editor. Sorry about that :/");
                        }
                        else if (Program.packageModule.GetPackageFile(pid, path,out data))
                        {
                            response.Add("data", data);
                        }

                        SendData(packet.sender,response.ToString(Newtonsoft.Json.Formatting.None));
                        break;
                    case 2:
                        //Save changes
                        path = message.GetValue("file").Value<string>();
                        data = message.GetValue("data").Value<string>();
                        bool fileexists = message.GetValue("exists").Value<bool>();

                        byte[] bdata = Convert.FromBase64String(data);
                        Program.packageModule.SavePackageFile(pid, path, bdata);
                        change = new ProjectChange();
                        change.user = profile.uid;

                        if (fileexists)
                        {
                            change.message = "File '" + path + "' edited";
                            change.type = ProjectChangeType.ChangedFile;
                        }
                        else
                        {
                            change.message = "File '" + path + "' added";
                            change.type = ProjectChangeType.AddedNewFile;
                        }

                        Program.projectModule.AddProjectChange(pid, change);
                        if (!fileexists)
                        {
                            response.Add("type", 2);
                            SendData(packet.sender, response.ToString(Newtonsoft.Json.Formatting.None));
                        }
                        break;
                    case 3:
                        //Add new dir
                        path = message.GetValue("path").Value<string>();
                        Program.packageModule.AddProjectDirectory(pid, path);
                        change = new ProjectChange();
                        change.user = profile.uid;
                        change.type = ProjectChangeType.AddedNewFile;
                        change.message = "Directory " + path + " added";
                        response.Add("type", 2);
                        SendData(packet.sender,response.ToString(Newtonsoft.Json.Formatting.None));
                        break;
                    case 4:
                        path = message.GetValue("file").Value<string>();
                        Program.packageModule.DeletePackageFile(pid, path);

                        change = new ProjectChange();
                        change.message = "File '" + path + "' deleted";
                        change.type = ProjectChangeType.DeletedFile;
                        change.user = profile.uid;
                        Program.projectModule.AddProjectChange(pid,change);
                        break;
                    case 6:
                        //Test game == repackage game.
                        Program.packageModule.CreatePackagePack(pid);
                        response.Add("type", 6);
                        SendData(packet.sender,response.ToString(Newtonsoft.Json.Formatting.None));
                        break;
                }
            }
        }
    }
}

