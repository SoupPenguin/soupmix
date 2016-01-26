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

using System.Security.Cryptography;

namespace Switch.Modules
{
    public class UserModule : HttpModule
    {
        const uint MAXSESSIONTIME = 172800; // 2 days
        const uint TOKENSIZE = 256;
        const int HASHITERATIONS = 25000;
        const bool RequireInvite = true;
        public Dictionary<string,SessionObject> currentSessions; 
        public UserModule() : base("User","user")
        {
            this.RequiresDatabase = true;
        }

        private string GenerateToken(){
            Random generator = new Random((int)DateTime.Now.Ticks);
            string token = "";
            for (uint i = 0; i < TOKENSIZE; i++)
            {
                token += (char)generator.Next(35, 126);
            }
            if(currentSessions.ContainsKey(token)){
                return GenerateToken();
            }
            return token;
        }

        public SessionObject RetriveSessionInfo(uint id){
            foreach(SessionObject s in currentSessions.Values){
                if (s.uid == id)
                    return s;
            }
            return default(SessionObject);
        }

        private bool CheckPrivilege(int uid,string priv){
            bool hasperm = false;
            using (MySqlConnection conn = Program.GetMysqlConnection())
            {
                MySqlCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT @priv
                    FROM permission
                    WHERE uid = @uid";
                cmd.Parameters.AddWithValue("@priv", priv);
                cmd.Parameters.AddWithValue("@uid", uid);
                cmd.Prepare();
                MySqlDataReader read = cmd.ExecuteReader(System.Data.CommandBehavior.CloseConnection);
                read.Read();
                hasperm = (read.GetInt16(0) == 1);
            }
            return hasperm;
        }

        private void CleanDatabase(object state){
            Program.debugMsgs.Enqueue("Cleaning Database");
            using (MySqlConnection conn = Program.GetMysqlConnection())
            {
                MySqlCommand cmd = conn.CreateCommand();
                cmd.CommandText = @"
            DELETE user, friend, packsub, profile, lastLogin
            FROM user  
            INNER JOIN friend ON (user.uid = friend.uidA OR user.uid = friend.uidB)
            INNER JOIN packsub ON (user.uid = packsub.uid)
            INNER JOIN profile ON (user.uid = profile.uid)
            INNER JOIN lastLogin ON (user.uid = lastLogin.uid)
            WHERE user.verified = 0 
            AND TIME_TO_SEC(TIMEDIFF(now(),user.dateCreated)) > 60*60*24*2;";
                cmd.Prepare();
                Program.debugMsgs.Enqueue("Removed " + cmd.ExecuteNonQuery() + " users from the database.");
            }

        }

        public override void Load()
        {
            acceptMethods["auth"] = new string[]{"POST"}; acceptHeaders["auth"] = new string[]{"content-type"};
            acceptMethods["signup"] = new string[]{"POST"}; acceptHeaders["signup"] = new string[]{"content-type"};
            acceptMethods["deauth"] = new string[]{"GET"}; acceptHeaders["deauth"] = new string[]{"content-type","LoginToken"};
            acceptMethods["profile"] = new string[]{"GET"}; acceptHeaders["profile"] = new string[]{"content-type","LoginToken"};
            acceptMethods["friends"] = new string[]{"GET"}; acceptHeaders["friends"] = new string[]{"content-type","LoginToken"};
            acceptMethods["permissions"] = new string[]{"GET"}; acceptHeaders["permissions"] = new string[]{"content-type","LoginToken"};
            acceptMethods["devinfo"] = new string[]{"GET"}; acceptHeaders["devinfo"] = new string[]{"content-type"};
            acceptMethods["bug"] = new string[]{"POST"}; acceptHeaders["bug"] = new string[]{"content-type"};
            currentSessions = new Dictionary<string, SessionObject>();
            //databaseCleanTimer = new System.Threading.Timer(CleanDatabase,null,5000,60*60*1000*5);
            base.Load();
        }

        private void AuthenticateUser(HttpListenerContext con,out byte[] message){
            string data = "";
            bool dataAvaliable = true;
            bool validationOK = true;
            uint uid = 0;
            while (dataAvaliable)
            {
                char c = (char)con.Request.InputStream.ReadByte();
                if (c != (char)UInt16.MaxValue)
                {
                    data += c;
                }
                else
                {
                    dataAvaliable = false;
                    con.Request.InputStream.Close();
                }
            }
            UsernamePasswordPair uandp = new UsernamePasswordPair();
            try
            {
                uandp = JsonConvert.DeserializeObject<UsernamePasswordPair>(data);
            }
            catch(JsonReaderException){
                con.Response.StatusCode = 406;
            }

            if (!uandp.Equals(default(UsernamePasswordPair)))
            {
                if (uandp.username.Length <= 4 && uandp.username.Length > 128)
                {
                    validationOK = false;
                }
                else if (uandp.password.Length <= 4 && uandp.password.Length > 128)
                {
                    validationOK = false;
                }
                using (MySqlConnection conn = Program.GetMysqlConnection())
                {
                    MySqlCommand cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT uid,password FROM webPlatform.user WHERE username = @username";
                    cmd.Parameters.AddWithValue("@username", uandp.username);
                    cmd.Prepare();
                    MySqlDataReader reader = cmd.ExecuteReader(System.Data.CommandBehavior.SingleResult | System.Data.CommandBehavior.CloseConnection);

                    if (reader.HasRows)
                    {
                        reader.Read();
                        uid = (uint)reader.GetInt32(0);
                        string passsalt = (string)reader.GetValue(1);
                        string salt = passsalt.Substring(0, 64);
                        string checkhash = HashPassword(uandp.password, salt);
                        if (checkhash != passsalt)
                        {
                            validationOK = false;
                        }
                    }
                    else
                    {
                        validationOK = false;
                    }
                    reader.Close();
                }

            }
            else
            {
                message = System.Text.UTF8Encoding.Default.GetBytes("{\"error\":\"Invalid JSON\"}");
            }

            if (!validationOK)
            {
                message = System.Text.UTF8Encoding.Default.GetBytes("{\"error\":\"Wrong Username/Password\"}");
            }
            else
            {
                SessionObject session = new SessionObject();
                session.agent = con.Request.UserAgent;
                session.host = con.Request.RemoteEndPoint.Address.ToString();
                session.timestamp = Util.GetEpoch();
                session.token = GenerateToken();
                session.uid = uid;
                currentSessions.Add(session.token, session);
                TokenReply reply;
                reply.token = session.token;
                message = System.Text.UTF8Encoding.Default.GetBytes(JsonConvert.SerializeObject(reply));

                //Log this login
                using (MySqlConnection conn = Program.GetMysqlConnection())
                {
                    MySqlCommand cmd = conn.CreateCommand();
                    cmd.CommandText = "INSERT INTO webPlatform.lastLogin VALUES(@uid,now(),@host)";
                    cmd.Parameters.AddWithValue("@uid", uid);
                    cmd.Parameters.AddWithValue("@host", session.host);
                    cmd.Prepare();
                    cmd.ExecuteNonQuery();
                }
                
                //Now we wait a little bit to throw off any brute forcers.
                Thread.Sleep( new Random(DateTime.Now.Millisecond).Next(0,1000));
            }
        }

        public static string HashPassword(string password,string salt = ""){
            //Generate a salt
            if (salt == "")
            {
                Random generator = new Random((int)DateTime.Now.Ticks);
                for (uint i = 0; i < 64; i++)
                {
                    salt += (char)generator.Next(35, 126);
                }
            }

			Rfc2898DeriveBytes encryptor = new  Rfc2898DeriveBytes(password,System.Text.Encoding.UTF8.GetBytes(salt),25000);
			string hashpword = System.Text.Encoding.UTF8.GetString(encryptor.GetBytes(512));

            return salt + hashpword;
        }

        public bool CheckAuthentication(HttpListenerContext con){
            string token = con.Request.Headers.Get("LoginToken");
            string useragent = con.Request.UserAgent;
            string host = con.Request.RemoteEndPoint.Address.ToString();
            return CheckAuthentication(token,useragent,host);
        }

        public bool CheckAuthentication(string token,string useragent,string hostaddress){
            if (token == null)
            {
                return false;
            }
            bool isValid = true;
            SessionObject obj;
            isValid = currentSessions.TryGetValue(token, out obj);
            if (isValid)
            {
                isValid = (obj.agent == useragent) && obj.host == hostaddress && (obj.timestamp + MAXSESSIONTIME > Util.GetEpoch()); 
                //Remove the session as it's either been partially compromised, or timestamp is up.
                if (!isValid)
                {
                    currentSessions.Remove(token);
                }
            }
            return isValid;
        }

        private int HashStringToInt(string str){
            int val = 0;
            foreach(char c in str){
                val += (int)c;
            }
            return val;
        }


        private bool InsertNewUser(UserSignupObject request){
            const string SQLSTATEMENT = @"USE webPlatform;
            INSERT INTO user (uid,username,password,dateCreated,email,verified,developer) VALUES(NULL,@username,@password,now(),@email,0,0);
            INSERT INTO profile (uid,nickname) VALUES(LAST_INSERT_ID(),@nickname);
            INSERT INTO permission VALUES(LAST_INSERT_ID(),1,1,0,0,0,0)";
            bool worked = false;
            using (MySqlConnection conn = Program.GetMysqlConnection())
            {
                MySqlCommand cmd = conn.CreateCommand();
                cmd.CommandText = SQLSTATEMENT;
                cmd.Parameters.AddWithValue("@username", request.username);
                cmd.Parameters.AddWithValue("@password", HashPassword(request.password));
                cmd.Parameters.AddWithValue("@email", request.email);
                cmd.Parameters.AddWithValue("@nickname", request.nickname);
                cmd.Prepare();
                worked = (cmd.ExecuteNonQuery() > 0);
            }
            
            if(RequireInvite){
                int uid = HashStringToInt(request.username);
                using (MySqlConnection conn = Program.GetMysqlConnection())
                {
                    MySqlCommand cmd = conn.CreateCommand();
                    cmd.CommandText = "USE webPlatform;UPDATE inviteCode SET uid = LAST_INSERT_ID() WHERE uid = @uid;";
                    cmd.Parameters.AddWithValue("@uid", uid);
                    cmd.Prepare();
                    cmd.ExecuteNonQuery();
                }
                
            }

            if (worked)
            {
              //Send mail.      
                
            }

            return worked;

        }

        private bool CreateUserRequest(HttpListenerContext con,out UserSignupObject obj){
            string data = "";
            bool dataAvaliable = true;
            obj = new UserSignupObject();
            while (dataAvaliable)
            {
                char c = (char)con.Request.InputStream.ReadByte();
                if (c != (char)UInt16.MaxValue)
                {
                    data += c;
                }
                else
                {
                    dataAvaliable = false;
                    con.Request.InputStream.Close();
                }
            }
            try
            {
                obj  = JsonConvert.DeserializeObject<UserSignupObject>(data);
            }
            catch(JsonSerializationException){
                return false;
            }


            if (obj.username != null && obj.password != null && obj.email != null)
            {
                if (obj.username.Length < 6 || obj.email.Length < 6 || obj.password.Length < 6 || new System.Net.Mail.MailAddress(obj.email).Address != obj.email || obj.nickname.Length < 1)
                {
                    return false;
                }
                if (CheckUsernameExists(obj.username))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            if (obj.username.Length > 128 || obj.password.Length > 128 || obj.email.Length > 128 || obj.nickname.Length > 32)
            {
                return false;
            }

            if (RequireInvite)
            {
                if (obj.invite != null)
                {
                    if (obj.invite.Length > 30)
                    {
                        return false;
                    }
                    //Create a temp uid
                    int uid = HashStringToInt(obj.username);
                    bool inviteAccepted = false;
                    //Check invite
                    using (MySqlConnection conn = Program.GetMysqlConnection())
                    {
                        MySqlCommand cmd = conn.CreateCommand();
                        cmd.CommandText = "UPDATE inviteCode SET uid = @uid WHERE code = @code AND uid = -1";
                        cmd.Parameters.AddWithValue("@code", obj.invite);
                        cmd.Parameters.AddWithValue("@uid", uid);
                        cmd.Prepare();
                        inviteAccepted = (cmd.ExecuteNonQuery() > 0);
                    }
                    
                    return inviteAccepted;
                }
            }

            return true;

        }

        private bool CheckUsernameExists(string username){
            const string SQLSTATEMENT = @"USE webPlatform;
            SELECT uid FROM webPlatform.user WHERE username = @username";
            bool hasRows = false;
            using (MySqlConnection conn = Program.GetMysqlConnection())
            {
                MySqlCommand cmd = conn.CreateCommand();
                cmd.CommandText = SQLSTATEMENT;
                cmd.Parameters.AddWithValue("@username", username);
                cmd.Prepare();
                MySqlDataReader reader = cmd.ExecuteReader(System.Data.CommandBehavior.CloseConnection);
                hasRows = reader.HasRows;
                reader.Close();
            }
            
            return hasRows;
        }


        public static string GetGravatar(string email)
        {
            email = email.ToLower();
            // Create a new instance of the MD5CryptoServiceProvider object.  
            MD5 md5Hasher = MD5.Create();

            // Convert the input string to a byte array and compute the hash.  
            byte[] data = md5Hasher.ComputeHash(Encoding.Default.GetBytes(email));

            // Create a new Stringbuilder to collect the bytes  
            // and create a string.  
            StringBuilder sBuilder = new StringBuilder();

            // Loop through each byte of the hashed data  
            // and format each one as a hexadecimal string.  
            for(int i = 0; i < data.Length; i++)
            {
                sBuilder.Append(data[i].ToString("x2"));
            }
            return "http://www.gravatar.com/avatar/" + sBuilder.ToString();  // Return the hexadecimal string. 
        }

        public UserProfile[] GetUserProfiles(int[] uids){
            UserProfile[] profiles;
            using (MySqlConnection conn = Program.GetMysqlConnection())
            {
                MySqlCommand cmd = conn.CreateCommand();
                string uidstring = String.Join(",", uids);
                cmd.CommandText = "SELECT nickname,email,developer\nFROM user, profile\nWHERE user.uid = profile.uid AND user.uid IN (" + uidstring + ")";
                cmd.Prepare();
                MySqlDataReader reader = cmd.ExecuteReader(System.Data.CommandBehavior.CloseConnection);
                profiles = new UserProfile[uids.Length];
                int i = 0;
                while (reader.Read())
                {
                    profiles[i].uid = uids[i];
                    profiles[i].nickname = reader.GetString(0);
                    profiles[i].avatar = GetGravatar(reader.GetString(1));
                    i++;
                }
                reader.Close();
            }
            
            return profiles;
        }


        public UserProfile GetUserProfile(string token) {
            try
            {
             SessionObject obj = currentSessions[token];
             return GetUserProfile((int)obj.uid);
            }
            catch(Exception){
                return default(UserProfile);
            }

        }

        public void HandleBugReport(HttpListenerContext con, out byte[] message){
            message = Encoding.UTF8.GetBytes("false");
            string data = "";
            bool dataAvaliable = true;
            JObject obj;
            while (dataAvaliable)
            {
                char c = (char)con.Request.InputStream.ReadByte();
                if (c != (char)UInt16.MaxValue)
                {
                    data += c;
                }
                else
                {
                    dataAvaliable = false;
                    con.Request.InputStream.Close();
                }
            }
            try
            {
                obj  = JObject.Parse(data);
            }
            catch(JsonException){
                return;
            }

            string category = "";
            int game = 0;
            string description = "";
            string useragent = "";
            try{
                category = obj.GetValue("category").Value<string>();
                description = obj.GetValue("description").Value<string>();
                useragent = obj.GetValue("userAgent").Value<string>();
                if(category == "Game Related"){
                    game = int.Parse(obj.GetValue("game").Value<string>());
                }
            }
            catch(Exception){
                return;
            }
            if (description.Length > 500)
            {
                description = description.Substring(0, 500);
            }
            using (MySqlConnection conn = Program.GetMysqlConnection())
            {
                MySqlCommand cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT INTO bug VALUES (NULL,@category,@game,@description,@useragent,NOW())";
                cmd.Parameters.AddWithValue("@category", category);
                cmd.Parameters.AddWithValue("@description", description);
                cmd.Parameters.AddWithValue("@userAgent", useragent);
                cmd.Parameters.AddWithValue("@game", game);
                cmd.Prepare();
                cmd.ExecuteNonQuery();
            }
            

        }

        public UserProfile GetUserProfile(int uid) {
            UserProfile profile = default(UserProfile);
            using (MySqlConnection conn = Program.GetMysqlConnection())
            {
                MySqlCommand cmd = conn.CreateCommand();
                profile.uid = uid;
                cmd.CommandText = "SELECT nickname,email,developer\nFROM user, profile\nWHERE user.uid = profile.uid AND user.uid  = @uid";
                cmd.Parameters.AddWithValue("@uid", uid);
                cmd.Prepare();
                using (MySqlDataReader reader = cmd.ExecuteReader(System.Data.CommandBehavior.SingleRow | System.Data.CommandBehavior.CloseConnection))
                {
                    reader.Read();
                    if (!reader.HasRows)
                    {
                        reader.Close();
                    
                        return default(UserProfile);
                    }
                    profile.nickname = reader.GetString(0);
                    profile.avatar = GetGravatar(reader.GetString(1));
                    reader.Close();
                }
            }
            
            return profile;
        }

        protected override bool HandleRequest(HttpListenerContext con, string requestURL, out byte[] message)
        {
            bool isauthenticated = CheckAuthentication(con);
            message = new byte[0];
            switch (requestURL)
            {
                case "auth":
                    if (con.Request.HttpMethod == "POST")
                    {
                        AuthenticateUser(con, out message);

                    }
                    else
                    {
                        message = System.Text.UTF8Encoding.Default.GetBytes("<h1>Wrong HTTP Method.</h1>");
                        con.Response.AddHeader("Allow", "POST");
                        con.Response.StatusCode = 405;
                    }
                    break;
                case "signup":
                    if (con.Request.HttpMethod == "POST")
                    {
                        UserSignupObject req;
                        if (CreateUserRequest(con, out req))
                        {
                            if (InsertNewUser(req))
                            {
                                message = UTF8Encoding.UTF8.GetBytes("true");
                            }
                            else
                            {
                                message = UTF8Encoding.UTF8.GetBytes("false");
                            }
                        }
                        else
                        {
                            message = UTF8Encoding.UTF8.GetBytes("false");
                        }
                    }
                    else
                    {
                        message = System.Text.UTF8Encoding.Default.GetBytes("<h1>Wrong HTTP Method.</h1>");
                        con.Response.AddHeader("Allow", "POST");
                        con.Response.StatusCode = 405;
                    }
                    break;
                case "deauth":
                    con.Response.ContentType = "text/html";//We aren't actually sending anything, but to make it happier...
                    string token = con.Request.Headers.Get("LoginToken");
                    if (token != null)
                    {
                        if (currentSessions.ContainsKey(token))
                        {
                            currentSessions.Remove(token);
                        }
                    }
                    break;
                case "profile":
                    if (con.Request.HttpMethod == "GET")
                    {
                        if (isauthenticated)
                        {
                            message = System.Text.UTF8Encoding.Default.GetBytes(JsonConvert.SerializeObject(GetUserProfile(con.Request.Headers.Get("LoginToken"))));
                        }
                        else
                        {
                            message = System.Text.UTF8Encoding.Default.GetBytes(JsonConvert.SerializeObject(false));
                        }
                    }
                    else
                    {
                        con.Response.ContentType = "text/html";
                        message = System.Text.UTF8Encoding.Default.GetBytes("<h1>Wrong HTTP Method.</h1>");
                        con.Response.AddHeader("Allow", "POST");
                        con.Response.StatusCode = 405;
                    }
                    break;
                case "friends":
                    if (con.Request.HttpMethod == "GET")
                    {
                        if (isauthenticated)
                        {
                            List<int> friends = new List<int>();
                            using (MySqlConnection conn = Program.GetMysqlConnection())
                            {
                                MySqlCommand cmd = conn.CreateCommand();
                                token = con.Request.Headers.Get("LoginToken");
                                int uid = (int)currentSessions[token].uid;
                                cmd.CommandText = "USE webPlatform;\nSELECT uidA,uidB\nFROM friend\nWHERE friend.uidA = @uid OR friend.uidB = @uid;";
                                cmd.Parameters.AddWithValue("@uid", uid);
                                cmd.Prepare();
                                MySqlDataReader reader = cmd.ExecuteReader(System.Data.CommandBehavior.CloseConnection);
                                while (reader.Read())
                                {
                                    int uidA = reader.GetInt16(0);
                                    int uidB = reader.GetInt16(1);
                                    if (uidA == uid)
                                    {
                                        friends.Add(uidB);
                                    }
                                    else
                                    {
                                        friends.Add(uidA);
                                    }
                                }
                                reader.Close();
                            }
                            
                            UserProfile[] profiles = new UserProfile[0];
                            if(friends.Count > 0){
                                profiles = GetUserProfiles(friends.ToArray());
                            }
                            message = System.Text.UTF8Encoding.Default.GetBytes(JsonConvert.SerializeObject(profiles));
                        }
                        else
                        {
                            message = System.Text.UTF8Encoding.Default.GetBytes(JsonConvert.SerializeObject(false));
                        }
                    }
                    break;
                case "bug":
                    HandleBugReport(con,out message);
                    break;
                default:
                    base.HandleRequest(con, requestURL, out message);
                    break;
            }
            return true;
        }
    }
}


