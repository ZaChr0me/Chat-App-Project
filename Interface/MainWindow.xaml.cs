using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using ChatClient;

namespace Interface
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        //use to communicate with Client
        private Client.InterfaceToClientEventSender ITCEventSender;

        private Client client;
        private bool ConnectedToServer;

        public ObservableCollection<Topic> Topics;

        public Dictionary<string, ChatUserControl> ChatUserControls;
        private Dictionary<string, string> CurrentChats;

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;

            Topics = new ObservableCollection<Topic>();
            CurrentChats = new Dictionary<string, string>();
            ChatUserControls = new Dictionary<string, ChatUserControl>();
            while (!IsInitialized)
            {
            }
            TopicsListView.ItemsSource = Topics;
            ConnectedToServer = false;
            client = new Client(false);
            Action action = () => InitAfterComponentInitialization();
            Task task = new Task(action);
            task.Start();
            //make sure both interface modules don't show up at start
            this.LoginGridModule.Visibility = Visibility.Hidden;
            this.MainTabControlModule.Visibility = Visibility.Collapsed;
        }

        public class Topic
        {
            public string ID { get; private set; }
            public string Name { get; private set; }

            public Topic(string id, string name)
            {
                ID = id;
                Name = name;
            }
        }

        private void InitAfterComponentInitialization()
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            CancellationToken ct = cts.Token;
            Action action = () => client.InitClient(ct);

            //init the client in the background
            Task task = new Task(action, ct);
            task.Start();

            client.CTIEventSender.EventHandler += CTIEventTriggered;
            ITCEventSender = new Client.InterfaceToClientEventSender();
            client.LinkInterfaceAndClient(ITCEventSender);

            while (!ConnectedToServer)
            {
            }
        }

        //use to get infos from the client
        private void CTIEventTriggered(object sender, Client.LinkClientToInterfaceArgs e)
        {
            CTIMessageTypes messageType = e.CTIMessageType;
            string content = e.Content;
            string targetChat = e.TargetChat;
            switch (messageType)
            {
                case CTIMessageTypes.SuccessfulConnect:
                    ConnectedToServer = true;
                    Dispatcher.BeginInvoke(new ThreadStart(() => AppInfoTextBlock.Text = "Connected to " + content));
                    Dispatcher.BeginInvoke(new ThreadStart(() =>
                    this.LoginGridModule.Visibility = Visibility.Visible));
                    break;

                case CTIMessageTypes.Chatting:
                    if (e.PrivateMessage)
                    {
                        bool noChatOpen = true;
                        foreach (string item in CurrentChats.Values)
                        {
                            if (item == e.TargetChat)
                            {
                                noChatOpen = false;
                                Dispatcher.BeginInvoke(new ThreadStart(() =>
                                ChatUserControls[item].AddToChat(e.Content)));
                            }
                        }
                        if (noChatOpen)
                        {
                            CreateChatTab(e.TargetChat, e.Content.Split(";")[0]);
                            Dispatcher.BeginInvoke(new ThreadStart(() =>
                                ChatUserControls[e.TargetChat].AddToChat(e.Content)));
                        }
                    }
                    else
                    {
                        foreach (string item in CurrentChats.Keys)
                        {
                            if (item == e.TargetChat)
                            {
                                Dispatcher.BeginInvoke(new ThreadStart(() =>
                                ChatUserControls[item].AddToChat(e.Content)));
                            }
                        }
                    }

                    break;

                case CTIMessageTypes.TopicList:

                    //clearing the list doesn't seem to work when it comes to refilling it
                    for (int i = Topics.Count - 1; i >= 0; i--)
                    {
                        Topics.RemoveAt(i);
                    }
                    foreach (var item in (Dictionary<string, string>)e.RawContent)
                    {
                        Topics.Add(new Topic(item.Key, item.Value));
                    }
                    TopicsListView.ItemsSource = Topics;
                    //Dispatcher.BeginInvoke(new ThreadStart(() => ReloadTopics()));

                    break;

                case CTIMessageTypes.Error:
                    break;

                case CTIMessageTypes.Login:
                    break;

                case CTIMessageTypes.NewAccountCreated:
                    Dispatcher.BeginInvoke(new ThreadStart(() =>
                    AppInfoTextBlock.Text = "New account created and logged in successfully"));
                    //an attempt to reduce lines
                    goto ConnectedLabel;

                case CTIMessageTypes.LoggedInAccount:
                    Dispatcher.BeginInvoke(new ThreadStart(() =>
                    AppInfoTextBlock.Text = "Successfully logged into account"));
                ConnectedLabel:
                    Dispatcher.BeginInvoke(new ThreadStart(() =>
                    this.LoginGridModule.Visibility = Visibility.Collapsed));
                    Dispatcher.BeginInvoke(new ThreadStart(() =>
                    this.MainTabControlModule.Visibility = Visibility.Visible));
                    Dispatcher.BeginInvoke(new ThreadStart(() =>
                    this.DisconnectButton.IsEnabled = true));
                    break;

                case CTIMessageTypes.LoginOrPasswordIncorrect:
                    Dispatcher.BeginInvoke(new ThreadStart(() =>
                    AppInfoTextBlock.Text = "Enter a valid Login and Password"));
                    break;

                case CTIMessageTypes.LoginAlreadyExistsOrIncorrect:
                    Dispatcher.BeginInvoke(new ThreadStart(() =>
                    AppInfoTextBlock.Text = "This login already exists or contains invalid characters"));
                    break;

                case CTIMessageTypes.CreateTopic:
                    string[] substring = e.Content.Split(";");
                    string id = substring[0];
                    string name = substring[1];
                    Dispatcher.BeginInvoke(new ThreadStart(() =>
                    CreateChatTab(id, name)));
                    break;

                case CTIMessageTypes.Disconnect:
                    break;

                case CTIMessageTypes.TopicAlreadyExists:
                    Dispatcher.BeginInvoke(new ThreadStart(() =>
                    AppInfoTextBlock.Text = "The Topic Already Exists, please enter another name"));
                    break;

                default:
                    break;
            }
        }

        private void CreateOrConnectToAccountButton_Click(object sender, RoutedEventArgs e)
        {
            if (LoginTextBox.Text == "" && PasswordTextBox.Text == "")
            {
                AppInfoTextBlock.Text = "Enter a valid Login and Password";
            }
            int logOrCreate = (((Button)sender).Name == "CreateAccountButton") ? 1 : 0;
            string login = LoginTextBox.Text;
            string password = PasswordTextBox.Text;

            Action action = () => client.Login(logOrCreate, login, password);
            Task task = new Task(action);
            task.Start();
        }

        private void PrivateMessageButton_Click(object sender, RoutedEventArgs e)
        {
            string name = PrivateMessageUserTextBox.Text;
            if (!CurrentChats.Values.Contains(name))
            {
                CreateChatTab("-1", name);
            }
            ITCEventSender.SendContentFromChatToClientEvent(CTIMessageTypes.Chatting, name, PrivateMessageContentTextBox.Text, true);
            PrivateMessageUserTextBox.Text = "";
            PrivateMessageContentTextBox.Text = "";
        }

        private void TopicsButton_Click(object sender, RoutedEventArgs e)
        {
            this.TopicsTabItem.Visibility = Visibility.Visible;
            this.MainTabControlModule.SelectedIndex = 1;
            ITCEventSender.SendContentToClient(CTIMessageTypes.TopicList, "");
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            ITCEventSender.SendContentToClient(CTIMessageTypes.Disconnect, "");
            //TODO Cleanup Interface
            this.LoginTextBox.Text = "";
            this.PasswordTextBox.Text = "";

            this.LoginGridModule.Visibility = Visibility.Visible;
            this.MainTabControlModule.Visibility = Visibility.Collapsed;
            this.DisconnectButton.IsEnabled = false;
        }

        private void CreateNewTopicButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.NewTopicNameTextBox.Text.Contains(";"))
            {
                this.AppInfoTextBlock.Text = "The topic name contains an illegal character (;)";
            }
            else
            {
                ITCEventSender.SendContentToClient(CTIMessageTypes.CreateTopic, this.NewTopicNameTextBox.Text);
            }
        }

        private void RefreshTopicsButton_Click(object sender, RoutedEventArgs e)
        {
            ITCEventSender.SendContentToClient(CTIMessageTypes.TopicList, "");
        }

        private void CreateChatTab(string id, string name)
        {
            TabItem newChat = new TabItem();
            newChat.Header = name;
            CurrentChats.Add(id, name);
            ChatUserControl chatUserControl = new ChatUserControl(id, name);
            ChatUserControls.Add(id, chatUserControl);
            chatUserControl.PassMessageToClientEventHandler += SendMessage;
            newChat.Content = chatUserControl;

            //BODY OF TOPIC
            this.MainTabControlModule.Items.Add(newChat);
            this.MainTabControlModule.SelectedIndex = MainTabControlModule.Items.Count - 1;
        }

        private void JoinTopicButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CurrentChats.ContainsKey(Topics[TopicsListView.SelectedIndex].ID))
            {
                if (TopicsListView.SelectedIndex != -1)
                {
                    ITCEventSender.SendContentToClient(CTIMessageTypes.JoinTopic, Topics[TopicsListView.SelectedIndex].ID);
                }
                CreateChatTab(Topics[TopicsListView.SelectedIndex].ID, Topics[TopicsListView.SelectedIndex].Name);
            }
        }

        private void SendMessage(object sender, ChatUserControl.PassMessageToClientEvent e)
        {
            if (e.ID == "-1")
            {
                ITCEventSender.SendContentFromChatToClientEvent(CTIMessageTypes.Chatting, e.Name, e.Message, true);
            }
            else
            {
                ITCEventSender.SendContentFromChatToClientEvent(CTIMessageTypes.Chatting, e.ID, e.Message);
            }
        }
    }
}