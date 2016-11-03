using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace SingleResponsibilityPrinciple
{
    public class TradeProcessor
    {
        private IEnumerable<string> ReadTradeData(Stream stream)
        {
            var tradeData = new List<string>();
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    tradeData.Add(line);
                }
            }
            return tradeData;
        }
        private IEnumerable<string> ReadURLTradeData(string url)
        {
            var tradeData = new List<string>();
            var client = new WebClient();
            using (var stream = client.OpenRead(url))
            using (var reader = new StreamReader(stream)) {
                string line;
                while ((line = reader.ReadLine()) != null) {
                    tradeData.Add(line);
                }
            }
            return tradeData;
        }
        private IEnumerable<TradeRecord> ParseTrades(IEnumerable<string> tradeData)
        {
            var trades = new List<TradeRecord>();
            var lineCount = 1;
            foreach (var line in tradeData)
            {
                var fields = line.Split(new char[] { ',' });

                if (!ValidateTradeData(fields, lineCount))
                {
                    continue;
                }

                var trade = MapTradeDataToTradeRecord(fields);

                trades.Add(trade);

                lineCount++;
            }

            return trades;
        }

        private bool ValidateTradeData(string[] fields, int currentLine)
        {
            string type = "WARN2";
            if (fields.Length != 3)
            {
                LogMessage(type,"Line {0} malformed. Only {1} field(s) found.", currentLine, fields.Length);

                return false;
            }

            if (fields[0].Length != 6)
            {
                LogMessage(type,"Trade currencies on line {0} malformed: '{1}'", currentLine, fields[0]);
                return false;
            }

            int tradeAmount;
            if (!int.TryParse(fields[1], out tradeAmount))
            {
                LogMessage(type,"Trade amount on line {0} not a valid integer: '{1}'", currentLine, fields[1]);
                return false;
            }

            if (tradeAmount > 100000 || tradeAmount < 1000)
            {
                LogMessage(type, "Trade amount on line {0} outside trade amount bounds (Trade amounts must be between 1,000 and 100,000 units.): '{1}'", currentLine, fields[1]);
                return false;
            }
            decimal tradePrice;
            if (!decimal.TryParse(fields[2], out tradePrice))
            {
                LogMessage(type, "Trade price on line {0} not a valid decimal: '{1}'", currentLine, fields[2]);
                return false;
            }

            return true;
        }
        //<log><type>INFO</type><message>4 trades processed</message></log> 
        private void LogMessage(string type, string message, params object[] args)
        {
            //string type = message.Substring(0, 4);
            Console.WriteLine(message, args);
            using (StreamWriter logfile = File.AppendText("log.xml"))
            {
                logfile.WriteLine("<log><type>"+type+"</type><message>" + message + "</message></log>", args);
            }

        }

        private TradeRecord MapTradeDataToTradeRecord(string[] fields)
        {
            var sourceCurrencyCode = fields[0].Substring(0, 3);
            var destinationCurrencyCode = fields[0].Substring(3, 3);
            var tradeAmount = int.Parse(fields[1]);
            var tradePrice = decimal.Parse(fields[2]);
            const float LotSize = 100000f;
            var trade = new TradeRecord
            {
                SourceCurrency = sourceCurrencyCode,
                DestinationCurrency = destinationCurrencyCode,
                Lots = tradeAmount / LotSize,
                Price = tradePrice
            };

            return trade;
        }

        private void StoreTrades(IEnumerable<TradeRecord> trades)
        {
            string type = "INFO";
            LogMessage(type, "Connecting to Database");
            using (var connection = new System.Data.SqlClient.SqlConnection(@"Data Source=(LocalDB)\MSSQLLocalDB;AttachDbFilename=|DataDirectory|\tradedatabase.mdf;Integrated Security=True;Connect Timeout=30;"))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    foreach (var trade in trades)
                    {
                        var command = connection.CreateCommand();
                        command.Transaction = transaction;
                        command.CommandType = System.Data.CommandType.StoredProcedure;
                        command.CommandText = "dbo.insert_trade";
                        command.Parameters.AddWithValue("@sourceCurrency", trade.SourceCurrency);
                        command.Parameters.AddWithValue("@destinationCurrency", trade.DestinationCurrency);
                        command.Parameters.AddWithValue("@lots", trade.Lots);
                        command.Parameters.AddWithValue("@price", trade.Price);

                        command.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }
                connection.Close();
            }

            LogMessage(type, "{0} trades processed", trades.Count());
        }
  

        public void ProcessTrades(String url)
        {
            var lines = ReadURLTradeData(url);
            var trades = ParseTrades(lines);
            StoreTrades(trades);
        }


    }
}
