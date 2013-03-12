using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using QuantBox.CSharp2Esunny;
using SmartQuant;
using SmartQuant.Data;
using SmartQuant.Execution;
using SmartQuant.FIX;
using SmartQuant.Instruments;
using SmartQuant.Providers;
using System.Runtime.InteropServices;


namespace QuantBox.OQ.Esunny
{
    partial class EsunnyProvider
    {
        private fnOnConnectionStatus _fnOnConnect_Holder;
        private fnOnConnectionStatus _fnOnDisconnect_Holder;
        private fnOnRspHistoryQuot _fnOnRspHistoryQuot_Holder;
        private fnOnRspMarketInfo _fnOnRspMarketInfo_Holder;
        private fnOnRspTraceData _fnOnRspTraceData_Holder;

        private void InitCallbacks()
        {
            //由于回调函数可能被GC回收，所以用成员变量将回调函数保存下来
            _fnOnConnect_Holder = OnConnect;
            _fnOnDisconnect_Holder = OnDisconnect;
            _fnOnRspHistoryQuot_Holder = OnRspHistoryQuot;
            _fnOnRspMarketInfo_Holder = OnRspMarketInfo;
            _fnOnRspTraceData_Holder = OnRspTraceData;
        }

        private IntPtr m_pMsgQueue = IntPtr.Zero;   //消息队列指针
        private IntPtr m_pQuotApi = IntPtr.Zero;    //行情对象指针

        //行情有效状态，约定连接上并通过认证为有效
        private volatile bool _bQuotConnected;

        //表示用户操作，也许有需求是用户有多个行情，只连接第一个等
        private bool _bWantQuotConnect;

        private readonly object _lockQuot = new object();
        private readonly object _lockMsgQueue = new object();

        //记录
        private readonly Dictionary<string, string> _dictCode2Market = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _dictCode2Name = new Dictionary<string, string>();

        private ServerItem server;
        private AccountItem account;

        #region 清除数据
        private void Clear()
        {
            _dictCode2Market.Clear();
        }

        #endregion

        #region 定时器
        private readonly System.Timers.Timer timerDisconnect = new System.Timers.Timer(20 * 1000);

        void timerDisconnect_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            //如果从来没有连接上，在用户不知情的情况下又会自动连接上，所以要求定时断开连接
            if (!isConnected)
            {
                ehlog.Warn("从未连接成功，停止尝试！");
                _Disconnect();
            }
        }
        #endregion

        #region 连接
        private void _Connect()
        {
            server = null;
            account = null;

            bool bCheckOk = false;

            do
            {
                if (0 == serversList.Count)
                {
                    MessageBox.Show("您还没有设置 服务器 信息，目前只选择第一条进行连接");
                    break;
                }
                if (0 == accountsList.Count)
                {
                    MessageBox.Show("您还没有设置 账号 信息，目前只选择第一条进行连接");
                    break;
                }

                server = serversList[0];
                account = accountsList[0];

                if (string.IsNullOrEmpty(server.Address))
                {
                    MessageBox.Show("Address不能为空");
                    break;
                }

                if (0 == server.Port)
                {
                    MessageBox.Show("Port不能为空");
                    break;
                }

                if (string.IsNullOrEmpty(account.InvestorId)
                || string.IsNullOrEmpty(account.Password))
                {
                    MessageBox.Show("账号信息不全");
                    break;
                }

                bCheckOk = true;

            } while (false);

            if (false == bCheckOk)
            {
                ChangeStatus(ProviderStatus.Disconnected);
                isConnected = false;
                return;
            }

            ChangeStatus(ProviderStatus.Connecting);
            //如果前面一次连接一直连不上，新改地址后也会没响应，所以先删除
            Disconnect_Quot();

            if (_bWantQuotConnect)
            {
                timerDisconnect.Enabled = true;
                Connect_MsgQueue();
            }
            if (_bWantQuotConnect)
            {
                Connect_Quot();
            }
        }


        private void Connect_MsgQueue()
        {
            //建立消息队列，只建一个，行情和交易复用一个
            lock (_lockMsgQueue)
            {
                if (null == m_pMsgQueue || IntPtr.Zero == m_pMsgQueue)
                {
                    m_pMsgQueue = CommApi.ES_CreateMsgQueue();

                    CommApi.ES_RegOnConnect(m_pMsgQueue, _fnOnConnect_Holder);
                    CommApi.ES_RegOnDisconnect(m_pMsgQueue, _fnOnDisconnect_Holder);

                    CommApi.ES_StartMsgQueue(m_pMsgQueue);
                }
            }
        }

        //建立行情
        private void Connect_Quot()
        {
            lock (_lockQuot)
            {
                if (_bWantQuotConnect
                   && (null == m_pQuotApi || IntPtr.Zero == m_pQuotApi))
                {
                    m_pQuotApi = QuotApi.QT_CreateQuotApi();
                    QuotApi.ES_RegOnRspHistoryQuot(m_pMsgQueue, _fnOnRspHistoryQuot_Holder);
                    QuotApi.ES_RegOnRspMarketInfo(m_pMsgQueue, _fnOnRspMarketInfo_Holder);
                    QuotApi.ES_RegOnRspTraceData(m_pMsgQueue, _fnOnRspTraceData_Holder);
                    QuotApi.QT_RegMsgQueue2QuotApi(m_pQuotApi, m_pMsgQueue);
                    QuotApi.QT_Connect(m_pQuotApi,server.Address,server.Port,account.InvestorId, account.Password);
                }
            }
        }
        #endregion

        #region 断开连接
        private void _Disconnect()
        {
            timerDisconnect.Enabled = false;

            Disconnect_Quot();
            Disconnect_MsgQueue();

            Clear();
            ChangeStatus(ProviderStatus.Disconnected);
            isConnected = false;
            EmitDisconnectedEvent();
        }

        private void Disconnect_MsgQueue()
        {
            lock (_lockMsgQueue)
            {
                if (null != m_pMsgQueue && IntPtr.Zero != m_pMsgQueue)
                {
                    CommApi.ES_StopMsgQueue(m_pMsgQueue);

                    CommApi.ES_ReleaseMsgQueue(m_pMsgQueue);
                    m_pMsgQueue = IntPtr.Zero;
                }
            }
        }

        private void Disconnect_Quot()
        {
            lock (_lockQuot)
            {
                if (null != m_pQuotApi && IntPtr.Zero != m_pQuotApi)
                {
                    QuotApi.QT_RegMsgQueue2QuotApi(m_pQuotApi, IntPtr.Zero);
                    QuotApi.QT_ReleaseQuotApi(m_pQuotApi);
                    m_pQuotApi = IntPtr.Zero;
                }
                _bQuotConnected = false;
            }
        }
        #endregion


        #region 连接状态回调
        private void OnConnect(IntPtr pQuotApi, int err, string errtext, ConnectionStatus result)
        {
            if (ConnectionStatus.E_logined == result)
            {
                _bQuotConnected = true;
            }

            ehlog.Info("{0}, err:{1}, errtext:{2}", result, err, errtext);

            if (_bQuotConnected)
            {
                timerDisconnect.Enabled = false;//都连接上了，用不着定时断开了
                ChangeStatus(ProviderStatus.LoggedIn);
                isConnected = true;
                EmitConnectedEvent();
            }
        }

        private void OnDisconnect(IntPtr pQuotApi, int err, string errtext, ConnectionStatus result)
        {
            if (isConnected)
            {
                _Connect();
            }

            ehlog.Info("{0}, err:{1}, errtext:{2}", result, err, errtext);

            if (!isConnected)//从来没有连接成功过，可能是密码错误，直接退出
            {
                //不能在线程中停止线程，这样会导致软件关闭进程不退出
                _Disconnect();
            }
            else
            {
                //以前连接过，现在断了次线，要等重连
                ChangeStatus(ProviderStatus.Connecting);
                EmitDisconnectedEvent();
            }
        }
        #endregion

        #region 市场信息包
        private void OnRspMarketInfo(IntPtr pQuotApi, IntPtr pBuffer, ref MarketInfo pMarketInfo, int bLast)
        {
            IntPtr ptrHead = (IntPtr)(pBuffer + Marshal.SizeOf(typeof(MarketInfo)));
            for (int i = 0; i < pMarketInfo.stocknum; ++i)
            {
                IntPtr ptr = (IntPtr)(ptrHead + Marshal.SizeOf(typeof(StockInfo)) * i);
                StockInfo si = (StockInfo)Marshal.PtrToStructure(ptr, typeof(StockInfo));
                _dictCode2Market[si.szCode] = pMarketInfo.Market;
            }
        }
        #endregion


    }
}