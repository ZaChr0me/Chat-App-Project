using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ChatServer.Logs
{
    public enum LogType
    {
        Untypped,
        Error,
        Comm_Input,
        Comm_Output,
        Connection_To_Client,
        Connection_To_Server,
        Application_Work
    }

    [Serializable]
    public class Log
    {
        public static void LogInfo(string logMessage)
        {
            WriteToFile(logMessage, LogType.Untypped);
        }

        public static void LogInfo(string logMessage, LogType logType)
        {
            WriteToFile(logMessage, logType);
        }

        private static void WriteToFile(string message, LogType logType)
        {
            //13\12\2020 x:x:x.txt
            DateTime dateTime = DateTime.Now;
            //cleaning date for writing file
            string datePath = dateTime.Year.ToString() + dateTime.Month.ToString() + dateTime.Day.ToString();
            string path = AppDomain.CurrentDomain.BaseDirectory + "\\logs\\" + datePath + ".txt";
            System.IO.FileInfo file = new System.IO.FileInfo(path);
            //create directory if it doesn't exist yet
            file.Directory.Create();
            //add the logs to the corresponding file
            using (StreamWriter sw = File.AppendText(file.FullName)) sw.WriteLine(dateTime.ToString() + "," + logType.ToString() + ":" + message + ";");
        }
    }
}