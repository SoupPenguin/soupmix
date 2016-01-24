using System;
namespace Switch.Structs
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
        public uint developer;
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

    public struct UserPermissions{
        public bool openLibrary;
        public bool openStore;
        public bool openSocial;
        public bool openGroups;
        public bool openDev;
        public bool openAdmin;
    }

    public struct Developer{
        public int id;
        public string name;
    }

    public struct Publisher{
        public int id;
        public string name;
    }

    public struct PackageInfo{
        public int id;
        public string name;
        public string description;
        public string mediumlogo;
        public string boxart;
        public bool hidden;
        public bool executable;
        public bool supportsFirefox;
        public bool supportsChrome;
        public bool supportsOpera;
        public bool supportsIE;
        public bool supportsSafari;
        public Developer developer;
        public Publisher publisher;
        public long updated;
    }

    public enum ProjectStatus{
        Public,
        Private,
        OnHold
    }

    public enum ProjectChangeType{
        CreatedProject,
        AddedNewFile,
        ChangedFile,
        DeletedFile,
        MovedFile,
        MadePrivate,
        MadePublic,
        PutOnHold
    }

    public struct ProjectChange{
        public ProjectChangeType type;
        public int user;
        public string usernick;
        public string message;
        public DateTime time;

    }

    public struct ProjectInfo{
        public int id;
        public int developer;
        public string name;
        public ProjectStatus status;
        public string statusline;
        public ProjectChange[] changes;
    }

    public struct PackageFile{
        public string name;
        public string type;
        public long size;
        public PackageFile[] children;
    }
}
