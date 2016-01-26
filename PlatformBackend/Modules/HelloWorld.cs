using System;
namespace SoupMix.Modules
{
	public class HelloWorld : HttpModule
	{
		public HelloWorld () : base("HelloWorld","helloworld")
		{
			
		}

		protected override bool HandleRequest (System.Net.HttpListenerContext context, string action, out byte[] message)
		{
			message = System.Text.Encoding.UTF8.GetBytes(action);
			return true;
		}
	}
}

