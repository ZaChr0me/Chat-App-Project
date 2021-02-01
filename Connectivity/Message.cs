using System;

namespace Connectivity
{
    public interface IMessage
    {
        public MessageType MessageType { get; set; }

        string ToString();
    }

    public enum MessageType
    {
        Connection_Test,
        Connection_Success,
        SendMessage_To_Server,
        SendMessage_To_Client,
        Login,
        Disconnect,
        Create_Account,
        Update,
        Create_Topic,
        List_Topic,
        Join_Topic,
        leave_topic,
        Chat_Topic,
        Chat_PrivateMessage
    }

    /// <summary>
    /// not yet implemented
    /// s for string, b for boolean, i for int,
    /// </summary>
    public enum MessageInfoType
    {
        s,
        b,
        i
    }

    [Serializable]
    public class Message : IMessage
    {
        public MessageType MessageType { get; set; }

        public string MessageBody { get; private set; }

        public Message(MessageType messageType)
        {
            MessageType = messageType;
            MessageBody = "";
        }

        public Message(MessageType messageType, string messageBody)
        {
            MessageType = messageType;
            MessageBody = messageBody;
        }

        public override string ToString()
        {
            return MessageType.ToString() + " : " + MessageBody;
        }
    }
}