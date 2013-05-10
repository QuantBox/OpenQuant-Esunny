using QuantBox.CSharp2Esunny;
using SmartQuant.Data;
using SmartQuant.Instruments;
using SmartQuant.Providers;
using SmartQuant.Providers.Design;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace QuantBox.OQ.Esunny
{
    public partial class EsunnyProvider : IHistoricalDataProvider
    {
        [TypeConverter(typeof(BarSizesTypeConverter))]
        [Category(CATEGORY_HISTORICAL)]
        public int[] BarSizes
        {
            get { return new int[] { 60, 300, 3600 }; }
        }

        [Category(CATEGORY_HISTORICAL)]
        public HistoricalDataRange DataRange
        {
            get { return HistoricalDataRange.DateTimeInterval; }
        }

        [Category(CATEGORY_HISTORICAL)]
        public HistoricalDataType DataType
        {
            get { return (HistoricalDataType.Daily | HistoricalDataType.Bar | HistoricalDataType.Trade | HistoricalDataType.Quote); }
        }

        [Category(CATEGORY_HISTORICAL)]
        public int MaxConcurrentRequests
        {
            get { return 3; }
        }

        [Category(CATEGORY_HISTORICAL_SPECIAL)]
        [Description("将下载的Bar全部生效，或只生效指定日期内Bar")]
        [DefaultValue(true)]
        public bool EnableDateFilter
        {
            get;
            set;
        }

        [Category(CATEGORY_HISTORICAL_SPECIAL)]
        [Description("下载Tick数据时，同时生效Trade和Quote")]
        [DefaultValue(false)]
        public bool BothTradeAndQuote
        {
            get;
            set;
        }

        public event HistoricalDataEventHandler HistoricalDataRequestCancelled;

        public event HistoricalDataEventHandler HistoricalDataRequestCompleted;

        public event HistoricalDataErrorEventHandler HistoricalDataRequestError;

        public event HistoricalBarEventHandler NewHistoricalBar;

        public event HistoricalMarketDepthEventHandler NewHistoricalMarketDepth;

        public event HistoricalQuoteEventHandler NewHistoricalQuote;

        public event HistoricalTradeEventHandler NewHistoricalTrade;

        private void EmitHistoricalDataError(HistoricalDataRequest request, string message)
        {
            if (HistoricalDataRequestError != null)
                HistoricalDataRequestError(this,
                    new HistoricalDataErrorEventArgs(request.RequestId, request.Instrument, this, -1, message));
        }

        private void EmitHistoricalDataCompleted(HistoricalDataRequest request)
        {
            if (HistoricalDataRequestCompleted != null)
                HistoricalDataRequestCompleted(this,
                    new HistoricalDataEventArgs(request.RequestId, request.Instrument, this, -1));
        }

        private void EmitHistoricalDataCancelled(HistoricalDataRequest request)
        {
            if (HistoricalDataRequestCancelled != null)
                HistoricalDataRequestCancelled(this,
                    new HistoricalDataEventArgs(request.RequestId, request.Instrument, this, -1));
        }

        private void EmitNewHistoricalBar(HistoricalDataRequest request, DateTime datetime, double open, double high, double low, double close, long volume, long openInt)
        {
            if (NewHistoricalBar != null)
            {
                Bar bar = new Bar(BarType.Time, request.BarSize, datetime, datetime.AddSeconds(request.BarSize), open, high, low, close, volume, openInt);
                NewHistoricalBar(this,
                    new HistoricalBarEventArgs(bar, request.RequestId, request.Instrument, this, -1));
            }
        }

        private void EmitNewHistoricalTrade(HistoricalDataRequest request, DateTime datetime, double price, int size)
        {
            if (NewHistoricalTrade != null)
            {
                Trade trade = new Trade(datetime, price, size);
                NewHistoricalTrade(this,
                    new HistoricalTradeEventArgs(trade, request.RequestId, request.Instrument, this, -1));
            }
        }

        private void EmitNewHistoricalQuote(HistoricalDataRequest request, DateTime datetime, double bid, int bidSize, double ask, int askSize)
        {
            if (NewHistoricalQuote != null)
            {
                Quote quote = new Quote(datetime, bid, bidSize, ask, askSize);
                NewHistoricalQuote(this,
                    new HistoricalQuoteEventArgs(quote, request.RequestId, request.Instrument, this, -1));
            }
        }

        #region IHistoricalDataProvider
        // 通过key能找到
        private Dictionary<string, DataRecord> historicalDataRecords_key = new Dictionary<string, DataRecord>();
        // 通过请求ID也能找到
        private Dictionary<string, DataRecord> historicalDataRecords_requestId = new Dictionary<string, DataRecord>();

        public void CancelHistoricalDataRequest(string requestId)
        {
            if (historicalDataRecords_requestId.ContainsKey(requestId))
            {
                DataRecord dr = historicalDataRecords_requestId[requestId] as DataRecord;
                EmitHistoricalDataCancelled(dr.request);

                historicalDataRecords_requestId.Remove(dr.request.RequestId);
                historicalDataRecords_key.Remove(dr.key);
            }
        }

        public void SendHistoricalDataRequest(HistoricalDataRequest request)
        {
            if (!_bQuotConnected)
            {
                EmitHistoricalDataError(request, "未连接到行情服务器，无法获取数据");
                return;
            }

            Instrument inst = request.Instrument as Instrument;
            string altSymbol = inst.GetSymbol(Name);
            string altExchange = inst.GetSecurityExchange(Name);

            StockInfoEx stockInfo;
            if (!_dictInstruments.TryGetValue(altSymbol, out stockInfo))
            {
                EmitHistoricalDataError(request, "找不到此合约！");
                return;
            }

            switch (request.DataType)
            {
                case HistoricalDataType.Trade:
                case HistoricalDataType.Quote:
                    SendHistoricalDataRequestTick(request, stockInfo.market);
                    return;
                case HistoricalDataType.Bar:
                case HistoricalDataType.Daily:
                    SendHistoricalDataRequestBar(request, stockInfo.market);
                    return;
                case HistoricalDataType.MarketDepth:
                default:
                    break;
            }
            EmitHistoricalDataError(request, "不支持的请求类型！");
        }
        #endregion

        #region 数据结构维护
        private void SaveRequest(HistoricalDataRequest request, string key, string market, string symbol, DateTime date)
        {
            DataRecord dr;
            if (!historicalDataRecords_key.TryGetValue(key, out dr))
            {
                dr = new DataRecord();
                dr.key = key;
                dr.market = market;
                dr.symbol = symbol;
                dr.request = request;
            }
            historicalDataRecords_key[key] = dr;
            historicalDataRecords_requestId[request.RequestId] = dr;

            dr.date = date;
        }

        private void RemoveRequest(string key)
        {
            DataRecord dr;
            if (!historicalDataRecords_key.TryGetValue(key, out dr))
            {
                return;
            }
            if (dr.date >= dr.request.EndDate)
            {
                EmitHistoricalDataCompleted(dr.request);

                historicalDataRecords_requestId.Remove(dr.request.RequestId);
                historicalDataRecords_key.Remove(dr.key);
                return;
            }
        }

        private void SendRequest_Tick(string key)
        {
            DataRecord dr;
            if (!historicalDataRecords_key.TryGetValue(key, out dr))
            {
                return;
            }
            
            // 移动到下一交易日
            do
            {
                dr.date = dr.date.AddDays(1);
                if(dr.date.DayOfWeek == DayOfWeek.Saturday
                    ||dr.date.DayOfWeek == DayOfWeek.Sunday)
                {
                    continue;
                }
                else
                {
                    break;
                }
            }while(true);
            
            // 超出日期了,可以中止了
            if (dr.date >= dr.request.EndDate)
            {
                EmitHistoricalDataCompleted(dr.request);

                historicalDataRecords_requestId.Remove(dr.request.RequestId);
                historicalDataRecords_key.Remove(dr.key);
                return;
            }

            // 发送请求
            int nRet = QuotApi.QT_RequestTrace(m_pQuotApi, dr.market, dr.symbol, dr.date.ToString("yyyyMMdd"));
            ehlog.Info("-->RequestTrace:{0},{1},{2} Return:{3}", dr.market, dr.symbol,dr.date.ToString("yyyyMMdd"), nRet);
            if (0 == nRet)
            {
            }
            else
            {
                EmitHistoricalDataError(dr.request, "API返回错误:" + nRet);
            }
        }
        #endregion

        #region Tick数据
        string GetKeyFromSTKTRACEDATA(ref STKTRACEDATA pTraceData)
        {
            return string.Format("{0}:{1}", pTraceData.Market, pTraceData.Code);
        }

        string GetKeyFromSTKTRACEDATA(string market, string altSymbol)
        {
            return string.Format("{0}:{1}", market, altSymbol);
        }

        // 同一合约的一次只能查一天，如果同时查多天就没法区分哪天返回的为0
        public void SendHistoricalDataRequestTick(HistoricalDataRequest request, string market)
        {
            TimeSpan ts = request.EndDate - request.BeginDate;
            if (ts.Days <= 0)
            {
                EmitHistoricalDataError(request, "开始与结束为同一天");
                return;
            }
            else if(ts.Days>7)
            {
                EmitHistoricalDataError(request, "为减少服务器负担，Tick数据一次只能取一周");
                return;
            }
            
            Instrument inst = request.Instrument as Instrument;
            string altSymbol = inst.GetSymbol(Name);

            string key = GetKeyFromSTKTRACEDATA(market, altSymbol);

            // 先存下来再发请求
            SaveRequest(request, key, market, altSymbol, request.BeginDate.AddDays(-1));
            SendRequest_Tick(key);
        }

        private void OnRspTraceData(IntPtr pQuotApi, IntPtr pBuffer, ref STKTRACEDATA pTraceData)
        {
            ehlog.Info("<--OnRspTraceData:{0},{1},{2}条", pTraceData.Market, pTraceData.Code, pTraceData.nCount);
            string key = GetKeyFromSTKTRACEDATA(ref pTraceData);
            DataRecord dr;
            if (historicalDataRecords_key.TryGetValue(key, out dr))
            {
                HistoricalDataRequest request = dr.request;

                int day = -1;
                float volume = 0;
                DateTime datetime = DateTime.Now;
                DateTime updatetime = DateTime.Now;

                IntPtr ptrHead = (IntPtr)(pBuffer + Marshal.SizeOf(typeof(STKTRACEDATA)));
                for (int i = 0; i < pTraceData.nCount; ++i)
                {
                    IntPtr ptr = (IntPtr)(ptrHead + Marshal.SizeOf(typeof(STOCKTRACEDATA)) * i);
                    STOCKTRACEDATA std = (STOCKTRACEDATA)Marshal.PtrToStructure(ptr, typeof(STOCKTRACEDATA));

                    DateTime dt = Convert.ToDateTime(std.time);
                    if (datetime == dt)
                    {
                        updatetime = updatetime.AddMilliseconds(250);
                    }
                    else
                    {
                        updatetime = dt;
                    }
                    if (day != updatetime.Day)
                    {
                        volume = 0;
                    }
                    day = updatetime.Day;
                    volume = std.m_Volume - volume;

                    if (updatetime >= request.BeginDate && updatetime < request.EndDate)
                    {
                        if (BothTradeAndQuote)
                        {
                            Trade trade = new Trade(updatetime, std.m_NewPrice, (int)volume);
                            NewHistoricalTrade(this,
                                new HistoricalTradeEventArgs(trade, request.RequestId, request.Instrument, this, -1));

                            if (std.m_BuyPrice == 0 && std.m_BuyVol == 0 
                                &&std.m_SellPrice == 0 && std.m_SellVol == 0)
                            {
                            }
                            else
                            {
                                Quote quote = new Quote(updatetime, std.m_BuyPrice, (int)std.m_BuyVol, std.m_SellPrice, (int)std.m_SellVol);
                                NewHistoricalQuote(this,
                                    new HistoricalQuoteEventArgs(quote, request.RequestId, request.Instrument, this, -1));
                            }
                        }
                        else
                        {
                            if (request.DataType == HistoricalDataType.Trade)
                            {
                                Trade trade = new Trade(updatetime, std.m_NewPrice, (int)volume);
                                NewHistoricalTrade(this,
                                    new HistoricalTradeEventArgs(trade, request.RequestId, request.Instrument, this, -1));
                            }
                            else
                            {
                                if (std.m_BuyPrice == 0 && std.m_BuyVol == 0
                                && std.m_SellPrice == 0 && std.m_SellVol == 0)
                                {
                                }
                                else
                                {
                                    Quote quote = new Quote(updatetime, std.m_BuyPrice, (int)std.m_BuyVol, std.m_SellPrice, (int)std.m_SellVol);
                                    NewHistoricalQuote(this,
                                        new HistoricalQuoteEventArgs(quote, request.RequestId, request.Instrument, this, -1));
                                }
                            }
                        }
                    }

                    datetime = dt;
                    volume = std.m_Volume;
                }

                RemoveRequest(key);
                SendRequest_Tick(key);
            }
        }
        #endregion

        #region 历史Bar数据
        // 将请求的周期传成易盛的周期类型
        PeriodType GetPeriodTypeFromDataType(HistoricalDataRequest request)
        {
            if (request.DataType == HistoricalDataType.Daily)
            {
                return PeriodType.Daily;
            }
            else if (request.DataType == HistoricalDataType.Bar)
            {
                switch (request.BarSize)
                {
                    case 60:
                        return PeriodType.Min1;
                    case 300:
                        return PeriodType.Min5;
                    case 3600:
                        return PeriodType.Min60;
                    default:
                        break;
                }
            }
            return PeriodType.MAX_PERIOD_TYPE;
        }

        // 生成历史数据的请求Key
        string GetKeyFromSTKHISDATA(ref STKHISDATA pHisData)
        {
            return string.Format("{0}:{1}:{2}", pHisData.Market, pHisData.Code, pHisData.nPeriod);
        }

        // 生成历史数据的请求Key
        string GetKeyFromSTKHISDATA(string market, string altSymbol, PeriodType pt)
        {
            return string.Format("{0}:{1}:{2}", market, altSymbol, (int)pt);
        }

        // 请求历史数据
        public void SendHistoricalDataRequestBar(HistoricalDataRequest request, string market)
        {
            Instrument inst = request.Instrument as Instrument;
            string altSymbol = inst.GetSymbol(Name);

            PeriodType pt = GetPeriodTypeFromDataType(request);
            if (pt == PeriodType.MAX_PERIOD_TYPE)
            {
                EmitHistoricalDataError(request, "不支持的时间周期！");
                return;
            }

            string key = GetKeyFromSTKHISDATA(market, altSymbol, pt);

            // 先发请求再存下来
            int nRet = QuotApi.QT_RequestHistory(m_pQuotApi, market, altSymbol, pt);
            ehlog.Info("-->RequestHistory:{0},{1} Return:{2}", market, altSymbol, nRet);
            if (0 == nRet)
            {
                SaveRequest(request, key, market, altSymbol, DateTime.MaxValue);
            }
            else
            {
                EmitHistoricalDataError(request, "API返回错误:" + nRet);
            }
        }

        private void OnRspHistoryQuot(IntPtr pQuotApi, IntPtr pBuffer, ref STKHISDATA pHisData)
        {
            ehlog.Info("<--OnRspHistoryQuot:{0},{1},{2},{3}条", pHisData.Market, pHisData.Code, (PeriodType)pHisData.nPeriod, pHisData.nCount);
            string key = GetKeyFromSTKHISDATA(ref pHisData);
            DataRecord dr;
            if (historicalDataRecords_key.TryGetValue(key, out dr))
            {
                HistoricalDataRequest request = dr.request;

                IntPtr ptrHead = (IntPtr)(pBuffer + Marshal.SizeOf(typeof(STKHISDATA)));
                for (int i = 0; i < pHisData.nCount; ++i)
                {
                    IntPtr ptr = (IntPtr)(ptrHead + Marshal.SizeOf(typeof(HISTORYDATA)) * i);
                    HISTORYDATA hd = (HISTORYDATA)Marshal.PtrToStructure(ptr, typeof(HISTORYDATA));

                    if (request.DataType == HistoricalDataType.Daily)
                    {
                        DateTime datetime = Convert.ToDateTime(hd.time.Substring(0, 10));

                        if (!EnableDateFilter||(datetime >= request.BeginDate && datetime < request.EndDate))
                        {
                            Daily daily = new Daily(datetime,
                                                            hd.fOpen, hd.fHigh, hd.fLow, hd.fClose, (long)hd.fVolume, (long)hd.fAmount);
                            NewHistoricalBar(this,
                                new HistoricalBarEventArgs(daily, request.RequestId, request.Instrument, this, -1));
                        }
                    }
                    else
                    {
                        DateTime datetime = Convert.ToDateTime(hd.time);
                        if (!EnableDateFilter || (datetime >= request.BeginDate && datetime < request.EndDate))
                        {
                            Bar bar = new Bar(BarType.Time, request.BarSize, datetime.AddSeconds(-request.BarSize), datetime,
                                hd.fOpen, hd.fHigh, hd.fLow, hd.fClose, (long)hd.fVolume, (long)hd.fAmount);
                            NewHistoricalBar(this,
                                new HistoricalBarEventArgs(bar, request.RequestId, request.Instrument, this, -1));
                        }
                    }
                }

                RemoveRequest(key);
            }
        }
        #endregion
    }
}
