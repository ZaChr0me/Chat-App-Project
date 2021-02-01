using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Interface
{
    /// <summary>
    /// Interaction logic for ChatUserControl.xaml
    /// </summary>
    public partial class ChatUserControl : UserControl
    {
        public string ID { get; private set; }
        public string Name { get; private set; }
        public EventHandler<PassMessageToClientEvent> PassMessageToClientEventHandler;

        public void AddToChat(string text)
        {
            this.ChatListView.Items.Add(text);
        }

        public ChatUserControl(string id, string name)
        {
            ID = id;
            Name = name;
            InitializeComponent();
        }

        private void SendMessageButton_Click(object sender, RoutedEventArgs e)
        {
            PassMessageToClientEventHandler(this, new PassMessageToClientEvent(ID, Name, this.MessageTextBox.Text));
            this.MessageTextBox.Clear();
        }

        public class PassMessageToClientEvent : EventArgs
        {
            public string ID { get; private set; }
            public string Name { get; private set; }
            public string Message { get; private set; }

            public PassMessageToClientEvent(string id, string name, string message)
            {
                ID = id;
                Name = name;
                Message = message;
            }
        }
    }
}