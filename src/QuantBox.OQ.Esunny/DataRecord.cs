using SmartQuant.Providers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace QuantBox.OQ.Esunny
{
    class DataRecord
    {
        public string key;
        public string market;
        public string symbol;
        public HistoricalDataRequest request;

        // 请求的当前时间
        public DateTime date;
    }
}
