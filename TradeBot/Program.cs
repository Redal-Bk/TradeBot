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
        if (user == null)
        {
            return 1;
        }
        if (user?.PhoneNumber != null)
        {
            return 0;
        }
        return 2;
    }
    public List<string?> GetCoinList()
    {
        var coin = _context.Coins.Select(x => x.Title).ToList();
        return coin;

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
        var symbols = program.GetCoinList();
       
       
        //await Console.Out.WriteLineAsync();
        var analyzer = new MarketAnalyzer();
        var signalGenerator = new SignalGenerator();
        var AccessIds = program.PremiumAccess();
        var telegram = new TelegramSender(_token, AccessIds);

        // اجرای بک‌تست قبل از شروع ربات
       // var backtester = new Backtester();
        //await backtester.RunBacktestAsync(symbols, months: 1);

        // ارسال خلاصه نتایج بک‌تست در تلگرام
        //await telegram.SendSignalMessage("✅ بک‌تست انجام شد. لطفاً نتایج را در لاگ بررسی کنید.");

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
                    var signal = signalGenerator.GenerateSignal(symbol, candles1h, candles4h);

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
            var sortedSignals = strongSignals
                .OrderByDescending(signal => ExtractScoreFromReason(signal.Reason))
                .Take(4);

            foreach (var signal in sortedSignals)
            {
                var dt = signal.Time;
                string persianTime = $"{pc.GetHour(dt):00}:{pc.GetMinute(dt):00} - {pc.GetYear(dt)}/{pc.GetMonth(dt):00}/{pc.GetDayOfMonth(dt):00}";

                string msg = $"📊 سیگنال جدید:\n" +
                             $"📌 رمز ارز: {signal.Symbol}\n" +
                             $"⏱ زمان: {persianTime}\n" +
                             $"💵 قیمت ورود: {signal.Price}\n" +
                             $"🎯 حد سود (TP1): {signal.TakeProfit1}\n" +
                             $"🎯 حد سود (TP2): {signal.TakeProfit2}\n" +
                             $"🎯 حد سود (TP3): {signal.TakeProfit3}\n" +
                             $"🛡 حد ضرر (SL): {signal.StopLoss}\n" +
                             $"📈 وضعیت: {signal.Action}\n" +
                             $"⚡ اهرم پیشنهادی: 10x\n" +
                             $"📝 دلیل: {signal.Reason}\n\n" +
                             $"📚 نکات مدیریت ریسک:\n" +
                             $"➖ لطفاً حتماً با رعایت مدیریت سرمایه وارد معامله شوید.\n" +
                             $"➖ با رسیدن قیمت به حد سود اول، بخشی از سود خود را سیو کرده و استاپ لاس را به نقطه ورود منتقل کنید (اصطلاحاً بی‌ضرر کنید).\n" +
                             $"➖ در صورت ادامه روند، استاپ لاس را به صورت پله‌ای همراه با قیمت جابجا کنید تا سود بیشتری حفظ شود.\n" +
                             $"➖ از ورود با حجم بالا و بدون برنامه مدیریت سرمایه خودداری کنید.";


                await Console.Out.WriteLineAsync("📡 سیگنال جدید ارسال شد: => " + msg);
                await telegram.SendSignalMessage(msg);
            }

            Log.Information(" Next Analys 4 hour later...");
            await Task.Delay(TimeSpan.FromHours(4));
        }

    }


    // تابع برای درآوردن نمره از متن Reason
    private static decimal ExtractScoreFromReason(string reason)
    {
        var match = Regex.Match(reason, @"(\d+(\.\d+)?)");
        if (match.Success && decimal.TryParse(match.Value, out var score))
            return score;

        return 0;
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
            else if(isExistPhoneNumber == 1)
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

    public async Task<List<Candle>> GetCandlesAsync(string symbol, DateTime? fromDate = null)
    {
        var url = $"https://api.kucoin.com/api/v1/market/candles?symbol={symbol}&type=4hour&limit=1000";
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
                Time = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(candleArray[0].GetString()) * 1000).UtcDateTime,
                Open = Convert.ToDecimal(candleArray[1].GetString()),
                Close = Convert.ToDecimal(candleArray[2].GetString()),
                High = Convert.ToDecimal(candleArray[3].GetString()),
                Low = Convert.ToDecimal(candleArray[4].GetString()),
                Volume = Convert.ToDecimal(candleArray[5].GetString())
            });
        }

        candles.Reverse();

        // ✅ فیلتر بر اساس fromDate اگر داده شده بود
        if (fromDate.HasValue)
        {
            candles = candles.Where(c => c.Time >= fromDate.Value).ToList();
        }

        return candles;
    }


    public async Task<List<Candle>> Get4hCandlesAsync(string symbol)
    {
        var url = $"https://api.kucoin.com/api/v1/market/candles?symbol={symbol}&type=4hour&limit=1000";
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
    public static List<decimal> ADX(List<Candle> candles, int period = 14)
    {
        var plusDM = new List<decimal>();
        var minusDM = new List<decimal>();
        var tr = new List<decimal>();
        var dx = new List<decimal>();
        var adx = new List<decimal>();

        for (int i = 1; i < candles.Count; i++)
        {
            decimal highDiff = candles[i].High - candles[i - 1].High;
            decimal lowDiff = candles[i - 1].Low - candles[i].Low;

            plusDM.Add(highDiff > lowDiff && highDiff > 0 ? highDiff : 0);
            minusDM.Add(lowDiff > highDiff && lowDiff > 0 ? lowDiff : 0);

            decimal trueRange = Math.Max(candles[i].High - candles[i].Low, Math.Max(
                Math.Abs(candles[i].High - candles[i - 1].Close),
                Math.Abs(candles[i].Low - candles[i - 1].Close)
            ));
            tr.Add(trueRange);
        }

        List<decimal> smoothedTR = Smooth(tr, period);
        List<decimal> smoothedPlusDM = Smooth(plusDM, period);
        List<decimal> smoothedMinusDM = Smooth(minusDM, period);

        for (int i = 0; i < smoothedTR.Count; i++)
        {
            decimal plusDI = smoothedTR[i] == 0 ? 0 : (smoothedPlusDM[i] / smoothedTR[i]) * 100;
            decimal minusDI = smoothedTR[i] == 0 ? 0 : (smoothedMinusDM[i] / smoothedTR[i]) * 100;

            decimal diDiff = Math.Abs(plusDI - minusDI);
            decimal diSum = plusDI + minusDI;

            decimal dxValue = diSum == 0 ? 0 : (diDiff / diSum) * 100;
            dx.Add(dxValue);
        }

        adx = Smooth(dx, period);
        return adx;
    }

    private static List<decimal> Smooth(List<decimal> values, int period)
    {
        var smoothed = new List<decimal>();
        decimal sum = 0;

        for (int i = 0; i < values.Count; i++)
        {
            if (i < period)
            {
                sum += values[i];
                if (i == period - 1)
                    smoothed.Add(sum / period);
            }
            else
            {
                decimal prev = smoothed.Last();
                decimal current = ((prev * (period - 1)) + values[i]) / period;
                smoothed.Add(current);
            }
        }

        return smoothed;
    }

}



public class SignalGenerator
{

    public TradeSignal GenerateSignal(string symbol, List<Candle> candles1h, List<Candle> candles4h,
    decimal slPercentBuy = 1.5m, decimal tpPercentBuy = 3.0m,
    decimal slPercentSell = 1.5m, decimal tpPercentSell = 3.0m)
    {
        bool isTrending = TrendConfirmationAnalyzer.IsTrendingMarket(candles1h);

        if (!isTrending)
        {
            return new TradeSignal
            {
                Symbol = symbol,
                Time = candles1h[^1].Time,
                Action = "Hold",
                Reason = "فاز بازار رنج است - سیگنال صادر نشد.",
                Price = candles1h[^1].Close,
                StopLoss = 0,
                TakeProfit1 = 0,
                TakeProfit2 = 0,
                TakeProfit3 = 0
            };
        }

        var ema20 = IndicatorCalculator.EMA(candles1h, 20);
        var ema50 = IndicatorCalculator.EMA(candles1h, 50);
        var rsi = IndicatorCalculator.RSI(candles1h, 14);
        var (macd, signalLine) = IndicatorCalculator.MACD(candles1h, 12, 26, 9);

        bool trendUp = TrendConfirmationAnalyzer.IsUptrend(candles4h);
        bool trendDown = TrendConfirmationAnalyzer.IsDowntrend(candles4h);

        decimal buyScore = 0;
        decimal sellScore = 0;

        // امتیازدهی به خرید
        if (CheckMacdCrossover(macd, signalLine))
            buyScore += 0.3m;
        if (CheckVolumeSpike(candles1h))
            buyScore += 0.2m;
        if (CheckEmaCrossover(ema20, ema50))
            buyScore += 0.2m;
        if (trendUp)
            buyScore += 0.2m;
        if (rsi[^1] > 50 && rsi[^1] < 70)
            buyScore += 0.1m;

        // امتیازدهی به فروش
        if (CheckMacdCrossunder(macd, signalLine))
            sellScore += 0.3m;
        if (CheckVolumeSpike(candles1h))
            sellScore += 0.2m;
        if (CheckEmaCrossunder(ema20, ema50))
            sellScore += 0.2m;
        if (trendDown)
            sellScore += 0.2m;
        if (rsi[^1] < 50 && rsi[^1] > 30)
            sellScore += 0.1m;
        if (DivergenceDetector.HasBearishDivergence(candles1h, rsi))
            sellScore += 0.2m;

        // تعیین اکشن نهایی
        string action = "Hold";
        string reason = "نمره کافی برای خرید یا فروش صادر نشد.";
        decimal stopLoss = 0;
        decimal takeProfit1 = 0, takeProfit2 = 0, takeProfit3 = 0;
        decimal entry = candles1h[^1].Close;

        Log.Information($"The Buying Rate Of {symbol} is : {buyScore}");
        Log.Information($"The Selling Rate Of {symbol} is : {sellScore}");

        if (buyScore >= 0.6m && buyScore >= sellScore)
        {
            action = "Buy";
            reason = $"امتیاز تحلیل خرید {buyScore:F2} (بیش از 0.7) - سیگنال خرید صادر شد.";

            stopLoss = entry - (entry * slPercentBuy / 100m);

            takeProfit1 = entry + (entry * slPercentBuy / 100m);  // 1 برابر SL
            takeProfit2 = entry + (entry * (slPercentBuy * 2) / 100m); // 2 برابر SL
            takeProfit3 = entry + (entry * (slPercentBuy * 3) / 100m); // 3 برابر SL
        }
        else if (sellScore >= 0.6m && sellScore > buyScore)
        {
            action = "Sell";
            reason = $"امتیاز تحلیل فروش {sellScore:F2} (بیش از 0.7) - سیگنال فروش صادر شد.";

            stopLoss = entry + (entry * slPercentSell / 100m);

            takeProfit1 = entry - (entry * slPercentSell / 100m);  // 1 برابر SL
            takeProfit2 = entry - (entry * (slPercentSell * 2) / 100m); // 2 برابر SL
            takeProfit3 = entry - (entry * (slPercentSell * 3) / 100m); // 3 برابر SL
        }

        return new TradeSignal
        {
            Symbol = symbol,
            Time = candles1h[^1].Time,
            Action = action,
            Reason = reason,
            Price = entry,
            StopLoss = stopLoss,
            TakeProfit1 = takeProfit1,
            TakeProfit2 = takeProfit2,
            TakeProfit3 = takeProfit3
        };
    }





    private bool CheckMacdCrossover(List<decimal> macd, List<decimal> signal)
    {
        return macd[^2] < signal[^2] && macd[^1] > signal[^1];
    }

    private bool CheckMacdCrossunder(List<decimal> macd, List<decimal> signal)
    {
        return macd[^2] > signal[^2] && macd[^1] < signal[^1];
    }

    private bool CheckEmaCrossover(List<decimal> emaFast, List<decimal> emaSlow)
    {
        return emaFast[^2] < emaSlow[^2] && emaFast[^1] > emaSlow[^1];
    }

    private bool CheckEmaCrossunder(List<decimal> emaFast, List<decimal> emaSlow)
    {
        return emaFast[^2] > emaSlow[^2] && emaFast[^1] < emaSlow[^1];
    }

    private bool CheckVolumeSpike(List<Candle> candles)
    {
        if (candles.Count < 2) return false;
        var avgVolume = candles.Take(candles.Count - 1).Select(c => c.Volume).Average();
        return candles[^1].Volume > avgVolume * 1.5m;
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

    public static bool IsTrendingMarket(List<Candle> candles, int period = 14, decimal threshold = 25m)
    {
        var adx = IndicatorCalculator.ADX(candles, period);
        return adx.Last() >= threshold;
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
    public decimal TakeProfit1 { get; set; }
    public decimal TakeProfit2 { get; set; }
    public decimal TakeProfit3 { get; set; }
}


public class Backtester
{
    private readonly MarketAnalyzer _analyzer;
    private readonly SignalGenerator _signalGenerator;

    private readonly Program _program;
    public Backtester()
    {
        _analyzer = new MarketAnalyzer();
        _signalGenerator = new SignalGenerator();
        _program = new Program();
    }

    public async Task RunBacktestAsync(List<string> symbols, int months = 6)
    {
        int totalTrades = 0;
        int winningTrades = 0;
        int buyTrades = 0;
        int sellTrades = 0;
        decimal initialEquity = 1000; 
        decimal equity = initialEquity;
        decimal peakEquity = equity;
        decimal maxDrawdown = 0;


        decimal leverage = 20; 
        decimal riskPerTradePercent = 1.0m; 
        decimal feePercentPerTrade = 0.08m; 

        foreach (var symbol in symbols)
        {
            try
            {
                var fromDate = DateTime.UtcNow.AddMonths(-months);
                var candles = await _analyzer.GetCandlesAsync(symbol, fromDate);

                for (int i = 50; i < candles.Count - 1; i++)
                {
                    var recentCandles = candles.Take(i + 1).ToList();
                    var signal = _signalGenerator.GenerateSignal(symbol, recentCandles, recentCandles,
                        slPercentBuy: 1.5m, tpPercentBuy: 3.0m,
                        slPercentSell: 1.5m, tpPercentSell: 3.0m);

                    if (signal.Action == "Buy" || signal.Action == "Sell")
                    {
                        totalTrades++;
                        if (signal.Action == "Buy") buyTrades++;
                        if (signal.Action == "Sell") sellTrades++;

                        var entry = signal.Price;
                        var sl = signal.StopLoss;
                        var tp = signal.TakeProfit3;

                        // outcome اولیه
                        var rawOutcome = SimulateTradeWithTrailingStop(candles.Skip(i + 1).Take(10).ToList(), entry, sl, tp, signal.Action);

                        // محاسبه ریسک روی موجودی فعلی
                        decimal riskAmount = equity * (riskPerTradePercent / 100m);

                        // سود یا ضرر واقعی با اهرم
                        decimal leveragedOutcome = (rawOutcome / entry) * leverage * riskAmount;

                        // محاسبه فی کل (ورود + خروج)
                        decimal totalFeePercent = feePercentPerTrade * 2;
                        decimal feeAmount = leveragedOutcome >= 0
                            ? leveragedOutcome * (totalFeePercent / 100m)
                            : Math.Abs(leveragedOutcome) * (totalFeePercent / 100m);

                        leveragedOutcome -= feeAmount; // کم کردن فی

                        if (leveragedOutcome > 0)
                            winningTrades++;

                        equity += leveragedOutcome;

                        if (equity > peakEquity)
                            peakEquity = equity;

                        var drawdown = (peakEquity - equity) / peakEquity;
                        if (drawdown > maxDrawdown)
                            maxDrawdown = drawdown;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"خطا در تحلیل {symbol}: {ex.Message}");
            }
        }

        decimal winRate = totalTrades > 0 ? (decimal)winningTrades / totalTrades * 100 : 0;



        Console.WriteLine($"\n✅ نتایج بک‌تست:");
        Console.WriteLine($"🔹 تعداد کل معاملات: {totalTrades}");
        Console.WriteLine($"🔹 تعداد معاملات خرید (Buy): {buyTrades}");
        Console.WriteLine($"🔹 تعداد معاملات فروش (Sell): {sellTrades}");
        Console.WriteLine($"🔹 درصد موفقیت: {winRate:F2}%");
        Console.WriteLine($"🔹 سرمایه نهایی: {equity:F2} $");
        Console.WriteLine($"🔹 سود خالص: {(equity - initialEquity):F2} $"); // ✅ اصلاح شد
        Console.WriteLine($"🔹 Drawdown ماکزیمم: {(maxDrawdown * 100):F2}%");

        //----------------------------------------------------------------------//

        var summaryMessage = $"✅ نتایج بک‌تست:\n" +
                      $"🔹 تعداد کل معاملات: {totalTrades}\n" +
                      $"🔹 تعداد معاملات خرید (Buy): {buyTrades}\n" +
                      $"🔹 تعداد معاملات فروش (Sell): {sellTrades}\n" +
                      $"🔹 درصد موفقیت: {winRate:F2}%\n" +
                      $"🔹 سرمایه نهایی: {equity:F2} $\n" +
                      $"🔹 سود خالص: {(equity - initialEquity):F2} $\n" + // ✅ اصلاح شد
                      $"🔹 Drawdown ماکزیمم: {(maxDrawdown * 100):F2}%";

        var token =  _program.GetToken();
        List<long> ids = new List<long> { 5200052562, 101705025, 7532880262 };


        var _telegramSender = new TelegramSender("7783393338:AAH7bsTePXfJvb7TJh7S2pr9hmLxaoxSIo4",ids);
        await _telegramSender.SendSignalMessage(summaryMessage);

    }

    public decimal SimulateTradeWithTrailingStop(List<Candle> futureCandles, decimal entryPrice, decimal stopLoss, decimal takeProfit, string action)
    {
        decimal highestPrice = entryPrice;
        decimal lowestPrice = entryPrice;
        decimal trailingPercent = 1.0m; // ✅ درصد فاصله تریلینگ استاپ از سقف (مثلا 1%)

        foreach (var candle in futureCandles)
        {
            if (action == "Buy")
            {
                if (candle.High > highestPrice)
                    highestPrice = candle.High;

                decimal trailingStop = highestPrice * (1 - trailingPercent / 100m);

                if (candle.Low <= trailingStop)
                {
                    // برخورد با تریلینگ استاپ در خرید
                    return trailingStop - entryPrice;
                }

                if (candle.High >= takeProfit)
                {
                    // تارگت خورد
                    return takeProfit - entryPrice;
                }

                if (candle.Low <= stopLoss)
                {
                    // استاپ خورد
                    return stopLoss - entryPrice;
                }
            }
            else if (action == "Sell")
            {
                if (candle.Low < lowestPrice)
                    lowestPrice = candle.Low;

                decimal trailingStop = lowestPrice * (1 + trailingPercent / 100m);

                if (candle.High >= trailingStop)
                {
                    // برخورد با تریلینگ استاپ در فروش
                    return entryPrice - trailingStop;
                }

                if (candle.Low <= takeProfit)
                {
                    // تارگت خورد
                    return entryPrice - takeProfit;
                }

                if (candle.High >= stopLoss)
                {
                    // استاپ خورد
                    return entryPrice - stopLoss;
                }
            }
        }

        // اگر هیچکدوم نخورده بود، معامله بسته نشده
        return 0;
    }





}
