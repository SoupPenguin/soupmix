using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SoupMix;
using System.Data;
using System.Data.Common;
using System.Linq;
namespace SoupMix
{
public class Post{
		public string id;
		public string title;
		public string subtitle;
		public DateTime created;
		public string[] tags;
		public int author;
		public string body {
			get;
			private set;
		}
		private Post ()
		{
			body = null;
		}

		public static Post FromId (string id)
		{
			Post post = null;
			if(!id.All(char.IsLetterOrDigit)){//Quick validate
				return post;
			}
			DbDataReader reader = DBHelper.ExecuteReader (
            "SELECT * FROM post_meta WHERE id LIKE @id LIMIT 1",
            new Dictionary<string,object> {
				{ "@id",id + '%' }
			},
		    true);
			if (reader.HasRows) {
				post = new Post();
				reader.Read();
				post.id = reader.GetString(0);
				post.title = reader.GetString(1);
				post.subtitle = reader.GetString(2);
				post.author = reader.GetInt16(3);
			}
			reader.Close();
			return post;
		}

		public static Post FromTemplate ()
		{
			//TODO: Write a templating system.
			Post post = new Post();
			return post;
		}

		public static Post[] GetRecent(int num){
			List<Post> posts = new List<Post>(num);

			DbDataReader reader = DBHelper.ExecuteReader (
            "SELECT * FROM post_meta ORDER BY created DESC LIMIT @limit;",
            new Dictionary<string,object> {
				{ "@limit",num }
			},
		    true);
			while (reader.Read()) {
				Post post = new Post();
				post.id = reader.GetString(0);
				post.title = reader.GetString(1);
				post.subtitle = reader.GetString(2);
				post.author = reader.GetInt16(3);
				string dt = reader.GetString(4);
				post.created = DateTime.Parse(dt);
				posts.Add(post);
			}
			reader.Close();
			return posts.ToArray();
		}

		public void GetPostContent(){
			const string query = @"
			SELECT content_size,content FROM post_content
			WHERE pid = @id
			ORDER BY `time` ASC
			LIMIT 1";
			DbDataReader reader = DBHelper.ExecuteReader (query,
            new Dictionary<string,object> {
					{ "@id",this.id}
			},
		    true);
			if(reader.HasRows){
				reader.Read();
				int size = reader.GetInt32(0);
				byte[] blob = new byte[size];
				reader.GetBytes(1,0,blob,0,size);
				blob = Util.GZip_Unpack(blob);
				body = System.Text.Encoding.UTF8.GetString(blob);
			}
		}

		public void DeletePost (User culprit,bool removeContent = true)
		{
			//TODO: Code for removing posts.
		}

		public bool SavePostContent(string newtext,User author){
			byte[] data = System.Text.Encoding.UTF8.GetBytes(newtext);
			data = Util.GZip_Pack(data);
			const string query = @"
			INSERT INTO post_content VALUES (now(),@id,@editor,@content,@size)";
			int result = DBHelper.ExecuteQuery(query,
            new Dictionary<string,object> {
					{ "@id",this.id},
					{ "@editor",author.Id},
					{ "@content",data},
					{ "@size",data.Length}
			});
			if(result == 1){
				body = newtext;
			}
			//TODO:Needs exception
			return (result == 1);
		}

		public void SavePost(){

		}
		
	}

}