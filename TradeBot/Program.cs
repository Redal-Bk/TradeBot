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
using System.Net;
using TradeBot.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using System.ComponentModel.DataAnnotations;
using System.Globalization;


public class Program
{
    
    private readonly SlySignalContext _context;
   

    public Program()
    {
       
        _context = new SlySignalContext();
    }

    public async Task AddUser(TradeBot.Entities.User user)
    {
       await _context.Users.AddAsync(user);
       await _context.SaveChangesAsync();
    }
    public async Task<string?> GetToken()
    {
        var res =  await _context.Token.FirstOrDefaultAsync();
        return res?.Token;
    }
    public bool UpdatePhoneNumber(string phoneNumber,long chatId)
    {   
        
        var user = _context.Users.FirstOrDefault(x => x.ChatId == chatId);
        if (user != null)
        {
            user.PhoneNumber = phoneNumber;
            _context.Users.Update(user);
            _context.SaveChanges();
            return true;
        }
        return false;
    }
    public List<long> PremiumAccess()
    {
        var accessIds = new List<long>();

        var accessUsers =  _context.Users
            .Where(x => x.IsPermium == true)
            .ToList();

        foreach (var user in accessUsers)
        {
            var chatId = user.ChatId;
            accessIds.Add(chatId);
        }

        return accessIds;
    }
    public int GetUserById(long ChatId)
    {
        var user = _context.Users.FirstOrDefault(x => x.ChatId == ChatId);
        if (user?.PhoneNumber != null)
        {
            return 0;
        }
        if(user == null)
        {
            return 1;
        }
        return 2;
    }
    static async Task Main(string[] args)
    {

        //await HandleUpdateAsync();

        var program = new Program();

         Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .MinimumLevel.Information()
            .CreateLogger();

        System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

       
        
        var _token = await program.GetToken();
        //Log.Information(_token);
        var symbols = new List<string>
        {
        "BTC-USDT", "ETH-USDT", "BNB-USDT", "XRP-USDT", "ADA-USDT",
        "SOL-USDT", "DOGE-USDT", "DOT-USDT", "TRX-USDT","TON-USDT","NOT-USDT","SHIB-USDT","LTC-USDT","LINK-USDT","OP-USDT"
        };

        var analyzer = new MarketAnalyzer();
        var signalGenerator = new SignalGenerator();
        var AccessIds = program.PremiumAccess();
        var telegram = new TelegramSender(
            _token,
           AccessIds
        );
        await telegram.StartListening();

        Log.Information(" The Bot Has Been Started");
        while (true)
        {
            var strongSignals = new List<TradeSignal>();

            foreach (var symbol in symbols)
            {
                try
                {
                    var candles1h = await analyzer.GetCandlesAsync(symbol);
                    var candles4h = await analyzer.Get4hCandlesAsync(symbol);
                    var signal = signalGenerator.GenerateSignal(symbol, candles1h,candles4h);

                    
                    if (signal.Action != "Hold")
                    {
                        strongSignals.Add(signal);
                        Log.Information($"[{signal.Time}] {signal.Symbol} => {signal.Action} | دلیل: {signal.Reason}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("❌ Error => {Symbol}: {Message}", symbol, ex.Message);
                }
            }


            var pc = new PersianCalendar();
            foreach (var signal in strongSignals.Take(4))
            {
                var dt = signal.Time;
                string persianTime = $"{pc.GetHour(dt):00}:{pc.GetMinute(dt):00} - {pc.GetYear(dt)}/{pc.GetMonth(dt):00}/{pc.GetDayOfMonth(dt):00}";

                string msg = $"\uD83D\uDCCA سیگنال جدید:\n" +
                             $"📌 رمز ارز: {signal.Symbol}\n" +
                             $"⏱ زمان: {persianTime}\n" +
                             $"💵 قیمت ورود: {signal.Price}\n" +
                             $"🎯 حد سود (TP): {signal.TakeProfit}\n" +
                             $"🛡 حد ضرر (SL): {signal.StopLoss}\n" +
                             $"📈 وضعیت: {signal.Action}\n" +
                             $"📝 دلیل: {signal.Reason}";

                await Console.Out.WriteLineAsync("📡 سیگنال جدید ارسال شد: => " + msg);
                await telegram.SendSignalMessage(msg);
            }


            Log.Information(" Next Analys 10  minute later...");
            await Task.Delay(TimeSpan.FromMinutes(10)); 

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
    
    public async Task SendMessageAsync(long chatId, string message)
    {   
        var program = new Program();
        var token = await program.GetToken();
        var url = $"https://api.telegram.org/bot{token}/sendMessage";
        using var client = new HttpClient();
        var data = new Dictionary<string, string>
    {
        { "chat_id", chatId.ToString() },
        { "text", message }
    };

        await client.PostAsync(url, new FormUrlEncodedContent(data));
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
            AllowedUpdates = Array.Empty<UpdateType>() 
        };

        _bot.StartReceiving(
            updateHandler: HandleUpdateAsync,
            pollingErrorHandler: HandlePollingErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: cancellationToken
        );
    }
    private static Dictionary<long, string> TempUserState = new();
    async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken cancellationToken)
    {

        var program = new Program();
        var _token = await program.GetToken() ?? "";
        var AccessIds = program.PremiumAccess();
        var telegram = new TelegramSender(
           _token,
          AccessIds
       );
        if (update.Message == null) return;

        var chatId = update.Message.Chat.Id;
        var text = update.Message.Text;

        using var _context = new SlySignalContext();
        var user = await _context.Users.FirstOrDefaultAsync(u => u.ChatId == chatId);

        if (text == "/start")
        {
            if (user == null)
            {
                
                TempUserState[chatId] = "awaiting_fullname";
                var keyboard = new ReplyKeyboardMarkup(new[]
                {
                    new[] { new KeyboardButton("📊 لیست قیمت‌ها") },
                    new[] { new KeyboardButton("📱 ارسال شماره تلفن") { RequestContact = true } }
                })
                {
                    ResizeKeyboard = true
                };

                await bot.SendTextMessageAsync(
                    chatId,
                    "👋 خوش آمدید! لطفاً نام و نام خانوادگی خود را به فارسی وارد کنید.",
                    replyMarkup: keyboard,
                    cancellationToken: cancellationToken
                );

            }
            else
            {
                var keyboard = new ReplyKeyboardMarkup(new[]
                 {
                    new[] { new KeyboardButton("📊 لیست قیمت‌ها") },
                    new[] { new KeyboardButton("📱 ارسال شماره تلفن") { RequestContact = true } }
                })
                {
                    ResizeKeyboard = true
                };

                await bot.SendTextMessageAsync(
                     chatId,
                     "✅ دوست خوبم، شما قبلاً ثبت‌نام کردی!\n\nمی‌تونی همین الان از امکانات ربات استفاده کنی 😍👇",
                     replyMarkup: keyboard,
                     cancellationToken: cancellationToken
                 );


            }
            return;
        }

        
        if (TempUserState.ContainsKey(chatId) && TempUserState[chatId] == "awaiting_fullname")
        {
            if (!IsPersian(text))
            {
                await telegram.SendMessageAsync(chatId, "❌ لطفاً نام را فقط به فارسی وارد کنید.");
                return;
            }

          
            var newUser = new TradeBot.Entities.User
            {
                ChatId = chatId,
                FullName = text

            };

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            TempUserState.Remove(chatId); 
            await telegram.SendMessageAsync(chatId, $"✅ {text} عزیز، ثبت‌نام شما با موفقیت انجام شد.");
        }

        if (update.Message is not { } message)
            return;

        
        Log.Information("Message From : => {ChatId}: {Text}", chatId, message.Text);

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
                    "SOL-USDT", "DOGE-USDT", "DOT-USDT", "TRX-USDT", "TON-USDT", "NOT-USDT","SHIB-USDT","LTC-USDT" , "LINK-USDT"
                };

                var priceList = await analyzer.GetPriceListAsync(symbols);

                
                await bot.SendTextMessageAsync(chatId, priceList, cancellationToken: cancellationToken);
            }

        }
        else if (message.Type == MessageType.Contact && message.Contact != null)
        {
            var isExistPhoneNumber = program.GetUserById(chatId);
            string phoneNumber = message.Contact.PhoneNumber;
            if (isExistPhoneNumber == 2)
            {
                program.UpdatePhoneNumber(phoneNumber, chatId);
                await bot.SendTextMessageAsync(chatId, $"شماره تماس شما: {phoneNumber} دریافت شد ✅", cancellationToken: cancellationToken);
            }
            else if(isExistPhoneNumber == 0)
            {
                await bot.SendTextMessageAsync(
                        chatId,
                        "📱 دوست خوبم!\n\nشماره تلفن شما قبلاً با موفقیت ثبت شده ✅\nمی‌تونی از امکانات ربات استفاده کنی 😉",
                        cancellationToken: cancellationToken
                    );

            }
            else
            {
                await bot.SendTextMessageAsync(
                    chatId,
                    "📲 دوست خوبم!\n\nبرای ثبت شماره تماس، اول باید ثبت‌نام کنی 🙏\n\nفقط کافیه دستور /start رو بزنی و خیلی راحت ثبت‌نامت رو انجام بدی 😊",
                    cancellationToken: cancellationToken
                );

            }

        }
    }
   
    private bool IsPersian(string input)
    {

        return Regex.IsMatch(input, @"^[\u0600-\u06FF\s]+$");
    } 

    Task HandlePollingErrorAsync(ITelegramBotClient bot, Exception exception, CancellationToken cancellationToken)
    {
        //Log.Error(exception, "Unhandeled Error Ocurired!");
        return Task.CompletedTask;
    }


}

public class MarketAnalyzer
{
    private readonly HttpClient _client = new HttpClient();

    public async Task<List<Candle>> GetCandlesAsync(string symbol)
    {
        var url = $"https://api.kucoin.com/api/v1/market/candles?symbol={symbol}&type=1hour&limit=100";
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
            if (candleArray.Length < 6) continue; 

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

        candles.Reverse(); 
        return candles;
    }

    public async Task<List<Candle>> Get4hCandlesAsync(string symbol)
    {
        var url = $"https://api.kucoin.com/api/v1/market/candles?symbol={symbol}&type=4hour&limit=100";
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
            if (candleArray.Length < 6) continue;

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

        candles.Reverse();
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
//indicator
public static class IndicatorCalculator
{
    public static List<decimal> EMA(List<Candle> candles, int period)
    {
        var ema = new List<decimal>();
        decimal multiplier = 2m / (period + 1);
        decimal sma = candles.Take(period).Average(c => c.Close);
        ema.Add(sma);

        for (int i = period; i < candles.Count; i++)
        {
            decimal value = (candles[i].Close - ema[^1]) * multiplier + ema[^1];
            ema.Add(value);
        }

        return ema;
    }

    public static List<decimal> RSI(List<Candle> candles, int period)
    {
        var rsi = new List<decimal>();
        decimal avgGain = 0, avgLoss = 0;

        for (int i = 1; i <= period; i++)
        {
            var change = candles[i].Close - candles[i - 1].Close;
            if (change > 0) avgGain += change;
            else avgLoss -= change;
        }

        avgGain /= period;
        avgLoss /= period;


        decimal rs = avgLoss == 0 ? 100 : avgGain / avgLoss;
        rsi.Add(100 - (100 / (1 + rs)));

        for (int i = period + 1; i < candles.Count; i++)
        {
            var change = candles[i].Close - candles[i - 1].Close;
            decimal gain = change > 0 ? change : 0;
            decimal loss = change < 0 ? -change : 0;

            avgGain = ((avgGain * (period - 1)) + gain) / period;
            avgLoss = ((avgLoss * (period - 1)) + loss) / period;

            rs = avgLoss == 0 ? 100 : avgGain / avgLoss;
            rsi.Add(100 - (100 / (1 + rs)));
        }

        return rsi;
    }

    public static (List<decimal> MACD, List<decimal> Signal) MACD(List<Candle> candles, int fast, int slow, int signal)
    {
        var emaFast = EMA(candles, fast);
        var emaSlow = EMA(candles, slow);

        var macd = new List<decimal>();
        int offset = emaFast.Count - emaSlow.Count;

        for (int i = 0; i < emaSlow.Count; i++)
            macd.Add(emaFast[i + offset] - emaSlow[i]);

        var signalLine = new List<decimal>();
        decimal multiplier = 2m / (signal + 1);
        decimal sma = macd.Take(signal).Average();
        signalLine.Add(sma);

        for (int i = signal; i < macd.Count; i++)
        {
            decimal signalVal = (macd[i] - signalLine[^1]) * multiplier + signalLine[^1];
            signalLine.Add(signalVal);
        }

        return (macd.Skip(macd.Count - signalLine.Count).ToList(), signalLine);
    }
}



public class SignalGenerator
{
    public  TradeSignal GenerateSignal(string symbol, List<Candle> candles1h, List<Candle> candles4h,
                                   decimal slPercent = 1.5m, decimal tpPercent = 3.0m)
    {
        var ema20 = IndicatorCalculator.EMA(candles1h, 20);
        var ema50 = IndicatorCalculator.EMA(candles1h, 50);
        var rsi = IndicatorCalculator.RSI(candles1h, 14);
        var (macd, signal) = IndicatorCalculator.MACD(candles1h, 12, 26, 9);

        bool trendUp = TrendConfirmationAnalyzer.IsUptrend(candles4h);
        bool trendDown = TrendConfirmationAnalyzer.IsDowntrend(candles4h);

        string action = "Hold";
        string reason = "سیگنال تأیید نشده یا روند مشخص نیست.";
        decimal stopLoss = 0, takeProfit = 0;
        decimal entry = candles1h[^1].Close;

        bool isBuy = CheckBuySignal(candles1h, ema20, ema50, rsi, macd, signal) && trendUp;
        bool isSell = CheckSellSignal(candles1h, ema20, ema50, rsi, macd, signal) && trendDown;

        if (isBuy)
        {
            action = "Buy";
            reason = "تأیید روند صعودی در تایم‌فریم ۴H + سیگنال خرید در ۱H";
            stopLoss = entry - (entry * slPercent / 100);
            takeProfit = entry + (entry * tpPercent / 100);
        }
        else if (isSell)
        {
            action = "Sell";
            reason = "تأیید روند نزولی در تایم‌فریم ۴H + سیگنال فروش در ۱H";
            stopLoss = entry + (entry * slPercent / 100);
            takeProfit = entry - (entry * tpPercent / 100);
        }

        return new TradeSignal
        {
            Symbol = symbol,
            Time = candles1h[^1].Time,
            Action = action,
            Reason = reason,
            Price = entry,
            StopLoss = stopLoss,
            TakeProfit = takeProfit
        };
    }

    public static class DivergenceDetector
    {
        public static bool HasBullishDivergence(List<Candle> candles, List<decimal> indicator)
        {
            if (candles.Count < 3 || indicator.Count < 3)
                return false;

            // قیمت: Lower Low
            bool priceLowerLow = candles[^1].Low < candles[^2].Low;

            // اندیکاتور: Higher Low
            bool indicatorHigherLow = indicator[^1] > indicator[^2];

            return priceLowerLow && indicatorHigherLow;
        }

        public static bool HasBearishDivergence(List<Candle> candles, List<decimal> indicator)
        {
            if (candles.Count < 3 || indicator.Count < 3)
                return false;

            // قیمت: Higher High
            bool priceHigherHigh = candles[^1].High > candles[^2].High;

            // اندیکاتور: Lower High
            bool indicatorLowerHigh = indicator[^1] < indicator[^2];

            return priceHigherHigh && indicatorLowerHigh;
        }
    }


    private bool CheckBuySignal(List<Candle> candles, List<decimal> ema20, List<decimal> ema50, List<decimal> rsi,
                                List<decimal> macd, List<decimal> signal)
    {
        bool macdCrossover = macd[^2] < signal[^2] && macd[^1] > signal[^1];
        decimal avgVolume = candles.Skip(candles.Count - 21).Take(20).Average(c => c.Volume);
        bool volumeSpike = candles[^1].Volume > avgVolume * 1.5m;
        bool strongBullishCandle = candles[^1].Close > candles[^1].Open &&
            ((candles[^1].Close - candles[^1].Open) / ((candles[^1].High - candles[^1].Low) + 0.0001m)) > 0.7m;

        return ema20[^1] > ema50[^1] &&
               rsi[^1] > 30 && rsi[^1] < 70 &&
               macdCrossover && volumeSpike && strongBullishCandle;
    }

    private bool CheckSellSignal(List<Candle> candles, List<decimal> ema20, List<decimal> ema50, List<decimal> rsi,
                                 List<decimal> macd, List<decimal> signal)
    {
        bool macdCrossdown = macd[^2] > signal[^2] && macd[^1] < signal[^1];
        decimal avgVolume = candles.Skip(candles.Count - 21).Take(20).Average(c => c.Volume);
        bool volumeSpike = candles[^1].Volume > avgVolume * 1.5m;
        bool strongBearishCandle = candles[^1].Close < candles[^1].Open &&
            ((candles[^1].Open - candles[^1].Close) / ((candles[^1].High - candles[^1].Low) + 0.0001m)) > 0.7m;

        return ema20[^1] < ema50[^1] &&
               rsi[^1] > 30 && rsi[^1] < 70 &&
               macdCrossdown && volumeSpike && strongBearishCandle;
    }
}
public static class TrendConfirmationAnalyzer
{
    public static bool IsUptrend(List<Candle> higherTimeframeCandles)
    {
        var ema20 = IndicatorCalculator.EMA(higherTimeframeCandles, 20);
        var ema50 = IndicatorCalculator.EMA(higherTimeframeCandles, 50);
        return ema20[^1] > ema50[^1];
    }

    public static bool IsDowntrend(List<Candle> higherTimeframeCandles)
    {
        var ema20 = IndicatorCalculator.EMA(higherTimeframeCandles, 20);
        var ema50 = IndicatorCalculator.EMA(higherTimeframeCandles, 50);
        return ema20[^1] < ema50[^1];
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

    public decimal StopLoss { get; set; }
    public decimal TakeProfit { get; set; }
}

