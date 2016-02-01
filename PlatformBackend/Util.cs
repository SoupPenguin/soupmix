using System;
using System.Security.Cryptography;
using System.IO.Compression;
using System.Text;
namespace SoupMix
{
    public class Util
    {
        public static string CalculateMD5Hash(string input)
        {
            // step 1, calculate MD5 hash from input
            MD5 md5 = System.Security.Cryptography.MD5.Create();
            byte[] inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
            byte[] hash = md5.ComputeHash(inputBytes);

            // step 2, convert byte array to hex string
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hash.Length; i++)
            {
                sb.Append(hash[i].ToString("X2"));
            }
            return sb.ToString();
		}

        public static uint GetEpoch(){
            return (uint)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        }

		public static byte[] GZip_Pack(byte[] data){
			System.IO.MemoryStream stream = new System.IO.MemoryStream();
			using(GZipStream gz = new GZipStream(stream,CompressionLevel.Fastest,true)){
				gz.Write(data,0,data.Length);
			}
			byte[] compressed = stream.ToArray();
			stream.Close();
			return compressed;
		}

		public static byte[] GZip_Unpack(byte[] data){
			System.IO.MemoryStream outstrm = new System.IO.MemoryStream();
			System.IO.MemoryStream stream = new System.IO.MemoryStream(data);
			using(GZipStream gz = new GZipStream(stream,CompressionMode.Decompress,true)){
				byte[] block = new byte[512];
				while(gz.Read(block,0,512) != 0){
					outstrm.Write(block,0,512);
				}
			}
			stream.Close();
			byte[] output = outstrm.ToArray();
			outstrm.Close();
			return output;
		}
    }
}

