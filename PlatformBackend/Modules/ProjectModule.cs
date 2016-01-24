using System;
using System.Security.Cryptography;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using MySql.Data.MySqlClient;
using Switch.Structs;
namespace Switch.Modules
{
    public class ProjectModule : HttpModule
    {
        public ProjectModule() : base("Project","project")
        {
            this.RequiresDatabase = true;
            this.Dependencies = new string[]{"Package","User"};
        }

        public override void Load()
        {
            acceptMethods["list"] = new string[]{"GET"};
            acceptHeaders["list"] = new string[]{"content-type","LoginToken"};

            acceptMethods["files"] = new string[]{"GET"};
            acceptHeaders["files"] = new string[]{"content-type","LoginToken"};

            acceptMethods["packages"] = new string[]{"GET"};
            acceptHeaders["packages"] = new string[]{"content-type","LoginToken"};

            acceptMethods["new"] = new string[]{"POST"};
            acceptHeaders["new"] = new string[]{"content-type","LoginToken"};

			acceptMethods["project"] = new string[]{"GET","DELETE","PUT"};
            acceptHeaders["project"] = new string[]{"content-type","LoginToken"};

            base.Load();
        }


        public void AddProjectChange(int pid, ProjectChange change){
            using (MySqlConnection conn = Program.GetMysqlConnection())
            {
                MySqlCommand cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO projectchange VALUES (NULL,@pid,@uid,@type,@message,now())";
                cmd.Parameters.AddWithValue("@pid", pid);
                cmd.Parameters.AddWithValue("@uid", change.user);
                cmd.Parameters.AddWithValue("@type", (int)change.type);
                cmd.Parameters.AddWithValue("@message", change.message);
                cmd.Prepare();
                cmd.ExecuteNonQuery();
                cmd.Dispose();
            }
            
        }

        public int CreateNewProject(string name,string framework,uint devid){
            int id = -1;
            using (MySqlConnection conn = Program.GetMysqlConnection())
            {
                MySqlCommand cmd = conn.CreateCommand();
                cmd.Parameters.AddWithValue("@devid", devid);
                cmd.Parameters.AddWithValue("@name", name);
                cmd.Parameters.AddWithValue("@status", 1);
                cmd.CommandText = "INSERT INTO project VALUES (NULL,@devid,@name,@status);SELECT LAST_INSERT_ID();";
                cmd.Prepare();
                MySqlDataReader reader = cmd.ExecuteReader(System.Data.CommandBehavior.CloseConnection);
                if (reader.Read())
                {
                    id = reader.GetInt16(0);
                }
                reader.Close();
            }
            
            return id;
        }

        public ProjectInfo GetChanges(ProjectInfo project,int limit=25)
        {
            using (MySqlConnection conn = Program.GetMysqlConnection())
            {
                MySqlCommand cmd = conn.CreateCommand();
                cmd.Parameters.AddWithValue("@pid", project.id);
                cmd.Parameters.AddWithValue("@limit", limit);
                cmd.CommandText = "SELECT uid,type,message,time\nFROM projectchange\nWHERE pid = @pid\nORDER BY time DESC\nLIMIT @limit";
                cmd.Prepare();
                MySqlDataReader reader = cmd.ExecuteReader(System.Data.CommandBehavior.CloseConnection);
                ProjectChange change = new ProjectChange();
                int rowid = 0;
                project.changes = new ProjectChange[limit];
                while (reader.Read())
                {
                    change.user = reader.GetInt16(0);
                    change.type = (ProjectChangeType)reader.GetInt16(1);
                    change.message = reader.GetString(2);
                    change.time = reader.GetDateTime(3);
                    project.changes[rowid] = change;
                    rowid++;
                }
                reader.Close();
            }
            
            return project;
        }

        public void HandleNewProject(HttpListenerContext context,UserProfile profile, out byte[] message){
            message = Encoding.UTF8.GetBytes("false");
            bool dataAvaliable = true;
            JObject obj;
            string data = "";
            while (dataAvaliable)
            {
                char c = (char)context.Request.InputStream.ReadByte();
                if (c != (char)UInt16.MaxValue)
                {
                    data += c;
                }
                else
                {
                    dataAvaliable = false;
                    context.Request.InputStream.Close();
                }
            }
            try
            {
                obj = JObject.Parse(data);
            }
            catch (JsonSerializationException)
            {
                return;
            }
            string name;
            string fw;
            try
            {
                name = obj.GetValue("name").ToObject<string>();
                fw = obj.GetValue("framework").ToObject<string>();
            }
            catch (Exception)
            {
                return;
            }
            if (name.Length < 6)
            {
                return;
            }
            int id = CreateNewProject(name, fw, profile.developer);
            //Create the default package
            int package = Program.packageModule.CreatePackage(name, "Add a description", true, true, profile.developer, -1, 3, id, fw);
            if (id == -1)
            {
                return;
            }
            //Add the right users.
            using (MySqlConnection conn = Program.GetMysqlConnection())
            {
                MySqlCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT uid
                    FROM webPlatform.user
                    WHERE developer = @devid";
                cmd.Parameters.AddWithValue("@devid", profile.developer);
                cmd.Prepare();
                MySqlDataReader reader = cmd.ExecuteReader(System.Data.CommandBehavior.CloseConnection);
                while (reader.Read())
                {
                    Program.packageModule.SubscribeUser(reader.GetUInt16(0), package);
                }
                reader.Close();
            }
            
            ProjectChange change = new ProjectChange();
            change.message = "Created Project";
            change.type = ProjectChangeType.CreatedProject;
            change.user = profile.uid;
            AddProjectChange(id, change);
            message = Encoding.UTF8.GetBytes("true");
        }

        public bool DeveloperOwnsPackage(int did,int pid){
            bool isowned = false;
            using (MySqlConnection conn = Program.GetMysqlConnection())
            {
                MySqlCommand cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT pid FROM packinfo WHERE developer = @devid AND pid = @pid";
                cmd.Parameters.AddWithValue("@devid", did);
                cmd.Parameters.AddWithValue("@pid", pid);
                cmd.Prepare();
                MySqlDataReader reader = cmd.ExecuteReader(System.Data.CommandBehavior.CloseConnection);
                isowned = reader.HasRows;
                reader.Close();
            }
            
            return isowned;
        }

        protected override bool HandleRequest(HttpListenerContext context,string action, out byte[] message)
        {
            bool isAuth = Program.userModule.CheckAuthentication(context);
            UserProfile profile = Program.userModule.GetUserProfile(context.Request.Headers.Get("LoginToken"));
            message = Encoding.UTF8.GetBytes ("false");
            string data = "";
            MySqlDataReader reader;
            MySqlCommand cmd;
            int pid;
            if(profile.Equals(default(UserProfile))){
                return true;
            }
            if (!isAuth || profile.developer == 0)
            {
                base.HandleRequest(context, action, out message);
                return true;
            }

            switch (action)
            {
        		case "project":
        			if (action == "PUT") {
        				//update
        			} else if (action == "DELETE") {
        				//remove project
        			} else if (action == "GET") {
        				//get project details.
        			}
        			break;
                case "new":
                    HandleNewProject(context,profile,out message);
                    break;
                case "packages":
                    if (int.TryParse(context.Request.QueryString.Get("pid"), out pid))
                    {    
                        List<int> packages = new List<int>();
                        using (MySqlConnection conn = Program.GetMysqlConnection())
                        {
                            cmd = conn.CreateCommand();
                            cmd.Parameters.AddWithValue("@pid", pid);
                            cmd.CommandText = "SELECT pid FROM packinfo WHERE project = @pid";
                            cmd.Prepare();
                            reader = cmd.ExecuteReader(System.Data.CommandBehavior.CloseConnection);
                            while (reader.Read())
                            {
                                packages.Add(reader.GetInt16(0));
                            }
                            reader.Close();
                            cmd.Dispose();
                        }
                        message = Encoding.UTF8.GetBytes(JArray.FromObject(packages).ToString(Formatting.None));
                        
                    }
                    break;
                case "list":
                    List<ProjectInfo> projects = new List<ProjectInfo>();
                    using (MySqlConnection conn = Program.GetMysqlConnection())
                    {
                        cmd = conn.CreateCommand();
                        ProjectInfo project = new ProjectInfo();
                        cmd.Parameters.AddWithValue("@did", profile.developer);
                        cmd.CommandText = "SELECT * FROM project WHERE devid = @did";
                        cmd.Prepare();
                        reader = cmd.ExecuteReader(System.Data.CommandBehavior.CloseConnection);
                        while (reader.Read())
                        {
                            project.id = reader.GetInt16(0);
                            project.developer = reader.GetInt16(1);
                            project.name = reader.GetString(2);
                            project.status = (ProjectStatus)reader.GetInt16(3);
                            projects.Add(project);
                        }
                        reader.Close();
                    }
                    ProjectInfo pro;
                    data = "[";
                    for(int i =0;i<projects.Count;i++){
                        pro = projects[i];
                        pro = GetChanges( pro, 1);
                        pro.changes[0].usernick = Program.userModule.GetUserProfile(pro.changes[0].user).nickname;
                        pro.changes[0].user = -1;//Hide user data
                        pro.statusline = Enum.GetName(typeof(ProjectStatus),pro.status);
                        data += JObject.FromObject(pro).ToString(Formatting.None);
                        if (i != projects.Count - 1)
                        {
                            data += ",";
                        }
                    }
                    data += "]";
                    message = Encoding.UTF8.GetBytes(data);
                    
                    break;
                case "files":
                    PackageFile listing;
                    if(int.TryParse(context.Request.QueryString.Get("pid"),out pid)){
                        
                        //check developer is owner of package
                        bool isowned = DeveloperOwnsPackage((int)profile.developer,pid);
                        if(isowned){
                            if (Program.packageModule.GetPackageFileListing(pid,out listing))
                            {
                                data = JObject.FromObject(listing).ToString(Formatting.Indented);
                                message = Encoding.UTF8.GetBytes(data);
                            }
                        }
                    }
                    break;
                default:
                    base.HandleRequest(context, action, out message);
                    break;
            }
            return true;
        } 
    }
}

