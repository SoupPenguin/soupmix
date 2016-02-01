using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SoupMix.Modules;
using System.Text;
namespace SoupMix
{
	public class PostsModule : HttpModule
	{
		const int MAXPOSTSIZE = 1000000;
		const int DEFAULTRECENT = 10;
		public PostsModule () : base("Posts","posts",false)
		{
			this.Dependencies = new string[1]{"User"};
			this.RequiresDatabase = true;
		}

		protected override bool HandleRequest (System.Net.HttpListenerContext context, string action, out byte[] message)
		{
			bool status = true;
			switch (action) {
			case "post":
				string post = "";
				if (context.Request.Url.Segments.Length >= 4) {
					post = context.Request.Url.Segments [3].Replace ("/", "");
				}
				if (post == "" && context.Request.HttpMethod == "GET") {
					Post[] posts = Post.GetRecent(DEFAULTRECENT);
					message = UTF8Encoding.UTF8.GetBytes(JArray.FromObject(posts).ToString());
				} else if (post != "") {
					PostAction(context.Request.HttpMethod,post,context,out message);
				} else {
					context.Response.StatusCode = 400;
					message = UTF8Encoding.UTF8.GetBytes(JObject.FromObject(new {error="Malformed request"}).ToString());
				}
				break;
			default:
				context.Response.StatusCode = 400;
				message = new byte[0];
				break;
			}


			return status;
		}

		//List<Post> postCache;
		bool PostAction (string method,string post,System.Net.HttpListenerContext context, out byte[] message)
		{
			string id = post;

			if (id.Length > 16) {
				message = UTF8Encoding.UTF8.GetBytes ("{\"error\":\"Invalid id\"}");
				return false;
			}

			if (method == "GET") {
				//Get post information
				Post p = Post.FromId (id);

				JObject obj;
				if(p != null){
					p.GetPostContent();
					obj = JObject.FromObject (p);
				}
				else
				{
					obj = new JObject();
				}
				
				message = UTF8Encoding.UTF8.GetBytes (obj.ToString (Formatting.None));
			} 
			else if(method == "PUT"){//TODO:Authenticate this first
				Post p = Post.FromId (id);
				if(p != null)
				{
					byte[] inputStream = new byte[MAXPOSTSIZE];
					int written = context.Request.InputStream.Read(inputStream,0,MAXPOSTSIZE);
					string articleText = System.Text.Encoding.UTF8.GetString(inputStream).TrimEnd((char)0x00);
					p.SavePostContent(articleText,new User(1));
					message = UTF8Encoding.UTF8.GetBytes("{'written':"+written+"}");
				}
				else
				{
					message = UTF8Encoding.UTF8.GetBytes("{'error':'Post does not exist!'}");
				}
			}
			else {
				message = UTF8Encoding.UTF8.GetBytes("{}");
			}
			return true;
		}

	}
}
