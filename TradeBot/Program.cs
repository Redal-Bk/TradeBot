using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Serilog;
using Telegram.Bot.Types;
using Serilog.Core;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Polling;

public class Program
{
    //private readonly static string _token = "7312818953:AAG4KMe_ULKvxKZM9een1WF9_uawg4VVEtI";   Telegram Token
    private readonly static string _token = "7312818953:AAG4KMe_ULKvxKZM9een1WF9_uawg4VVEtI";
    static async Task Main(string[] args)
    {
         Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .MinimumLevel.Information()
            .CreateLogger();





        var symbols = new List<string>
    {
        "BTC-USDT", "ETH-USDT", "BNB-USDT", "XRP-USDT", "ADA-USDT",
        "SOL-USDT", "DOGE-USDT", "DOT-USDT", "TRX-USDT","TON-USDT","NOT-USDT"
    };

        var analyzer = new MarketAnalyzer();
        var signalGenerator = new SignalGenerator();

        var telegram = new TelegramSender(
            _token,
            new List<long> { 5200052562 , 7532880262 } // Access Ids
        );
        await telegram.StartListening();

        Log.Information("🚀 ربات RedalSignalBot فعال شد...");
        while (true)
        {
            var strongSignals = new List<TradeSignal>();

            foreach (var symbol in symbols)
            {
                try
                {
                    var candles = await analyzer.GetCandlesAsync(symbol);
                    var signal = signalGenerator.GenerateSignal(symbol, candles);

                    
                    if (signal.Action != "Hold")
                    {
                        strongSignals.Add(signal);
                        Log.Information($"[{signal.Time}] {signal.Symbol} => {signal.Action} | دلیل: {signal.Reason}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("❌ خطا در پردازش {Symbol}: {Message}", symbol, ex.Message);
                }
            }

            
            foreach (var signal in strongSignals.Take(4))
            {
                string msg = $"\uD83D\uDCCA سیگنال جدید:\n" +
                             $"رمز ارز: {signal.Symbol}\n" +
                             $"⏱ زمان: {signal.Time:HH:mm yyyy/MM/dd}\n" +
                             $"💵 قیمت: {signal.Price}\n" +
                             $"\uD83D\uDCC8 وضعیت: {signal.Action}\n" +
                             $"\uD83D\uDCDD دلیل: {signal.Reason}";
                await Console.Out.WriteLineAsync("the signal is : => " + msg);
                await telegram.SendSignalMessage(msg);
            }

            Log.Information("⌛ Next Analys 15 minute later...");
            await Task.Delay(TimeSpan.FromMinutes(15)); 

        }

    }

    


}

public class TelegramSender
{
    private readonly TelegramBotClient _bot;
    private readonly List<long> _authorizedChatIds;

    public TelegramSender(string token, List<long> chatIds)
    {
        _bot = new TelegramBotClient(token);
        _authorizedChatIds = chatIds;
    }
    

    public async Task SendSignalMessage(string message)
    {
        foreach (var chatId in _authorizedChatIds)
        {
            await _bot.SendTextMessageAsync(chatId, message);
        }
    }
    public async Task ShowMainMenu(long chatId)
    {
        var keyboard = new Telegram.Bot.Types.ReplyMarkups.ReplyKeyboardMarkup(
            new[]
            {
            new[]
            {
                new Telegram.Bot.Types.ReplyMarkups.KeyboardButton("📊 لیست قیمت‌ها"),
                new Telegram.Bot.Types.ReplyMarkups.KeyboardButton("📱 ارسال شماره من") { RequestContact = true }
            }
            })
        {
            ResizeKeyboard = true
        };

        await _bot.SendTextMessageAsync(chatId, "یکی از گزینه‌ها رو انتخاب کن:", replyMarkup: keyboard);
    }

    public async Task StartListening()
    {
        var cancellationToken = new CancellationTokenSource().Token;

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = Array.Empty<UpdateType>() // همه نوع آپدیت رو می‌گیره
        };

        _bot.StartReceiving(
            updateHandler: HandleUpdateAsync,
            pollingErrorHandler: HandlePollingErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: cancellationToken
        );
    }
    async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken cancellationToken)
    {
        if (update.Message is not { } message)
            return;

        var chatId = message.Chat.Id;
        Log.Information("دریافت پیام از {ChatId}: {Text}", chatId, message.Text);

        if (message.Type == MessageType.Text)
        {
            if (message.Text == "/start")
            {
                var keyboard = new ReplyKeyboardMarkup(new[]
                {
                new KeyboardButton[] { "📊 لیست قیمت‌ها" },
                new KeyboardButton[] {
                    new KeyboardButton("📱 ارسال شماره تلفن") { RequestContact = true }
                }
            })
                {
                    ResizeKeyboard = true
                };

                await bot.SendTextMessageAsync(chatId, "یکی از گزینه‌ها رو انتخاب کن:", replyMarkup: keyboard, cancellationToken: cancellationToken);
            }
            else if (message.Text == "📊 لیست قیمت‌ها")
            {
                var analyzer = new MarketAnalyzer();
                var symbols = new List<string>
                {
                    "BTC-USDT", "ETH-USDT", "BNB-USDT", "XRP-USDT", "ADA-USDT",
                    "SOL-USDT", "DOGE-USDT", "DOT-USDT", "TRX-USDT", "TON-USDT", "NOT-USDT"
                };

                var priceList = await analyzer.GetPriceListAsync(symbols);

                // ارسال به همان کاربری که دکمه را زده
                await bot.SendTextMessageAsync(chatId, priceList, cancellationToken: cancellationToken);
            }

        }
        else if (message.Type == MessageType.Contact && message.Contact != null)
        {
            string phoneNumber = message.Contact.PhoneNumber;
            await bot.SendTextMessageAsync(chatId, $"شماره تماس شما: {phoneNumber} دریافت شد ✅", cancellationToken: cancellationToken);
        }
    }

    // متد هندل خطا
    Task HandlePollingErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken cancellationToken)
    {
        Log.Error(exception, "خطایی در هنگام دریافت اطلاعات رخ داد");
        return Task.CompletedTask;
    }


}

public class MarketAnalyzer
{
    private readonly HttpClient _client = new HttpClient();

    public async Task<List<Candle>> GetCandlesAsync(string symbol)
    {
        var url = $"https://api.kucoin.com/api/v1/market/candles?symbol={symbol}&type=15min&limit=100";
        var response = await _client.GetStringAsync(url);
        var json = JsonSerializer.Deserialize<JsonElement>(response);

        if (!json.TryGetProperty("data", out var dataElement) || dataElement.ValueKind != JsonValueKind.Array)
        {
            throw new Exception($"داده‌ای برای نماد {symbol} دریافت نشد یا فرمت آن اشتباه است.");
        }

        var rawData = dataElement.EnumerateArray();
        var candles = new List<Candle>();

        foreach (var item in rawData)
        {
            var candleArray = item.EnumerateArray().ToArray();
            if (candleArray.Length < 6) continue; // جلوگیری از خطای ایندکس

            candles.Add(new Candle
            {
                Time = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(candleArray[0].GetString()) * 1000).DateTime,
                Open = Convert.ToDecimal(candleArray[1].GetString()),
                Close = Convert.ToDecimal(candleArray[2].GetString()),
                High = Convert.ToDecimal(candleArray[3].GetString()),
                Low = Convert.ToDecimal(candleArray[4].GetString()),
                Volume = Convert.ToDecimal(candleArray[5].GetString())
            });
        }

        candles.Reverse(); // مرتب‌سازی از قدیمی به جدید
        return candles;
    }


    public async Task<string> GetPriceListAsync(List<string> symbols)
    {
        var tasks = symbols.Select(async symbol =>
        {
            try
            {
                var candles = await GetCandlesAsync(symbol);
                var latest = candles[^1];
                return $"🔹 {symbol}: {latest.Close} USDT";
            }
            catch
            {
                return $"❌ {symbol} دریافت نشد";
            }
        });

        var results = await Task.WhenAll(tasks);
        return string.Join("\n", results);
    }




}


public class SignalGenerator
{
    public TradeSignal GenerateSignal(string symbol, List<Candle> candles)
    {
        var ema20 = EMA(candles, 20);
        var ema50 = EMA(candles, 50);
        var rsi = RSI(candles, 14);
        var macdData = MACD(candles, 12, 26, 9);

        string action = "Hold";
        string reason = "شرایط خرید تأیید نشده است";

        
        bool macdCrossover = macdData.MACD[^2] < macdData.Signal[^2] && macdData.MACD[^1] > macdData.Signal[^1];

        
        var avgVolume = candles.GetRange(candles.Count - 21, 20).Average(c => c.Volume);
        bool volumeSpike = candles[^1].Volume > avgVolume * 1.5m;

        
        bool strongBullishCandle = (candles[^1].Close > candles[^1].Open) &&
                                    ((candles[^1].Close - candles[^1].Open) / ((candles[^1].High - candles[^1].Low) + 0.0001m)) > 0.7m;

        if (ema20[^1] > ema50[^1] && rsi[^1] < 70 && rsi[^1] > 30 &&
            macdCrossover && volumeSpike && strongBullishCandle)
        {
            action = "Buy";
            reason = "EMA کراس مثبت + RSI نرمال + MACD کراس + افزایش حجم + کندل صعودی قوی";
        }
        
        if (ema20[^1] < ema50[^1] && rsi[^1] > 30 && rsi[^1] < 70 &&
            macdData.MACD[^2] > macdData.Signal[^2] && macdData.MACD[^1] < macdData.Signal[^1] &&
            candles[^1].Volume > avgVolume * 1.5m &&
            (candles[^1].Close < candles[^1].Open) &&
            ((candles[^1].Open - candles[^1].Close) / ((candles[^1].High - candles[^1].Low) + 0.0001m)) > 0.7m)
        {
            action = "Sell";
            reason = "EMA کراس منفی + RSI نرمال + MACD کراس منفی + افزایش حجم + کندل نزولی قوی";
        }

        return new TradeSignal
        {
            Symbol = symbol,
            Time = candles[^1].Time,
            Action = action,
            Reason = reason,
            Price = candles[^1].Close
        };
    }
    public (List<decimal> MACD, List<decimal> Signal) MACD(List<Candle> candles, int fastPeriod, int slowPeriod, int signalPeriod)
    {
        var emaFast = EMA(candles, fastPeriod);
        var emaSlow = EMA(candles, slowPeriod);
        var macdLine = new List<decimal>();

        int offset = slowPeriod - fastPeriod;
        for (int i = 0; i < emaSlow.Count; i++)
        {
            macdLine.Add(emaFast[i + offset] - emaSlow[i]);
        }

        var signalLine = new List<decimal>();
        decimal multiplier = 2m / (signalPeriod + 1);
        decimal sma = macdLine.Take(signalPeriod).Average();
        signalLine.Add(sma);

        for (int i = signalPeriod; i < macdLine.Count; i++)
        {
            decimal signal = (macdLine[i] - signalLine[^1]) * multiplier + signalLine[^1];
            signalLine.Add(signal);
        }

        return (macdLine.Skip(macdLine.Count - signalLine.Count).ToList(), signalLine);
    }


    private List<decimal> EMA(List<Candle> candles, int period)
    {
        var ema = new List<decimal>();
        decimal multiplier = 2m / (period + 1);
        decimal sma = 0;

        for (int i = 0; i < period; i++)
            sma += candles[i].Close;
        sma /= period;
        ema.Add(sma);

        for (int i = period; i < candles.Count; i++)
        {
            decimal value = (candles[i].Close - ema[^1]) * multiplier + ema[^1];
            ema.Add(value);
        }
        return ema;
    }

    private List<decimal> RSI(List<Candle> candles, int period)
    {
        var gains = new List<decimal>();
        var losses = new List<decimal>();

        for (int i = 1; i <= period; i++)
        {
            var diff = candles[i].Close - candles[i - 1].Close;
            if (diff > 0) gains.Add(diff); else losses.Add(-diff);
        }

        decimal avgGain = gains.Count > 0 ? Sum(gains) / period : 0;
        decimal avgLoss = losses.Count > 0 ? Sum(losses) / period : 0;
        var rs = avgLoss == 0 ? 100 : avgGain / avgLoss;
        var rsiList = new List<decimal> { 100 - (100 / (1 + rs)) };

        for (int i = period + 1; i < candles.Count; i++)
        {
            var diff = candles[i].Close - candles[i - 1].Close;
            decimal gain = diff > 0 ? diff : 0;
            decimal loss = diff < 0 ? -diff : 0;

            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;
            rs = avgLoss == 0 ? 100 : avgGain / avgLoss;
            rsiList.Add(100 - (100 / (1 + rs)));
        }

        return rsiList;
    }

    private decimal Sum(List<decimal> list)
    {
        decimal sum = 0;
        foreach (var item in list) sum += item;
        return sum;
    }
}

public class Candle
{
    public DateTime Time { get; set; }
    public decimal Open { get; set; }
    public decimal Close { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Volume { get; set; }
    public decimal Turnover { get; set; }
}

public class TradeSignal
{
    public string Symbol { get; set; }
    public DateTime Time { get; set; }
    public string Action { get; set; }
    public string Reason { get; set; }
    public decimal Price { get; set; } 
}
