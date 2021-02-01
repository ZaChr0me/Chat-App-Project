using System;
using System.Collections.Generic;
using System.Text;

namespace ChatServer
{
    public static class Globals
    {
        //plus tard peut etre y passer en parametres d'appli

        public static string BASE_DIRECTORY = AppDomain.CurrentDomain.BaseDirectory;
        public static string PARAMETERS_PATH = "parameters.json";

        public static string DB_PATH = BASE_DIRECTORY + "server.db";

        //database statements

        public static string DB_TABLE_PROFILES = "profiles";
        public static string DB_TABLE_TOPICS = "topics";

        public static string DB_LOGIN = "Login";
        public static string DB_PASSWORD = "Password";
        public static string DB_TOPICS_NAME = "Name";
        public static string DB_TOPICS_ID = "idTopic";
    }
}