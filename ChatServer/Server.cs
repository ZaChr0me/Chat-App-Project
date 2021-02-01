using ChatServer.Logs;
using Connectivity;
using GeneralLibrary;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace ChatServer
{
    public class Server
    {
        private ServerParameters ServerParameter;
        private List<ClientLink> ClientLinks;
        private List<string> ConnectedUsers;

        //to ensure the cleanliness of the ClientLinks list;
        private static Semaphore CleaningSemaphore;

        //ensure the database is only accessed by 1 link at a time
        private static Semaphore DatabaseAccessSemaphore;

        //Topicmanager is a server resource so it is shared for all links, thus the need for a semaphore
        private static Semaphore TopicManagerAccessSemaphore;

        private static Semaphore PrivateMessageManagerAccessSemaphore;

        private TopicManager InternalTopicManager;
        private PrivateMessageManager InternalPrivateMessageManager;

        public Server()
        {
            Log.LogInfo("Setting up...", LogType.Application_Work);

            //create server parameter file if it doesn't exist
            if (!File.Exists(Globals.BASE_DIRECTORY + Globals.PARAMETERS_PATH))
            {
                JSONManagement.WriteToJsonFile<ServerParameters>(
                    Globals.BASE_DIRECTORY + Globals.PARAMETERS_PATH,
                    new ServerParameters(8407, "127.0.0.1"));
            }

            ServerParameter = JSONManagement.ReadFromJsonFile<ServerParameters>(AppDomain.CurrentDomain.BaseDirectory + Globals.PARAMETERS_PATH);
            InternalTopicManager = new TopicManager();
            InternalPrivateMessageManager = new PrivateMessageManager();
            ClientLinks = new List<ClientLink>();
            //Disconnect gracefully every link when server closes
            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;
        }

        private void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            foreach (var link in ClientLinks)
            {
                CleaningSemaphore.WaitOne();
                link.StopWorking();
                if (link.Comm.Connected) link.Comm.Close();
            }
        }

        public bool Start()
        {
            Log.LogInfo("Starting Server...", LogType.Application_Work);
            TcpListener tcpL = new TcpListener(System.Net.IPAddress.Parse(ServerParameter.ipAddress), ServerParameter.port);
            tcpL.Start();
            Log.LogInfo("Server Started", LogType.Application_Work);

            //star the cleaner to purge disconnected clientlinks
            CleaningSemaphore = new Semaphore(1, 1);
            DatabaseAccessSemaphore = new Semaphore(1, 1);
            TopicManagerAccessSemaphore = new Semaphore(1, 1);
            PrivateMessageManagerAccessSemaphore = new Semaphore(1, 1);
            new Thread(ConnectionCleaner).Start();
            while (true)
            {
                TcpClient comm = tcpL.AcceptTcpClient();
                Log.LogInfo(comm.ToString() + " Connecting...", LogType.Connection_To_Client);
                //little exchange of message to ensure communication is properly functionnal, then procede with init
                if (Communication.rcvMsg(comm.GetStream()).MessageType == MessageType.Connection_Test)
                {
                    Log.LogInfo(MessageType.Connection_Test.ToString(), LogType.Comm_Input);
                    Communication.sendMsg(comm.GetStream(), new Message(MessageType.Connection_Success));
                    Log.LogInfo(MessageType.Connection_Test.ToString(), LogType.Comm_Output);
                }
                Console.WriteLine("Connection established @" + comm);
                Log.LogInfo(comm.ToString() + " Connected.", LogType.Connection_To_Client);

                ClientLink clientLink = new ClientLink(comm, ServerParameter, InternalTopicManager, InternalPrivateMessageManager);
                new Thread(() => clientLink.Work(ConnectedUsers, DatabaseAccessSemaphore, TopicManagerAccessSemaphore, PrivateMessageManagerAccessSemaphore)).Start();
                //add client link to list without stopping process
                new Thread(() => AddLinkToList(clientLink)).Start();
            }
        }

        private void ConnectionCleaner()
        {
            do
            {
                if (ClientLinks.Count > 0)
                {
                    CleaningSemaphore.WaitOne();
                    foreach (var link in ClientLinks)
                    {
                        //to keep the cleaner working at minimum rate. Find a better solution for more than a few clients :/

                        if (!link.Comm.Connected) link.StopWorking();
                    }
                    CleaningSemaphore.Release();
                }
                Thread.Sleep(5000);
            } while (true);
        }

        private void AddLinkToList(ClientLink clientLink)
        {
            CleaningSemaphore.WaitOne();
            ClientLinks.Add(clientLink);
            CleaningSemaphore.Release();
        }

        /// <summary>
        /// Deals with 1 single client while it's connected. Basically the core of the functionnalities
        /// </summary>
        private class ClientLink
        {
            public TcpClient Comm { get; private set; }
            private bool KeepWorking = true;
            public string User { get; private set; }
            public string CurrentTopic { get; private set; }
            public ServerParameters ServerParameter { get; set; }
            public TopicManager TopicManager { get; private set; }
            public PrivateMessageManager PrivateMessageManager { get; private set; }

            public ClientLink(TcpClient tcpC, ServerParameters serverParameters, TopicManager topicManager, PrivateMessageManager privateMessageManager)
            {
                Comm = tcpC;
                ServerParameter = serverParameters;
                TopicManager = topicManager;
                PrivateMessageManager = privateMessageManager;
            }

            public void Work(List<string> connectedUsers, Semaphore databaseSemaphore, Semaphore TopicManagerAccessSemaphore, Semaphore PrivateMessageManagerAccessSemaphore)
            {
                //vars used all over the work method

                //connection to the db
                var dbCon = new SQLiteConnection(@"uri=file:" + Globals.DB_PATH);
                //statement for sql requests
                string statement = "";
                //request maker and executer
                SQLiteCommand cmd;
                //request's result reader
                SQLiteDataReader rdr;

                while (KeepWorking)
                {
                    Message message = (Message)Communication.rcvMsg(Comm.GetStream());
                    Log.LogInfo(message.MessageType.ToString(), LogType.Comm_Input);
                    switch (message.MessageType)
                    {
                        case MessageType.Connection_Test:
                            break;

                        case MessageType.Connection_Success:
                            break;

                        case MessageType.SendMessage_To_Server:
                            break;

                        case MessageType.SendMessage_To_Client:
                            break;

                        case MessageType.Login:
                            string[] login_Password = message.MessageBody.Split(';');
                            databaseSemaphore.WaitOne();

                            dbCon.Open();

                            statement = "SELECT * FROM " + Globals.DB_TABLE_PROFILES + " WHERE " +
                                Globals.DB_LOGIN + "='" + login_Password[0] + "' AND " +
                                Globals.DB_PASSWORD + "='" + login_Password[1] + "'";

                            cmd = new SQLiteCommand(statement, dbCon);
                            rdr = cmd.ExecuteReader();

                            if (rdr.Read())
                            {
                                rdr.Close();
                                Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Create_Account, "success"));
                                Log.LogInfo("login successful", LogType.Comm_Output);
                                User = login_Password[0];
                                PrivateMessageManagerAccessSemaphore.WaitOne();
                                PrivateMessageManager.GetLinkPMEventHandler(User).EventHandler += ClientLink_PrivateMessageEventHandler;
                                PrivateMessageManagerAccessSemaphore.Release();
                            }
                            else
                            {
                                rdr.Close();
                                Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Create_Account, "error;login or password incorrect"));
                                Log.LogInfo("login failed", LogType.Comm_Output);
                            }

                            dbCon.Close();

                            databaseSemaphore.Release();

                            break;

                        case MessageType.Disconnect:
                            PrivateMessageManagerAccessSemaphore.WaitOne();
                            PrivateMessageManager.GetLinkPMEventHandler(User).EventHandler += ClientLink_PrivateMessageEventHandler;
                            PrivateMessageManager.RemoveLinkPMEventHandler(User);
                            User = null;
                            PrivateMessageManagerAccessSemaphore.Release();

                            break;

                        case MessageType.Create_Account:
                            string[] accountInfos = message.MessageBody.Split(' ');
                            databaseSemaphore.WaitOne();

                            dbCon.Open();

                            statement = "SELECT * FROM " + Globals.DB_TABLE_PROFILES + " WHERE " +
                                Globals.DB_LOGIN + "='" + accountInfos[0] + "'";

                            cmd = new SQLiteCommand(statement, dbCon);
                            rdr = cmd.ExecuteReader();
                            if (rdr.Read())
                            {
                                rdr.Close();
                                Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Create_Account, "error;login exists"));
                                Log.LogInfo("creation of account failed : login exists already", LogType.Comm_Output);
                            }
                            else
                            {
                                rdr.Close();
                                //add profile to database
                                cmd = new SQLiteCommand(dbCon);
                                cmd.CommandText = "INSERT INTO " + Globals.DB_TABLE_PROFILES + " (" + Globals.DB_LOGIN + "," + Globals.DB_PASSWORD + ") VALUES(@login, @password)";
                                cmd.Parameters.AddWithValue("@login", accountInfos[0]);
                                cmd.Parameters.AddWithValue("@password", accountInfos[1]);
                                cmd.Prepare();
                                cmd.ExecuteNonQuery();
                                Log.LogInfo("creation of account successful", LogType.Application_Work);
                                Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Create_Account, "success;profile created"));
                                Log.LogInfo("creation of account successful", LogType.Comm_Output);
                            }

                            dbCon.Close();

                            databaseSemaphore.Release();

                            PrivateMessageManagerAccessSemaphore.WaitOne();
                            PrivateMessageManager.GetLinkPMEventHandler(User).EventHandler += ClientLink_PrivateMessageEventHandler;
                            PrivateMessageManagerAccessSemaphore.Release();

                            break;

                        case MessageType.Update:
                            break;

                        case MessageType.Create_Topic:
                            databaseSemaphore.WaitOne();

                            dbCon.Open();

                            statement = "SELECT * FROM " + Globals.DB_TABLE_TOPICS + " WHERE " +
                                Globals.DB_TOPICS_NAME + "='" + message.MessageBody + "'";
                            cmd = new SQLiteCommand(statement, dbCon);
                            rdr = cmd.ExecuteReader();
                            if (rdr.Read())
                            {
                                rdr.Close();
                                Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Create_Topic, "error"));
                                //LOG
                            }
                            else
                            {
                                //add topic to db
                                cmd = new SQLiteCommand(dbCon);
                                cmd.CommandText = "INSERT INTO " + Globals.DB_TABLE_TOPICS + " (" + Globals.DB_TOPICS_NAME + ") VALUES(@name)";
                                cmd.Parameters.AddWithValue("@name", message.MessageBody.Replace("'", "''"));
                                cmd.Prepare();
                                cmd.ExecuteNonQuery();
                                //LOG

                                //check topic id
                                statement = "SELECT " + Globals.DB_TOPICS_ID + " FROM " + Globals.DB_TABLE_TOPICS + " WHERE " +
                                Globals.DB_TOPICS_NAME + "='" + message.MessageBody + "'";
                                cmd = new SQLiteCommand(statement, dbCon);
                                (rdr = cmd.ExecuteReader()).Read();

                                //create topic table
                                cmd = new SQLiteCommand(dbCon);
                                cmd.CommandText = "CREATE TABLE IF NOT EXISTS topics_" + rdr.GetInt32(0) + "( 'idMessage' INTEGER PRIMARY KEY AUTOINCREMENT,'Date' TEXT,'User' TEXT,'Message' TEXT); ";
                                cmd.Prepare();
                                cmd.ExecuteNonQuery();
                                Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Create_Topic, "success"));
                            }

                            dbCon.Close();

                            databaseSemaphore.Release();

                            message = (Message)Communication.rcvMsg(Comm.GetStream());

                            break;

                        case MessageType.List_Topic:
                            databaseSemaphore.WaitOne();

                            dbCon.Open();

                            statement = "SELECT * FROM " + Globals.DB_TABLE_TOPICS;
                            cmd = new SQLiteCommand(statement, dbCon);
                            rdr = cmd.ExecuteReader();
                            while (rdr.Read())
                            {
                                Communication.sendMsg(Comm.GetStream(), new Message(MessageType.List_Topic, rdr.GetInt32(0) + ";" + rdr.GetString(1)));
                                //LOG
                            }
                            Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Join_Topic));
                            //LOG
                            rdr.Close();
                            dbCon.Close();

                            databaseSemaphore.Release();
                            break;

                        case MessageType.Join_Topic:
                            databaseSemaphore.WaitOne();

                            dbCon.Open();

                            statement = "SELECT * FROM " + Globals.DB_TABLE_TOPICS + " WHERE " +
                                Globals.DB_TOPICS_ID + "='" + message.MessageBody + "'";
                            cmd = new SQLiteCommand(statement, dbCon);
                            rdr = cmd.ExecuteReader();
                            rdr.Read();
                            //send topic name again, should clean this up in client
                            Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Join_Topic, rdr.GetString(1)));
                            //LOG
                            statement = "SELECT * FROM topics_" + message.MessageBody;
                            cmd = new SQLiteCommand(statement, dbCon);
                            rdr = cmd.ExecuteReader();
                            while (rdr.Read())
                            {
                                //chat format : User : date     text
                                Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Join_Topic, rdr.GetString(2) + ":" + rdr.GetString(1) + "\t" + rdr.GetString(3)));
                                //LOG
                            }
                            rdr.Close();
                            dbCon.Close();

                            databaseSemaphore.Release();
                            Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Chat_Topic));
                            //LOG

                            //store topic id for later use
                            CurrentTopic = message.MessageBody;

                            TopicManagerAccessSemaphore.WaitOne();
                            TopicManager.GetTopicEventHandler(CurrentTopic).EventHandler += ClientLink_TopicEventHandler;
                            TopicManagerAccessSemaphore.Release();

                            break;

                        case MessageType.Chat_Topic:
                            Console.WriteLine(message.MessageBody);
                            string[] topicID_messageBody = message.MessageBody.Split(';');
                            DateTime dateTime = DateTime.Now;
                            /*
                            databaseSemaphore.WaitOne();

                            dbCon.Open();

                            //add messsage to db
                            //doesn't work for some reasons ...
                            cmd = new SQLiteCommand(dbCon);
                            cmd.CommandText = "INSERT INTO topics_" + topicID_messageBody[0] + "('Date','User','Message') VALUES('@date','@user','@message')";
                            cmd.Parameters.AddWithValue("@date", dateTime.Date.ToString());
                            cmd.Parameters.AddWithValue("@user", User);
                            cmd.Parameters.AddWithValue("@message", topicID_messageBody[1].Replace("'", "''"));
                            cmd.Prepare();
                            cmd.ExecuteNonQuery();

                            dbCon.Close();

                            databaseSemaphore.Release();
                            */
                            TopicManagerAccessSemaphore.WaitOne();
                            TopicManager.TopicEventManager[CurrentTopic].NewMessageEvent(User, dateTime, topicID_messageBody[1]);
                            TopicManagerAccessSemaphore.Release();

                            break;

                        case MessageType.leave_topic:
                            TopicManagerAccessSemaphore.WaitOne();
                            TopicManager.GetTopicEventHandler(CurrentTopic).EventHandler -= ClientLink_TopicEventHandler;
                            TopicManagerAccessSemaphore.Release();
                            CurrentTopic = null;
                            break;

                        case MessageType.Chat_PrivateMessage:
                            string[] User_messageBody = message.MessageBody.Split(';');
                            dateTime = DateTime.Now;
                            PrivateMessageManagerAccessSemaphore.WaitOne();
                            PrivateMessageManager.PrivateMessageEventManager[User_messageBody[0]].NewMessageEvent(User, dateTime, User_messageBody[1], User_messageBody[0]);
                            PrivateMessageManagerAccessSemaphore.Release();
                            break;

                        default:
                            break;
                    }
                }
            }

            private void ClientLink_PrivateMessageEventHandler(object sender, PrivateMessageEventArgs e)
            {
                Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Chat_PrivateMessage, e.User + ":" + e.Time.Date.ToString() + "\t" + e.MessageBody));
            }

            private void ClientLink_TopicEventHandler(object sender, TopicEventArgs e)
            {
                //chat format : User : date     text
                Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Chat_Topic, e.User + ":" + e.Time.Date.ToString() + "\t" + e.MessageBody));
            }

            public void StopWorking()
            {
                KeepWorking = false;
            }
        }

        [Serializable]
        private class ServerParameters
        {
            [JsonProperty("port")]
            public int port { get; private set; }

            [JsonProperty("ipAddress")]
            public string ipAddress { get; private set; }

            public ServerParameters()
            {
            }

            public ServerParameters(int p, string ip)
            {
                port = p;
                ipAddress = ip;
            }
        }

        private class TopicManager
        {
            public Dictionary<string, TopicEventSender> TopicEventManager;

            public TopicManager()
            {
                TopicEventManager = new Dictionary<string, TopicEventSender>();
            }

            public TopicEventSender GetTopicEventHandler(string topicID)
            {
                if (TopicEventManager.ContainsKey(topicID)) return TopicEventManager[topicID];
                else
                {
                    TopicEventManager.Add(topicID, new TopicEventSender());
                    return TopicEventManager[topicID];
                }
            }
        }

        public class TopicEventSender
        {
            public event EventHandler<TopicEventArgs> EventHandler;

            public void NewMessageEvent(string user, DateTime time, string messageBody, bool privateMessage = false, string targetUser = "")
            {
                RaiseEvent(new TopicEventArgs(user, time, messageBody, privateMessage, targetUser));
            }

            protected virtual void RaiseEvent(TopicEventArgs args)
            {
                //temp copy : anti race conditions
                EventHandler<TopicEventArgs> eventRaiser = EventHandler;
                //null if no subs
                if (eventRaiser != null)
                {
                    //get a raise
                    eventRaiser(this, args);
                }
            }
        }

        public class TopicEventArgs : EventArgs
        {
            public string User { get; private set; }
            public DateTime Time { get; private set; }
            public string MessageBody { get; private set; }
            public bool Private { get; private set; }
            public string TargetUser { get; private set; }

            public TopicEventArgs(string user, DateTime time, string messageBody, bool privateMessage = false, string targetUser = "") : base()
            {
                User = user;
                Time = time;
                MessageBody = messageBody;
                Private = privateMessage;
                if (privateMessage) TargetUser = targetUser;
            }
        }

        //TODO replace with a better thing when doing the interface
        private class PrivateMessageManager
        {
            public Dictionary<string, PrivateMessageEventSender> PrivateMessageEventManager;

            public PrivateMessageManager()
            {
                PrivateMessageEventManager = new Dictionary<string, PrivateMessageEventSender>();
            }

            public PrivateMessageEventSender GetLinkPMEventHandler(string user)
            {
                if (PrivateMessageEventManager.ContainsKey(user)) return PrivateMessageEventManager[user];
                else
                {
                    PrivateMessageEventManager.Add(user, new PrivateMessageEventSender());
                    return PrivateMessageEventManager[user];
                }
            }

            public void RemoveLinkPMEventHandler(string user)
            {
                PrivateMessageEventManager.Remove(user);
            }
        }

        public class PrivateMessageEventSender
        {
            public event EventHandler<PrivateMessageEventArgs> EventHandler;

            public void NewMessageEvent(string user, DateTime time, string messageBody, string targetUser)
            {
                RaiseEvent(new PrivateMessageEventArgs(user, time, messageBody, targetUser));
            }

            protected virtual void RaiseEvent(PrivateMessageEventArgs args)
            {
                //temp copy : anti race conditions
                EventHandler<PrivateMessageEventArgs> eventRaiser = EventHandler;
                //null if no subs
                if (eventRaiser != null)
                {
                    //get a raise
                    eventRaiser(this, args);
                }
            }
        }

        public class PrivateMessageEventArgs : EventArgs
        {
            public string User { get; private set; }
            public DateTime Time { get; private set; }
            public string MessageBody { get; private set; }
            public string TargetUser { get; private set; }

            public PrivateMessageEventArgs(string user, DateTime time, string messageBody, string targetUser) : base()
            {
                User = user;
                Time = time;
                MessageBody = messageBody;
                TargetUser = targetUser;
            }
        }
    }
}