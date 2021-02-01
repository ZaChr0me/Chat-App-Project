using System;
using System.Collections.Generic;
using System.Text;

namespace ChatServer
{
    public class Program
    {
        private static void Main(string[] args)
        {
            Server server = new Server();
            server.Start();
        }
    }
}