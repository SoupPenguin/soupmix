using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using SoupMix.Structs;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
namespace SoupMix.Modules
{    
    public struct RecieveStruct{
        public Socket sock;
        public byte[] buffer;
    }

    public struct WSPacket{
        public uint opcode;
        public bool lastPacket;
        public byte[] binaryData;
        public string textData;
        public ulong length;
        public Socket sender;
    }
    public class WebsocketModule : BackendModule
    {   
        const string HTTPRESPONSE = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {0}\r\n\r\n";
        const string WSMagic = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        public const int OPCODE_TEXT = 1;
        public const int OPCODE_CLOSE = 8;
        public const int OPCODE_PING = 9;
        public const int OPCODE_INTERNAL_FAIL = 127;
        public const int WSREADSIZE = 2; //1 for mime +opcode,1 for len, 2 for extra len
        public bool shouldRun = true;
        protected Socket server;
        protected List<Socket> sockets;
        private Dictionary<Socket,byte[]> fragments;
        protected int port;
        public ConcurrentQueue<WSPacket> processingQueue;
        public WebsocketModule(string name,int port) : base(name)
        {
            this.port = port;
        }

        private Dictionary<string,string> getHeaders(string data){
            Dictionary<string,string> headers = new Dictionary<string, string>();
            headers.Add("Method",data.Substring(0,data.IndexOf('/')-1));
            headers.Add("HTTP",data.Substring(data.IndexOf("HTTP")+5,3));
            int i = data.IndexOf('\n')+1;
            while (i < data.Length && data.IndexOf(':',i+1) != -1)
            {
                string key = data.Substring(i, data.IndexOf(':',i+1) - i);
                i = data.IndexOf(':',i+1);
                string val = data.Substring(i+2, data.IndexOf('\r',i+1) - i - 2);
                i = data.IndexOf('\n',i+1)+1;
                headers.Add(key, val);
            }
            return headers;
        }

        protected void CloseSocket(Socket socket){
            WSPacket packet = new WSPacket();
            packet.opcode = OPCODE_CLOSE;
            packet.lastPacket = true;
            byte[] newdata = EncodePacket(packet);
            try
            {
                socket.Send(newdata);
                socket.Close();
            }
            catch(ObjectDisposedException){

            }
        }

        private void SocketDataRecieved(IAsyncResult e){
            RecieveStruct state = (RecieveStruct)e.AsyncState;
            int ecode = 0;
            try
            {
                ecode = state.sock.EndReceive(e);
            }
            catch(SocketException){
                sockets.Remove(state.sock);
                return;
            }
            catch(ObjectDisposedException){
                sockets.Remove(state.sock);
                return;
            }
            if (ecode == 0)
            {
                //We should NEVER get zero data.
                sockets.Remove(state.sock);
                return;
            }
            WSPacket packet;
            try
            {
                packet = DecodePacket(state.buffer,state.sock);
            }
            catch(Exception){
                CloseSocket(state.sock);
                return;
            }
            if (packet.opcode == OPCODE_INTERNAL_FAIL)
            {
                //Decoder could not get any more data from the packet, so the connection must be terminated.
                sockets.Remove(state.sock);
                return;
            }
            if (packet.lastPacket)
            {
                if (fragments.ContainsKey(state.sock))
                {
                    byte[] lastPacket = fragments[state.sock];
                    byte[] buffer = new byte[lastPacket.Length + packet.binaryData.Length];
                    Array.Copy(lastPacket, buffer, lastPacket.Length);
                    Array.Copy(packet.binaryData, 0, buffer, lastPacket.Length, packet.binaryData.Length);
                    fragments[state.sock] = buffer;
                    packet.binaryData = buffer;
                    if (packet.opcode == OPCODE_TEXT)
                    {
                        packet.textData = Encoding.UTF8.GetString(packet.binaryData);
                    }
                    fragments.Remove(state.sock);
                }
                packet.sender = state.sock;
                //Do something with the packet.
                if (packet.opcode == OPCODE_CLOSE)
                {
                    state.sock.Close();
                }
                else if (packet.opcode == OPCODE_PING)
                {
                    WSPacket newpack = new WSPacket();
                    packet.opcode = 0xA;
                    packet.length = 0;
                    packet.lastPacket = true;
                    byte[] newdata = EncodePacket(newpack);
                    state.sock.BeginSend(newdata, 0, newdata.Length, SocketFlags.None, SocketDataSent, state);
                }
                else
                {
                    processingQueue.Enqueue(packet);
                    if (CanInterrupt)
                    {
                        UpdateThread.Interrupt();
                    }
                }
            }
            else
            {
                if (!fragments.ContainsKey(state.sock))
                {
                    fragments.Add(state.sock,packet.binaryData);
                }
                else
                {
                    byte[] lastPacket = fragments[state.sock];
                    byte[] buffer = new byte[lastPacket.Length + packet.binaryData.Length];
                    lastPacket.CopyTo(buffer, 0);
                    packet.binaryData.CopyTo(buffer, lastPacket.Length);
                    fragments[state.sock] = buffer;
                }
            }

            if (state.sock.Connected)
            {
                state.sock.BeginReceive(state.buffer, 0, WSREADSIZE, SocketFlags.None, SocketDataRecieved, state);
            }
        }

        private void SocketDataSent(IAsyncResult e){
            RecieveStruct state = (RecieveStruct)e.AsyncState;
            if (state.sock.Connected)
            {
                state.sock.EndSend(e);
            }
        }

        protected void SendData(Socket socket,string data){
            WSPacket packet = new WSPacket();
            packet.opcode = 0x1;
            packet.textData = data;
            packet.lastPacket = true;
            byte[] newdata = EncodePacket(packet);
            RecieveStruct rs = new RecieveStruct();
            rs.sock = socket;
            socket.BeginSend(newdata, 0, newdata.Length, SocketFlags.None, SocketDataSent, rs);
        }

        private void AcceptWS(IAsyncResult res){
            if (!shouldRun)
            {
                return;
            }
            Socket sock = server.EndAccept(res);
            byte[] buffer = new byte[1024];
            sock.Receive(buffer);
            Dictionary<string,string> headers = getHeaders(UTF8Encoding.UTF8.GetString(buffer));
            bool worked = TryHandshake(sock,headers);
            if (!worked)
            {
                sock.Close();
            }
            else
            {
                sockets.Add(sock);
                RecieveStruct state = new RecieveStruct();
                state.buffer = new byte[WSREADSIZE];//4 bytes 
                state.sock = sock;
                sock.BeginReceive(state.buffer,0,WSREADSIZE,SocketFlags.None,SocketDataRecieved,state);
            }
            server.BeginAccept(AcceptWS, null);
        }

        public override void Load(){
            UpdateThread = new Thread(Update);
            sockets = new List<Socket>();
            fragments = new Dictionary<Socket, byte[]>();
            server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);
            server.Bind(new IPEndPoint(IPAddress.Any, port));
            server.Blocking = true;
            server.Listen(128);
            Program.debugMsgs.Enqueue("Websocket server created for " + this.MODNAME + " on " + port);
            processingQueue = new ConcurrentQueue<WSPacket>();
			UpdateThread.Start();
            base.Load();
        }

        private bool TryHandshake(Socket sock,Dictionary<string,string> headers){
            if (!headers.ContainsKey("Upgrade") || !headers.ContainsKey("Sec-WebSocket-Key"))
            {
                return false;
            }
            string key = headers["Sec-WebSocket-Key"] + WSMagic;
            SHA1 sha = new SHA1CryptoServiceProvider(); 
            string newkey = System.Convert.ToBase64String(sha.ComputeHash(UTF8Encoding.UTF8.GetBytes(key)));
            byte[] buffer = Encoding.UTF8.GetBytes(string.Format(HTTPRESPONSE, newkey));
            sock.Send(buffer);
            return true;
        }



        public void BitArrayReverse(BitArray array)
        {
            int length = array.Length;
            int mid = (length / 2);

            for (int i = 0; i < mid; i++)
            {
                bool bit = array[i];
                array[i] = array[length - i - 1];
                array[length - i - 1] = bit;
            }    
        }

        public BitArray BytesToBits(byte[] bytes,int offset,int length){
            byte[] subBytes = new byte[length];
            for (int i = offset; i < offset + length; i++)
            {
                BitArray ba = new BitArray(new byte[]{ bytes[i] });
                //Convert endianness
                if (BitConverter.IsLittleEndian){
                    BitArrayReverse(ba);
                }
                ba.CopyTo(subBytes, i - offset);
            }
            return new BitArray(subBytes);
        }

        private WSPacket DecodePacket(byte[] buffer,Socket sock){
            int byteCounter = 0;
            WSPacket packet = new WSPacket();
            BitArray byteZero = BytesToBits(buffer,0,1);
            bool moreToCome = !byteZero.Get(0);
            int opcode = Convert.ToInt16(byteZero.Get(4))*8 + 
                Convert.ToInt16(byteZero.Get(5))*4 +  
                Convert.ToInt16(byteZero.Get(6))*2 +  
                Convert.ToInt16(byteZero.Get(7))*1;
            BitArray byteOne = BytesToBits(buffer,1,1);
            bool isMasked = byteOne.Get(0);
            if (!isMasked)
            {
                sock.Disconnect(false);
            }
            UInt64 length = 0;
            uint factor = 0;
            uint bit = 0;
            for (var i = 1; i < 8; i++)
            {
                factor = (uint)Math.Pow(2, 7 - i);
                bit = Convert.ToUInt16(byteOne.Get(i));
                length += bit * factor;
            }

            if (length > 125)
            {
                int bytesToGet = 2;
                if (length == 127)
                {
                    bytesToGet = 8;
                }

                byte[] lenbuffer = new byte[bytesToGet];
                if (sock.Connected)
                {
                    sock.Receive(lenbuffer);
                }
                else
                {
                    packet.opcode = OPCODE_INTERNAL_FAIL;
                    return packet;
                }
                BitArray lengthArr = BytesToBits(lenbuffer,0,bytesToGet);
                byteCounter += bytesToGet;
                length = 0;
                for (var i = 0; i < bytesToGet * 8; i++)
                {
                    factor = (uint)Math.Pow(2, ((bytesToGet * 8)-1) - i);
                    bit = Convert.ToUInt16(lengthArr.Get(i));
                    length += bit * factor;
                }
            }

            byte[] mask = new byte[4];
            if (sock.Connected)
            {
                sock.Receive(mask);
            }
            else
            {
                packet.opcode = OPCODE_INTERNAL_FAIL;
                return packet;
            }

            byte[] edata = new byte[length];
            byte[] ddata = new byte[edata.Length];

            if (sock.Connected)
            {
                int bcount=0;
                while (bcount < edata.Length)
                {
                    bcount += sock.Receive(edata,bcount,edata.Length-bcount,0);
                }
            }
            else
            {
                packet.opcode = OPCODE_INTERNAL_FAIL;
                return packet;
            }

            for (int i = 0; i < edata.Length; i++) {
                ddata[i] = (byte)((int)edata[i] ^ mask[i % 4]);
            }
            packet.binaryData = ddata;
            packet.length = length;
            packet.opcode = (uint)opcode;
            packet.lastPacket = !moreToCome;
            if (opcode == 1 && !moreToCome)
            {
                packet.textData = Encoding.UTF8.GetString(ddata);
            }
            return packet;
        }

        private byte[] EncodePacket(WSPacket packet){
            int packetSize = 2;
            if (packet.opcode == 1)
            {
                packet.binaryData = Encoding.UTF8.GetBytes(packet.textData);
                packet.length = (ulong)packet.binaryData.Length;
            }
            if ((int)packet.length <= 125)
            {
                //Nothing
            }
            else if (packet.length < (long)Int16.MaxValue)
            {
                if (packet.length < byte.MaxValue)
                {
                    packetSize += 2;
                }
                else
                {
                    packetSize += 2;
                }
            }
            else
            {
                packetSize += 8;
            }
            packetSize += (int)packet.length;
            byte[] bpacket = new byte[packetSize];


            if (packet.lastPacket)
            {
                bpacket[0] = (byte)128;
            }
            bpacket[0] = Convert.ToByte((int)bpacket[0] + packet.opcode);//Fin + OpCode

            if (packet.length <= 125)
            {
                bpacket[1] = (byte)packet.length; 
            }
            else if (packet.length < (int)Int16.MaxValue)
            {
                bpacket[1] = 126;
            }
            else
            {
                bpacket[1] = 127;
            }
            bpacket[1] = Convert.ToByte((int)bpacket[1]);//Masking

            if (packetSize > 125)
            {
                byte[] blen = BitConverter.GetBytes(packet.length);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(blen);
                }
                if (packetSize <= Int16.MaxValue)
                {
                    blen = new byte[2]{ blen[6], blen[7] };
                }
                for (int i = 0; i < blen.Length; i++)
                {
                    bpacket[2 + i] = blen[i];
                }
            }

            //Masking - Don't do it
            //byte[] mask = new byte[4];
            //new Random().NextBytes(mask);
            int offset = (packetSize - (int)packet.length);
            //for (int i = 0; i < 4; i++)
            //{
            //    bpacket[offset + i] = mask[i];
            //}
            //offset += 4;
            for (int i = 0; i < (int)packet.length; i++)
            {
                bpacket[offset + i] = packet.binaryData[i];
            }

            return bpacket;
        }

        public virtual void HandlePacket(WSPacket packet){
            SendData(packet.sender, packet.textData);
        }

        public void Update(){
            server.BeginAccept(AcceptWS, null);
            WSPacket packet;
            while (shouldRun)
            {
                for (int i = 0; i < sockets.Count; i++)
                {
                    if (!sockets[i].Connected)
                    {
                        sockets.Remove(sockets[i]);
                    }
                }
                while (processingQueue.TryDequeue(out packet))
                {
                    try
                    {
                        HandlePacket(packet);
                    }
                    #if DEBUG
                    catch(Exception e){
                    #else
                    catch(Exception){
                    #endif
                        CloseSocket(packet.sender);
                        #if DEBUG
                            Program.debugMsgs.Enqueue("Closed WS connection due to exception.");
                            Program.debugMsgs.Enqueue("  " + e.GetType().Name);
                            Program.debugMsgs.Enqueue("  " + e.Message);
                        #endif
                    }
                }


                try
                {
                    CanInterrupt = true;
                    Thread.Sleep(Timeout.InfiniteTimeSpan);
                }
                catch (ThreadInterruptedException)
                {
                    CanInterrupt = false;
                }
            }
        } 

        public override void Unload(){
            shouldRun = false;
            Interrupt();
            server.Close();
            base.Unload();
        }
    }
}

