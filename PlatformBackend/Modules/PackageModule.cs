using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Switch.Structs;
using System.Web;
using MySql.Data.MySqlClient;
namespace Switch.Modules
{
    struct FileWrite{
        public string path;
        public byte[] data;
    }

    public class PackageModule : HttpModule
    {
        const string rootDir = "storage/";

        private ConcurrentDictionary<string,DirectoryInfo> fPackages;
        private List<string> templates = new List<string>(){"vanilla","phaser","pixijs"};
        private Timer indexTimer;
        const int sendBufferSize = 1048576 * 8;
        const int IOTIMEOUT = 5000;
        ConcurrentQueue<FileWrite> writes;
        public PackageModule() : base("Package","package")
        {
            sleepFor = TimeSpan.FromMilliseconds(25);
            this.RequiresDatabase = true;
            this.Dependencies = new string[]{"User"};
        }

        public void DeletePackageFile(int pid,string p){
            string path = rootDir + "packages/" + pid + "/" + p;
            if(path.Contains("..")){
                return;
            }
            FileInfo f = new FileInfo(path);
            if (f.Attributes == FileAttributes.Directory)
            {
                Directory.Delete(path, true);
            }
            else
            {
                File.Delete(path);
            }
        }

        public void SavePackageFile(int pid,string p,byte[] data){
            string path = rootDir + "packages/" + pid + "/" + p;
            if (path.Contains("..") || path.EndsWith("/"))
            {
                //Disallowed!
                return;
            }
            FileWrite write = new FileWrite();
            write.data = data;
            write.path = path;
            writes.Enqueue(write);
            File.SetLastWriteTime(rootDir + "packages/" + pid, DateTime.Now);
                
        }


        public bool GetPackageFile(int pid,string p,out string file){
            string path = rootDir + "packages/" + pid + "/" + p;
            file = "";

            if(path.Contains("..")){
                return false;
            }

            if (File.Exists(path))
            {
                file = File.ReadAllText(path, Encoding.UTF8);
                return true;
            }

            file = null;
            return false;
        }

        public void SubscribeUser(int uid,int pid){
            using(MySqlConnection conn = Program.GetMysqlConnection()){
                MySqlCommand cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO packsub VALUES (NULL,@pid,@uid);INSERT INTO packstat VALUES (@pid,@uid,NOW(),0);";
                cmd.Parameters.AddWithValue("@uid", uid);
                cmd.Parameters.AddWithValue("@pid", pid);
                cmd.Prepare();
                cmd.ExecuteNonQuery();
            }
            
        }

        public bool GetPackageFileListing(int package,out PackageFile output,string path = ""){
            PackageFile root = new PackageFile();
            if(path == ""){
                path = rootDir + "packages/" + package;
                root.name = package.ToString();
            }
            if (Directory.Exists(path))
            {
                string[] directories = Directory.GetDirectories(path);
                string[] files = Directory.GetFiles(path);
                string[] items = new string[directories.Length + files.Length];
                directories.CopyTo(items, 0);
                files.CopyTo(items, directories.Length);
                root.children = new PackageFile[items.Length];
                PackageFile pf;
                FileInfo f;
                int i = 0;
                foreach (string file in items)
                {
                    f = new FileInfo(file);
                    pf = new PackageFile();
                    if(f.Attributes.HasFlag(FileAttributes.Directory)){
                        GetPackageFileListing(package,out pf,file);
                        pf.name = f.Name;
                    }
                    else
                    {
                        pf.name = f.Name;
                        pf.type = MimeMapping.GetMimeMapping(f.Name);
                        pf.size = f.Length;
                    }
                    root.children[i] = pf;
                    i++;
                }
                output = root;
                return true;
            }
            else
            {
                output = root;
                return false;
            }
        }

        public void AddProjectDirectory(int pid,string p){
            string path = rootDir + "packages/" + pid + "/" + p;
            if (!path.Contains(".."))
            {
                Directory.CreateDirectory(path);
            }

        }

        public int CreatePackage(string name,string description,bool hidden,bool executable,uint developer,int publisher,int browsers,int project,string template = ""){
            int id = 0;
            using (MySqlConnection conn = Program.GetMysqlConnection())
            {
                MySqlCommand cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO packinfo VALUES (NULL,@name,@des,@hidden,@exe,@dev,@pub,@browsers,NOW(),@project);SELECT LAST_INSERT_ID();";
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@des", description);
                cmd.Parameters.AddWithValue("@hidden", hidden);
                cmd.Parameters.AddWithValue("@exe", executable);
                cmd.Parameters.AddWithValue("@dev", developer);
                cmd.Parameters.AddWithValue("@pub", publisher);
                cmd.Parameters.AddWithValue("@browsers", browsers);
                cmd.Parameters.AddWithValue("@project", project);
                cmd.Prepare();
                MySqlDataReader reader = cmd.ExecuteReader(System.Data.CommandBehavior.CloseConnection);
                reader.Read();
                id = reader.GetInt16(0);
                reader.Close();
            }
            

            //Create the directory
            Directory.CreateDirectory(rootDir+"packages/"+id);

            if (templates.Contains(template))
            {
                string[] files = Directory.GetFiles(rootDir + "templates/" + template, "*", SearchOption.AllDirectories);
                foreach (string file in files)
                {
                    if(file.StartsWith(".")){
                        continue;
                    }
                    FileInfo fi = new FileInfo(file);    

                    File.Copy(file, rootDir + "packages/" + id + "/" + fi.Name);
                }
            }

            return id;
        }

        public void CreatePackagePack(int package){
            CreatePackagePack(new DirectoryInfo(rootDir + "packages/" + package));
        }

        private void CreatePackagePack(DirectoryInfo package){
            string binpath = rootDir + "binaries/" + package.Name;
            File.Delete(binpath);
            MemoryStream stream;
            stream = new MemoryStream();
            FileInfo[] files = package.GetFiles("*", SearchOption.AllDirectories);
            JObject header = new JObject();
            MD5 md5 = MD5.Create();
            foreach (FileInfo file in files)
            {
                if (file.Name.StartsWith("."))
                {
                    continue;
                }
                string name = file.FullName.Replace(package.FullName,"");
                JObject obj = new JObject();
                obj.Add("length", file.Length);
                obj.Add("mime", MimeMapping.GetMimeMapping(file.Name));

                byte[] hash = md5.ComputeHash(file.OpenRead());
                obj.Add("md5", Encoding.UTF8.GetString(hash));

                header.Add(name, obj);
            }
            byte[] bhead = UTF8Encoding.UTF8.GetBytes(header.ToString(Formatting.None));
            byte[] headsize = Encoding.UTF8.GetBytes(bhead.Length.ToString().PadLeft(5));
            stream.Write(headsize, 0, headsize.Length);
            stream.Write(bhead, 0, bhead.Length);
            stream.Flush();
            int buffSize = (int)(Math.Pow(1024, 2) * 4);
            byte[] buffer = new byte[buffSize];
            foreach (FileInfo file in files)
            {
                if (file.Name.StartsWith("."))
                {
                    continue;
                }
                FileStream fs = file.OpenRead();
                int len;
                while ((len = fs.Read(buffer, 0, buffSize)) > 0)
                {
                    stream.Write(buffer, 0, len);
                    stream.Flush();
                }
                fs.Close();
            }

            FileWrite write = new FileWrite();
            write.data = stream.GetBuffer();
            write.path = binpath;
            writes.Enqueue(write);
            stream.Close();
        }

        public void IndexPackageFiles(object state){
            List<string> removedFiles = new List<string>(fPackages.Keys);
            foreach(string f in Directory.GetDirectories(rootDir+"packages")){
                DirectoryInfo oi;
                DirectoryInfo fi = new DirectoryInfo(f);
                if (fPackages.TryGetValue(fi.Name,out oi))
                {
                    removedFiles.Remove(fi.Name);
                    if (oi.LastWriteTimeUtc.TimeOfDay.TotalSeconds != fi.LastWriteTimeUtc.TimeOfDay.TotalSeconds)
                    {
                        Program.debugMsgs.Enqueue("Package changed '" + f + "'.");
                        CreatePackagePack(fi);
                        fPackages.TryRemove(fi.Name,out oi);
                        fPackages[fi.Name] = fi;
                        using (MySqlConnection conn = Program.GetMysqlConnection())
                        {
                            MySqlCommand cmd = conn.CreateCommand();
                            cmd.CommandText = "USE webPlatform;\nUPDATE packinfo SET packinfo.lastupdate = now() WHERE packinfo.pid = @pid;";
                            cmd.Parameters.AddWithValue("@pid", fi.Name);
                            cmd.Prepare();
                            cmd.ExecuteNonQuery();
                        }
                        
                    }
                }
                else
                {
                    //New File
                    Program.debugMsgs.Enqueue("New Package found '" + f + "'.");
                    fPackages.TryAdd(fi.Name, fi);
                    CreatePackagePack(fi);
                }
            }
            foreach (string f in removedFiles)
            {
                DirectoryInfo df;
                fPackages.TryRemove(f,out df);
                Program.debugMsgs.Enqueue("Package deleted '" + f + "'.");
            }
        }

        public override void Load()
        {
            shouldRun = true;
            Program.httpModule.prefixModList.Add("package/",this);
            acceptMethods = new Dictionary<string, string[]>();
            acceptHeaders = new Dictionary<string, string[]>();
            acceptMethods["getsubs"] = new string[]{"GET"};acceptHeaders["getsubs"] = new string[]{ "Content-Type", "LoginToken" };
            acceptMethods["getsubdata"] = new string[]{"GET"};acceptHeaders["getsubdata"] = acceptHeaders["getsubs"];
            acceptMethods["details"] = new string[]{"GET"};acceptHeaders["details"] = acceptHeaders["getsubs"];
            requests = new ConcurrentQueue<HttpListenerContext>();
            fPackages = new ConcurrentDictionary<string, DirectoryInfo>();
            //Setup Storage
            if (!Directory.Exists(rootDir))
            {
                Directory.CreateDirectory(rootDir);
                Directory.CreateDirectory(rootDir + "packages");
                Directory.CreateDirectory(rootDir + "media");
                Directory.CreateDirectory(rootDir + "binaries");
            }
            writes = new ConcurrentQueue<FileWrite>();
            indexTimer = new Timer(IndexPackageFiles, null, 500, 100);
            base.Load();
        }

        private bool IsUserSubbed(int pid,int uid){
            const string SQLSTATEMENT = @"
                SELECT packsub.pid
                FROM packsub
                WHERE packsub.pid = @pid
                AND packsub.uid = @uid
                ";
            bool result = false;
            using (MySqlConnection conn = Program.GetMysqlConnection())
            {
                MySqlCommand cmd = conn.CreateCommand();
                cmd.CommandText = SQLSTATEMENT;
                cmd.Parameters.AddWithValue("@uid", uid);
                cmd.Parameters.AddWithValue("@pid", pid);
                cmd.Prepare();
                MySqlDataReader reader = cmd.ExecuteReader(System.Data.CommandBehavior.SingleRow | System.Data.CommandBehavior.CloseConnection);
                result = reader.HasRows;
                reader.Close();
            }
            
            return result;
        }


        private void GetSubs(string token,bool getDetail,out byte[] message){
            SessionObject session;
            MySqlDataReader reader;
            UserProfile user = Program.userModule.GetUserProfile(token);
            if (!Program.userModule.currentSessions.TryGetValue(token, out session))
            {
                message = Encoding.UTF8.GetBytes("false");
                return;
            }
            string SQLSTATEMENT;
            if (getDetail)
            {
                SQLSTATEMENT = @"
                SELECT  packinfo.pid, packinfo.name, description, ishidden, executable, browsers, developer.name, publisher.name, developer, publisher, lastupdate
                FROM packinfo,packsub, developer, publisher
                WHERE packsub.uid = @uid
                AND packsub.pid = packinfo.pid
                ";
            }
            else
            {
                SQLSTATEMENT = @"SELECT packsub.pid FROM packsub WHERE packsub.uid = @uid";
            }
            var packages = new List<object>();
            using (MySqlConnection conn = Program.GetMysqlConnection())
            {
                MySqlCommand cmd = conn.CreateCommand();
                cmd.CommandText = SQLSTATEMENT;
                cmd.Parameters.AddWithValue("@uid", session.uid);
                cmd.Prepare();
                reader = cmd.ExecuteReader(System.Data.CommandBehavior.CloseConnection);
                while (reader.Read())
                {
                    if (getDetail)
                    {
                        PackageInfo packInfo;
                        packInfo.id = reader.GetInt16(0);
                        packInfo.name = reader.GetString(1);
                        packInfo.description = reader.GetString(2);
                        packInfo.hidden = (reader.GetInt16(3) == 1);
                        packInfo.executable = (reader.GetInt16(4) == 1);
                        BitArray browserSupport = new BitArray(new byte[1]{ (byte)reader.GetInt16(5) });
                        packInfo.supportsFirefox = browserSupport[0];
                        packInfo.supportsChrome = browserSupport[1];
                        packInfo.supportsOpera = browserSupport[2];
                        packInfo.supportsIE = browserSupport[3];
                        packInfo.supportsSafari = browserSupport[4];
                        packInfo.developer.name = reader.GetString(6);
                        packInfo.publisher.name = reader.GetString(7);
                        packInfo.developer.id = reader.GetInt16(8);
                        packInfo.publisher.id = reader.GetInt16(9);
                        packInfo.updated = (long)reader.GetDateTime(10).ToUniversalTime().Subtract(new DateTime(1970, 1, 1, 0, 0, 0)).TotalSeconds;
                        //Logos
                        packInfo.mediumlogo = "media/logos/" + packInfo.id + "_medium.png";
                        packInfo.boxart = "media/logos/" + packInfo.id + "_box.png";

                        if (packInfo.executable && (!packInfo.hidden || packInfo.developer.id == user.developer))
                        {
                            packages.Add(packInfo);
                        }
                    }
                    else
                    {
                        packages.Add(reader.GetInt16(0));
                    }
                }
                reader.Close();
            }
            
            string json = JsonConvert.SerializeObject(packages.ToArray());
            message = Encoding.UTF8.GetBytes(json);
        }

        public override void UpdateProcess()
        {
            FileWrite write;
            while (writes.TryDequeue(out write))
            {
                try
                {
                    File.WriteAllBytes(write.path, write.data);
                }
                catch(IOException){
                    writes.Enqueue(write);
                }
            }
            base.UpdateProcess();
        }

        protected override bool HandleRequest(HttpListenerContext con, string action, out byte[] message)
        {
            message = Encoding.UTF8.GetBytes("false");
            bool isAuth = Program.userModule.CheckAuthentication(con);
            string token =  con.Request.Headers.Get("LoginToken");
            switch (action)
            {
                case "getsubs":
                    if (isAuth)
                    {
                        bool packageData = ("1" == con.Request.QueryString.Get("detail"));
                        GetSubs(token,packageData, out message);
                    }
                    break;
                case "getsubdata":
                    int pid;
                    int packetN;
                    SessionObject session;
                    string spid = con.Request.QueryString.Get("pid");
                    string spacketN = con.Request.QueryString.Get("n");

                    if (!int.TryParse(spacketN, out packetN) || !int.TryParse(spid, out pid))
                    {
                        message = UTF8Encoding.UTF8.GetBytes("false");
                        break;
                    }

                    if (!isAuth)
                    {
                        message = UTF8Encoding.UTF8.GetBytes("false");
                        break;
                    }
                    if (!Program.userModule.currentSessions.TryGetValue(token, out session))
                    {
                        message = UTF8Encoding.UTF8.GetBytes("false");
                        break;
                    }

                    if (IsUserSubbed(pid, (int)session.uid))
                    {
                        con.Response.ContentType = "application/octet-stream";
                        string file = rootDir + "binaries/" + pid;
                        byte[] buffer;
                        byte[] headbuf = new byte[5];
                        FileStream fs;
                        fs = new FileInfo(file).OpenRead();
                        fs.Read(headbuf, 0, 5);
                        fs.Close();
             
                        string headersizeS = Encoding.UTF8.GetString(headbuf);
                        int headSize = int.Parse(headersizeS) + 5;
                        if (packetN == 0)
                        {
                            buffer = new byte[sendBufferSize + headSize];
                        }
                        else
                        {
                            buffer = new byte[sendBufferSize];
                        }
                        long seekBy = 0;
                        if (packetN > 0)
                        {
                            seekBy += headSize;
                            while (packetN > 0)
                            {
                                seekBy += sendBufferSize;
                                packetN--;
                            }
                        }
                        message = new byte[0];
                        using (fs = new FileInfo(file).OpenRead())
                        {
                            fs.Seek(seekBy, SeekOrigin.Begin);
                            int read = fs.Read(buffer, 0, buffer.Length);
                            fs.Close();
                            con.Response.OutputStream.BeginWrite(buffer, 0, read,OnPackageStreamFinished,con);
                            return false;
                        }
                    }
                    break;
                case "details":
                    if (isAuth && token != null)
                    {
                        //Get details about a package, with relavence to the user.
                        using (MySqlConnection conn = Program.GetMysqlConnection())
                        {
                            MySqlCommand cmd = conn.CreateCommand();
                            spid = con.Request.QueryString.Get("pid");
                            uint uid = Program.userModule.currentSessions[token].uid;
                            cmd.CommandText = "SELECT subbedon, playtime FROM packstat WHERE uid = @uid AND pid = @pid";
                            cmd.Parameters.AddWithValue("@uid", uid);
                            cmd.Parameters.AddWithValue("@pid", spid);
                            cmd.Prepare();
                            JObject response = new JObject();
                            MySqlDataReader reader = cmd.ExecuteReader(System.Data.CommandBehavior.CloseConnection);
                            if (reader.Read())
                            {
                                response.Add("id", int.Parse(spid));
                                response.Add("subdate", reader.GetDateTime(0).ToShortDateString());
                                response.Add("playtime", reader.GetInt32(1));
                                message = Encoding.UTF8.GetBytes(response.ToString());
                            }
                            reader.Close();
                        }
                        
                    }
                    else
                    {
                        message = UTF8Encoding.UTF8.GetBytes("false");
                    }
                    break;
                default:
                    base.HandleRequest(con, action, out message);
                    break;
            }
            return true;
        }
           
        private void OnPackageStreamFinished(IAsyncResult result){
            HttpListenerContext con = (HttpListenerContext)result.AsyncState;
            try
            {
                con.Response.OutputStream.EndWrite(result);
                con.Response.Close();
            }
            catch(IOException){
                con.Response.Close();
                #if TRACEPACK
                Program.debugMsgs.Enqueue("[Package] Client transfer failed! Socket Failure");
                #endif
            }
            #if TRACEPACK
            Program.debugMsgs.Enqueue("[Package] Client transfer succeded!");
            #endif
        }


        public override void Unload(){
            indexTimer.Dispose();
            base.Unload();
        }
    }
}

