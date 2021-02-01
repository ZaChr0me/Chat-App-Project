using ChatServer.Logs;
using Connectivity;
using GeneralLibrary;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ChatClient
{
    public enum ClientState
    {
        Login,
        Disconnecting,
        Working,
        Exiting
    }

    public enum CTIMessageTypes
    {
        SuccessfulConnect,
        Chatting,
        PersonnalMessage,
        TopicList,
        TopicAlreadyExists,
        CreateTopic,
        JoinTopic,
        Login,
        LoginOrPasswordIncorrect,
        LoginAlreadyExistsOrIncorrect,
        NewAccountCreated,
        LoggedInAccount,
        Disconnect,
        Error
    }

    public class Client
    {
        private string Hostname;
        private int Port;
        private TcpClient Comm;
        private Message ReceivedMessage;

        private bool KeepWorking;
        private bool KeepChatting;
        private bool ConsoleMode;
        private string User;
        private ClientState State;

        public ClientToInterfaceEventSender CTIEventSender;

        public Client(bool directInit)
        {
            ConsoleMode = false;
            Log.LogInfo("Setting up Client...", LogType.Application_Work);
            KeepWorking = true;

            CTIEventSender = new ClientToInterfaceEventSender();

            if (!File.Exists(AppDomain.CurrentDomain.BaseDirectory + Globals.PARAMETERS_PATH))
            {
                JSONManagement.WriteToJsonFile<ClientParameters>(
                    AppDomain.CurrentDomain.BaseDirectory + Globals.PARAMETERS_PATH,
                    new ClientParameters(8407, "127.0.0.1"));
            }

            ClientParameters clientParameters = JSONManagement.ReadFromJsonFile<ClientParameters>(AppDomain.CurrentDomain.BaseDirectory + Globals.PARAMETERS_PATH);
            this.Port = clientParameters.port;
            this.Hostname = clientParameters.ipAddress;

            //when using the console, init directly
            if (directInit) InitClient();
        }

        /// <summary>
        /// synchronous version for the Client Constructor
        /// </summary>
        private void InitClient()
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            CancellationToken ct = cts.Token;
            Action action = () => Connect(ct);

            //start trying to connect
            Task task = new Task(action, ct);
            task.Start();

            //event subscribing for disconnecting when app closes
            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;

            //wait 15 sec (in ms) before timeout
            if (task.Wait(15000))
            {
                Log.LogInfo("Connected to server", LogType.Connection_To_Server);
                Console.WriteLine("Connected to server " + Hostname + ":" + Port);
            }
            else
            {
                Log.LogInfo("Connection to server failed", LogType.Connection_To_Server);
                Console.WriteLine("Could not connect to server " + Hostname + ":" + Port);
                return;
            }
            cts.Cancel();
            cts.Dispose();
            task.Wait();
            task.Dispose();
            Log.LogInfo("Shutting down the connection process.", LogType.Application_Work);

            Console.WriteLine("you are now connected to the chat server");
            State = ClientState.Login;
            do
            {
                switch (State)
                {
                    case ClientState.Login:
                        Login();
                        break;

                    case ClientState.Disconnecting:
                        Disconnect();
                        break;

                    case ClientState.Working:
                        Work();
                        break;

                    case ClientState.Exiting:
                        break;

                    default:
                        break;
                }
            } while (State != ClientState.Disconnecting);
        }

        /// <summary>
        /// asynchronous version for the Interface
        /// </summary>
        /// <param name="cancellationToken"></param>
        public void InitClient(CancellationToken cancellationToken)
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            CancellationToken ct = cts.Token;
            Action action = () => Connect(ct);

            //start trying to connect
            Task task = new Task(action, ct);
            task.Start();

            //event subscribing for disconnecting when app closes
            AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;

            //wait 15 sec (in ms) before timeout
            if (task.Wait(15000))
            {
                Log.LogInfo("Connected to server", LogType.Connection_To_Server);
                CTIEventSender.SendContentToInterface(CTIMessageTypes.SuccessfulConnect, Hostname + ":" + Port);
            }
            else
            {
                Log.LogInfo("Connection to server failed", LogType.Connection_To_Server);
                CTIEventSender.SendContentToInterface(CTIMessageTypes.Error, "Could not connect to server " + Hostname + ":" + Port);
                return;
            }
            cts.Cancel();
            cts.Dispose();
            task.Wait();
            task.Dispose();
            Log.LogInfo("Shutting down the connection process.", LogType.Application_Work);
        }

        private void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            KeepWorking = false;
            Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Exit));
            Communication.rcvMsg(Comm.GetStream());
            if (Comm.Connected) Comm.Close();
        }

        private void Connect(CancellationToken cancellationToken)
        {
            Log.LogInfo("Starting the Connection process", LogType.Application_Work);
            //allows for force stopping of task
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            //internal time counter
            Stopwatch stopWatch = new Stopwatch();
            do
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    Comm = new TcpClient(Hostname, Port);
                    Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Connection_Test));
                    Log.LogInfo(MessageType.Connection_Test.ToString(), LogType.Comm_Output);
                    //start/restart time counter to give time for the server to respond
                    stopWatch.Restart();
                    while (stopWatch.ElapsedMilliseconds <= 1000 && Comm.Connected)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return;
                        }
                        if (Communication.rcvMsg(Comm.GetStream()).MessageType == MessageType.Connection_Success)
                        {
                            Log.LogInfo(MessageType.Connection_Success.ToString(), LogType.Comm_Input);
                            Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Connection_Success));
                            Log.LogInfo(MessageType.Connection_Success.ToString(), LogType.Comm_Output);
                            return;
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            } while (true);
        }

        //Console Version
        private void Login()
        {
            char answer;
            bool repeat = false;
            do
            {
                if (repeat) Console.WriteLine("Incorrect answer");
                Console.WriteLine("Please choose one of the following :");
                Console.WriteLine("1.\tConnect to an account\n2.\tCreate an account");
                answer = Console.ReadKey().KeyChar;
                repeat = true;
                Console.Clear();
            } while (answer != '1' && answer != '2');
            repeat = false;

            if (answer == '1')
            {
                string login = "";
                string password = "";
                bool connected = false;
                //log into account
                do
                {
                    if (repeat) Console.WriteLine("\nhe login or password you entered are incorrect or do not correspond to an account. Please try again");
                    Console.WriteLine("Enter your login :");
                    login = Console.ReadLine();
                    Console.WriteLine("Enter your password :");
                    //Obfuscate and crypt it
                    password = Console.ReadLine();

                    //send login and password to server and wait for response
                    Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Login, login + ";" + password));
                    connected = ((Message)Communication.rcvMsg(Comm.GetStream())).MessageBody.Contains("success");
                    repeat = true;
                } while (!connected);
                Console.WriteLine("Connected successfully. Welcome, " + login);
                User = login;
            }
            else
            {
                string login = "";
                string password = "";
                bool connected = false;
                //create an account
                do
                {
                    if (repeat) Console.WriteLine("The login you entered already correspond to an account. Please try again");
                    bool incorrectChar = false;
                    do
                    {
                        if (incorrectChar) Console.WriteLine("incorrect character, please retry");
                        Console.WriteLine("Enter your login :");
                        login = Console.ReadLine();
                        incorrectChar = true;
                    } while (login.Contains(";"));
                    Console.WriteLine("Enter your password :");
                    //Obfuscate and crypt it
                    password = Console.ReadLine();
                    //send login and password to server and wait for response
                    Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Create_Account, login + ";" + password));
                    connected = ((Message)Communication.rcvMsg(Comm.GetStream())).MessageBody.Contains("success");
                    repeat = true;
                } while (!connected);
                Console.WriteLine("Account created successfully. Welcome, " + login);
                User = login;
            }
            State = ClientState.Working;
        }

        //UI Version
        public void Login(int loginOrCreateAccount, string login, string password)
        {
            bool repeat = false;

            if (loginOrCreateAccount == 0)
            {
                //log into account

                //Obfuscate and crypt password here

                //send login and password to server and wait for response
                Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Login, login + ";" + password));
                //LOG
                bool connected = ((Message)Communication.rcvMsg(Comm.GetStream())).MessageBody.Contains("success");
                //LOG
                if (connected)
                {
                    CTIEventSender.SendContentToInterface(CTIMessageTypes.LoggedInAccount, "");
                    Action RMaction = () => ReceiveMessage();
                    Task RMtask = new Task(RMaction);
                    RMtask.Start();
                    User = login;
                }
                else
                {
                    CTIEventSender.SendContentToInterface(CTIMessageTypes.LoginOrPasswordIncorrect, "");
                }
            }
            else if (loginOrCreateAccount == 1)
            {
                //create an account

                //check for incorrect characters
                if (login.Contains(";"))
                {
                    CTIEventSender.SendContentToInterface(CTIMessageTypes.LoginAlreadyExistsOrIncorrect, "");
                    return;
                }
                //Obfuscate and crypt password here
                //send login and password to server and wait for response
                Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Create_Account, login + ";" + password));
                //LOG
                string message = ((Message)Communication.rcvMsg(Comm.GetStream())).MessageBody;
                if (message.Contains("success"))
                {
                    CTIEventSender.SendContentToInterface(CTIMessageTypes.NewAccountCreated, "");
                }
                else if (message.Contains("login exists"))
                {
                    CTIEventSender.SendContentToInterface(CTIMessageTypes.LoginAlreadyExistsOrIncorrect, "");
                }
                else
                {
                    CTIEventSender.SendContentToInterface(CTIMessageTypes.NewAccountCreated, "");
                    User = login;
                    Action RMaction = () => ReceiveMessage();
                    Task RMtask = new Task(RMaction);
                    RMtask.Start();
                }
            }
        }

        //For Console
        private void Work()
        {
            char answerChar;
            string answerString;
            string currentChat;

            //TODO start earlier

            do
            {
                answerString = ConsoleManagement.GetUserInput("\nchoose one : \n" +
                    "1.\tcreate topic\n" +
                    "2.\tsee list of topic and join\n" +
                    "3.\tsend private message to a user\n" +
                    "4.\tdisconnect\n",
                    new List<string> { "1", "2", "3", "4" });
                bool repeat = false;
                switch (answerString)
                {
                    default:
                        break;

                    case "1"://Creating a topic

                        do
                        {
                            if (repeat) Console.WriteLine("\nThe topic already exists");
                            Console.WriteLine("enter the name of the topic :");
                            answerString = Console.ReadLine();
                            //TODO bloquer les charactères interdits

                            //does that topic name already exist?
                            Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Create_Topic, answerString));
                            repeat = !CheckReceivedMessage("success", true);
                        } while (repeat);

                        Console.WriteLine("Enter a description for the topic :");
                        answerString = Console.ReadLine();
                        Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Create_Topic, answerString));
                        answerString = ConsoleManagement.GetUserInput("do you want to join now?\n\tyes\n\tno\n", new List<string> { "yes", "no" });
                        if (answerString == "yes")
                        {
                            //TODO
                            break;
                        }
                        else
                        {
                            break;
                        }

                    case "2"://Listing and Joining Topics
                        Console.WriteLine("\nHere are the existing topics: ");
                        Communication.sendMsg(Comm.GetStream(), new Message(MessageType.List_Topic));
                        repeat = true;
                        List<string> topics = new List<string>();
                        do
                        {
                            repeat = CheckReceivedMessage(MessageType.List_Topic, false);
                            if (repeat)
                            {
                                string[] substring = ReceivedMessage.MessageBody.Split(';');
                                ReceivedMessage = null;
                                Console.WriteLine(substring[0] + ".\t" + substring[1] + "\n");
                                topics.Add(substring[0]);
                            }
                            if (topics.Count == 0) Console.WriteLine("There are no topics, please create one and come back");
                        } while (repeat);
                        ReceivedMessage = null;
                        answerString = ConsoleManagement.GetUserInput("\nEnter the id of the topic you wish to join or enter 'return' to go back to the menu :", new List<string>(topics) { "return" });
                        currentChat = answerString;
                        if (answerString == "return")
                        {
                            //close working and restart it to go back to the menu fast
                            State = ClientState.Working;
                            return;
                        }

                        //join topic and get old messages
                        Console.Clear();
                        Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Join_Topic, answerString));
                        while (ReceivedMessage == null) { }
                        Console.WriteLine("joined topic :" + ReceivedMessage.MessageBody + ". Enter 'menu' to leave the topic");
                        do
                        {
                            repeat = true;
                            if (ReceivedMessage != null)
                            {
                                repeat = CheckReceivedMessage(MessageType.Chat_Topic, false);
                                if (repeat)
                                {
                                    Console.WriteLine(ReceivedMessage.MessageBody);
                                    ReceivedMessage = null;
                                }
                            }
                        } while (repeat);

                        //chatting
                        string chat = "";
                        KeepChatting = true;
                        do
                        {
                            chat = Console.ReadLine();
                            //replace with button and textboxes
                            if (chat != "menu")
                            {
                                Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Chat_Topic, currentChat + ";" + chat));
                            }
                        } while (chat != "menu" && chat != "Menu");//TODO mieux que ça, ça sera dans l'interface, hein
                        KeepChatting = false;
                        Communication.sendMsg(Comm.GetStream(), new Message(MessageType.leave_topic));
                        break;

                    case "3":
                        Console.WriteLine("enter a user to send a message to or enter 'menu' to leave :");
                        chat = Console.ReadLine();
                        if (chat != "menu")
                        {
                            string targerUser = chat;
                            Console.WriteLine("write your message or enter menu to leave :");
                            if ((chat = Console.ReadLine()) != "menu")
                            {
                                Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Chat_PrivateMessage, targerUser + ";" + chat));
                            }
                        }
                        break;

                    case "4":
                        State = ClientState.Disconnecting;
                        return;
                }
            } while (KeepWorking);
        }

        private void Disconnect()
        {
            User = null;
            Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Disconnect));
            State = ClientState.Login;
        }

        private void ReceiveMessage()
        {
            Message message = null;
            do
            {
                while (ReceivedMessage != null)
                {
                }
                try
                {
                    message = (Message)Communication.rcvMsg(Comm.GetStream());
                }
                catch (System.Runtime.Serialization.SerializationException)
                {
                    //serialization error guaranteed on exit
                }
                switch (message.MessageType)
                {
                    //all those cases are only used for communication, so best pass them to the rest of the app and keep this thread working simply
                    case MessageType.Connection_Test:
                    case MessageType.Connection_Success:
                    case MessageType.SendMessage_To_Server:
                    case MessageType.SendMessage_To_Client:
                    case MessageType.Login:
                    case MessageType.Disconnect:
                    case MessageType.Create_Account:
                    case MessageType.Update:
                    case MessageType.Create_Topic:
                    case MessageType.List_Topic:
                    case MessageType.Join_Topic:
                    case MessageType.leave_topic:
                        ReceivedMessage = message;
                        break;

                    case MessageType.Chat_Topic:
                        if (KeepChatting && ConsoleMode)
                        {
                            Console.WriteLine("\n");
                            Console.WriteLine(message.MessageBody);
                        }
                        else
                        {
                            string[] substring = message.MessageBody.Split(";");
                            //TODO pass all substrings to the client for it to deal with presentation
                            CTIEventSender.SendContentToChatEvent(CTIMessageTypes.Chatting, substring[0], substring[1] + ":" + substring[2] + "\t" + substring[3]);
                        }
                        break;

                    case MessageType.Chat_PrivateMessage:

                        if (ConsoleMode)
                        {
                            Console.WriteLine("\n");
                            Console.WriteLine(message.MessageBody);
                            Console.WriteLine("\n");
                        }
                        else
                        {
                            string[] substring = message.MessageBody.Split();
                            //TODO pass all substrings to the client for it to deal with presentation
                            CTIEventSender.SendContentToChatEvent(CTIMessageTypes.Chatting, substring[0], substring[0] + ":" + substring[1] + "\t" + substring[2], true);
                        }
                        break;

                    default:
                        break;
                }
            } while (true);
        }

        /// <summary>
        /// If resetMessage is true, empties the ReceivedMessage
        /// </summary>
        /// <param name="expectedInfo"></param>
        /// <param name="resetMessage"></param>
        /// <returns></returns>
        private bool CheckReceivedMessage(string expectedInfo, bool resetMessage)
        {
            Log.LogInfo((ReceivedMessage == null).ToString());
            bool response;
            while (ReceivedMessage == null)
            {
            }
            response = (ReceivedMessage.MessageBody == expectedInfo) ? true : false;
            ReceivedMessage = (resetMessage) ? null : ReceivedMessage;
            return response;
        }

        /// <summary>
        /// If resetMessage is true, empties the ReceivedMessage
        /// </summary>
        /// <param name="expectedmessageType"></param>
        /// <param name="resetMessage"></param>
        /// <returns></returns>
        private bool CheckReceivedMessage(MessageType expectedmessageType, bool resetMessage)
        {
            bool response;
            while (ReceivedMessage == null)
            {
            }
            response = (ReceivedMessage.MessageType == expectedmessageType) ? true : false;
            ReceivedMessage = (resetMessage) ? null : ReceivedMessage;
            return response;
        }

        [Serializable]
        private class ClientParameters
        {
            [JsonProperty("port")]
            public int port { get; private set; }

            [JsonProperty("ipAddress")]
            public string ipAddress { get; private set; }

            public ClientParameters()
            {
            }

            public ClientParameters(int p, string ip)
            {
                port = p;
                ipAddress = ip;
            }
        }

        public void LinkInterfaceAndClient(InterfaceToClientEventSender interfaceToClientEventSender)
        {
            interfaceToClientEventSender.EventHandler += InterfaceToClientEventTriggered;
        }

        //Equivalent to Work() (but setup for Interface)
        private void InterfaceToClientEventTriggered(object sender, LinkInterfaceToClientArgs e)
        {
            switch (e.CTIMessageType)
            {
                case CTIMessageTypes.SuccessfulConnect:
                    break;

                case CTIMessageTypes.Chatting:
                    if (e.PrivateMessage)
                    {
                        Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Chat_PrivateMessage, e.TargetChat + ";" + e.Content));
                    }
                    else
                    {
                        Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Chat_Topic, e.TargetChat + ";" + e.Content));
                    }
                    break;

                case CTIMessageTypes.PersonnalMessage:
                    break;

                case CTIMessageTypes.TopicList:
                    Communication.sendMsg(Comm.GetStream(), new Message(MessageType.List_Topic));
                    Dictionary<string, string> topics = new Dictionary<string, string>();
                    bool repeat = false;
                    do
                    {
                        repeat = CheckReceivedMessage(MessageType.List_Topic, false);
                        if (repeat)
                        {
                            string[] substring = ReceivedMessage.MessageBody.Split(';');
                            ReceivedMessage = null;
                            topics.Add(substring[0], substring[1]);
                        }
                        else
                        {
                            ReceivedMessage = null;
                        }
                    } while (repeat);
                    CTIEventSender.SendContentToInterface(CTIMessageTypes.TopicList, topics);
                    break;

                case CTIMessageTypes.Login:
                    break;

                case CTIMessageTypes.LoginOrPasswordIncorrect:
                    break;

                case CTIMessageTypes.LoginAlreadyExistsOrIncorrect:
                    break;

                case CTIMessageTypes.NewAccountCreated:
                    break;

                case CTIMessageTypes.LoggedInAccount:
                    break;

                case CTIMessageTypes.Disconnect:
                    Disconnect();
                    break;

                case CTIMessageTypes.Error:
                    break;

                case CTIMessageTypes.CreateTopic:

                    Action CTaction = () => CreateTopic(e.Content);
                    Task CTtast = new Task(CTaction);
                    CTtast.Start();
                    break;

                case CTIMessageTypes.TopicAlreadyExists:
                    break;

                case CTIMessageTypes.JoinTopic:
                    Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Join_Topic, e.Content));
                    break;

                default:
                    break;
            }
        }

        private void CreateTopic(string Content)
        {
            Communication.sendMsg(Comm.GetStream(), new Message(MessageType.Create_Topic, Content));
            while (ReceivedMessage == null || ReceivedMessage.MessageType != MessageType.Create_Topic)
            {
            }
            if (ReceivedMessage.MessageBody.Contains("success"))
            {
                CTIEventSender.SendContentToInterface(CTIMessageTypes.CreateTopic, ReceivedMessage.MessageBody[8..]);
            }
            else
            {
                CTIEventSender.SendContentToInterface(CTIMessageTypes.TopicAlreadyExists, "");
            }
            ReceivedMessage = null;
        }

        public class InterfaceToClientEventSender
        {
            public event EventHandler<LinkInterfaceToClientArgs> EventHandler;

            public void SendContentFromChatToClientEvent(CTIMessageTypes ctiMessageType, string targetChat, string content, bool privateMessage = false)
            {
                RaiseEvent(new LinkInterfaceToClientArgs(ctiMessageType, targetChat, content, privateMessage));
            }

            public void SendContentToClient(CTIMessageTypes ctiMessageType, string content)
            {
                RaiseEvent(new LinkInterfaceToClientArgs(ctiMessageType, content));
            }

            public void SendContentToClient(CTIMessageTypes ctiMessageType, object content)
            {
                RaiseEvent(new LinkInterfaceToClientArgs(ctiMessageType, content));
            }

            protected virtual void RaiseEvent(LinkInterfaceToClientArgs args)
            {
                //temp copy : anti race conditions
                EventHandler<LinkInterfaceToClientArgs> eventRaiser = EventHandler;
                //null if no subs
                if (eventRaiser != null)
                {
                    //get a raise
                    eventRaiser(this, args);
                }
            }
        }

        public class LinkInterfaceToClientArgs : EventArgs
        {
            public CTIMessageTypes CTIMessageType { get; private set; }
            public string Content { get; private set; }
            public string TargetChat { get; private set; }
            public object RawContent { get; private set; }
            public bool PrivateMessage { get; private set; }

            public LinkInterfaceToClientArgs(CTIMessageTypes ctiMessageType, string targetChat, string content, bool privateMessage)
            {
                CTIMessageType = ctiMessageType;
                TargetChat = targetChat;
                Content = content;
                RawContent = null;
                PrivateMessage = privateMessage;
            }

            //for non chat communication between client and interface
            public LinkInterfaceToClientArgs(CTIMessageTypes ctiMessageType, string content)
            {
                CTIMessageType = ctiMessageType;
                TargetChat = "n/A";
                Content = content;
                RawContent = null;
            }

            public LinkInterfaceToClientArgs(CTIMessageTypes ctiMessageType, object rawContent)
            {
                CTIMessageType = ctiMessageType;
                TargetChat = "n/A";
                Content = null;
                RawContent = rawContent;
            }
        }

        public class ClientToInterfaceEventSender
        {
            public event EventHandler<LinkClientToInterfaceArgs> EventHandler;

            public void SendContentToChatEvent(CTIMessageTypes ctiMessageType, string targetChat, string content, bool privateMessage = false)
            {
                RaiseEvent(new LinkClientToInterfaceArgs(ctiMessageType, targetChat, content, privateMessage));
            }

            public void SendContentToInterface(CTIMessageTypes ctiMessageType, string content)
            {
                RaiseEvent(new LinkClientToInterfaceArgs(ctiMessageType, content));
            }

            public void SendContentToInterface(CTIMessageTypes ctiMessageType, object content)
            {
                RaiseEvent(new LinkClientToInterfaceArgs(ctiMessageType, content));
            }

            protected virtual void RaiseEvent(LinkClientToInterfaceArgs args)
            {
                //temp copy : anti race conditions
                EventHandler<LinkClientToInterfaceArgs> eventRaiser = EventHandler;
                //null if no subs
                if (eventRaiser != null)
                {
                    //get a raise
                    eventRaiser(this, args);
                }
            }
        }

        public class LinkClientToInterfaceArgs : EventArgs
        {
            public CTIMessageTypes CTIMessageType { get; private set; }
            public string Content { get; private set; }
            public string TargetChat { get; private set; }
            public object RawContent { get; private set; }
            public bool PrivateMessage { get; private set; }

            public LinkClientToInterfaceArgs(CTIMessageTypes ctiMessageType, string targetChat, string content, bool privateMessage)
            {
                CTIMessageType = ctiMessageType;
                TargetChat = targetChat;
                Content = content;
                RawContent = null;
                PrivateMessage = privateMessage;
            }

            //for non chat communication between client and interface
            public LinkClientToInterfaceArgs(CTIMessageTypes ctiMessageType, string content)
            {
                CTIMessageType = ctiMessageType;
                TargetChat = "n/A";
                Content = content;
                RawContent = null;
            }

            public LinkClientToInterfaceArgs(CTIMessageTypes ctiMessageType, object rawContent)
            {
                CTIMessageType = ctiMessageType;
                TargetChat = "n/A";
                Content = null;
                RawContent = rawContent;
            }
        }
    }
}