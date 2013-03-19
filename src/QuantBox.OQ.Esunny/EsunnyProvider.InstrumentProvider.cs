using SmartQuant.FIX;
using SmartQuant.Providers;
using System;
using System.Collections.Generic;

namespace QuantBox.OQ.Esunny
{
    public partial class EsunnyProvider : IInstrumentProvider
    {

        public event SecurityDefinitionEventHandler SecurityDefinition;

        public void SendSecurityDefinitionRequest(FIXSecurityDefinitionRequest request)
        {
            lock (this)
            {
                string symbol = request.ContainsField(EFIXField.Symbol) ? request.Symbol : null;
                string securityType = request.ContainsField(EFIXField.SecurityType) ? request.SecurityType : null;
                string securityExchange = request.ContainsField(EFIXField.SecurityExchange) ? request.SecurityExchange : null;

                #region 过滤
                List<StockInfoEx> list = new List<StockInfoEx>();
                foreach (StockInfoEx inst in _dictInstruments.Values)
                {
                    int flag = 0;
                    if (null == symbol)
                    {
                        ++flag;
                    }
                    else if (inst.stockinfo.szCode.ToUpper().StartsWith(symbol.ToUpper()))
                    {
                        ++flag;
                    }

                    if (null == securityExchange)
                    {
                        ++flag;
                    }
                    else if (inst.market.ToUpper().StartsWith(securityExchange.ToUpper()))
                    {
                        ++flag;
                    }

                    if (null == securityType)
                    {
                        ++flag;
                    }
                    else
                    {
                        if (FIXSecurityType.Future == securityType)
                        {
                            ++flag;
                        }
                    }

                    if (3 == flag)
                    {
                        list.Add(inst);
                    }
                }
                #endregion

                list.Sort(SortStockInfoEx);

                //如果查出的数据为0，应当想法立即返回
                if (0 == list.Count)
                {
                    FIXSecurityDefinition definition = new FIXSecurityDefinition
                    {
                        SecurityReqID = request.SecurityReqID,
                        SecurityResponseID = request.SecurityReqID,
                        SecurityResponseType = request.SecurityRequestType,
                        TotNoRelatedSym = 1//有个除0错误的问题
                    };
                    if (SecurityDefinition != null)
                    {
                        SecurityDefinition(this, new SecurityDefinitionEventArgs(definition));
                    }
                }

                foreach (StockInfoEx inst in list)
                {
                    FIXSecurityDefinition definition = new FIXSecurityDefinition
                    {
                        SecurityReqID = request.SecurityReqID,
                        //SecurityResponseID = request.SecurityReqID,
                        SecurityResponseType = request.SecurityRequestType,
                        TotNoRelatedSym = list.Count
                    };

                    definition.AddField(EFIXField.SecurityType, FIXSecurityType.Future);
                    definition.AddField(EFIXField.Symbol, inst.stockinfo.szCode);
                    definition.AddField(EFIXField.SecurityExchange, inst.market);
                    definition.AddField(EFIXField.Currency, "CNY");//Currency.CNY
                    definition.AddField(EFIXField.SecurityDesc, inst.stockinfo.szName);

                    //还得补全内容

                    if (SecurityDefinition != null)
                    {
                        SecurityDefinition(this, new SecurityDefinitionEventArgs(definition));
                    }
                }
            }
        }

        private static int SortStockInfoEx(StockInfoEx a1, StockInfoEx a2)
        {
            return a1.stockinfo.szCode.CompareTo(a2.stockinfo.szCode);
        }
    }
}
