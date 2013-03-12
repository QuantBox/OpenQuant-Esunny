using System;
using System.ComponentModel;
using System.Linq;
using System.Xml.Linq;
using SmartQuant;
using System.Drawing.Design;
using System.Collections.Generic;
using System.Xml.Serialization;
using System.IO;

namespace QuantBox.OQ.Esunny
{
    partial class EsunnyProvider
    {
        private const string CATEGORY_ACCOUNT = "Account";
        private const string CATEGORY_BARFACTORY = "Bar Factory";
        private const string CATEGORY_DEBUG = "Debug";
        private const string CATEGORY_EXECUTION = "Settings - Execution";
        private const string CATEGORY_HISTORICAL = "Settings - Historical Data";
        private const string CATEGORY_INFO = "Information";
        private const string CATEGORY_NETWORK = "Settings - Network";
        private const string CATEGORY_STATUS = "Status";

        private BindingList<ServerItem> serversList = new BindingList<ServerItem>();
        [Category("Settings")]
        [Description("服务器信息，只选择第一条登录")]
        public BindingList<ServerItem> Server
        {
            get { return serversList; }
            set { serversList = value; }
        }

        private BindingList<AccountItem> accountsList = new BindingList<AccountItem>();
        [Category("Settings")]
        [Description("账号信息，只选择第一条登录")]
        public BindingList<AccountItem> Account
        {
            get { return accountsList; }
            set { accountsList = value; }
        }

        private BindingList<BrokerItem> brokersList = new BindingList<BrokerItem>();
        [Category("Settings"), Editor(typeof(ServersManagerTypeEditor), typeof(UITypeEditor)),
        Description("点击(...)查看经纪商列表")]
        public BindingList<BrokerItem> Brokers
        {
            get { return brokersList; }
            set { brokersList = value; }
        }

        private void InitSettings()
        {
            _bWantQuotConnect = true;

            LoadAccounts();
            LoadServers();

            serversList.ListChanged += ServersList_ListChanged;
            accountsList.ListChanged += AccountsList_ListChanged;
        }

        void ServersList_ListChanged(object sender, ListChangedEventArgs e)
        {
            SettingsChanged();
        }

        void AccountsList_ListChanged(object sender, EventArgs e)
        {
            SettingsChanged();
        }

        void ServerItem_ListChanged(object sender, EventArgs e)
        {
            SettingsChanged();
        }

        public void SettingsChanged()
        {
            SaveAccounts();
            SaveServers();
        }

        private readonly string accountsFile = string.Format(@"{0}\Esunny.Accounts.xml", Framework.Installation.IniDir);
        void LoadAccounts()
        {
            accountsList.Clear();

            try
            {
                XmlSerializer serializer = new XmlSerializer(accountsList.GetType());
                using (FileStream stream = new FileStream(accountsFile, FileMode.Open))
                {
                    accountsList = (BindingList<AccountItem>)serializer.Deserialize(stream);
                    stream.Close();
                }
            }
            catch (Exception ex)
            {
            }
        }

        void SaveAccounts()
        {
            XmlSerializer serializer = new XmlSerializer(accountsList.GetType());
            using (TextWriter writer = new StreamWriter(accountsFile))
            {
                serializer.Serialize(writer, accountsList);
                writer.Close();
            }
        }

        private readonly string serversFile = string.Format(@"{0}\Esunny.Servers.xml", Framework.Installation.IniDir);
        void LoadServers()
        {
            serversList.Clear();

            try
            {
                XmlSerializer serializer = new XmlSerializer(serversList.GetType());
                using (FileStream stream = new FileStream(serversFile, FileMode.Open))
                {
                    serversList = (BindingList<ServerItem>)serializer.Deserialize(stream);
                    stream.Close();
                }
            }
            catch(Exception ex)
            {
            }
        }

        void SaveServers()
        {
            XmlSerializer serializer = new XmlSerializer(serversList.GetType());
            using (TextWriter writer = new StreamWriter(serversFile))
            {
                serializer.Serialize(writer, serversList);
                writer.Close();
            }
        }

        private readonly string brokersFile = string.Format(@"{0}\Esunny.Brokers.xml", Framework.Installation.IniDir);
        public void LoadBrokers()
        {
            brokersList.Clear();

            try
            {
                XmlSerializer serializer = new XmlSerializer(brokersList.GetType());
                using (FileStream stream = new FileStream(brokersFile, FileMode.Open))
                {
                    brokersList = (BindingList<BrokerItem>)serializer.Deserialize(stream);
                    stream.Close();
                }
            }
            catch (Exception ex)
            {
            }
        }

        void SaveBrokers()
        {
            XmlSerializer serializer = new XmlSerializer(brokersList.GetType());
            using (TextWriter writer = new StreamWriter(brokersFile))
            {
                serializer.Serialize(writer, brokersList);
                writer.Close();
            }
        }
    }
}
