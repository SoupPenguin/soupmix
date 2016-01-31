#!/bin/python3
import socket, argparse, sys
from json import dumps, loads

VERSION = "0.1"

def processCmd(cmd,host,port):
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    try:
        s.connect((host, port))
    except socket.error as e:
        return e.errno

    data = s.recv(512).decode()
    errpack = loads(data)
    if errpack['errorCode'] != 0:
        return errpack['errorCode']

    data = dumps({"cmd":cmd}).encode()
    s.send(data)
    if(cmd[0] == "restart" or cmd[0] == "kill"):
        s.close()
        return 1

    data = s.recv(512).decode()
    pack = loads(data)
    if pack['error'] != 0:
        return pack['error']
    else:
        processResponse(cmd[0],pack['r'])
    s.close()
    return 0

def processResponse(cmd,response):
    if cmd == "echo" or cmd == "version":
        print(response)
    if cmd == "lastlogin":
        print("Last login from",response)
    elif cmd == "load" or cmd == "help":
        for m in response:
            print(m," "*(24-len(m)),response[m])
    elif cmd == "uptime":
        m, s = divmod(response, 60)
        h, m = divmod(m, 60)
        d, h = divmod(h, 24)

        dstring = str(int(s)) + " seconds."
        if m > 0:
            dstring = str(int(m)) + " minutes and " + dstring
        if h > 0:
            dstring = str(int(h)) + " hours, " + dstring
        if d > 0:
            dstring = str(int(d)) + " days, " + dstring

        print("Sever has been up for " + dstring)


def main():
    errorList = {-81:"Command not found!",-50:"Module not found!"}
    parser = argparse.ArgumentParser(description='Connect to a soupmix server and issue commands.')
    parser.add_argument('-c', type=str,nargs='+',help='Command to send')
    parser.add_argument('--host', default="127.0.0.1", type=str,help='Hostname')
    parser.add_argument('--port', default=5512, type=int,help='Port')
    parser.add_argument('-v', action='store_true')

    #parser.add_argument('-d', action='store_true',help='use database')
    args = parser.parse_args()

    if args.v or (args.c == None):
        print("🐧 SoupMix Communication Tool")
        print("Version",VERSION)
        if args.v:
            return

    shellMode = True
    if (args.c == None):
        result = processCmd(["lastlogin"],args.host,args.port)
        if result != 0:
            print("Error connecting to server")
            shellMode = False

    while(shellMode):
        shellMode = (args.c == None)
        cmd = args.c
        if shellMode:
            cmd = input(">")
            cmd = cmd.split(" ")
            if cmd[0] == "exit":
                shellMode = False
                break
        result = processCmd(cmd,args.host,args.port)
        if result == 111:
            print("Couldn't connect to server!")
            break
        if result == 1:
            print("Server closed")
            break
        elif result < 0:
            print("Server gave error:",errorList[result])

if __name__ == "__main__":
    main()
