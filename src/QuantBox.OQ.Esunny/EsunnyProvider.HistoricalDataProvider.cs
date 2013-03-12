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
            get { return -1; }
        }

        private Dictionary<string, HistoricalDataRequest> historicalDataIds = new Dictionary<string, HistoricalDataRequest>();
        private Dictionary<string, string> historicalDataRecords = new Dictionary<string, string>();

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
        public void CancelHistoricalDataRequest(string requestId)
        {
            if (historicalDataIds.ContainsKey(requestId))
            {
                HistoricalDataRequest request = historicalDataIds[requestId] as HistoricalDataRequest;
                historicalDataIds.Remove(requestId);
                EmitHistoricalDataCancelled(request);
            }
        }

        public void SendHistoricalDataRequest(HistoricalDataRequest request)
        {
            Instrument inst = request.Instrument as Instrument;
            string altSymbol = inst.GetSymbol(Name);
            string altExchange = inst.GetSecurityExchange(Name);

            string market;
            if (!_dictCode2Market.TryGetValue(altSymbol, out market))
            {
                EmitHistoricalDataError(request, "找不到此合约！");
                return;
            }

            switch (request.DataType)
            {
                case HistoricalDataType.Trade:
                case HistoricalDataType.Quote:
                    SendHistoricalDataRequestTick(request, market);
                    return;
                case HistoricalDataType.Bar:
                case HistoricalDataType.Daily:
                    SendHistoricalDataRequestBar(request, market);
                    return;
                case HistoricalDataType.MarketDepth:
                default:
                    break;
            }
            EmitHistoricalDataError(request, "不支持的请求类型！");
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

        public void SendHistoricalDataRequestTick(HistoricalDataRequest request, string market)
        {
            Instrument inst = request.Instrument as Instrument;
            string altSymbol = inst.GetSymbol(Name);

            string beginDate = request.BeginDate.ToString("yyyyMMdd");
            string endDate = request.EndDate.ToString("yyyyMMdd");
            string date = beginDate;

            string key = GetKeyFromSTKTRACEDATA(market, altSymbol);

            historicalDataIds[request.RequestId] = request;
            historicalDataRecords[key] = request.RequestId;

            int nRet = QuotApi.QT_RequestTrace(m_pQuotApi, market, altSymbol, date);
            if (0 == nRet)
            {
                historicalDataIds[request.RequestId] = request;
                historicalDataRecords[key] = request.RequestId;
            }
            else
            {
                EmitHistoricalDataError(request, "API返回错误:" + nRet);
            }
        }

        private void OnRspTraceData(IntPtr pQuotApi, IntPtr pBuffer, ref STKTRACEDATA pTraceData)
        {
            string key = GetKeyFromSTKTRACEDATA(ref pTraceData);
            string requestId;
            if (historicalDataRecords.TryGetValue(key, out requestId))
            {
                HistoricalDataRequest request;
                if (historicalDataIds.TryGetValue(requestId, out request))
                {
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
                            if (request.DataType == HistoricalDataType.Trade)
                            {
                                Trade trade = new Trade(updatetime, std.m_NewPrice, (int)volume);
                                NewHistoricalTrade(this,
                                    new HistoricalTradeEventArgs(trade, request.RequestId, request.Instrument, this, -1));
                            }
                            else
                            {
                                Quote quote = new Quote(updatetime, std.m_BuyPrice, (int)std.m_BuyVol, std.m_SellPrice, (int)std.m_SellVol);
                                NewHistoricalQuote(this,
                                    new HistoricalQuoteEventArgs(quote, request.RequestId, request.Instrument, this, -1));
                            }
                        }

                        datetime = dt;
                        volume = std.m_Volume;
                    }

                    historicalDataIds.Remove(request.RequestId);
                    EmitHistoricalDataCompleted(request);
                }
                historicalDataRecords.Remove(key);
            }
        }
        #endregion

        #region 历史Bar数据
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

        string GetKeyFromSTKHISDATA(ref STKHISDATA pHisData)
        {
            return string.Format("{0}:{1}:{2}", pHisData.Market, pHisData.Code, pHisData.nPeriod);
        }

        string GetKeyFromSTKHISDATA(string market, string altSymbol, PeriodType pt)
        {
            return string.Format("{0}:{1}:{2}", market, altSymbol, (int)pt);
        }

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

            int nRet = QuotApi.QT_RequestHistory(m_pQuotApi, market, altSymbol, pt);
            if (0 == nRet)
            {
                historicalDataIds[request.RequestId] = request;
                historicalDataRecords[key] = request.RequestId;
            }
            else
            {
                EmitHistoricalDataError(request, "API返回错误:" + nRet);
            }
        }

        private void OnRspHistoryQuot(IntPtr pQuotApi, IntPtr pBuffer, ref STKHISDATA pHisData)
        {
            string key = GetKeyFromSTKHISDATA(ref pHisData);
            string requestId;
            if (historicalDataRecords.TryGetValue(key, out requestId))
            {
                HistoricalDataRequest request;
                if (historicalDataIds.TryGetValue(requestId, out request))
                {
                    IntPtr ptrHead = (IntPtr)(pBuffer + Marshal.SizeOf(typeof(STKHISDATA)));
                    for (int i = 0; i < pHisData.nCount; ++i)
                    {
                        IntPtr ptr = (IntPtr)(ptrHead + Marshal.SizeOf(typeof(HISTORYDATA)) * i);
                        HISTORYDATA hd = (HISTORYDATA)Marshal.PtrToStructure(ptr, typeof(HISTORYDATA));

                        if (request.DataType == HistoricalDataType.Daily)
                        {
                            DateTime datetime = Convert.ToDateTime(hd.time.Substring(0, 10));
                            
                            if (datetime >= request.BeginDate && datetime < request.EndDate)
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
                            if (datetime >= request.BeginDate && datetime < request.EndDate)
                            {
                                Bar bar = new Bar(BarType.Time, request.BarSize, datetime.AddSeconds(-request.BarSize),
                                    datetime,
                                    hd.fOpen, hd.fHigh, hd.fLow, hd.fClose, (long)hd.fVolume, (long)hd.fAmount);
                                NewHistoricalBar(this,
                                    new HistoricalBarEventArgs(bar, request.RequestId, request.Instrument, this, -1));
                            }
                        }
                    }

                    historicalDataIds.Remove(request.RequestId);
                    EmitHistoricalDataCompleted(request);
                }
                historicalDataRecords.Remove(key);
            }
        }
        #endregion
    }
}
