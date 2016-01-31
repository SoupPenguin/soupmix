using System;
using System.Data;
using System.Data.Common;
using MySql.Data.MySqlClient;
using System.Collections.Generic;
namespace SoupMix
{

	public enum DBDriver
	{
		None,
		SQLite,
		MySql
	}
	public class DBHelper
	{
		static string host;
		static string db;
		static string username;
		static string password;
		static DBDriver driver;
		public static bool Connect ()
		{
			//Set up mysql
			bool status = false;
			string sdriver = "";
			Settings.Get<string> ("db.driver", out sdriver);
			if (!Enum.TryParse<DBDriver>(sdriver,out driver)) {
				Program.debugMsgs.Enqueue ("No database driver has been specified");
				//No database has been specified
				driver = DBDriver.None;
			}

			if (driver == DBDriver.MySql) {
				Settings.Get<string>("db.host",out host);
	            Settings.Get<string>("db.database",out db);
				Settings.Get<string>("db.username",out username);
				Settings.Get<string>("db.password",out password);

				Console.WriteLine("MYSQL Connection Connection Attempt.");
				MySqlConnection contest = new MySqlConnection(string.Format("Server={0};Database={1};User ID={2};Password={3}",host,db,username,password));
	            try
	            {
	                contest.Open();
	            }
	            catch(Exception e){
	                Console.WriteLine("[E] Connection to DB failed!\n    Server will be in low functionality until this is fixed!");
					Console.WriteLine("[E] Exception Details:" + e.Message);
					driver = DBDriver.None;
	            }
	            if (contest.State == System.Data.ConnectionState.Open)
	            {
	                Console.WriteLine("MYSQL Connection Connection Succeded.");
					status = true;
	                contest.Close();
	            }
			}

			return status;

         
		}

		public static DbDataReader ExecuteReader (string text,Dictionary<string,object> values,bool singleRow = false)
		{
			if (driver == DBDriver.MySql) {
				MySqlCommand cmd = My_Execute(text,values);
				DbDataReader reader = cmd.ExecuteReader(System.Data.CommandBehavior.CloseConnection);
				return reader;
			}
			return null;
		}

		public static int ExecuteQuery (string text,Dictionary<string,object> values)
		{
			if (driver == DBDriver.MySql) {
				MySqlCommand cmd = My_Execute(text,values);
				int modified = cmd.ExecuteNonQuery();
				cmd.Connection.Close();
				return modified;
			}
			return -1;
		}

		private static MySqlCommand My_Execute (string text, Dictionary<string,object> values)
		{
			MySqlConnection conn = MY_GetConnection ();
			MySqlCommand cmd = conn.CreateCommand ();
			text = "USE "+db+"; "+text;
			cmd.CommandText = text;
			foreach (KeyValuePair<string,object> kv in values) {
				cmd.Parameters.AddWithValue(kv.Key,kv.Value);
			}
			cmd.Prepare();
			return cmd;
		}


		private static MySqlConnection MY_GetConnection(){
			MySqlConnection newcon = new MySqlConnection(string.Format("Server={0};Database={1};User ID={2};Password={3};Pooling=true",host,db,username,password));
            try
            {
                newcon.Open();
                return newcon;
            }
            catch(Exception e)
            {
                #if TRACEMYSQL
                Program.debugMsgs.Enqueue("[MYSQL] Could not create a connection.");
                #endif
                Console.WriteLine(e.ToString());
                throw new Exception("Could not get a mysql connection");
            }
        }

	}
}

