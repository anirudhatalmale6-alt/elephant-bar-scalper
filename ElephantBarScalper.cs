#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class ElephantBarScalper : Strategy
    {
        #region Private Variables
        private double dailyPnL;
        private int tradesToday;
        private bool dailyTargetHit;
        private bool dailyLossHit;
        private bool strategyEnabled;
        private DateTime lastTradeDate;
        private Order entryOrder;
        private Order stopOrder;
        private Order targetOrder;
        private double elephantBarHigh;
        private double elephantBarLow;
        private double elephantBarClose;
        private double elephantBarOpen;
        private double elephantBarBodySize;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                 = "Elephant Bar Scalper (Oliver Velez method) - Bullish price action scalping strategy";
                Name                        = "ElephantBarScalper";
                Calculate                   = Calculate.OnBarClose;
                EntriesPerDirection          = 1;
                EntryHandling               = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds    = 30;
                IsFillLimitOnTouch           = false;
                MaximumBarsLookBack          = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution          = OrderFillResolution.Standard;
                Slippage                     = 2;
                StartBehavior                = StartBehavior.WaitUntilFlat;
                TimeInForce                  = TimeInForce.Gtc;
                TraceOrders                  = true;
                RealtimeErrorHandling        = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling           = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade          = 20;
                IsInstantiatedOnEachOptimizationIteration = true;

                // Position sizing
                ContractsPerTrade            = 2;

                // Oliver Velez elephant bar parameters
                SearchFactor                 = 1.2;
                RangeLookback                = 16;
                MinBodyPercent               = 65.0;
                MinElephantBarTicks          = 4;

                // Entry & exit
                TargetMultiplier             = 1.0;
                StopOffsetTicks              = 2;

                // Risk management
                DailyProfitTarget            = 500.0;
                DailyMaxLoss                 = 300.0;
                MaxTradesPerDay              = 10;

                // Session filter
                UseSessionFilter             = false;
                EnableNYSession              = true;
                NYSessionStart               = DateTime.Parse("09:30", System.Globalization.CultureInfo.InvariantCulture);
                NYSessionEnd                 = DateTime.Parse("15:45", System.Globalization.CultureInfo.InvariantCulture);
                EnableAsianSession           = true;
                AsianSessionStart            = DateTime.Parse("18:00", System.Globalization.CultureInfo.InvariantCulture);
                AsianSessionEnd              = DateTime.Parse("02:00", System.Globalization.CultureInfo.InvariantCulture);

                // Control
                EnableStrategy               = true;
            }
            else if (State == State.Configure)
            {
                dailyPnL = 0;
                tradesToday = 0;
                dailyTargetHit = false;
                dailyLossHit = false;
                strategyEnabled = EnableStrategy;
                lastTradeDate = DateTime.MinValue;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBars[0] < BarsRequiredToTrade)
                return;

            ResetDailyCounters();

            if (!strategyEnabled || dailyTargetHit || dailyLossHit)
                return;

            if (tradesToday >= MaxTradesPerDay)
                return;

            if (!IsWithinTradingHours())
                return;

            if (Position.MarketPosition != MarketPosition.Flat)
                return;

            if (IsElephantBar())
            {
                elephantBarHigh = High[0];
                elephantBarLow = Low[0];
                elephantBarClose = Close[0];
                elephantBarOpen = Open[0];
                elephantBarBodySize = Close[0] - Open[0];
                EnterBullishTrade();
            }
        }

        private void ResetDailyCounters()
        {
            if (Time[0].Date != lastTradeDate.Date)
            {
                dailyPnL = 0;
                tradesToday = 0;
                dailyTargetHit = false;
                dailyLossHit = false;
                lastTradeDate = Time[0].Date;
                LogMessage("New trading day started: " + Time[0].Date.ToShortDateString());
            }
        }

        private bool IsWithinTradingHours()
        {
            if (!UseSessionFilter)
                return true;

            int currentMinutes = Time[0].Hour * 60 + Time[0].Minute;

            if (EnableNYSession)
            {
                int nyStart = NYSessionStart.Hour * 60 + NYSessionStart.Minute;
                int nyEnd = NYSessionEnd.Hour * 60 + NYSessionEnd.Minute;
                if (currentMinutes >= nyStart && currentMinutes <= nyEnd)
                    return true;
            }

            if (EnableAsianSession)
            {
                int asianStart = AsianSessionStart.Hour * 60 + AsianSessionStart.Minute;
                int asianEnd = AsianSessionEnd.Hour * 60 + AsianSessionEnd.Minute;

                // Asian session crosses midnight (e.g. 18:00 - 02:00)
                if (asianStart > asianEnd)
                {
                    if (currentMinutes >= asianStart || currentMinutes <= asianEnd)
                        return true;
                }
                else
                {
                    if (currentMinutes >= asianStart && currentMinutes <= asianEnd)
                        return true;
                }
            }

            return false;
        }

        private bool IsElephantBar()
        {
            double open = Open[0];
            double close = Close[0];
            double high = High[0];
            double low = Low[0];

            // Must be a bullish candle
            if (close <= open)
                return false;

            double bodySize = close - open;
            double totalRange = high - low;

            if (totalRange <= 0)
                return false;

            // Oliver Velez body requirement: body must be >= MinBodyPercent of total range
            double bodyPercent = (bodySize / totalRange) * 100.0;
            if (bodyPercent < MinBodyPercent)
                return false;

            // Check minimum tick threshold
            double rangeInTicks = totalRange / TickSize;
            if (rangeInTicks < MinElephantBarTicks)
                return false;

            // Calculate average RANGE of previous candles
            double avgRange = 0;
            int validBars = 0;
            for (int i = 1; i <= RangeLookback; i++)
            {
                if (CurrentBar >= i)
                {
                    avgRange += High[i] - Low[i];
                    validBars++;
                }
            }

            if (validBars > 0)
                avgRange /= validBars;

            // Oliver Velez search factor: current range must exceed avgRange * SearchFactor
            if (avgRange > 0 && totalRange < avgRange * SearchFactor)
                return false;

            LogMessage(string.Format("ELEPHANT BAR detected | Close: {0} | Range: {1} ticks | Avg Range: {2} ticks | Factor: {3:F2}x | Body: {4:F0}%",
                close, rangeInTicks, (avgRange / TickSize), (avgRange > 0 ? totalRange / avgRange : 0), bodyPercent));

            return true;
        }

        private void EnterBullishTrade()
        {
            double stopPrice = elephantBarLow - (StopOffsetTicks * TickSize);
            double targetDistance = elephantBarBodySize * TargetMultiplier;
            double targetPrice = Close[0] + targetDistance;

            double stopTicks = (Close[0] - stopPrice) / TickSize;
            double targetTicks = targetDistance / TickSize;

            if (stopTicks <= 0 || targetTicks <= 0)
            {
                LogMessage("ENTRY SKIPPED | Invalid stop or target distance");
                return;
            }

            SetStopLoss("ElephantEntry", CalculationMode.Ticks, stopTicks, false);
            SetProfitTarget("ElephantEntry", CalculationMode.Ticks, targetTicks);

            EnterLong(ContractsPerTrade, "ElephantEntry");

            LogMessage(string.Format("ENTRY SIGNAL (confirmation bar) | Entry: {0} | Stop: {1} ({2:F0} ticks) | Target: {3} ({4:F0} ticks) | Contracts: {5}",
                Close[0], stopPrice, stopTicks, targetPrice, targetTicks, ContractsPerTrade));
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution.Order != null && execution.Order.OrderState == OrderState.Filled)
            {
                LogMessage(string.Format("ORDER FILLED | {0} | Price: {1} | Qty: {2} | Position: {3}",
                    execution.Order.Name, price, quantity, marketPosition));
            }
        }

        protected override void OnPositionUpdate(Cbi.Position position, double averagePrice, int quantity, Cbi.MarketPosition marketPosition)
        {
            if (marketPosition == MarketPosition.Flat && SystemPerformance != null)
            {
                if (SystemPerformance.AllTrades.Count > 0)
                {
                    Trade lastTrade = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1];
                    double tradePnL = lastTrade.ProfitCurrency;
                    dailyPnL += tradePnL;
                    tradesToday++;

                    LogMessage(string.Format("TRADE CLOSED | P&L: ${0:F2} | Daily P&L: ${1:F2} | Trades today: {2}",
                        tradePnL, dailyPnL, tradesToday));

                    if (dailyPnL >= DailyProfitTarget)
                    {
                        dailyTargetHit = true;
                        LogMessage(string.Format("DAILY TARGET HIT | ${0:F2} >= ${1:F2} | Bot stopping for the day.", dailyPnL, DailyProfitTarget));
                    }

                    if (dailyPnL <= -DailyMaxLoss)
                    {
                        dailyLossHit = true;
                        LogMessage(string.Format("DAILY MAX LOSS HIT | ${0:F2} <= -${1:F2} | Bot stopping for the day.", dailyPnL, DailyMaxLoss));
                    }
                }
            }
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            if (order.Name == "ElephantEntry")
                entryOrder = order;

            if (orderState == OrderState.Cancelled || orderState == OrderState.Rejected)
            {
                LogMessage(string.Format("ORDER {0} | {1} | Error: {2} {3}",
                    orderState, order.Name, error, nativeError));
            }
        }

        private void LogMessage(string message)
        {
            string timestamp = (Time != null && Time.Count > 0) ? Time[0].ToString("yyyy-MM-dd HH:mm:ss") : DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            Print(string.Format("[ElephantBarScalper] [{0}] {1}", timestamp, message));
        }

        #region Properties

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts Per Trade", Description = "Number of contracts per trade", Order = 1, GroupName = "1. Position Sizing")]
        public int ContractsPerTrade { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, 5.0)]
        [Display(Name = "Search Factor", Description = "Oliver Velez search factor - candle range must exceed avg range x this value (default 1.3 = 30% above average)", Order = 2, GroupName = "2. Elephant Bar Detection")]
        public double SearchFactor { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Range Lookback", Description = "Number of previous bars to calculate average range (default 16)", Order = 3, GroupName = "2. Elephant Bar Detection")]
        public int RangeLookback { get; set; }

        [NinjaScriptProperty]
        [Range(50.0, 95.0)]
        [Display(Name = "Min Body Percent", Description = "Minimum body as percentage of total candle range (Oliver Velez default: 70%)", Order = 4, GroupName = "2. Elephant Bar Detection")]
        public double MinBodyPercent { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Min Elephant Bar Ticks", Description = "Minimum candle range in ticks to qualify", Order = 5, GroupName = "2. Elephant Bar Detection")]
        public int MinElephantBarTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 5.0)]
        [Display(Name = "Target Multiplier", Description = "Profit target as multiple of elephant bar body size (1.0 = one candle target)", Order = 6, GroupName = "3. Exits")]
        public double TargetMultiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Stop Offset Ticks", Description = "Extra ticks below the elephant bar low for stop loss", Order = 7, GroupName = "3. Exits")]
        public int StopOffsetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Daily Profit Target ($)", Description = "Stop trading after reaching this daily profit", Order = 8, GroupName = "4. Risk Management")]
        public double DailyProfitTarget { get; set; }

        [NinjaScriptProperty]
        [Range(0, 10000)]
        [Display(Name = "Daily Max Loss ($)", Description = "Stop trading after this daily loss (prop account protection)", Order = 9, GroupName = "4. Risk Management")]
        public double DailyMaxLoss { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Max Trades Per Day", Description = "Maximum number of trades allowed per day", Order = 10, GroupName = "4. Risk Management")]
        public int MaxTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Use Session Filter", Description = "When OFF, bot trades any time. When ON, only trades during selected sessions.", Order = 10, GroupName = "5. Session Filter")]
        public bool UseSessionFilter { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable NY Session", Description = "Trade during New York session", Order = 11, GroupName = "5. Session Filter")]
        public bool EnableNYSession { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "NY Session Start", Description = "New York session start (ET)", Order = 12, GroupName = "5. Session Filter")]
        public DateTime NYSessionStart { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "NY Session End", Description = "New York session end (ET)", Order = 13, GroupName = "5. Session Filter")]
        public DateTime NYSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Asian Session", Description = "Trade during Asian session", Order = 14, GroupName = "5. Session Filter")]
        public bool EnableAsianSession { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Asian Session Start", Description = "Asian session start (ET)", Order = 15, GroupName = "5. Session Filter")]
        public DateTime AsianSessionStart { get; set; }

        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "Asian Session End", Description = "Asian session end (ET)", Order = 16, GroupName = "5. Session Filter")]
        public DateTime AsianSessionEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Strategy", Description = "Master on/off switch for the strategy", Order = 17, GroupName = "6. Control")]
        public bool EnableStrategy { get; set; }

        #endregion
    }
}
