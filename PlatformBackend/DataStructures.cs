using System;
namespace SoupMix.Structs
{
    struct TokenReply{
        public string token;
    }

    public struct SessionObject{
        public string token;
        public uint timestamp;
        public string host;
        public string agent;
        public uint uid;
    }

    public struct UserProfile{
        public string nickname;
        public string avatar;
        public int uid;
    }

    struct UsernamePasswordPair{
       public string username;
       public string password;
    }

    struct UserSignupObject{
       public string username;
       public string password;
       public string email;
       public string nickname;
       public string invite;
    }

}
