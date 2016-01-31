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

		public PostsModule () : base("Posts","posts")
		{
			this.Dependencies = new string[1]{"User"};
			this.RequiresDatabase = true;
		}

		protected override bool HandleRequest (System.Net.HttpListenerContext context, string action, out byte[] message)
		{
			bool status = true;
			switch (action) {
				case "posts":
					message = UTF8Encoding.UTF8.GetBytes("[]");
					break;
				case "post":
					PostAction(context.Request.HttpMethod,context.Request.QueryString,out message);
					break;
				default:
					status = false;
					message = UTF8Encoding.UTF8.GetBytes("false");
					break;
			}


			return status;
		}

		//List<Post> postCache;
		bool PostAction (string method, NameValueCollection querystring, out byte[] message)
		{
			string sid = querystring ["id"];
			int id;
			if (sid == null) {
				message = UTF8Encoding.UTF8.GetBytes ("{\"error\":\"Missing id from query\"}");
				return false;
			}

			if (!int.TryParse (sid, out id)) {
				message = UTF8Encoding.UTF8.GetBytes ("{\"error\":\"Invalid id\"}");
				return false;
			}

			if (method == "GET") {
				//Get post information
				Post p = Post.FromId (id);
				JObject obj = JObject.FromObject (p);
				obj.Add ("body", p.GetPostContent ());
				message = UTF8Encoding.UTF8.GetBytes (obj.ToString (Formatting.None));
			} else {
				message = UTF8Encoding.UTF8.GetBytes("{}");
			}

			Console.WriteLine(method);
			Console.WriteLine(querystring);
			return true;
		}

		Post[] GetPosts(){
			List<Post> posts = new List<Post>();
			return posts.ToArray();
		}

	}

	public class Post{
		public int id;
		public string title;
		public string subtitle;
		public string[] tags;
		public int author;

		private Post ()
		{
			
		}

		public static Post FromId (int id)
		{
			//TODO: Implement database code for getting posts.
			return Post.FromTemplate();
		}

		public static Post FromTemplate ()
		{
			//TODO: Write a templating system.
			Post post = new Post();
			return post;
		}

		public string GetPostContent(){
			return "Spagetti";//TODO: Yeah, get the *actual* content.
		}

		public void DeletePost ()
		{
			//TODO: Code for removing posts.
		}

		public void SavePostContent(){

		}

		public void SavePost(){

		}

	}
}

