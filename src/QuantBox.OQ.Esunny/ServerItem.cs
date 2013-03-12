using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;

namespace QuantBox.OQ.Esunny
{
    [DefaultPropertyAttribute("Label")]
    public class ServerItem
    {
        [CategoryAttribute("服务端信息"),
        DescriptionAttribute("行情服务器IP")]
        public string Address { get; set; }

        [CategoryAttribute("服务端信息"),
        DescriptionAttribute("行情服务器端口")]
        public int Port { get; set; }

        [CategoryAttribute("标签"),
        DescriptionAttribute("标签不能重复")]
        public string Label
        {
            get;
            set;
        }

        public override string ToString()
        {
            return "标签不能重复";
        }

        [BrowsableAttribute(false)]
        public string Name
        {
            get { return Label; }
        }
    }
}
