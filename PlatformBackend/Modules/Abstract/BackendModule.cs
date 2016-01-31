using System;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net;

namespace SoupMix.Modules
{
    public class BackendModule
    {

        /// <summary>
        /// Module Name
        /// </summary>
        public string MODNAME = "NoModName";

        /// <summary>
        /// Is the current module running?
        /// </summary>
        public bool IsRunning = false;

        /// <summary>
        /// Does this module require a database connection?
        /// </summary>
        public bool RequiresDatabase = false;

        /// <summary>
        /// Thread that checks for requests and runs Update().
        /// <seealso cref="Update()"/>
        /// </summary>
        protected Thread UpdateThread;

		/// <summary>
        /// Is the current thread sleeping?
        /// </summary>
        public bool CanInterrupt = true;

        /// <summary>
        /// Names of required modules.
        /// </summary>
        public string[] Dependencies = new string[0];

        public BackendModule(string name)
        {
            MODNAME = name;
        }

        public virtual void Load(){
            IsRunning = true;
        }

        public virtual void Unload(){
            IsRunning = false;
        }

        /// <summary>
        /// Current load of the module. E.g. requests currently queued.
        /// </summary>
        /// <returns>The load.</returns>
        public virtual int CurrentLoad(){
        	return 0;
        }
        /// <summary>
        /// Interrupts the thread, if it is safe to do so.
        /// </summary>
        public bool Interrupt ()
		{
			if (CanInterrupt) {
				UpdateThread.Interrupt();
			}
			return CanInterrupt;
        }
    }
}

