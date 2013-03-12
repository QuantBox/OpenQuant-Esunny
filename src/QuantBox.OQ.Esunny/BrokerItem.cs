using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace QuantBox.OQ.Esunny
{
    public class BrokerItem
    {
        public string Label
        {
            get;
            set;
        }

        public BindingList<ServerItem> Server
        {
            get;
            set;
        }
    }
}
