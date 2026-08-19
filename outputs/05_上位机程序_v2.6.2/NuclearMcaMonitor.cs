using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

[assembly: System.Reflection.AssemblyTitle("STM32G474 AD7980 Nuclear MCA Monitor")]
[assembly: System.Reflection.AssemblyDescription("Safe USB CDC monitor for STM32G474 + AD7980 nuclear MCA")]
[assembly: System.Reflection.AssemblyCompany("Nuclear MCA Project")]
[assembly: System.Reflection.AssemblyProduct("Nuclear MCA Monitor")]
[assembly: System.Reflection.AssemblyVersion("2.6.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("2.6.0.0")]

namespace NuclearMcaMonitor
{
    internal sealed class FormulaSpec
    {
        public string Title;
        public string Prefix;
        public string Numerator;
        public string Denominator;
        public string Suffix;
        public string Variables;
        public string Note;

        public FormulaSpec(string title, string prefix, string numerator, string denominator, string suffix, string variables, string note)
        {
            Title = title;
            Prefix = prefix;
            Numerator = numerator;
            Denominator = denominator;
            Suffix = suffix;
            Variables = variables;
            Note = note;
        }
    }

    internal sealed class SpectrumCursorReading
    {
        public int Channel;
        public int RawStart;
        public int RawEnd;
        public double AdcLowMv;
        public double AdcHighMv;
        public double AdcCenterMv;
        public double ChannelWidthUv;

        public static SpectrumCursorReading FromChannel(int channel, int channels)
        {
            if (channels < 1 || channels > 65536) throw new ArgumentOutOfRangeException("channels");
            channel = Math.Max(0, Math.Min(channels - 1, channel));
            double widthMv = SpectrumMetrics.AdcSpectrumFullScaleMv / channels;
            int rawStart = (int)(((long)channel * 65536L) / channels);
            int rawEnd = (int)((((long)(channel + 1) * 65536L) / channels) - 1L);
            if (rawEnd < rawStart) rawEnd = rawStart;
            return new SpectrumCursorReading
            {
                Channel = channel,
                RawStart = rawStart,
                RawEnd = rawEnd,
                AdcLowMv = channel * widthMv,
                AdcHighMv = (channel + 1.0) * widthMv,
                AdcCenterMv = (channel + 0.5) * widthMv,
                ChannelWidthUv = widthMv * 1000.0
            };
        }
    }

    internal sealed class CursorPeakMetrics
    {
        public int PeakChannel;
        public long PeakCount;
        public double FwhmChannels;
        public double ResolutionPercent;
    }

    internal sealed class SampleRecord
    {
        public uint TimestampMs;
        public uint Sequence;
        public ushort Raw;
        public uint VoltageMv;
        public ushort Channel;
        public ushort ExpectedMv;
        public uint ExpectedHz;
        public ushort ThresholdMv;
        public uint Overruns;
        public uint TxDrops;
    }

    internal static class ProtocolParser
    {
        private static readonly Regex StatusPair = new Regex(@"([A-Za-z_]+)=([^\s]+)", RegexOptions.Compiled);

        public static bool TryParseSample(string line, out SampleRecord sample)
        {
            sample = null;
            string[] fields = line.Split(',');
            if (fields.Length != 10) return false;

            uint timestamp;
            uint sequence;
            ushort raw;
            uint voltage;
            ushort channel;
            ushort expectedMv;
            uint expectedHz;
            ushort threshold;
            uint overruns;
            uint drops;
            if (!UInt32.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out timestamp) ||
                !UInt32.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out sequence) ||
                !UInt16.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out raw) ||
                !UInt32.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out voltage) ||
                !UInt16.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out channel) ||
                !UInt16.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out expectedMv) ||
                !UInt32.TryParse(fields[6], NumberStyles.None, CultureInfo.InvariantCulture, out expectedHz) ||
                !UInt16.TryParse(fields[7], NumberStyles.None, CultureInfo.InvariantCulture, out threshold) ||
                !UInt32.TryParse(fields[8], NumberStyles.None, CultureInfo.InvariantCulture, out overruns) ||
                !UInt32.TryParse(fields[9], NumberStyles.None, CultureInfo.InvariantCulture, out drops)) return false;
            sample = new SampleRecord();
            sample.TimestampMs = timestamp;
            sample.Sequence = sequence;
            sample.Raw = raw;
            sample.VoltageMv = voltage;
            sample.Channel = channel;
            sample.ExpectedMv = expectedMv;
            sample.ExpectedHz = expectedHz;
            sample.ThresholdMv = threshold;
            sample.Overruns = overruns;
            sample.TxDrops = drops;
            return true;
        }

        public static bool TryParseRaw16Batch(string line, out uint firstSequence, out ushort[] codes)
        {
            firstSequence = 0U;
            codes = null;
            if (line.StartsWith("@B16,", StringComparison.Ordinal))
            {
                string[] packed = line.Split(new[] { ',' }, 5);
                uint count;
                ushort expectedCrc;
                byte[] bytes;
                if (packed.Length != 5 ||
                    !UInt32.TryParse(packed[1], NumberStyles.None, CultureInfo.InvariantCulture, out firstSequence) ||
                    !UInt32.TryParse(packed[2], NumberStyles.None, CultureInfo.InvariantCulture, out count) ||
                    !UInt16.TryParse(packed[3], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out expectedCrc) ||
                    count < 1U || count > 168U) return false;
                try { bytes = Convert.FromBase64String(packed[4]); }
                catch (FormatException) { return false; }
                if (bytes.Length != (int)count * 2 || Crc16Ccitt(bytes) != expectedCrc) return false;
                ushort[] decoded = new ushort[count];
                for (int i = 0; i < decoded.Length; i++)
                    decoded[i] = (ushort)(bytes[i * 2] | (bytes[i * 2 + 1] << 8));
                codes = decoded;
                return true;
            }

            if (!line.StartsWith("@R16,", StringComparison.Ordinal)) return false;
            string[] fields = line.Split(new[] { ',' }, 4);
            uint legacyCount;
            if (fields.Length != 4 ||
                !UInt32.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out firstSequence) ||
                !UInt32.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out legacyCount) ||
                legacyCount < 1U || legacyCount > 64U || fields[3].Length != (int)legacyCount * 4) return false;

            ushort[] parsed = new ushort[legacyCount];
            for (int i = 0; i < parsed.Length; i++)
            {
                if (!UInt16.TryParse(fields[3].Substring(i * 4, 4), NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out parsed[i])) return false;
            }
            codes = parsed;
            return true;
        }

        private static ushort Crc16Ccitt(byte[] data)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < data.Length; i++)
            {
                crc ^= (ushort)(data[i] << 8);
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
            }
            return crc;
        }

        public static bool TryParseStatus(string line, out Dictionary<string, string> values)
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!line.StartsWith("# status ", StringComparison.Ordinal)) return false;
            MatchCollection matches = StatusPair.Matches(line);
            foreach (Match match in matches) values[match.Groups[1].Value] = match.Groups[2].Value;
            return values.Count > 0;
        }

        public static bool TryParseHistogramBin(string line, out int channel, out long count)
        {
            channel = -1;
            count = 0;
            string[] fields = line.Split(',');
            if (fields.Length != 2) return false;
            if (!Int32.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out channel) ||
                !Int64.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out count)) return false;
            return channel >= 0 && channel < SpectrumMetrics.HistogramChannels && count >= 0;
        }

        public static uint GetUInt(Dictionary<string, string> values, string name, uint fallback)
        {
            string text;
            uint value;
            return values.TryGetValue(name, out text) && UInt32.TryParse(text, out value) ? value : fallback;
        }
    }

    internal sealed class SpectrumMetrics
    {
        public const int HistogramChannels = 65536;
        public const double AdcSpectrumFullScaleMv = 2500.0;
        public const double IdealLsbMv = AdcSpectrumFullScaleMv / HistogramChannels;
        public int PeakChannel;
        public long PeakCount;
        public long TotalCounts;
        public long PeakArea;
        public double NetPeakArea;
        public double BackgroundCountsPerBin;
        public double CentroidChannel;
        public double FwhmChannels;
        public double ResolutionPercent;
        public double PeakMv;
        public double StatisticalPrecisionPercent;
        public int MetricSmoothingBins;
        public string QualityNote = "--";

        public static SpectrumMetrics Calculate(long[] bins)
        {
            return Calculate(bins, 0, HistogramChannels - 1, HistogramChannels);
        }

        public static SpectrumMetrics Calculate(long[] bins, int roiStart, int roiEnd)
        {
            return Calculate(bins, roiStart, roiEnd, HistogramChannels);
        }

        public static SpectrumMetrics Calculate(long[] bins, int roiStart, int roiEnd, int activeChannels)
        {
            SpectrumMetrics result = new SpectrumMetrics();
            if (bins == null || bins.Length != HistogramChannels) return result;
            if (activeChannels != 4096 && activeChannels != 8192 && activeChannels != 16384 && activeChannels != 65536) return result;
            roiStart = Math.Max(0, Math.Min(activeChannels - 1, roiStart));
            roiEnd = Math.Max(roiStart, Math.Min(activeChannels - 1, roiEnd));
            int smoothRadius = activeChannels >= 16384 ? 2 : activeChannels >= 8192 ? 1 : 0;
            result.MetricSmoothingBins = smoothRadius * 2 + 1;
            int peak = roiStart;
            for (int i = 0; i < activeChannels; i++) result.TotalCounts += bins[i];
            for (int i = roiStart + 1; i <= roiEnd; i++)
                if (SmoothedAt(bins, i, roiStart, roiEnd, smoothRadius) > SmoothedAt(bins, peak, roiStart, roiEnd, smoothRadius)) peak = i;
            result.PeakChannel = peak;
            result.PeakCount = (long)Math.Round(SmoothedAt(bins, peak, roiStart, roiEnd, smoothRadius));
            result.CentroidChannel = peak;
            if (result.PeakCount <= 0) return result;

            int backgroundHalfWidth = activeChannels / 16;
            int backgroundLeft = Math.Max(roiStart, peak - backgroundHalfWidth);
            int backgroundRight = Math.Min(roiEnd, peak + backgroundHalfWidth);
            List<long> backgroundSample = new List<long>();
            for (int i = backgroundLeft; i <= backgroundRight; i++) backgroundSample.Add(bins[i]);
            backgroundSample.Sort();
            if (backgroundSample.Count > 0) result.BackgroundCountsPerBin = backgroundSample[(backgroundSample.Count - 1) / 5];
            double peakHeight = result.PeakCount - result.BackgroundCountsPerBin;
            if (peakHeight <= 0.0)
            {
                result.PeakMv = (peak + 0.5) * AdcSpectrumFullScaleMv / activeChannels;
                return result;
            }

            double half = result.BackgroundCountsPerBin + peakHeight / 2.0;
            int left = peak;
            while (left > roiStart && SmoothedAt(bins, left, roiStart, roiEnd, smoothRadius) >= half) left--;
            int right = peak;
            while (right < roiEnd && SmoothedAt(bins, right, roiStart, roiEnd, smoothRadius) >= half) right++;
            /* A crossing exactly in the first/last ROI interval is valid.  It is
             * invalid only when the boundary bin itself is still above half-height. */
            if (SmoothedAt(bins, left, roiStart, roiEnd, smoothRadius) >= half || SmoothedAt(bins, right, roiStart, roiEnd, smoothRadius) >= half)
            {
                result.PeakMv = (peak + 0.5) * AdcSpectrumFullScaleMv / activeChannels;
                return result;
            }

            double leftCross = InterpolateCrossing(left, SmoothedAt(bins, left, roiStart, roiEnd, smoothRadius), left + 1, SmoothedAt(bins, left + 1, roiStart, roiEnd, smoothRadius), half);
            double rightCross = InterpolateCrossing(right - 1, SmoothedAt(bins, right - 1, roiStart, roiEnd, smoothRadius), right, SmoothedAt(bins, right, roiStart, roiEnd, smoothRadius), half);
            result.FwhmChannels = Math.Max(0.0, rightCross - leftCross);
            double sigma = result.FwhmChannels / 2.354820045;
            int integrationLeft = Math.Max(roiStart, peak - Math.Max(2, (int)Math.Ceiling(3.0 * sigma)));
            int integrationRight = Math.Min(roiEnd, peak + Math.Max(2, (int)Math.Ceiling(3.0 * sigma)));
            double weighted = 0.0;
            for (int i = integrationLeft; i <= integrationRight; i++)
            {
                result.PeakArea += bins[i];
                double net = Math.Max(0.0, bins[i] - result.BackgroundCountsPerBin);
                result.NetPeakArea += net;
                weighted += i * net;
            }
            if (result.NetPeakArea > 0.0) result.CentroidChannel = weighted / result.NetPeakArea;
            result.PeakMv = (result.CentroidChannel + 0.5) * AdcSpectrumFullScaleMv / activeChannels;
            if (result.CentroidChannel + 0.5 > 0.0) result.ResolutionPercent = result.FwhmChannels * 100.0 / (result.CentroidChannel + 0.5);
            if (result.NetPeakArea > 0.0 && result.CentroidChannel + 0.5 > 0.0)
                result.StatisticalPrecisionPercent = sigma * 100.0 / (Math.Sqrt(result.NetPeakArea) * (result.CentroidChannel + 0.5));
            result.QualityNote = result.NetPeakArea < 1000.0 ? "统计量偏低" :
                (smoothRadius > 0 ? "稳健半高宽(仅指标平滑" + result.MetricSmoothingBins + "道，原谱未改)" : "原始半高宽");
            return result;
        }

        private static double SmoothedAt(long[] bins, int channel, int start, int end, int radius)
        {
            if (radius <= 0) return bins[channel];
            long sum = 0;
            int count = 0;
            for (int i = Math.Max(start, channel - radius); i <= Math.Min(end, channel + radius); i++) { sum += bins[i]; count++; }
            return count == 0 ? 0.0 : sum / (double)count;
        }

        private static double InterpolateCrossing(int x1, double y1, int x2, double y2, double target)
        {
            if (y1 == y2) return (x1 + x2) / 2.0;
            return x1 + (target - y1) * (x2 - x1) / (double)(y2 - y1);
        }
    }

    internal sealed class TestPoint
    {
        public DateTime Time;
        public string SignalMode;
        public string Termination;
        public double GeneratorDisplayMv;
        public double ActualInputMv;
        public double MeasuredInputMv;
        public double AdcPeakMv;
        public double FwhmChannels;
        public double ResolutionPercent;
        public double AccuracyPercent;
        public double ReferenceRateHz;
        public double MeasuredRateHz;
        public double PassRatePercent;
        public double ProcessingEfficiencyPercent;
        public double Counts;
        public double MeasurementWindowSeconds;
        public uint WindowSamples;
        public uint WindowBusy;
        public double TriggerAcceptancePercent;
        public string PassQuality;
    }

    internal static class InputMath
    {
        public static double BoardLoadOhms(bool terminated50)
        {
            return terminated50 ? (50.0 * 1000000.0 / (50.0 + 1000000.0)) : 1000000.0;
        }

        public static double SourceOpenCircuitMv(double displayedMv, double sourceOhms, int generatorMode)
        {
            if (displayedMv < 0 || sourceOhms <= 0 || (generatorMode != 0 && generatorMode != 1)) return Double.NaN;
            return generatorMode == 1
                ? displayedMv * (sourceOhms + 50.0) / 50.0 : displayedMv;
        }

        public static double ActualInputMv(double displayedMv, double sourceOhms, double loadOhms, int generatorMode)
        {
            if (displayedMv < 0 || sourceOhms <= 0 || loadOhms <= 0 || (generatorMode != 0 && generatorMode != 1)) return Double.NaN;
            /* "50 ohm display" means the shown voltage is specified across a
             * nominal 50 ohm load. Recover the Thevenin open-circuit voltage
             * using the source impedance entered by the user; it is exactly 2x
             * only when the source itself is 50 ohm. */
            double openCircuitMv = SourceOpenCircuitMv(displayedMv, sourceOhms, generatorMode);
            return openCircuitMv * loadOhms / (sourceOhms + loadOhms);
        }

        public static double SourceOpenEquivalentMv(double measuredBoardMv, double sourceOhms, double loadOhms)
        {
            if (measuredBoardMv < 0 || sourceOhms <= 0 || loadOhms <= 0) return Double.NaN;
            return measuredBoardMv * (sourceOhms + loadOhms) / loadOhms;
        }

        public static bool LinearFit(IList<TestPoint> points, out double slope, out double intercept, out double rSquared,
            out double maxNonlinearityPercent, out double maxSpanNonlinearityPercent, out double maxResidualMv)
        {
            slope = intercept = rSquared = maxNonlinearityPercent = maxSpanNonlinearityPercent = maxResidualMv = Double.NaN;
            List<TestPoint> valid = points.Where(delegate(TestPoint p)
            {
                return !Double.IsNaN(p.ActualInputMv) && !Double.IsInfinity(p.ActualInputMv) &&
                       !Double.IsNaN(p.MeasuredInputMv) && !Double.IsInfinity(p.MeasuredInputMv);
            }).ToList();
            if (valid.Count < 3) return false;
            double meanX = valid.Average(delegate(TestPoint p) { return p.ActualInputMv; });
            double meanY = valid.Average(delegate(TestPoint p) { return p.MeasuredInputMv; });
            double sxx = 0.0, sxy = 0.0, syy = 0.0;
            foreach (TestPoint p in valid)
            {
                double dx = p.ActualInputMv - meanX;
                double dy = p.MeasuredInputMv - meanY;
                sxx += dx * dx; sxy += dx * dy; syy += dy * dy;
            }
            if (sxx <= 0.0) return false;
            slope = sxy / sxx;
            intercept = meanY - slope * meanX;
            double residual = 0.0;
            double span = valid.Max(delegate(TestPoint p) { return p.ActualInputMv; }) - valid.Min(delegate(TestPoint p) { return p.ActualInputMv; });
            maxResidualMv = 0.0;
            foreach (TestPoint p in valid)
            {
                double fitted = slope * p.ActualInputMv + intercept;
                double error = p.MeasuredInputMv - fitted;
                residual += error * error;
                maxResidualMv = Math.Max(maxResidualMv, Math.Abs(error));
                if (Math.Abs(fitted) > 1e-12)
                    maxNonlinearityPercent = Double.IsNaN(maxNonlinearityPercent) ? Math.Abs(error / fitted) * 100.0 : Math.Max(maxNonlinearityPercent, Math.Abs(error / fitted) * 100.0);
            }
            maxSpanNonlinearityPercent = span > 0.0 ? maxResidualMv * 100.0 / span : Double.NaN;
            rSquared = syy <= 0.0 ? 1.0 : Math.Max(0.0, Math.Min(1.0, 1.0 - residual / syy));
            return true;
        }

        public static bool WindowMetrics(uint startSamples, uint startBusy, uint startUptimeMs,
            uint samples, uint busy, uint uptimeMs, double referenceHz,
            out double seconds, out uint sampleDelta, out uint busyDelta,
            out double measuredHz, out double passPercent, out double processingPercent, out double triggerAcceptancePercent)
        {
            seconds = measuredHz = passPercent = processingPercent = triggerAcceptancePercent = Double.NaN;
            sampleDelta = busyDelta = 0U;
            if (samples < startSamples || busy < startBusy || uptimeMs == 0U || startUptimeMs == 0U) return false;
            uint deltaMs = unchecked(uptimeMs - startUptimeMs);
            if (deltaMs == 0U || deltaMs >= 0x80000000U) return false;
            seconds = deltaMs / 1000.0;
            sampleDelta = samples - startSamples;
            busyDelta = busy - startBusy;
            measuredHz = sampleDelta / seconds;
            processingPercent = busyDelta > 0U ? sampleDelta * 100.0 / busyDelta : Double.NaN;
            if (referenceHz > 0.0)
            {
                passPercent = measuredHz * 100.0 / referenceHz;
                triggerAcceptancePercent = busyDelta * 100.0 / (referenceHz * seconds);
            }
            return true;
        }
    }

    /* 指标卡数值标签：文字在固定卡片高度内换行时，鼠标滚轮可上下滚动查看
     * 被裁剪的第二行及后续行。单行文本完全不拦截滚轮，行为与普通 Label 一致；
     * 文本始终由 TextRenderer 直接绘制（与 Label 相同的 ClearType 渲染），
     * 滚动只通过 GDI 视口原点偏移实现，不经过位图，因此字体与原来完全一致。 */
    internal sealed class ScrollableMetricLabel : Label
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct PointOrg { public int X; public int Y; }

        [DllImport("gdi32.dll")]
        private static extern bool SetViewportOrgEx(IntPtr hdc, int x, int y, out PointOrg oldPoint);

        private static readonly TextFormatFlags TextFlags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.Left | TextFormatFlags.Top;
        private int scrollOffset;
        private int maxScroll;
        private bool adjusting;

        public ScrollableMetricLabel()
        {
            AutoSize = false;
            /* 只启用 UserPaint 以便自绘滚动；不要双缓冲/整窗重绘，
             * 否则文本先渲染到离屏位图会丢失 ClearType，字体与普通 Label 不一致。 */
            SetStyle(ControlStyles.UserPaint, true);
        }

        internal bool NeedsScrolling { get { return maxScroll > 0; } }
        internal int ScrollOffset { get { return scrollOffset; } }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            if (Parent != null)
            {
                Parent.Layout += ParentLayoutChanged;
                Parent.Resize += ParentLayoutChanged;
                Parent.BackColorChanged += delegate { Invalidate(); };
            }
            FitToCell();
        }

        private void ParentLayoutChanged(object sender, EventArgs e)
        {
            FitToCell();
        }

        /* 让本控件正好占满卡片内标题下方的可见区域：宽度=卡片可用宽度，
         * 高度=剩余可见高度。这样布局与原来 AutoSize 单行时完全一致，多行时
         * 也不再溢出卡片，被裁剪的内容改为在控件内部滚动显示。 */
        private void FitToCell()
        {
            if (adjusting || Parent == null) return;
            adjusting = true;
            try
            {
                int availableWidth = Parent.ClientSize.Width - Parent.Padding.Left - Parent.Padding.Right;
                if (availableWidth > 0 && Width != availableWidth) Width = availableWidth;
                int visibleHeight = Parent.ClientSize.Height - Top - Parent.Padding.Bottom;
                int targetHeight = Math.Max(18, visibleHeight);
                if (Height != targetHeight) Height = targetHeight;
            }
            finally { adjusting = false; }
            RecomputeScroll();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            RecomputeScroll();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RecomputeScroll();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            RecomputeScroll();
        }

        private void RecomputeScroll()
        {
            int textHeight = TextRenderer.MeasureText(Text, Font, new Size(Math.Max(1, ClientSize.Width), int.MaxValue), TextFlags).Height;
            int newMax = Math.Max(0, textHeight - ClientSize.Height);
            if (scrollOffset > newMax) scrollOffset = newMax;
            maxScroll = newMax;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (maxScroll <= 0) { base.OnMouseWheel(e); return; }
            ProcessWheelDelta(e.Delta);
            ((HandledMouseEventArgs)e).Handled = true;
        }

        /* 滚轮向上滚(delta>0)回看上方，向下滚(delta<0)查看下方被裁剪的行。 */
        internal void ProcessWheelDelta(int delta)
        {
            if (maxScroll <= 0) return;
            int step = Math.Max(14, Font.Height);
            int next = delta > 0 ? Math.Max(0, scrollOffset - step) : Math.Min(maxScroll, scrollOffset + step);
            if (next != scrollOffset) { scrollOffset = next; Invalidate(); }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            /* 透明背景：由 OnPaint 填充父卡片背景色，保持与普通 Label 相同的透明效果。 */
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color background = Parent != null ? Parent.BackColor : BackColor;
            using (SolidBrush fill = new SolidBrush(background))
                e.Graphics.FillRectangle(fill, ClientRectangle);
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            int textHeight = TextRenderer.MeasureText(Text, Font, new Size(ClientSize.Width, int.MaxValue), TextFlags).Height;
            int offset = Math.Min(scrollOffset, Math.Max(0, textHeight - ClientSize.Height));
            if (offset <= 0)
            {
                /* 未滚动：与普通 Label 完全一致，TextRenderer 直接绘制，字体原样。 */
                TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(0, 0, ClientSize.Width, ClientSize.Height), ForeColor, TextFlags);
                return;
            }
            /* 滚动：通过 GDI 视口原点平移文本，TextRenderer 的 GDI 输出跟随偏移，
             * 超出控件客户区的行由 GDI 自动裁剪。先 GetHdc 设置视口并立即释放，
             * 再调用 TextRenderer（它内部会再次取 HDC 并沿用当前视口状态）；
             * 绘制完成后必须恢复视口原点，否则会影响下一次绘制。
             * 注意 Graphics 被 GetHdc 锁定时不能调用 TextRenderer，否则会死锁。 */
            PointOrg oldOrg;
            IntPtr hdc = e.Graphics.GetHdc();
            SetViewportOrgEx(hdc, 0, -offset, out oldOrg);
            e.Graphics.ReleaseHdc(hdc);
            try
            {
                TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(0, 0, ClientSize.Width, textHeight), ForeColor, TextFlags);
            }
            finally
            {
                hdc = e.Graphics.GetHdc();
                SetViewportOrgEx(hdc, oldOrg.X, oldOrg.Y, out oldOrg);
                e.Graphics.ReleaseHdc(hdc);
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private static readonly Color Navy = Color.FromArgb(22, 54, 78);
        private static readonly Color NavyDeep = Color.FromArgb(15, 37, 54);
        private static readonly Color Teal = Color.FromArgb(25, 137, 132);
        private static readonly Color TealSoft = Color.FromArgb(231, 247, 245);
        private static readonly Color Canvas = Color.FromArgb(242, 246, 249);
        private static readonly Color Surface = Color.White;
        private static readonly Color Border = Color.FromArgb(213, 223, 231);
        private static readonly Color TextPrimary = Color.FromArgb(30, 49, 63);
        private static readonly Color TextMuted = Color.FromArgb(93, 111, 124);

        private readonly ComboBox portBox = new ComboBox();
        private readonly Button refreshPortsButton = new Button();
        private readonly Button connectButton = new Button();
        private readonly Button helpButton = new Button();
        private readonly Label connectionLabel = new Label();
        private readonly ComboBox channelBox = new ComboBox();
        private readonly ComboBox logLevelBox = new ComboBox();
        private readonly Label liveSummaryLabel = new Label();
        private readonly TabControl sideTabs = new TabControl();
        private readonly GroupBox controlGroup = new GroupBox();
        private readonly ComboBox profileBox = new ComboBox();
        private readonly NumericUpDown thresholdInput = new NumericUpDown();
        private readonly NumericUpDown amplitudeInput = new NumericUpDown();
        private readonly NumericUpDown frequencyInput = new NumericUpDown();
        private readonly NumericUpDown decimateInput = new NumericUpDown();
        private readonly CheckBox streamCheck = new CheckBox();
        private readonly CheckBox autoHistogramCheck = new CheckBox();
        private readonly NumericUpDown histogramIntervalInput = new NumericUpDown();
        private readonly NumericUpDown roiStartInput = new NumericUpDown();
        private readonly NumericUpDown roiEndInput = new NumericUpDown();
        private readonly CheckBox logScaleCheck = new CheckBox();
        private readonly CheckBox pauseDisplayCheck = new CheckBox();
        private readonly CheckBox showProtocolCheck = new CheckBox();
        private readonly CheckBox knownAmplitudeCheck = new CheckBox();
        private readonly CheckBox knownRateCheck = new CheckBox();
        private readonly ComboBox terminationBox = new ComboBox();
        private readonly ComboBox generatorModeBox = new ComboBox();
        private readonly NumericUpDown generatorAmplitudeInput = new NumericUpDown();
        private readonly NumericUpDown sourceImpedanceInput = new NumericUpDown();
        private readonly NumericUpDown analogGainInput = new NumericUpDown();
        private readonly NumericUpDown analogOffsetInput = new NumericUpDown();
        private readonly CheckBox useMeasuredCurveCheck = new CheckBox();
        private readonly NumericUpDown linearitySlopeInput = new NumericUpDown();
        private readonly NumericUpDown linearityInterceptInput = new NumericUpDown();
        private readonly NumericUpDown referenceRateInput = new NumericUpDown();
        private readonly Label impedanceSummary = new Label();
        private readonly Label correctionSummary = new Label();
        private readonly Label linearitySummary = new Label();
        private readonly DataGridView testPointGrid = new DataGridView();
        private readonly Button recordButton = new Button();
        private readonly Label recordLabel = new Label();
        private readonly Chart spectrumChart = new Chart();
        private readonly Panel spectrumCursorCard = new Panel();
        private readonly CheckBox cursorPeakSnapCheck = new CheckBox();
        private readonly NumericUpDown viewStartInput = new NumericUpDown();
        private readonly NumericUpDown viewEndInput = new NumericUpDown();
        private readonly RichTextBox terminal = new RichTextBox();
        private readonly Label samplesValue = MetricLabel();
        private readonly Label busyValue = MetricLabel();
        private readonly Label rateValue = MetricLabel();
        private readonly Label overrunValue = MetricLabel();
        private readonly Label dropsValue = MetricLabel();
        private readonly Label meanValue = MetricLabel();
        private readonly Label peakValue = MetricLabel();
        private readonly Label fwhmValue = MetricLabel();
        private readonly Label resolutionValue = MetricLabel();
        private readonly Label inputPeakValue = MetricLabel();
        private readonly Label accuracyValue = MetricLabel();
        private readonly Label statisticalValue = MetricLabel();
        private readonly Label passRateValue = MetricLabel();
        private readonly Label processingValue = MetricLabel();
        private readonly Label elapsedValue = MetricLabel();
        private readonly System.Windows.Forms.Timer serviceTimer = new System.Windows.Forms.Timer();
        private readonly Dictionary<Control, FormulaSpec> metricFormulae = new Dictionary<Control, FormulaSpec>();
        private readonly Panel formulaHoverCard = new Panel();
        private readonly System.Windows.Forms.Timer formulaHoverTimer = new System.Windows.Forms.Timer();
        private Control pendingFormulaControl;
        private FormulaSpec pendingFormula;
        private FormulaSpec visibleFormula;

        private SerialPort serialPort;
        private readonly StringBuilder receiveBuffer = new StringBuilder();
        private readonly object pendingSerialLock = new object();
        private readonly StringBuilder pendingSerialText = new StringBuilder();
        private bool serialDispatchPending;
        private readonly long[] spectrum = new long[SpectrumMetrics.HistogramChannels];
        private int activeChannels = 4096;
        private long[] incomingHistogram;
        private bool[] incomingHistogramSeen;
        private bool histogramTransfer;
        private bool histogramRequestPending;
        private int histogramBinsReceived;
        private bool chartDirty;
        private DateTime nextStatusRequest = DateTime.MinValue;
        private DateTime nextHistogramRequest = DateTime.MinValue;
        private DateTime histogramRequestStarted = DateTime.MinValue;
        private DateTime lastDeviceActivity = DateTime.MinValue;
        private DateTime nextLinkResync = DateTime.MinValue;
        private DateTime lastLinkWarning = DateTime.MinValue;
        private uint lastSamples;
        private DateTime lastRateTime = DateTime.MinValue;
        private StreamWriter recorder;
        private int recorderLinesSinceFlush;
        private string recordingPath;
        private int terminalLines;
        private readonly List<TestPoint> testPoints = new List<TestPoint>();
        private SpectrumMetrics currentMetrics = new SpectrumMetrics();
        private uint latestBusy;
        private uint latestOverruns;
        private uint latestSamples;
        private uint latestDrops;
        private uint latestUsbRecoveries;
        private uint latestQueueDepth;
        private uint latestRangeOverflows;
        private uint latestUptimeMs;
        private uint lastRateUptimeMs;
        private uint firmwareAdcSpectrumFsMv;
        private uint firmwareHistogramChannels;
        private uint firmwareFrontendGainMilli;
        private bool firmwareStatusSeen;
        private bool firmwareMappingCompatible = true;
        private bool firmwareWarningShown;
        private uint nextRawSequence;
        private bool rawSequenceValid;
        private ulong pcStreamGapSamples;
        private uint latestStreamLostSamples;
        private double latestMeasuredRate;
        private readonly Stopwatch measurementClock = new Stopwatch();
        private uint measurementStartBusy;
        private uint measurementStartSamples;
        private uint measurementStartRangeOverflows;
        private uint measurementStartUptimeMs;
        private bool measurementBaselineValid;
        private bool measurementBaselinePending;
        private bool applyingLogScale;
        private bool suppressDisplayDialogs;
        private DateTime nextPortScan = DateTime.MinValue;
        private string lastPortInventory = "";
        private bool spectrumCursorVisible;
        private bool spectrumCursorPinned;
        private Point spectrumCursorPoint;
        private int spectrumCursorChannel = -1;
        private SpectrumCursorReading spectrumCursorReading;
        private CursorPeakMetrics spectrumCursorPeak;
        private DateTime nextSpectrumCursorPaint = DateTime.MinValue;
        private int viewStartChannel;
        private int viewEndChannelExclusive = 4096;
        private bool synchronizingSpectrumView;

        public MainForm()
        {
            Text = "STM32G474 + AD7980 自适应道址分析系统 v2.6.0";
            Width = 1500;
            Height = 900;
            MinimumSize = new Size(1200, 760);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Canvas;
            BuildInterface();
            ConfigureFormulaHoverCard();
            SetConnectedState(false);
            RefreshPorts(true);
            Shown += delegate { ActiveControl = null; };
            serviceTimer.Interval = 400;
            serviceTimer.Tick += ServiceTimerTick;
            serviceTimer.Start();
            FormClosing += MainFormClosing;
            Resize += delegate { HideFormulaHoverCard(); spectrumCursorPinned = false; HideSpectrumCursor(); };
            MouseWheel += delegate { HideFormulaHoverCard(); };
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape && spectrumCursorVisible)
                {
                    spectrumCursorPinned = false;
                    HideSpectrumCursor();
                    e.Handled = true;
                }
            };
        }

        private static Label MetricLabel()
        {
            return new ScrollableMetricLabel { Text = "--", Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold), ForeColor = TextPrimary, Margin = new Padding(0, 4, 0, 0) };
        }

        private void BuildInterface()
        {
            TableLayoutPanel shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0), Padding = new Padding(0) };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
            Controls.Add(shell);

            Panel header = new Panel { Dock = DockStyle.Fill, BackColor = Navy, Padding = new Padding(14, 10, 14, 8) };
            shell.Controls.Add(header, 0, 0);
            FlowLayoutPanel headerFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoSize = false };
            header.Controls.Add(headerFlow);
            headerFlow.Controls.Add(new Label { Text = "AD7980 MCA", AutoSize = true, ForeColor = Color.White, Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold), Margin = new Padding(0, 5, 18, 0) });
            headerFlow.Controls.Add(new Label { Text = "USB CDC", AutoSize = true, ForeColor = Color.FromArgb(206, 222, 233), Margin = new Padding(0, 8, 7, 0) });
            portBox.Width = 125;
            portBox.DropDownStyle = ComboBoxStyle.DropDownList;
            portBox.FlatStyle = FlatStyle.Flat;
            portBox.Font = new Font("Microsoft YaHei UI", 9F);
            portBox.TabStop = false;
            headerFlow.Controls.Add(portBox);
            ConfigureButton(refreshPortsButton, "重新扫描", RefreshPortsClicked, 82);
            ConfigureButton(connectButton, "连接", ConnectClicked, 82);
            headerFlow.Controls.Add(refreshPortsButton);
            headerFlow.Controls.Add(connectButton);
            headerFlow.Controls.Add(new Label { Text = "自动发现", AutoSize = true, ForeColor = Color.FromArgb(126, 232, 171), Margin = new Padding(8, 8, 4, 0) });
            ConfigureButton(helpButton, "使用说明", delegate { ShowManual(); }, 86);
            headerFlow.Controls.Add(helpButton);
            connectionLabel.Text = "未连接";
            connectionLabel.AutoSize = true;
            connectionLabel.ForeColor = Color.FromArgb(255, 205, 90);
            connectionLabel.Margin = new Padding(12, 8, 12, 0);
            headerFlow.Controls.Add(connectionLabel);
            ConfigureButton(recordButton, "开始记录", RecordClicked, 92);
            headerFlow.Controls.Add(recordButton);
            recordLabel.Text = "未记录";
            recordLabel.AutoEllipsis = true;
            recordLabel.Width = 360;
            recordLabel.ForeColor = Color.White;
            recordLabel.Margin = new Padding(8, 8, 0, 0);
            headerFlow.Controls.Add(recordLabel);
            StyleHeaderButton(connectButton, true);
            StyleHeaderButton(recordButton, true);
            StyleHeaderButton(refreshPortsButton, false);
            StyleHeaderButton(helpButton, false);

            Label safety = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "安全模式：仅发送固件白名单命令；不控制CNV/GPIO。采集时必须拔下LCD/FPC，PA2/PA3/PC4/PC5/PF2保持接地高阻。",
                BackColor = Color.FromArgb(255, 247, 229),
                ForeColor = Color.FromArgb(139, 83, 17),
                Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold)
            };
            shell.Controls.Add(safety, 0, 1);

            TableLayoutPanel terminalPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0), Padding = new Padding(0), BackColor = NavyDeep };
            terminalPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            terminalPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            terminalPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            liveSummaryLabel.Dock = DockStyle.Fill;
            liveSummaryLabel.Text = "状态摘要：等待连接";
            liveSummaryLabel.ForeColor = Color.FromArgb(210, 230, 245);
            liveSummaryLabel.BackColor = NavyDeep;
            liveSummaryLabel.Padding = new Padding(10, 4, 0, 0);
            terminalPanel.Controls.Add(liveSummaryLabel, 0, 0);
            terminal.Dock = DockStyle.Fill;
            terminal.Margin = new Padding(0);
            terminal.ReadOnly = true;
            terminal.BackColor = Color.FromArgb(20, 25, 31);
            terminal.ForeColor = Color.FromArgb(205, 232, 205);
            terminal.Font = new Font("Consolas", 9F);
            terminal.WordWrap = false;
            terminal.BorderStyle = BorderStyle.None;
            terminalPanel.Controls.Add(terminal, 0, 1);
            shell.Controls.Add(terminalPanel, 0, 3);

            TableLayoutPanel content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), Padding = new Padding(0) };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 410));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.Controls.Add(content, 0, 2);

            Panel left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 10, 6, 8), AutoScroll = true, BackColor = Canvas };
            content.Controls.Add(left, 0, 0);
            BuildControlPanel(left);

            TableLayoutPanel central = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8, 8, 12, 8), Margin = new Padding(0), BackColor = Canvas };
            central.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            central.RowStyles.Add(new RowStyle(SizeType.Absolute, 184));
            content.Controls.Add(central, 1, 0);
            BuildChart(central);
            BuildMetrics(central);
        }

        private void BuildControlPanel(Panel parent)
        {
            sideTabs.Dock = DockStyle.Fill;
            sideTabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            sideTabs.ItemSize = new Size(112, 32);
            sideTabs.SizeMode = TabSizeMode.Fixed;
            sideTabs.Padding = new Point(12, 5);
            sideTabs.DrawItem += DrawSideTab;
            parent.Controls.Add(sideTabs);
            TabPage acquisitionTab = new TabPage("采集与显示");
            TabPage inputTab = new TabPage("输入/阻抗/校准");
            TabPage testTab = new TabPage("测试点与报告");
            sideTabs.TabPages.Add(acquisitionTab);
            sideTabs.TabPages.Add(inputTab);
            sideTabs.TabPages.Add(testTab);
            acquisitionTab.BackColor = Surface;
            inputTab.BackColor = Surface;
            testTab.BackColor = Surface;

            controlGroup.Text = "采集控制（全部带范围校验）";
            controlGroup.Dock = DockStyle.Fill;
            controlGroup.ForeColor = TextPrimary;
            acquisitionTab.Controls.Add(controlGroup);
            TableLayoutPanel table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), ColumnCount = 2, RowCount = 16, AutoScroll = true };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
            table.RowStyles.Clear();
            for (int row = 0; row < 16; row++) table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            controlGroup.Controls.Add(table);

            profileBox.DropDownStyle = ComboBoxStyle.DropDownList;
            profileBox.Items.AddRange(new object[] { "未知信号（默认，仅测量）", "baseline 500mV/1kHz", "amplitude 幅度扫描", "frequency 频率扫描" });
            profileBox.SelectedIndex = 0;
            AddRow(table, 0, "测试模式", WithAction(profileBox, "应用", delegate { SendProfile(); }));

            channelBox.DropDownStyle = ComboBoxStyle.DropDownList;
            channelBox.Items.AddRange(new object[] { "4096道（MCU累计/最高吞吐）", "8192道（原始码重分道）", "16384道（原始码重分道）", "65536道（满16位）" });
            channelBox.SelectedIndex = 0;
            AddRow(table, 1, "能谱道址", WithAction(channelBox, "应用", delegate { ApplyChannelMode(); }));

            logLevelBox.DropDownStyle = ComboBoxStyle.DropDownList;
            logLevelBox.Items.AddRange(new object[] { "仅告警", "操作与告警（推荐）", "完整协议" });
            logLevelBox.SelectedIndex = 1;
            logLevelBox.SelectedIndexChanged += delegate { showProtocolCheck.Checked = logLevelBox.SelectedIndex == 2; };
            AddRow(table, 2, "下方日志", logLevelBox);

            thresholdInput.Minimum = 50; thresholdInput.Maximum = 200; thresholdInput.Value = 100; thresholdInput.Suffix(" mV");
            AddRow(table, 3, "比较器阈值", WithAction(thresholdInput, "应用", delegate { ApplyThreshold(); }));

            amplitudeInput.Minimum = 100; amplitudeInput.Maximum = 900; amplitudeInput.Increment = 1; amplitudeInput.Value = 500; amplitudeInput.Suffix(" mV");
            AddRow(table, 4, "标记幅度", WithAction(amplitudeInput, "应用", delegate { ApplyAmplitude(); }));

            frequencyInput.Minimum = 1; frequencyInput.Maximum = 100; frequencyInput.Value = 1; frequencyInput.Suffix(" kHz");
            AddRow(table, 5, "标记频率", WithAction(frequencyInput, "应用", delegate { ApplyFrequency(); }));

            decimateInput.Minimum = 1; decimateInput.Maximum = 1; decimateInput.Value = 1;
            decimateInput.Enabled = false;
            AddRow(table, 6, "流输出抽样", WithAction(decimateInput, "应用", delegate { SendSafe("decimate " + Decimal.ToInt32(decimateInput.Value).ToString(CultureInfo.InvariantCulture)); }));

            streamCheck.Text = "事件流开启";
            streamCheck.Checked = false;
            streamCheck.Enabled = false;
            streamCheck.CheckedChanged += delegate { if (controlGroup.Enabled && streamCheck.Enabled) SendSafe(streamCheck.Checked ? "stream on" : "stream off"); };
            AddRow(table, 7, "实时数据", streamCheck);

            autoHistogramCheck.Text = "MCU完整4096道累计";
            autoHistogramCheck.Checked = true;
            autoHistogramCheck.Enabled = true;
            AddRow(table, 8, "完整能谱", autoHistogramCheck);
            histogramIntervalInput.Minimum = 1; histogramIntervalInput.Maximum = 10; histogramIntervalInput.Value = 1; histogramIntervalInput.Suffix(" s");
            histogramIntervalInput.Enabled = true;
            AddRow(table, 9, "刷新间隔", histogramIntervalInput);

            roiStartInput.Minimum = 0; roiStartInput.Maximum = SpectrumMetrics.HistogramChannels - 2; roiStartInput.Value = 0;
            roiStartInput.ValueChanged += delegate { ValidateRoi(); chartDirty = true; };
            AddRow(table, 10, "寻峰ROI起点", roiStartInput);
            roiEndInput.Minimum = 1; roiEndInput.Maximum = SpectrumMetrics.HistogramChannels - 1; roiEndInput.Value = activeChannels - 1;
            roiEndInput.ValueChanged += delegate { ValidateRoi(); chartDirty = true; };
            AddRow(table, 11, "寻峰ROI终点", roiEndInput);

            logScaleCheck.Text = "Y轴对数显示（0计数留空）";
            logScaleCheck.CheckedChanged += delegate { ApplyLogScale(); };
            AddRow(table, 12, "显示方式", logScaleCheck);
            pauseDisplayCheck.Text = "暂停图形刷新（采集继续）";
            AddRow(table, 13, "界面刷新", pauseDisplayCheck);

            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = true };
            Button statusButton = SmallButton("读取状态", delegate { SendSafe("status"); });
            Button dumpButton = SmallButton("立即刷新能谱", delegate { RequestHistogram(); });
            Button clearButton = SmallButton("清空统计", delegate { ClearStatistics(); });
            Button autoRoiButton = SmallButton("自动ROI", delegate { AutoSelectRoi(); });
            Button fullRoiButton = SmallButton("全范围ROI", delegate { SetFullRoi(); });
            Button exportButton = SmallButton("导出能谱CSV", delegate { ExportSpectrum(); });
            Button exportTxtButton = SmallButton("导出能谱TXT", delegate { ExportSpectrumText(); });
            Button imageButton = SmallButton("保存能谱PNG", delegate { SaveSpectrumImage(); });
            actions.Controls.Add(statusButton); actions.Controls.Add(dumpButton); actions.Controls.Add(clearButton);
            actions.Controls.Add(autoRoiButton); actions.Controls.Add(fullRoiButton);
            actions.Controls.Add(exportButton); actions.Controls.Add(exportTxtButton); actions.Controls.Add(imageButton);
            table.Controls.Add(actions, 0, 14);
            table.SetColumnSpan(actions, 2);
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label note = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                MaximumSize = new Size(370, 0),
                Text = "4096道在MCU本机累计，适合100 kcps高速连续测量；8192/16384/65536道逐事件传输原始16位码，由PC重分道。高道数提高显示粒度，不会提高ADC本身ENOB；FWHM只对显示指标轻度平滑，原始谱与导出数据不改写。",
                ForeColor = Color.FromArgb(80, 85, 90)
            };
            table.Controls.Add(note, 0, 15);
            table.SetColumnSpan(note, 2);

            BuildInputPanel(inputTab);
            BuildTestPanel(testTab);
        }

        private void BuildInputPanel(TabPage page)
        {
            TableLayoutPanel table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 2, RowCount = 17, AutoScroll = true };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            page.Controls.Add(table);

            knownAmplitudeCheck.Text = "已知参考幅度";
            knownAmplitudeCheck.Checked = false;
            knownAmplitudeCheck.CheckedChanged += delegate { UpdateInputConfiguration(); UpdateDerivedMetrics(); };
            AddRow(table, 0, "输入幅度", knownAmplitudeCheck);

            terminationBox.DropDownStyle = ComboBoxStyle.DropDownList;
            terminationBox.Items.AddRange(new object[] { "JP1 OPEN：高阻≈1MΩ", "JP1 SHORT：50Ω端接" });
            terminationBox.SelectedIndex = 0;
            terminationBox.SelectedIndexChanged += delegate { UpdateInputConfiguration(); UpdateDerivedMetrics(); };
            AddRow(table, 1, "实物板端接", terminationBox);

            generatorModeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            generatorModeBox.Items.AddRange(new object[] { "发生器High-Z幅度显示", "发生器50Ω负载显示", "未知/不作幅度换算" });
            generatorModeBox.SelectedIndex = 2;
            generatorModeBox.SelectedIndexChanged += delegate { UpdateInputConfiguration(); UpdateDerivedMetrics(); };
            AddRow(table, 2, "发生器标定", generatorModeBox);

            sourceImpedanceInput.DecimalPlaces = 1; sourceImpedanceInput.Minimum = 1; sourceImpedanceInput.Maximum = 10000; sourceImpedanceInput.Value = 50; sourceImpedanceInput.Increment = 0.1M;
            sourceImpedanceInput.ValueChanged += delegate { UpdateInputConfiguration(); UpdateDerivedMetrics(); };
            AddRow(table, 3, "发生器源阻抗/Ω", sourceImpedanceInput);

            generatorAmplitudeInput.DecimalPlaces = 1; generatorAmplitudeInput.Minimum = 0; generatorAmplitudeInput.Maximum = 5000; generatorAmplitudeInput.Value = 500; generatorAmplitudeInput.Increment = 0.1M;
            generatorAmplitudeInput.ValueChanged += delegate { UpdateInputConfiguration(); UpdateDerivedMetrics(); };
            AddRow(table, 4, "发生器显示/mV", generatorAmplitudeInput);

            knownRateCheck.Text = "已知输入脉冲率";
            knownRateCheck.Checked = false;
            knownRateCheck.CheckedChanged += delegate { UpdateDerivedMetrics(); };
            AddRow(table, 5, "通过率参考", knownRateCheck);
            referenceRateInput.DecimalPlaces = 1; referenceRateInput.Minimum = 0; referenceRateInput.Maximum = 1000000; referenceRateInput.Value = 1000; referenceRateInput.Increment = 0.1M;
            referenceRateInput.ValueChanged += delegate { UpdateDerivedMetrics(); };
            AddRow(table, 6, "参考脉冲率/Hz", referenceRateInput);

            analogGainInput.DecimalPlaces = 4; analogGainInput.Minimum = 0.0001M; analogGainInput.Maximum = 100; analogGainInput.Value = 2.0000M; analogGainInput.Increment = 0.001M;
            analogGainInput.ValueChanged += delegate { UpdateInputConfiguration(); UpdateDerivedMetrics(); };
            AddRow(table, 7, "前端总电压增益", analogGainInput);
            analogOffsetInput.DecimalPlaces = 2; analogOffsetInput.Minimum = -1000; analogOffsetInput.Maximum = 1000; analogOffsetInput.Value = 0; analogOffsetInput.Increment = 0.1M;
            analogOffsetInput.ValueChanged += delegate { UpdateDerivedMetrics(); };
            AddRow(table, 8, "ADC端零点/mV", analogOffsetInput);

            useMeasuredCurveCheck.Text = "启用系统标准线性校准（默认，可编辑）";
            useMeasuredCurveCheck.Checked = true;
            useMeasuredCurveCheck.Enabled = true;
            useMeasuredCurveCheck.CheckedChanged += delegate { UpdateInputConfiguration(); UpdateDerivedMetrics(); };
            table.Controls.Add(useMeasuredCurveCheck, 0, 9); table.SetColumnSpan(useMeasuredCurveCheck, 2);

            linearitySlopeInput.DecimalPlaces = 9; linearitySlopeInput.Minimum = 0.1M; linearitySlopeInput.Maximum = 10M;
            linearitySlopeInput.Value = 1.004148380M; linearitySlopeInput.Increment = 0.000001M;
            linearitySlopeInput.ValueChanged += delegate { UpdateInputConfiguration(); UpdateDerivedMetrics(); };
            AddRow(table, 10, "线性拟合斜率", linearitySlopeInput);
            linearityInterceptInput.DecimalPlaces = 6; linearityInterceptInput.Minimum = -1000M; linearityInterceptInput.Maximum = 1000M;
            linearityInterceptInput.Value = -1.160606M; linearityInterceptInput.Increment = 0.001M;
            linearityInterceptInput.ValueChanged += delegate { UpdateInputConfiguration(); UpdateDerivedMetrics(); };
            AddRow(table, 11, "线性拟合截距/mV", linearityInterceptInput);

            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
            buttons.Controls.Add(SmallButton("重新计算", delegate { UpdateInputConfiguration(); UpdateDerivedMetrics(); }));
            Button defaultGainButton = SmallButton("恢复默认 2.0000", delegate
            {
                analogGainInput.Value = 2.0000M;
                UpdateInputConfiguration();
                UpdateDerivedMetrics();
            });
            buttons.Controls.Add(defaultGainButton);
            buttons.Controls.Add(SmallButton("恢复系统标定", delegate
            {
                linearitySlopeInput.Value = 1.004148380M;
                linearityInterceptInput.Value = -1.160606M;
                useMeasuredCurveCheck.Checked = true;
            }));
            table.Controls.Add(buttons, 0, 12); table.SetColumnSpan(buttons, 2);

            impedanceSummary.AutoSize = true; impedanceSummary.MaximumSize = new Size(365, 0); impedanceSummary.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            table.Controls.Add(impedanceSummary, 0, 13); table.SetColumnSpan(impedanceSummary, 2);
            correctionSummary.AutoSize = true; correctionSummary.MaximumSize = new Size(365, 0); correctionSummary.ForeColor = Color.FromArgb(35, 75, 110);
            table.Controls.Add(correctionSummary, 0, 14); table.SetColumnSpan(correctionSummary, 2);

            showProtocolCheck.Text = "显示周期性status/hist协议日志";
            showProtocolCheck.Checked = false;
            showProtocolCheck.CheckedChanged += delegate
            {
                if (showProtocolCheck.Checked && logLevelBox.SelectedIndex != 2) logLevelBox.SelectedIndex = 2;
                else if (!showProtocolCheck.Checked && logLevelBox.SelectedIndex == 2) logLevelBox.SelectedIndex = 1;
            };
            table.Controls.Add(showProtocolCheck, 0, 15); table.SetColumnSpan(showProtocolCheck, 2);
            Label warning = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(365, 0),
                Text = "重要：此处选择只负责换算和写入报告，不能电控JP1。软件选项必须与板上跳帽一致。50Ω同轴线不等于已做50Ω端接。",
                BackColor = Color.FromArgb(255, 235, 220),
                ForeColor = Color.Firebrick,
                Padding = new Padding(5)
            };
            table.Controls.Add(warning, 0, 16); table.SetColumnSpan(warning, 2);
            UpdateInputConfiguration();
        }

        private void BuildTestPanel(TabPage page)
        {
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(7) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            page.Controls.Add(layout);

            linearitySummary.Dock = DockStyle.Fill;
            linearitySummary.AutoSize = false;
            linearitySummary.Text = "测试点不足。幅度扫描至少记录3个已知幅度点后计算斜率、R²、1-R²和最大非线性。";
            linearitySummary.ForeColor = Color.FromArgb(35, 75, 110);
            layout.Controls.Add(linearitySummary, 0, 0);

            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
            buttons.Controls.Add(SmallButton("记录当前测试点", delegate { CaptureTestPoint(); }));
            buttons.Controls.Add(SmallButton("删除最后一点", delegate { RemoveLastTestPoint(); }));
            buttons.Controls.Add(SmallButton("清空测试点", delegate { ClearTestPoints(); }));
            buttons.Controls.Add(SmallButton("导出报告CSV", delegate { ExportTestReport(false); }));
            buttons.Controls.Add(SmallButton("导出报告TXT", delegate { ExportTestReport(true); }));
            layout.Controls.Add(buttons, 0, 1);

            testPointGrid.Dock = DockStyle.Fill;
            testPointGrid.ReadOnly = true;
            testPointGrid.AllowUserToAddRows = false;
            testPointGrid.AllowUserToDeleteRows = false;
            testPointGrid.RowHeadersVisible = false;
            testPointGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
            testPointGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            testPointGrid.BackgroundColor = Surface;
            testPointGrid.BorderStyle = BorderStyle.FixedSingle;
            testPointGrid.GridColor = Border;
            testPointGrid.EnableHeadersVisualStyles = false;
            testPointGrid.ColumnHeadersDefaultCellStyle.BackColor = Navy;
            testPointGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            testPointGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            testPointGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Navy;
            testPointGrid.DefaultCellStyle.BackColor = Surface;
            testPointGrid.DefaultCellStyle.ForeColor = TextPrimary;
            testPointGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(212, 237, 235);
            testPointGrid.DefaultCellStyle.SelectionForeColor = TextPrimary;
            testPointGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(247, 250, 252);
            testPointGrid.Columns.Add("Index", "#");
            testPointGrid.Columns.Add("Input", "实际输入mV");
            testPointGrid.Columns.Add("Measured", "测得输入mV");
            testPointGrid.Columns.Add("Resolution", "分辨率%");
            testPointGrid.Columns.Add("Pass", "通过率%");
            testPointGrid.Columns.Add("Rate", "计数率Hz");
            layout.Controls.Add(testPointGrid, 0, 2);
        }

        private void BuildChart(TableLayoutPanel parent)
        {
            TableLayoutPanel host = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0), BackColor = Surface };
            host.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            host.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            FlowLayoutPanel toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(10, 6, 8, 3), BackColor = Surface };
            toolbar.Controls.Add(new Label { Text = "谱图视图", AutoSize = true, ForeColor = TextMuted, Margin = new Padding(0, 6, 8, 0) });
            cursorPeakSnapCheck.Text = "游标吸附峰值";
            cursorPeakSnapCheck.Checked = true;
            cursorPeakSnapCheck.AutoSize = true;
            cursorPeakSnapCheck.ForeColor = TextPrimary;
            cursorPeakSnapCheck.Margin = new Padding(0, 5, 0, 0);
            cursorPeakSnapCheck.CheckedChanged += delegate { spectrumCursorPinned = false; HideSpectrumCursor(); };
            toolbar.Controls.Add(cursorPeakSnapCheck);
            toolbar.Controls.Add(new Label { Text = "显示", AutoSize = true, ForeColor = TextMuted, Margin = new Padding(12, 6, 5, 0) });
            ConfigureViewInput(viewStartInput, 0);
            ConfigureViewInput(viewEndInput, activeChannels - 1);
            toolbar.Controls.Add(viewStartInput);
            toolbar.Controls.Add(new Label { Text = "–", AutoSize = true, ForeColor = TextMuted, Margin = new Padding(4, 6, 4, 0) });
            toolbar.Controls.Add(viewEndInput);
            toolbar.Controls.Add(SmallButton("应用范围", delegate { ApplySpectrumViewFromInputs(); }));
            toolbar.Controls.Add(SmallButton("峰区放大", delegate { ZoomToMainPeak(); }));
            toolbar.Controls.Add(SmallButton("ROI范围", delegate { ApplySpectrumView(Decimal.ToInt32(roiStartInput.Value), Decimal.ToInt32(roiEndInput.Value) + 1); }));
            toolbar.Controls.Add(SmallButton("全谱", delegate { ResetSpectrumView(); }));
            host.Controls.Add(toolbar, 0, 0);

            spectrumChart.Dock = DockStyle.Fill;
            spectrumChart.BackColor = Surface;
            spectrumChart.BorderlineColor = Border;
            spectrumChart.BorderlineDashStyle = ChartDashStyle.Solid;
            spectrumChart.BorderlineWidth = 1;
            spectrumChart.AntiAliasing = AntiAliasingStyles.All;
            ChartArea area = new ChartArea("Spectrum");
            area.BackColor = Surface;
            area.Position = new ElementPosition(7F, 11F, 90F, 80F);
            area.AxisX.Minimum = 0;
            area.AxisX.Maximum = activeChannels;
            area.AxisX.Interval = 512;
            area.AxisX.Title = "AD7980能谱道址（0–4095；2.5 V参考）";
            area.AxisY.Title = "计数";
            area.AxisY.Minimum = 0.0;
            area.AxisY.Maximum = 1.0;
            area.AxisX.MajorGrid.LineColor = Color.FromArgb(231, 237, 241);
            area.AxisY.MajorGrid.LineColor = Color.FromArgb(231, 237, 241);
            area.AxisX.LineColor = Color.FromArgb(119, 136, 148);
            area.AxisY.LineColor = Color.FromArgb(119, 136, 148);
            area.AxisX.LabelStyle.ForeColor = TextMuted;
            area.AxisY.LabelStyle.ForeColor = TextMuted;
            area.AxisX.TitleForeColor = TextMuted;
            area.AxisY.TitleForeColor = TextMuted;
            area.AxisX.MajorTickMark.LineColor = Color.FromArgb(119, 136, 148);
            area.AxisY.MajorTickMark.LineColor = Color.FromArgb(119, 136, 148);
            spectrumChart.ChartAreas.Add(area);
            Series series = new Series("能谱") { ChartType = SeriesChartType.FastLine, Color = Teal, BorderWidth = 2 };
            series.EmptyPointStyle.Color = Color.Transparent;
            // A zero-count spectrum still needs two valid points; otherwise the legacy
            // WinForms chart control postpones painting axes until the first event.
            series.Points.AddXY(0.0, 0.0);
            series.Points.AddXY(activeChannels - 1.0, 0.0);
            spectrumChart.Series.Add(series);
            Title title = new Title("实时4096道能谱（MCU本机累计，高速推荐）", Docking.Top, new Font("Microsoft YaHei UI", 13F, FontStyle.Bold), Navy);
            spectrumChart.Titles.Add(title);
            ConfigureSpectrumCursor();
            host.Controls.Add(spectrumChart, 0, 1);
            parent.Controls.Add(host, 0, 0);
        }

        private static void ConfigureViewInput(NumericUpDown input, int value)
        {
            input.Minimum = 0;
            input.Maximum = 65535;
            input.Value = value;
            input.Width = 76;
            input.ThousandsSeparator = true;
            input.Increment = 1;
            input.Margin = new Padding(0, 1, 0, 0);
        }

        private void ConfigureSpectrumCursor()
        {
            spectrumCursorCard.Size = new Size(390, 149);
            spectrumCursorCard.BackColor = Surface;
            spectrumCursorCard.Visible = false;
            spectrumCursorCard.Enabled = false;
            spectrumCursorCard.Paint += SpectrumCursorCardPaint;
            spectrumChart.Controls.Add(spectrumCursorCard);
            spectrumCursorCard.BringToFront();

            spectrumChart.MouseMove += SpectrumChartMouseMove;
            spectrumChart.MouseLeave += delegate
            {
                Point client = spectrumChart.PointToClient(System.Windows.Forms.Cursor.Position);
                if (!spectrumCursorPinned && !spectrumChart.ClientRectangle.Contains(client)) HideSpectrumCursor();
            };
            spectrumChart.MouseClick += SpectrumChartMouseClick;
            spectrumChart.MouseEnter += delegate { spectrumChart.Focus(); };
            spectrumChart.MouseWheel += SpectrumChartMouseWheel;
            spectrumChart.PostPaint += SpectrumChartPostPaint;
        }

        private void SpectrumChartMouseWheel(object sender, MouseEventArgs e)
        {
            RectangleF plot = SpectrumPlotRectangle();
            if (!plot.Contains(e.Location) || e.Delta == 0) return;
            int span = Math.Max(2, viewEndChannelExclusive - viewStartChannel);
            int minimumSpan = Math.Max(8, activeChannels / 8192);
            int newSpan = e.Delta > 0 ? Math.Max(minimumSpan, (int)Math.Round(span * 0.72))
                : Math.Min(activeChannels, (int)Math.Round(span / 0.72));
            double ratio = Math.Max(0.0, Math.Min(1.0, (e.X - plot.Left) / Math.Max(1.0, plot.Width)));
            double anchor = viewStartChannel + ratio * span;
            int start = (int)Math.Round(anchor - ratio * newSpan);
            ApplySpectrumView(start, start + newSpan);
        }

        private RectangleF SpectrumPlotRectangle()
        {
            if (spectrumChart.ChartAreas.Count == 0 || spectrumChart.ClientSize.Width <= 0 || spectrumChart.ClientSize.Height <= 0)
                return RectangleF.Empty;
            ChartArea area = spectrumChart.ChartAreas[0];
            ElementPosition outer = area.Position;
            ElementPosition inner = area.InnerPlotPosition;
            float left = outer.X + outer.Width * inner.X / 100F;
            float top = outer.Y + outer.Height * inner.Y / 100F;
            float width = outer.Width * inner.Width / 100F;
            float height = outer.Height * inner.Height / 100F;
            RectangleF calculated = new RectangleF(left * spectrumChart.ClientSize.Width / 100F,
                top * spectrumChart.ClientSize.Height / 100F,
                width * spectrumChart.ClientSize.Width / 100F,
                height * spectrumChart.ClientSize.Height / 100F);
            if (calculated.Width >= 20F && calculated.Height >= 20F) return calculated;

            // ChartArea auto-layout can briefly report a zero InnerPlotPosition during
            // first paint or resize. Keep hover usable and contained during that frame.
            float fallbackLeft = Math.Max(58F, spectrumChart.ClientSize.Width * 0.075F);
            float fallbackTop = Math.Max(44F, spectrumChart.ClientSize.Height * 0.075F);
            float fallbackRight = Math.Max(24F, spectrumChart.ClientSize.Width * 0.025F);
            float fallbackBottom = Math.Max(62F, spectrumChart.ClientSize.Height * 0.13F);
            return new RectangleF(fallbackLeft, fallbackTop,
                Math.Max(20F, spectrumChart.ClientSize.Width - fallbackLeft - fallbackRight),
                Math.Max(20F, spectrumChart.ClientSize.Height - fallbackTop - fallbackBottom));
        }

        private void SpectrumChartMouseMove(object sender, MouseEventArgs e)
        {
            if (spectrumCursorPinned) return;
            RectangleF plot = SpectrumPlotRectangle();
            if (!plot.Contains(e.Location))
            {
                HideSpectrumCursor();
                return;
            }
            if (DateTime.UtcNow < nextSpectrumCursorPaint && Math.Abs(e.X - spectrumCursorPoint.X) < 3) return;
            nextSpectrumCursorPaint = DateTime.UtcNow.AddMilliseconds(32);
            UpdateSpectrumCursor(e.Location);
        }

        private void SpectrumChartMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                spectrumCursorPinned = false;
                HideSpectrumCursor();
                return;
            }
            if (e.Button != MouseButtons.Left || !SpectrumPlotRectangle().Contains(e.Location)) return;
            if (!spectrumCursorPinned) UpdateSpectrumCursor(e.Location);
            spectrumCursorPinned = !spectrumCursorPinned;
            spectrumCursorCard.Invalidate();
            spectrumChart.Invalidate();
        }

        private void UpdateSpectrumCursor(Point point)
        {
            try
            {
                ChartArea area = spectrumChart.ChartAreas[0];
                double xValue;
                try { xValue = area.AxisX.PixelPositionToValue(point.X); }
                catch
                {
                    RectangleF plot = SpectrumPlotRectangle();
                    xValue = viewStartChannel + (point.X - plot.Left) * (viewEndChannelExclusive - viewStartChannel) / Math.Max(1.0, plot.Width);
                }
                int channel = Math.Max(0, Math.Min(activeChannels - 1, (int)Math.Floor(xValue)));
                if (cursorPeakSnapCheck.Checked)
                {
                    RectangleF plot = SpectrumPlotRectangle();
                    int visibleSpan = Math.Max(1, viewEndChannelExclusive - viewStartChannel);
                    int radius = Math.Max(2, (int)Math.Ceiling(visibleSpan * 14.0 / Math.Max(80.0, plot.Width)));
                    channel = FindLocalPeak(spectrum, channel, radius, viewStartChannel, viewEndChannelExclusive);
                }
                spectrumCursorPoint = point;
                spectrumCursorChannel = channel;
                spectrumCursorReading = SpectrumCursorReading.FromChannel(channel, activeChannels);
                spectrumCursorPeak = CalculateCursorPeakMetrics(spectrum, channel, viewStartChannel, viewEndChannelExclusive);
                spectrumCursorVisible = true;
                PositionSpectrumCursorCard(point);
                spectrumCursorCard.Visible = true;
                spectrumCursorCard.BringToFront();
                spectrumCursorCard.Invalidate();
                spectrumChart.Invalidate();
            }
            catch
            {
                HideSpectrumCursor();
            }
        }

        private void PositionSpectrumCursorCard(Point pointer)
        {
            RectangleF plot = SpectrumPlotRectangle();
            int gap = 16;
            int x = pointer.X + gap;
            int y = pointer.Y + gap;
            if (x + spectrumCursorCard.Width > plot.Right - 6) x = pointer.X - spectrumCursorCard.Width - gap;
            if (y + spectrumCursorCard.Height > plot.Bottom - 6) y = pointer.Y - spectrumCursorCard.Height - gap;
            int minX = Math.Max(6, (int)Math.Ceiling(plot.Left + 6));
            int minY = Math.Max(6, (int)Math.Ceiling(plot.Top + 6));
            int maxX = Math.Max(minX, Math.Min(spectrumChart.ClientSize.Width - spectrumCursorCard.Width - 6,
                (int)Math.Floor(plot.Right - spectrumCursorCard.Width - 6)));
            int maxY = Math.Max(minY, Math.Min(spectrumChart.ClientSize.Height - spectrumCursorCard.Height - 6,
                (int)Math.Floor(plot.Bottom - spectrumCursorCard.Height - 6)));
            spectrumCursorCard.Location = new Point(Math.Max(minX, Math.Min(maxX, x)), Math.Max(minY, Math.Min(maxY, y)));
        }

        private void HideSpectrumCursor()
        {
            if (!spectrumCursorVisible && !spectrumCursorCard.Visible) return;
            spectrumCursorVisible = false;
            spectrumCursorChannel = -1;
            spectrumCursorPeak = null;
            spectrumCursorCard.Visible = false;
            spectrumChart.Invalidate();
        }

        private void SpectrumCursorCardPaint(object sender, PaintEventArgs e)
        {
            if (spectrumCursorReading == null || spectrumCursorChannel < 0) return;
            Graphics g = e.Graphics;
            g.Clear(Surface);
            using (Pen borderPen = new Pen(Border)) g.DrawRectangle(borderPen, 0, 0, spectrumCursorCard.Width - 1, spectrumCursorCard.Height - 1);
            using (SolidBrush accent = new SolidBrush(Teal)) g.FillRectangle(accent, 0, 0, 4, spectrumCursorCard.Height);

            SpectrumCursorReading r = spectrumCursorReading;
            long count = spectrum[r.Channel];
            bool inRoi = r.Channel >= Decimal.ToInt32(roiStartInput.Value) && r.Channel <= Decimal.ToInt32(roiEndInput.Value);
            double boardMv = BoardInputFromAdcMv(r.AdcCenterMv);
            double sourceMv = Double.IsNaN(boardMv) ? Double.NaN : SourceEquivalentFromBoardMv(boardMv);
            string mode = spectrumCursorPinned ? "已锁定" : (cursorPeakSnapCheck.Checked ? "峰值吸附" : "自由游标");
            using (Font titleFont = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold))
            using (Font modeFont = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, "谱图读数", titleFont, new Point(15, 9), Navy);
                Size modeSize = TextRenderer.MeasureText(mode, modeFont);
                Rectangle modeRect = new Rectangle(spectrumCursorCard.Width - modeSize.Width - 23, 8, modeSize.Width + 12, 23);
                using (SolidBrush soft = new SolidBrush(TealSoft)) g.FillRectangle(soft, modeRect);
                TextRenderer.DrawText(g, mode, modeFont, modeRect, Teal,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, string.Format(CultureInfo.InvariantCulture, "Ch {0:N0}    计数 {1:N0}", r.Channel, count),
                    titleFont, new Point(15, 37), TextPrimary);
            }
            TextRenderer.DrawText(g, string.Format(CultureInfo.InvariantCulture, "ADC {0:F3} mV    道宽 {1:F3} µV", r.AdcCenterMv, r.ChannelWidthUv),
                Font, new Point(15, 61), TextPrimary);
            string inputText = Double.IsNaN(boardMv)
                ? "板端 --    源端等效 --"
                : string.Format(CultureInfo.InvariantCulture, "板端 {0:F3} mV    源端等效 {1:F3} mV", boardMv, sourceMv);
            TextRenderer.DrawText(g, inputText, Font, new Point(15, 82), TextPrimary);
            string peakText = spectrumCursorPeak != null && spectrumCursorPeak.PeakCount > 0
                ? string.Format(CultureInfo.InvariantCulture, "局部峰 Ch{0:N0}  ·  FWHM {1:F2} ch  ·  R {2:F3}%",
                    spectrumCursorPeak.PeakChannel, spectrumCursorPeak.FwhmChannels, spectrumCursorPeak.ResolutionPercent)
                : "局部峰 --  ·  FWHM --  ·  R --";
            TextRenderer.DrawText(g, peakText, Font, new Point(15, 103), TextPrimary);
            string raw = r.RawStart == r.RawEnd ? r.RawStart.ToString(CultureInfo.InvariantCulture)
                : r.RawStart.ToString(CultureInfo.InvariantCulture) + "–" + r.RawEnd.ToString(CultureInfo.InvariantCulture);
            using (Font detailFont = new Font("Microsoft YaHei UI", 8.5F))
                TextRenderer.DrawText(g, "Raw " + raw + "  ·  " + (inRoi ? "ROI 内" : "ROI 外") + "  ·  单击锁定 / 右键隐藏",
                    detailFont, new Point(15, 126), TextMuted);
        }

        internal static int FindLocalPeak(long[] bins, int requested, int radius, int minimum, int maximumExclusive)
        {
            if (bins == null || bins.Length == 0) return 0;
            minimum = Math.Max(0, minimum);
            maximumExclusive = Math.Min(bins.Length, Math.Max(minimum + 1, maximumExclusive));
            requested = Math.Max(minimum, Math.Min(maximumExclusive - 1, requested));
            int first = Math.Max(minimum, requested - Math.Max(0, radius));
            int last = Math.Min(maximumExclusive - 1, requested + Math.Max(0, radius));
            int best = requested;
            long bestCount = bins[best];
            for (int i = first; i <= last; i++)
            {
                if (bins[i] > bestCount || (bins[i] == bestCount && Math.Abs(i - requested) < Math.Abs(best - requested)))
                {
                    best = i;
                    bestCount = bins[i];
                }
            }
            return best;
        }

        internal static CursorPeakMetrics CalculateCursorPeakMetrics(long[] bins, int peak, int minimum, int maximumExclusive)
        {
            if (bins == null || peak < 0 || peak >= bins.Length || bins[peak] <= 0) return null;
            minimum = Math.Max(0, minimum);
            maximumExclusive = Math.Min(bins.Length, maximumExclusive);
            double half = bins[peak] * 0.5;
            int left = peak;
            while (left > minimum && bins[left] >= half) left--;
            int right = peak;
            while (right < maximumExclusive - 1 && bins[right] >= half) right++;
            if (left == minimum && bins[left] >= half) return new CursorPeakMetrics { PeakChannel = peak, PeakCount = bins[peak] };
            if (right == maximumExclusive - 1 && bins[right] >= half) return new CursorPeakMetrics { PeakChannel = peak, PeakCount = bins[peak] };
            double leftCross = left;
            double leftRise = bins[left + 1] - bins[left];
            if (Math.Abs(leftRise) > Double.Epsilon) leftCross = left + (half - bins[left]) / leftRise;
            double rightCross = right;
            if (right > peak)
            {
                double rightFall = bins[right - 1] - bins[right];
                if (Math.Abs(rightFall) > Double.Epsilon) rightCross = (right - 1) + (bins[right - 1] - half) / rightFall;
            }
            double width = Math.Max(0.0, rightCross - leftCross);
            return new CursorPeakMetrics
            {
                PeakChannel = peak,
                PeakCount = bins[peak],
                FwhmChannels = width,
                ResolutionPercent = peak > 0 && width > 0 ? width / (peak + 0.5) * 100.0 : 0.0
            };
        }

        private void SpectrumChartPostPaint(object sender, ChartPaintEventArgs e)
        {
            if (!spectrumCursorVisible || spectrumCursorReading == null || !(e.ChartElement is ChartArea)) return;
            ChartArea area = (ChartArea)e.ChartElement;
            if (area.Name != "Spectrum") return;
            try
            {
                RectangleF plot = SpectrumPlotRectangle();
                float x = (float)area.AxisX.ValueToPixelPosition(spectrumCursorReading.Channel + 0.5);
                using (Pen shadow = new Pen(Color.FromArgb(90, Teal), 3F)) e.ChartGraphics.Graphics.DrawLine(shadow, x, plot.Top, x, plot.Bottom);
                using (Pen line = new Pen(Color.White, 1F)) e.ChartGraphics.Graphics.DrawLine(line, x, plot.Top, x, plot.Bottom);
                long count = spectrum[spectrumCursorReading.Channel];
                if (count > 0 && (!area.AxisY.IsLogarithmic || count >= 1))
                {
                    float y = (float)area.AxisY.ValueToPixelPosition(count);
                    if (y >= plot.Top && y <= plot.Bottom)
                    {
                        using (SolidBrush white = new SolidBrush(Color.White)) e.ChartGraphics.Graphics.FillEllipse(white, x - 4, y - 4, 8, 8);
                        using (Pen marker = new Pen(Teal, 2F)) e.ChartGraphics.Graphics.DrawEllipse(marker, x - 4, y - 4, 8, 8);
                    }
                }
            }
            catch { }
        }

        private void BuildMetrics(TableLayoutPanel parent)
        {
            TableLayoutPanel metrics = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 3, BackColor = Canvas, Padding = new Padding(0, 7, 0, 0), Margin = new Padding(0) };
            for (int i = 0; i < 5; i++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            for (int i = 0; i < 3; i++) metrics.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333F));
            AddMetric(metrics, 0, 0, "有效样本", samplesValue, "MCU成功完成16位读取并纳入能谱的事件累计数。它不是USB输出行数；抽样输出不会减少本机能谱计数。");
            AddMetric(metrics, 1, 0, "触发/Busy", busyValue, "AD7980转换完成/BUSY事件累计数。队列表示待处理事件；恢复表示固件检测到异常电平后执行的安全恢复次数。");
            AddMetric(metrics, 2, 0, "实测计数率", rateValue, "同一MCU时间窗内计算：R=ΔN_sample/Δt_MCU，单位cps。计数器清零或重启后的首帧只建立新基线，不参与速率计算。");
            AddMetric(metrics, 3, 0, "Overrun", overrunValue, "前一事件尚未处理完成时又到达转换完成事件的累计次数。理想值为0；它衡量采集实时性，不等同于USB丢行。");
            AddMetric(metrics, 4, 0, "USB输出丢行", dropsValue, "固件发送失败、流序号缺口和USB恢复的诊断量。MCU本机直方图模式下，USB日志丢行通常不等于ADC样本丢失。");
            AddMetric(metrics, 0, 1, "均值", meanValue, "固件累计样本电压的算术平均值：mean=(ΣV_i)/N。多峰谱时它不是主峰峰位。");
            AddMetric(metrics, 1, 1, "峰位（ADC端）", peakValue, "ROI内扣除线性背景后质心：μ=Σ[ch_i·max(N_i-B_i,0)]/Σmax(N_i-B_i,0)，再按ADC满量程换算为mV。");
            AddMetric(metrics, 2, 1, "FWHM", fwhmValue, "主峰左右半高交点的插值距离。仅指标计算可使用轻度平滑；保存和导出的原始道计数不被修改。");
            AddMetric(metrics, 3, 1, "分辨率", resolutionValue, "能量分辨率近似：η=FWHM/峰位×100%。同一线性刻度下也等于FWHM道数/质心道址×100%；越小越好，不等于ADC的LSB或ENOB。");
            AddMetric(metrics, 4, 1, "源端等效 / 板端输入", inputPeakValue, "先由ADC峰值按可编辑增益和零点反算板端输入；若启用线性校准，再用x=(y-b)/k修正。源端等效值还依据发生器源阻抗和JP1实际端接换算。");
            AddMetric(metrics, 0, 2, "幅度精度", accuracyValue, "已知参考幅度时：|V_meas−V_ref|/V_ref×100%。未知输入、映射不兼容或ADC溢出时不报告该量。");
            AddMetric(metrics, 1, 2, "统计精度", statisticalValue, "质心统计不确定度近似：σ_mean/μ≈σ_peak/(sqrt(N_net)·μ)×100%。它只描述有限计数统计误差，不含系统非线性、漂移或发生器误差。");
            AddMetric(metrics, 2, 2, "脉冲通过率", passRateValue, "同一同步窗口：P=ΔN_sample/(f_ref·Δt_MCU)×100%=R_meas/f_ref×100%。要求参考频率真实、窗口≥1s且期间计数器未复位；软件不钳位结果。");
            AddMetric(metrics, 3, 2, "处理效率", processingValue, "ε_proc=ΔN_sample/ΔN_busy×100%，衡量已触发事件中被固件成功读取的比例。它与相对外部发生器频率的通过率是两个不同物理量。");
            AddMetric(metrics, 4, 2, "测量时长", elapsedValue, "从最近一次同步基线到当前状态帧的MCU uptime差值。清空统计、切换测试模式或检测到计数器回退后重新起算。");
            parent.Controls.Add(metrics, 0, 1);
        }

        private void AddMetric(TableLayoutPanel panel, int column, int row, string caption, Label value, string explanation)
        {
            FlowLayoutPanel cell = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(4),
                Padding = new Padding(9, 6, 8, 5),
                Cursor = Cursors.Help,
                BackColor = Surface,
                BorderStyle = BorderStyle.None
            };
            cell.Paint += delegate(object sender, PaintEventArgs args)
            {
                Rectangle bounds = new Rectangle(0, 0, Math.Max(0, cell.ClientSize.Width - 1), Math.Max(0, cell.ClientSize.Height - 1));
                using (Pen borderPen = new Pen(Border)) args.Graphics.DrawRectangle(borderPen, bounds);
                using (Brush accentBrush = new SolidBrush(Teal)) args.Graphics.FillRectangle(accentBrush, 0, 0, 3, cell.ClientSize.Height);
            };
            Label title = new Label { AutoSize = true, Text = caption, ForeColor = TextMuted, Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular), Cursor = Cursors.Help, Margin = new Padding(0) };
            cell.Controls.Add(title);
            cell.Controls.Add(value);
            panel.Controls.Add(cell, column, row);
            FormulaSpec formula = CreateFormulaSpec(caption, explanation);
            RegisterFormulaTip(cell, formula);
            RegisterFormulaTip(title, formula);
            RegisterFormulaTip(value, formula);
            EventHandler show = delegate { HideFormulaHoverCard(); ShowFormulaDetails(formula); };
            cell.Click += show; title.Click += show; value.Click += show;
            EventHandler enter = delegate { cell.BackColor = TealSoft; };
            EventHandler leave = delegate { cell.BackColor = Surface; };
            cell.MouseEnter += enter; title.MouseEnter += enter; value.MouseEnter += enter;
            cell.MouseLeave += leave; title.MouseLeave += leave; value.MouseLeave += leave;
        }

        private void RegisterFormulaTip(Control control, FormulaSpec formula)
        {
            metricFormulae[control] = formula;
            control.MouseEnter += FormulaControlMouseEnter;
            control.MouseLeave += FormulaControlMouseLeave;
        }

        private void ConfigureFormulaHoverCard()
        {
            formulaHoverCard.Size = new Size(610, 205);
            formulaHoverCard.Visible = false;
            formulaHoverCard.BackColor = Color.White;
            formulaHoverCard.Cursor = Cursors.Hand;
            formulaHoverCard.Paint += delegate(object sender, PaintEventArgs args)
            {
                if (visibleFormula != null) DrawFormulaCard(args.Graphics, formulaHoverCard.ClientRectangle, visibleFormula, false);
            };
            formulaHoverCard.MouseEnter += delegate { formulaHoverTimer.Stop(); };
            formulaHoverCard.MouseLeave += delegate { BeginInvoke((MethodInvoker)HideFormulaHoverCardUnlessPointerInside); };
            formulaHoverCard.Click += delegate
            {
                FormulaSpec selected = visibleFormula;
                HideFormulaHoverCard();
                if (selected != null) ShowFormulaDetails(selected);
            };
            Controls.Add(formulaHoverCard);
            formulaHoverCard.BringToFront();
            formulaHoverTimer.Interval = 850;
            formulaHoverTimer.Tick += delegate
            {
                formulaHoverTimer.Stop();
                ShowPendingFormulaHoverCard();
            };
        }

        private void FormulaControlMouseEnter(object sender, EventArgs e)
        {
            Control control = sender as Control;
            FormulaSpec formula;
            if (control == null || !metricFormulae.TryGetValue(control, out formula)) return;
            pendingFormulaControl = control;
            pendingFormula = formula;
            if (visibleFormula == formula)
            {
                formulaHoverTimer.Stop();
                return;
            }
            formulaHoverTimer.Stop();
            formulaHoverTimer.Start();
        }

        private void FormulaControlMouseLeave(object sender, EventArgs e)
        {
            FormulaSpec formula;
            Control control = sender as Control;
            if (control == null || !metricFormulae.TryGetValue(control, out formula)) return;
            BeginInvoke((MethodInvoker)delegate
            {
                if (!IsPointerOverFormula(formula))
                {
                    if (pendingFormula == formula)
                    {
                        formulaHoverTimer.Stop();
                        pendingFormula = null;
                        pendingFormulaControl = null;
                    }
                    if (visibleFormula == formula) HideFormulaHoverCard();
                }
            });
        }

        private bool IsPointerOverFormula(FormulaSpec formula)
        {
            Point screenPoint = System.Windows.Forms.Cursor.Position;
            foreach (KeyValuePair<Control, FormulaSpec> pair in metricFormulae)
            {
                if (pair.Value == formula && pair.Key.Visible && pair.Key.RectangleToScreen(pair.Key.ClientRectangle).Contains(screenPoint)) return true;
            }
            return formulaHoverCard.Visible && formulaHoverCard.RectangleToScreen(formulaHoverCard.ClientRectangle).Contains(screenPoint);
        }

        private Control FindFormulaAnchor(Control source, FormulaSpec formula)
        {
            Control anchor = source;
            while (anchor != null && anchor.Parent != null)
            {
                FormulaSpec parentFormula;
                if (!metricFormulae.TryGetValue(anchor.Parent, out parentFormula) || parentFormula != formula) break;
                anchor = anchor.Parent;
            }
            return anchor ?? source;
        }

        private void ShowPendingFormulaHoverCard()
        {
            if (pendingFormula == null || pendingFormulaControl == null || !IsPointerOverFormula(pendingFormula)) return;
            Control anchor = FindFormulaAnchor(pendingFormulaControl, pendingFormula);
            ShowFormulaHoverCard(anchor, pendingFormula);
        }

        private void ShowFormulaHoverCard(Control anchor, FormulaSpec formula)
        {
            Rectangle anchorBounds = RectangleToClient(anchor.RectangleToScreen(anchor.ClientRectangle));
            const int edge = 12;
            const int gap = 10;
            int x = anchorBounds.Left;
            int y = anchorBounds.Bottom + gap;

            if (x + formulaHoverCard.Width > ClientSize.Width - edge)
                x = anchorBounds.Right - formulaHoverCard.Width;
            if (y + formulaHoverCard.Height > ClientSize.Height - edge)
                y = anchorBounds.Top - formulaHoverCard.Height - gap;

            x = Math.Max(edge, Math.Min(x, Math.Max(edge, ClientSize.Width - formulaHoverCard.Width - edge)));
            y = Math.Max(edge, Math.Min(y, Math.Max(edge, ClientSize.Height - formulaHoverCard.Height - edge)));
            visibleFormula = formula;
            formulaHoverCard.Location = new Point(x, y);
            formulaHoverCard.Visible = true;
            formulaHoverCard.BringToFront();
            formulaHoverCard.Invalidate();
        }

        private void HideFormulaHoverCardUnlessPointerInside()
        {
            if (visibleFormula == null || !IsPointerOverFormula(visibleFormula)) HideFormulaHoverCard();
        }

        private void HideFormulaHoverCard()
        {
            formulaHoverTimer.Stop();
            formulaHoverCard.Visible = false;
            visibleFormula = null;
            pendingFormula = null;
            pendingFormulaControl = null;
        }

        private static FormulaSpec CreateFormulaSpec(string caption, string explanation)
        {
            switch (caption)
            {
                case "有效样本": return new FormulaSpec(caption, "N =", "Nsample(t) − Nsample(t₀)", null, "", "Nsample：固件成功读取并计入能谱的事件数", explanation);
                case "触发/Busy": return new FormulaSpec(caption, "Nbusy =", "Nbusy(t) − Nbusy(t₀)", null, "", "Nbusy：检测到的 ADC Busy/转换完成事件数", explanation);
                case "实测计数率": return new FormulaSpec(caption, "Rmeas =", "ΔNsample", "ΔtMCU", "Hz", "ΔtMCU：同一状态窗口内的 MCU 运行时间差", explanation);
                case "Overrun": return new FormulaSpec(caption, "Noverrun =", "Σ（来不及处理的触发事件）", null, "", "理想值为 0；它不是 USB 丢包数", explanation);
                case "USB输出丢行": return new FormulaSpec(caption, "Nloss =", "Ntx_drop + Nstream_gap", null, "", "分别表示设备发送失败与 PC 检出的序号缺口", explanation);
                case "均值": return new FormulaSpec(caption, "V̄ =", "Σ Ni · Vi", "Σ Ni", "", "Ni：第 i 道计数；Vi：第 i 道对应的 ADC 电压", explanation);
                case "峰位（ADC端）": return new FormulaSpec(caption, "μch =", "Σ i · max(Ni − Bi, 0)", "Σ max(Ni − Bi, 0)", "", "Bi：ROI 内估计的线性背景；峰值电压由 μch 按满量程换算", explanation);
                case "FWHM": return new FormulaSpec(caption, "FWHM =", "xR(H/2) − xL(H/2)", null, "ch", "H：扣除背景后的峰高；xL、xR：半高交点插值位置", explanation);
                case "分辨率": return new FormulaSpec(caption, "R =", "FWHMch", "μch", "× 100 %", "同一线性标度下，道址比值等价于能量比值；越小越好", explanation);
                case "源端等效 / 板端输入": return new FormulaSpec(caption, "Vboard =", "VADC − V0", "G", "", "若启用线性校准：Vcorrected=(Vboard−b)/k；源端值再按端接关系换算", explanation);
                case "幅度精度": return new FormulaSpec(caption, "εA =", "|Vmeas − Vref|", "Vref", "× 100 %", "只在参考幅度已知且量程、映射有效时报告", explanation);
                case "统计精度": return new FormulaSpec(caption, "uμ / μ ≈", "σch", "μch · √Nnet", "× 100 %", "σch=FWHM/2.35482；Nnet 为扣除背景后的净峰面积", explanation);
                case "脉冲通过率": return new FormulaSpec(caption, "ηpass =", "ΔNsample", "fref · ΔtMCU", "× 100 %", "参考频率必须真实，且分子、分母必须来自同一同步时间窗", explanation);
                case "处理效率": return new FormulaSpec(caption, "ηproc =", "ΔNsample", "ΔNbusy", "× 100 %", "反映已触发事件中被固件成功读取的比例", explanation);
                case "测量时长": return new FormulaSpec(caption, "tmeasure =", "uptime − uptime₀", "1000", "s", "从最近一次有效同步基线开始计时", explanation);
                default: return new FormulaSpec(caption, "", explanation, null, "", "", explanation);
            }
        }

        private static void DrawFormulaCard(Graphics g, Rectangle box, FormulaSpec formula, bool detailed)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float scale = detailed ? 1.16F : 1F;
            int headerHeight = detailed ? 46 : 38;
            using (Brush background = new SolidBrush(Color.FromArgb(247, 250, 252))) g.FillRectangle(background, box);
            using (Brush header = new SolidBrush(Color.FromArgb(27, 61, 91))) g.FillRectangle(header, 0, 0, box.Width, headerHeight);
            using (Brush accent = new SolidBrush(Color.FromArgb(38, 166, 154))) g.FillRectangle(accent, 0, 0, 6, headerHeight);
            using (Pen border = new Pen(Color.FromArgb(196, 211, 224), 1F)) g.DrawRectangle(border, 0, 0, box.Width - 1, box.Height - 1);

            using (Font titleFont = new Font("Microsoft YaHei UI", detailed ? 12F : 10F, FontStyle.Bold))
            using (Font mathFont = new Font("Cambria Math", 15F * scale, FontStyle.Regular))
            using (Font mathSmall = new Font("Cambria Math", 12F * scale, FontStyle.Regular))
            using (Font textFont = new Font("Microsoft YaHei UI", detailed ? 9.5F : 8.8F, FontStyle.Regular))
            using (Font hintFont = new Font("Microsoft YaHei UI", detailed ? 9F : 8.2F, FontStyle.Regular))
            using (Brush whiteBrush = new SolidBrush(Color.White))
            using (Brush mathBrush = new SolidBrush(Color.FromArgb(19, 43, 63)))
            using (Brush noteBrush = new SolidBrush(Color.FromArgb(57, 72, 84)))
            using (Brush hintBrush = new SolidBrush(Color.FromArgb(83, 113, 134)))
            using (Pen fractionPen = new Pen(Color.FromArgb(19, 43, 63), detailed ? 1.7F : 1.4F))
            using (Pen equationBorder = new Pen(Color.FromArgb(218, 228, 236), 1F))
            {
                g.DrawString(formula.Title, titleFont, whiteBrush, 17F, detailed ? 10F : 8F);
                string headerNote = detailed ? "计算公式与物理含义" : "计算公式";
                SizeF noteSize = g.MeasureString(headerNote, hintFont);
                g.DrawString(headerNote, hintFont, whiteBrush, box.Width - noteSize.Width - 15F, detailed ? 14F : 11F);

                RectangleF equationBox = new RectangleF(14F, headerHeight + 10F, box.Width - 28F, detailed ? 112F : 92F);
                using (Brush equationBackground = new SolidBrush(Color.White)) g.FillRectangle(equationBackground, equationBox);
                g.DrawRectangle(equationBorder, equationBox.X, equationBox.Y, equationBox.Width, equationBox.Height);
                float mathTop = equationBox.Y + (detailed ? 17F : 10F);
                float prefixWidth = String.IsNullOrEmpty(formula.Prefix) ? 0F : g.MeasureString(formula.Prefix, mathFont).Width;
                float suffixWidth = String.IsNullOrEmpty(formula.Suffix) ? 0F : g.MeasureString(formula.Suffix, mathFont).Width;
                if (String.IsNullOrEmpty(formula.Denominator))
                {
                    string expression = (formula.Prefix + "  " + formula.Numerator + "  " + formula.Suffix).Trim();
                    SizeF expressionSize = g.MeasureString(expression, mathFont);
                    g.DrawString(expression, mathFont, mathBrush, (box.Width - expressionSize.Width) / 2F, mathTop + (detailed ? 24F : 19F));
                }
                else
                {
                    SizeF numeratorSize = g.MeasureString(formula.Numerator, mathSmall);
                    SizeF denominatorSize = g.MeasureString(formula.Denominator, mathSmall);
                    float fractionWidth = Math.Max(numeratorSize.Width, denominatorSize.Width) + 24F;
                    float totalWidth = prefixWidth + fractionWidth + suffixWidth + 22F;
                    float startX = Math.Max(24F, (box.Width - totalWidth) / 2F);
                    float centerOffset = detailed ? 29F : 23F;
                    if (!String.IsNullOrEmpty(formula.Prefix)) g.DrawString(formula.Prefix, mathFont, mathBrush, startX, mathTop + centerOffset);
                    float fractionX = startX + prefixWidth + 10F;
                    g.DrawString(formula.Numerator, mathSmall, mathBrush, fractionX + (fractionWidth - numeratorSize.Width) / 2F, mathTop);
                    float lineY = mathTop + (detailed ? 35F : 29F);
                    g.DrawLine(fractionPen, fractionX, lineY, fractionX + fractionWidth, lineY);
                    g.DrawString(formula.Denominator, mathSmall, mathBrush, fractionX + (fractionWidth - denominatorSize.Width) / 2F, lineY + 4F);
                    if (!String.IsNullOrEmpty(formula.Suffix)) g.DrawString(formula.Suffix, mathFont, mathBrush, fractionX + fractionWidth + 10F, mathTop + centerOffset);
                }

                float noteY = equationBox.Bottom + 9F;
                g.DrawString(formula.Variables, textFont, noteBrush, new RectangleF(17F, noteY, box.Width - 34F, detailed ? 42F : 30F));
                if (!detailed) g.DrawString("单击查看变量说明、适用条件与计算口径", hintFont, hintBrush, 17F, box.Height - 23F);
            }
        }

        private void ShowFormulaDetails(FormulaSpec formula)
        {
            using (Form dialog = new Form())
            {
                dialog.Text = formula.Title + "｜计算说明";
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.Size = new Size(790, 540);
                dialog.MinimumSize = new Size(700, 480);
                dialog.BackColor = Color.FromArgb(242, 246, 249);
                dialog.Font = new Font("Microsoft YaHei UI", 9F);
                dialog.ShowIcon = false;

                TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(14) };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 245F));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                dialog.Controls.Add(layout);

                Panel formulaPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Color.White };
                formulaPanel.Paint += delegate(object sender, PaintEventArgs args) { DrawFormulaCard(args.Graphics, formulaPanel.ClientRectangle, formula, true); };
                layout.Controls.Add(formulaPanel, 0, 0);

                TableLayoutPanel content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(6, 12, 6, 0), BackColor = Color.White };
                content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                content.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
                content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                content.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
                layout.Controls.Add(content, 0, 1);
                Label section = new Label { Dock = DockStyle.Fill, Text = "定义、计算口径与适用条件", Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold), ForeColor = Color.FromArgb(27, 61, 91), TextAlign = ContentAlignment.MiddleLeft };
                content.Controls.Add(section, 0, 0);
                RichTextBox details = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, ForeColor = Color.FromArgb(45, 55, 65), Font = new Font("Microsoft YaHei UI", 10F), Text = formula.Note, Margin = new Padding(0, 4, 0, 8) };
                content.Controls.Add(details, 0, 1);
                Button close = new Button { Text = "关闭", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(27, 61, 91), ForeColor = Color.White, Margin = new Padding(0, 4, 0, 0) };
                close.FlatAppearance.BorderSize = 0;
                close.Click += delegate { dialog.Close(); };
                content.Controls.Add(close, 0, 2);
                dialog.AcceptButton = close;
                dialog.CancelButton = close;
                dialog.ShowDialog(this);
            }
        }

        public void RenderFormulaPreviewFile(string path)
        {
            using (Bitmap image = new Bitmap(610, 205))
            using (Graphics graphics = Graphics.FromImage(image))
            {
                FormulaSpec formula;
                if (!metricFormulae.TryGetValue(resolutionValue, out formula)) formula = CreateFormulaSpec("分辨率", "");
                DrawFormulaCard(graphics, new Rectangle(0, 0, image.Width, image.Height), formula, false);
                image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        public void ShowBottomRightFormulaPreview()
        {
            FormulaSpec formula;
            if (!metricFormulae.TryGetValue(elapsedValue, out formula)) return;
            ShowFormulaHoverCard(FindFormulaAnchor(elapsedValue, formula), formula);
        }

        public string FormulaHoverDiagnostics()
        {
            return String.Format(CultureInfo.InvariantCulture, "visible={0}; location={1},{2}; size={3}x{4}; z={5}; formula={6}",
                formulaHoverCard.Visible, formulaHoverCard.Left, formulaHoverCard.Top,
                formulaHoverCard.Width, formulaHoverCard.Height, Controls.GetChildIndex(formulaHoverCard),
                visibleFormula == null ? "none" : visibleFormula.Title);
        }

        public void CompositeFormulaHoverPreview(Bitmap target)
        {
            if (!formulaHoverCard.Visible) return;
            using (Bitmap card = new Bitmap(formulaHoverCard.Width, formulaHoverCard.Height))
            using (Graphics graphics = Graphics.FromImage(target))
            {
                formulaHoverCard.DrawToBitmap(card, new Rectangle(Point.Empty, card.Size));
                graphics.DrawImageUnscaled(card, formulaHoverCard.Location);
            }
        }

        private static void AddRow(TableLayoutPanel table, int row, string caption, Control control)
        {
            Label label = new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 4, 2), ForeColor = TextPrimary };
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(3, 4, 3, 4);
            ComboBox combo = control as ComboBox;
            if (combo != null) combo.FlatStyle = FlatStyle.Flat;
            table.Controls.Add(label, 0, row);
            table.Controls.Add(control, 1, row);
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        private static void AddButtonRow(TableLayoutPanel table, int row, string text, EventHandler handler)
        {
            Button button = SmallButton(text, handler);
            table.Controls.Add(button, 1, row);
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        private static Control WithAction(Control editor, string text, EventHandler handler)
        {
            TableLayoutPanel panel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), Padding = new Padding(0) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            editor.Dock = DockStyle.Fill;
            editor.Margin = new Padding(0, 2, 5, 2);
            Button action = SmallButton(text, handler);
            action.AutoSize = false;
            action.Width = 64;
            action.Height = 28;
            action.Margin = new Padding(0, 2, 0, 2);
            panel.Controls.Add(editor, 0, 0);
            panel.Controls.Add(action, 1, 0);
            return panel;
        }

        private static Button SmallButton(string text, EventHandler handler)
        {
            Button button = new Button
            {
                Text = text,
                AutoSize = true,
                Height = 30,
                Margin = new Padding(3),
                Padding = new Padding(7, 0, 7, 0),
                FlatStyle = FlatStyle.Flat,
                BackColor = Surface,
                ForeColor = Navy,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = TealSoft;
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(211, 237, 234);
            button.Click += handler;
            return button;
        }

        private void DrawSideTab(object sender, DrawItemEventArgs e)
        {
            TabPage page = sideTabs.TabPages[e.Index];
            Rectangle bounds = e.Bounds;
            bool selected = e.Index == sideTabs.SelectedIndex;
            using (Brush background = new SolidBrush(selected ? Surface : Color.FromArgb(232, 238, 243)))
                e.Graphics.FillRectangle(background, bounds);
            if (selected)
            {
                using (Brush accent = new SolidBrush(Teal))
                    e.Graphics.FillRectangle(accent, bounds.Left, bounds.Bottom - 3, bounds.Width, 3);
            }
            using (Font tabFont = new Font("Microsoft YaHei UI", 8.8F, selected ? FontStyle.Bold : FontStyle.Regular))
                TextRenderer.DrawText(e.Graphics, page.Text, tabFont, bounds, selected ? Navy : TextMuted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private static void StyleHeaderButton(Button button, bool primary)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.Cursor = Cursors.Hand;
            button.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            button.BackColor = primary ? Teal : Navy;
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderColor = primary ? Teal : Color.FromArgb(142, 170, 190);
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(30, 157, 150) : Color.FromArgb(38, 76, 103);
            button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(18, 116, 111) : NavyDeep;
        }

        private void ValidateRoi()
        {
            int start = Decimal.ToInt32(roiStartInput.Value);
            int end = Decimal.ToInt32(roiEndInput.Value);
            if (start >= end)
            {
                if (roiStartInput.Focused) roiEndInput.Value = Math.Min(activeChannels - 1, start + 1);
                else roiStartInput.Value = Math.Max(0, end - 1);
            }
        }

        private void AutoSelectRoi()
        {
            SpectrumMetrics full = SpectrumMetrics.Calculate(spectrum, 0, activeChannels - 1, activeChannels);
            if (full.PeakCount <= 0)
            {
                MessageBox.Show(this, "当前能谱没有非零计数，无法自动选择ROI。", "自动ROI", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int halfWidth = full.FwhmChannels > 0.0 ? Math.Max(64, (int)Math.Ceiling(full.FwhmChannels * 4.0)) : 256;
            int start = Math.Max(0, full.PeakChannel - halfWidth);
            int end = Math.Min(activeChannels - 1, full.PeakChannel + halfWidth);
            if (end <= start) end = Math.Min(activeChannels - 1, start + 1);
            roiEndInput.Value = end;
            roiStartInput.Value = start;
            chartDirty = true;
            if (!pauseDisplayCheck.Checked) UpdateChart();
            AppendTerminal(string.Format(CultureInfo.InvariantCulture, "[PC] 自动ROI={0}..{1}，围绕主峰Ch{2}。\r\n", start, end, full.PeakChannel));
        }

        private void SetFullRoi()
        {
            roiEndInput.Value = activeChannels - 1;
            roiStartInput.Value = 0;
            chartDirty = true;
            if (!pauseDisplayCheck.Checked) UpdateChart();
        }

        private void ApplyChannelMode()
        {
            int[] modes = { 4096, 8192, 16384, 65536 };
            int selected = channelBox.SelectedIndex >= 0 && channelBox.SelectedIndex < modes.Length
                ? modes[channelBox.SelectedIndex] : 4096;
            activeChannels = selected;
            spectrumCursorPinned = false;
            HideSpectrumCursor();
            firmwareWarningShown = false;
            firmwareMappingCompatible = false;
            Array.Clear(spectrum, 0, spectrum.Length);
            rawSequenceValid = false;
            pcStreamGapSamples = 0U;
            histogramTransfer = false;
            histogramRequestPending = false;
            incomingHistogram = null;
            incomingHistogramSeen = null;
            roiStartInput.Maximum = activeChannels - 2;
            roiEndInput.Maximum = activeChannels - 1;
            roiStartInput.Value = 0;
            roiEndInput.Value = activeChannels - 1;
            streamCheck.Enabled = false;
            streamCheck.Checked = activeChannels != 4096;
            autoHistogramCheck.Text = activeChannels == 4096 ? "MCU完整4096道累计" : "PC实时累计原始码";
            autoHistogramCheck.Checked = true;
            histogramIntervalInput.Enabled = activeChannels == 4096;
            nextHistogramRequest = DateTime.Now;
            ConfigureChartForActiveChannels();
            InvalidateMeasurementBaseline();
            chartDirty = true;
            if (serialPort != null && serialPort.IsOpen)
            {
                SendSafe("channels " + activeChannels.ToString(CultureInfo.InvariantCulture));
                SendSafe("status");
            }
            AppendTerminal("[PC] 已切换为" + activeChannels.ToString(CultureInfo.InvariantCulture) +
                (activeChannels == 4096 ? "道MCU累计高速模式。\r\n" : "道原始16位码流重分道模式。\r\n"));
        }

        private void ConfigureChartForActiveChannels()
        {
            viewStartChannel = 0;
            viewEndChannelExclusive = activeChannels;
            SynchronizeSpectrumViewInputs();
            ConfigureXAxisView();
            double lsbUv = SpectrumMetrics.AdcSpectrumFullScaleMv * 1000.0 / activeChannels;
            spectrumChart.ChartAreas[0].AxisX.Title = string.Format(CultureInfo.InvariantCulture,
                "道址（0–{0}；2.5 V参考；显示1道={1:F3} µV）", activeChannels - 1, lsbUv);
            spectrumChart.Titles[0].Text = activeChannels == 4096
                ? "实时4096道能谱（MCU本机累计，高速推荐）"
                : string.Format(CultureInfo.InvariantCulture,
                     "实时{0}道能谱（原始16位码流；分析与导出保持所选道数）", activeChannels);
            UpdateChart();
        }

        private void ApplySpectrumViewFromInputs()
        {
            if (synchronizingSpectrumView) return;
            ApplySpectrumView(Decimal.ToInt32(viewStartInput.Value), Decimal.ToInt32(viewEndInput.Value) + 1);
        }

        private void ResetSpectrumView()
        {
            ApplySpectrumView(0, activeChannels);
        }

        private void ZoomToMainPeak()
        {
            if (currentMetrics == null || currentMetrics.PeakCount <= 0)
            {
                AppendTerminal("[PC] 当前谱图尚无可定位主峰。\r\n");
                return;
            }
            int center = Math.Max(0, Math.Min(activeChannels - 1, (int)Math.Round(currentMetrics.CentroidChannel)));
            int span = currentMetrics.FwhmChannels > 0
                ? Math.Max(32, (int)Math.Ceiling(currentMetrics.FwhmChannels * 10.0))
                : Math.Max(32, activeChannels / 32);
            span = Math.Min(activeChannels, span);
            ApplySpectrumView(center - span / 2, center - span / 2 + span);
        }

        private void ApplySpectrumView(int start, int endExclusive)
        {
            int minimumSpan = Math.Max(2, activeChannels / 16384);
            start = Math.Max(0, Math.Min(activeChannels - minimumSpan, start));
            endExclusive = Math.Max(start + minimumSpan, Math.Min(activeChannels, endExclusive));
            if (endExclusive > activeChannels)
            {
                endExclusive = activeChannels;
                start = Math.Max(0, endExclusive - minimumSpan);
            }
            viewStartChannel = start;
            viewEndChannelExclusive = endExclusive;
            spectrumCursorPinned = false;
            HideSpectrumCursor();
            SynchronizeSpectrumViewInputs();
            ConfigureXAxisView();
            UpdateChart();
        }

        private void SynchronizeSpectrumViewInputs()
        {
            synchronizingSpectrumView = true;
            try
            {
                viewStartInput.Maximum = activeChannels - 1;
                viewEndInput.Maximum = activeChannels - 1;
                viewStartInput.Value = Math.Max(0, Math.Min(activeChannels - 1, viewStartChannel));
                viewEndInput.Value = Math.Max(0, Math.Min(activeChannels - 1, viewEndChannelExclusive - 1));
            }
            finally { synchronizingSpectrumView = false; }
        }

        private void ConfigureXAxisView()
        {
            if (spectrumChart.ChartAreas.Count == 0) return;
            ChartArea area = spectrumChart.ChartAreas[0];
            int span = Math.Max(1, viewEndChannelExclusive - viewStartChannel);
            area.AxisX.Minimum = viewStartChannel;
            area.AxisX.Maximum = viewEndChannelExclusive;
            area.AxisX.Interval = NiceAxisInterval(span, 8);
        }

        private static double NiceAxisInterval(double span, int targetTicks)
        {
            if (span <= 0 || Double.IsNaN(span) || Double.IsInfinity(span)) return 1.0;
            double raw = span / Math.Max(2, targetTicks);
            double magnitude = Math.Pow(10.0, Math.Floor(Math.Log10(raw)));
            double normalized = raw / magnitude;
            double nice = normalized <= 1.0 ? 1.0 : (normalized <= 2.0 ? 2.0 : (normalized <= 5.0 ? 5.0 : 10.0));
            return Math.Max(1.0, nice * magnitude);
        }

        private void ShowManual()
        {
            Form manual = new Form
            {
                Text = "STM32G474 + AD7980 最终版使用说明",
                Width = 900,
                Height = 680,
                MinimumSize = new Size(720, 520),
                StartPosition = FormStartPosition.CenterParent,
                Font = Font
            };
            TabControl tabs = new TabControl { Dock = DockStyle.Fill };
            manual.Controls.Add(tabs);
            AddManualPage(tabs, "快速使用",
                "1. 确认LCD/FPC已拔除，PA2、PA3、PC4、PC5、PF2保持接地高阻。\r\n" +
                "2. 连接开发板原生USB CDC，选择COM口并连接。\r\n" +
                "3. 比赛高计数率优先选4096道；精细峰形分析按需要选8192、16384或65536道。\r\n" +
                "4. 设置比较器端实际阈值50–200 mV；固件会自动补偿1 kΩ/(9.1 kΩ+1 kΩ)分压。\r\n" +
                "5. 设置ROI后读取FWHM、分辨率、计数率和处理效率；记录或导出CSV/TXT。\r\n\r\n" +
                "CNV由模拟硬件产生，上位机不会控制CNV或任意GPIO。PH_RESTART仅由固件在完成ADC读取后产生。\r\n");
            AddManualPage(tabs, "道址与传输",
                "4096道：MCU直接用AD7980原始码右移4位累计，仅定期传输4096项直方图，吞吐最高，推荐用于100 kcps连续测试。\r\n\r\n" +
                "8192/16384/65536道：固件以带序号和CRC16的B16批次传输每个16位原始码，PC分别右移3/2/0位累计。道数越高，量化更细，但USB与PC负担更大。\r\n\r\n" +
                "提高道数不会改善模拟前端、ADC噪声或事件统计决定的真实分辨率。导出文件始终保存所选道数的原始计数，不对能谱做插值或人为缩峰。\r\n\r\n" +
                "谱图游标：默认在鼠标附近自动吸附局部峰，并显示精确计数、ADC电压、原始码范围、局部FWHM和分辨率；也可切换自由道址。左键锁定，右键或Esc隐藏。顶部可输入显示起止道、放大主峰/ROI、恢复全谱；鼠标滚轮以指针为中心连续缩放。\r\n");
            AddManualPage(tabs, "指标说明",
                "所有指标标题带ⓘ；鼠标悬停可看公式，点击可打开完整定义。\r\n\r\n" +
                "峰位：ROI内扣除背景后的质心 μ=Σ(ch·Nnet)/ΣNnet 及其ADC端电压。\r\n" +
                "FWHM：对显示道数自适应的小窗口平滑仅用于稳定估计半高宽；原始直方图和导出数据不被修改。\r\n" +
                "分辨率：FWHM/峰位×100%。细道模式会暴露发生器DAC码跳、触发时间游走和峰形细结构，因此数值可能不随幅度严格单调。\r\n" +
                "统计精度：σpeak/[sqrt(Nnet)·μ]×100%，只估计有限计数导致的质心统计误差。\r\n" +
                "实测计数率：Δ有效样本/ΔMCU uptime。清空或计数器回退后首帧只建立同步基线。\r\n" +
                "处理效率：Δ有效样本/ΔBusy×100%；脉冲通过率：Δ有效样本/(参考Hz·ΔMCU时间)×100%。两者不可混用，通过率不做钳位且统计窗至少1秒。通过率≥99%且≤100.1%时绿色显示；处理效率≥99.9%时绿色显示。\r\n" +
                "线性校准：系统标准标定采用测得=k·实际+b；显示输入使用逆变换 实际=(测得-b)/k。该标定默认启用，增益、斜率和截距仍可编辑，也可按实验需要关闭。\r\n");
            AddManualPage(tabs, "故障与安全",
                "0 cps且触发正常：先看状态摘要中的SDO、Busy、队列和USB恢复；断开后重新连接会重新声明道址模式。\r\n" +
                "高计数率丢失：优先使用4096道，关闭事件流；高道数模式需要逐事件USB传输，满16位不适合长期100 kcps无损连续流。\r\n" +
                "分辨率异常：固定模拟幅度重复采集，缩小ROI并比较FWHM的mV值；若异常随FPGA DAC码或触发位置变化，属于发生器/模拟链而非道址换算。\r\n" +
                "禁止启用LCD/FPC及USART2；禁止将PA2、PA3、PC4、PC5、PF2配置为输出。AD7980 ARM与BRM的软件串行接口相同。\r\n");
            manual.ShowDialog(this);
        }

        private static void AddManualPage(TabControl tabs, string title, string text)
        {
            TabPage page = new TabPage(title);
            RichTextBox box = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.White,
                BorderStyle = BorderStyle.None,
                Font = new Font("Microsoft YaHei UI", 10.5F),
                Text = text,
                Padding = new Padding(14)
            };
            page.Controls.Add(box);
            tabs.TabPages.Add(page);
        }

        private void ApplyLogScale()
        {
            if (applyingLogScale) return;
            applyingLogScale = true;
            try
            {
                chartDirty = true;
                UpdateChart();
            }
            catch (Exception ex)
            {
                logScaleCheck.Checked = false;
                ChartArea area = spectrumChart.ChartAreas[0];
                area.AxisY.IsLogarithmic = false;
                area.AxisY.Minimum = 0.0;
                area.AxisY.Maximum = Double.NaN;
                chartDirty = true;
                UpdateChart();
                AppendTerminal("[PC] 对数轴已安全回退为线性轴：" + ex.Message + "\r\n");
                if (!suppressDisplayDialogs)
                    MessageBox.Show(this, "对数轴设置未能完成，软件已安全恢复为线性轴。采集数据没有丢失。", "显示已恢复", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { applyingLogScale = false; }
        }

        private double BoardInputFromAdcMv(double adcMv)
        {
            double gain = (double)analogGainInput.Value;
            if (gain <= 0.0) return Double.NaN;
            double measuredBoardMv = (adcMv - (double)analogOffsetInput.Value) / gain;
            if (!useMeasuredCurveCheck.Checked) return measuredBoardMv;
            double slope = (double)linearitySlopeInput.Value;
            return slope > 0.0 ? (measuredBoardMv - (double)linearityInterceptInput.Value) / slope : Double.NaN;
        }

        private double PredictedAdcFromBoardInputMv(double boardMv)
        {
            double measuredBoardMv = useMeasuredCurveCheck.Checked
                ? (double)linearitySlopeInput.Value * boardMv + (double)linearityInterceptInput.Value
                : boardMv;
            return measuredBoardMv * (double)analogGainInput.Value + (double)analogOffsetInput.Value;
        }

        private double BoardLoadOhms()
        {
            return InputMath.BoardLoadOhms(terminationBox.SelectedIndex == 1);
        }

        private bool TryGetActualInputMv(out double actualMv)
        {
            actualMv = Double.NaN;
            if (!knownAmplitudeCheck.Checked || generatorModeBox.SelectedIndex < 0 || generatorModeBox.SelectedIndex > 1) return false;
            actualMv = InputMath.ActualInputMv((double)generatorAmplitudeInput.Value, (double)sourceImpedanceInput.Value, BoardLoadOhms(), generatorModeBox.SelectedIndex);
            return !Double.IsNaN(actualMv) && actualMv > 0.0;
        }

        private bool TryGetSourceReferenceMv(out double sourceMv)
        {
            sourceMv = Double.NaN;
            if (!knownAmplitudeCheck.Checked || generatorModeBox.SelectedIndex < 0 || generatorModeBox.SelectedIndex > 1) return false;
            sourceMv = InputMath.SourceOpenCircuitMv((double)generatorAmplitudeInput.Value,
                (double)sourceImpedanceInput.Value, generatorModeBox.SelectedIndex);
            return !Double.IsNaN(sourceMv) && sourceMv > 0.0;
        }

        private double SourceEquivalentFromBoardMv(double boardMv)
        {
            return InputMath.SourceOpenEquivalentMv(boardMv,
                (double)sourceImpedanceInput.Value, BoardLoadOhms());
        }

        private void UpdateInputConfiguration()
        {
            double load = BoardLoadOhms();
            bool terminated = terminationBox.SelectedIndex == 1;
            impedanceSummary.Text = terminated
                ? string.Format(CultureInfo.InvariantCulture, "板端：JP1必须实际短接，等效负载 {0:F3} Ω（50 Ω∥1 MΩ）", load)
                : "板端：JP1必须实际开路，等效负载约 1 MΩ（高阻）";
            impedanceSummary.ForeColor = terminated ? Color.DarkOrange : Color.SeaGreen;

            double actual;
            if (TryGetActualInputMv(out actual))
            {
                double displayed = (double)generatorAmplitudeInput.Value;
                double factor = displayed > 0.0 ? actual / displayed : 0.0;
                double sourceOpen = InputMath.SourceOpenCircuitMv(displayed,
                    (double)sourceImpedanceInput.Value, generatorModeBox.SelectedIndex);
                double predictedAdc = PredictedAdcFromBoardInputMv(actual);
                correctionSummary.Text = string.Format(CultureInfo.InvariantCulture,
                    "换算系数 {0:F6}；源端开路参考≈{1:F3} mV；50Ω端接后的板端输入≈{2:F3} mV；按当前增益预计ADC峰≈{3:F3} mV。{4}",
                    factor, sourceOpen, actual, predictedAdc, predictedAdc >= SpectrumMetrics.AdcSpectrumFullScaleMv ? "警告：预计超出ADC端0–2.5 V量程，会进入满量程码！" : "量程检查通过。") +
                    string.Format(CultureInfo.InvariantCulture, " 当前使用可编辑增益{0:F4}；{1}", (double)analogGainInput.Value,
                        useMeasuredCurveCheck.Checked
                            ? string.Format(CultureInfo.InvariantCulture, "启用逆线性校准 x=(y-{0:F6})/{1:F9}。", (double)linearityInterceptInput.Value, (double)linearitySlopeInput.Value)
                            : "线性校准未启用。") +
                    " 50 Ω同轴线本身不代表板端已做50 Ω端接。";
                correctionSummary.ForeColor = predictedAdc >= SpectrumMetrics.AdcSpectrumFullScaleMv ? Color.Firebrick : Color.FromArgb(35, 75, 110);
            }
            else
            {
                correctionSummary.Text = "输入未知：只显示ADC实测谱、计数率、FWHM和分辨率；不虚构输入幅度或绝对精度。若需精度/通过率，请勾选已知参考并选择真实发生器标定方式。";
                correctionSummary.ForeColor = Color.FromArgb(35, 75, 110);
            }
        }

        private void UpdateDerivedMetrics()
        {
            // Avoid committing partially typed text. Accessing NumericUpDown.Value
            // while its editor has focus can reformat the field and move the caret.
            if (ConfigurationEditing()) return;
            uint overflowDelta = latestRangeOverflows >= measurementStartRangeOverflows
                ? latestRangeOverflows - measurementStartRangeOverflows : latestRangeOverflows;
            double measuredBoardInput = BoardInputFromAdcMv(currentMetrics.PeakMv);
            double measuredSourceEquivalent = !Double.IsNaN(measuredBoardInput)
                ? SourceEquivalentFromBoardMv(measuredBoardInput) : Double.NaN;
            double actual;
            string sourceText = currentMetrics.PeakCount > 0 && !Double.IsNaN(measuredSourceEquivalent)
                ? measuredSourceEquivalent.ToString("F3", CultureInfo.InvariantCulture) + " mV"
                : "--";
            string boardText = currentMetrics.PeakCount > 0 && !Double.IsNaN(measuredBoardInput)
                ? measuredBoardInput.ToString("F3", CultureInfo.InvariantCulture) + " mV"
                : "--";
            inputPeakValue.Text = sourceText + " / " + boardText;

            if (firmwareStatusSeen && !firmwareMappingCompatible)
            {
                inputPeakValue.Text = "固件映射不兼容";
                accuracyValue.Text = "请重烧修正版";
                accuracyValue.ForeColor = Color.Firebrick;
            }
            else if (overflowDelta > 0U)
            {
                accuracyValue.Text = "ADC>2V溢出 " + overflowDelta.ToString(CultureInfo.InvariantCulture);
                accuracyValue.ForeColor = Color.Firebrick;
            }
            else if (currentMetrics.PeakCount > 0 && TryGetSourceReferenceMv(out actual))
            {
                double accuracy = Math.Abs(measuredSourceEquivalent - actual) * 100.0 / actual;
                accuracyValue.Text = accuracy.ToString("F3", CultureInfo.InvariantCulture) + " %";
                accuracyValue.ForeColor = accuracy < 1.0 ? Color.SeaGreen : Color.Firebrick;
            }
            else
            {
                accuracyValue.Text = "未知参考";
                accuracyValue.ForeColor = Color.FromArgb(30, 60, 90);
            }
            statisticalValue.Text = currentMetrics.StatisticalPrecisionPercent > 0.0
                ? currentMetrics.StatisticalPrecisionPercent.ToString("F4", CultureInfo.InvariantCulture) + " %"
                : "--";

            double windowSeconds = Double.NaN, windowRate = Double.NaN, windowPass = Double.NaN, windowEfficiency = Double.NaN, triggerAcceptance = Double.NaN;
            uint windowSamples = 0U, windowBusy = 0U;
            bool windowValid = measurementBaselineValid && InputMath.WindowMetrics(
                measurementStartSamples, measurementStartBusy, measurementStartUptimeMs,
                latestSamples, latestBusy, latestUptimeMs,
                knownRateCheck.Checked ? (double)referenceRateInput.Value : 0.0,
                out windowSeconds, out windowSamples, out windowBusy, out windowRate, out windowPass, out windowEfficiency, out triggerAcceptance);
            if (windowValid)
            {
                processingValue.Text = !Double.IsNaN(windowEfficiency) ? windowEfficiency.ToString("F3", CultureInfo.InvariantCulture) + " %" : "--";
                processingValue.ForeColor = !Double.IsNaN(windowEfficiency) && windowEfficiency >= 99.9 ? Color.SeaGreen : TextPrimary;
                elapsedValue.Text = windowSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s";
                if (knownRateCheck.Checked && referenceRateInput.Value > 0)
                {
                    passRateValue.Text = windowSeconds < 1.0 ? "稳定中 " + windowSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s"
                        : windowPass.ToString("F4", CultureInfo.InvariantCulture) + " %";
                    passRateValue.ForeColor = windowSeconds >= 1.0 && windowPass >= 99.0 && windowPass <= 100.1 ? Color.SeaGreen : Color.DarkOrange;
                }
                else
                {
                    passRateValue.Text = "未知参考";
                    passRateValue.ForeColor = Color.FromArgb(30, 60, 90);
                }
            }
            else
            {
                processingValue.Text = "--";
                processingValue.ForeColor = TextPrimary;
                passRateValue.Text = knownRateCheck.Checked ? (measurementBaselinePending ? "等待同步状态" : "等待稳定窗口") : "未知参考";
                elapsedValue.Text = "--";
            }
        }

        private double MeasurementElapsedSeconds()
        {
            if (latestUptimeMs != 0U && measurementStartUptimeMs != 0U)
            {
                uint deltaMs = unchecked(latestUptimeMs - measurementStartUptimeMs);
                if (deltaMs < 0x80000000U) return deltaMs / 1000.0;
            }
            return measurementClock.Elapsed.TotalSeconds;
        }

        private void ResetMeasurementBaseline()
        {
            measurementClock.Restart();
            measurementStartBusy = latestBusy;
            measurementStartSamples = latestSamples;
            measurementStartRangeOverflows = latestRangeOverflows;
            measurementStartUptimeMs = latestUptimeMs;
            measurementBaselineValid = true;
            measurementBaselinePending = false;
            lastRateTime = DateTime.MinValue;
            lastRateUptimeMs = 0U;
            if (!ConfigurationEditing()) UpdateDerivedMetrics();
        }

        private void InvalidateMeasurementBaseline()
        {
            measurementClock.Reset();
            measurementBaselineValid = false;
            measurementBaselinePending = true;
            lastRateTime = DateTime.MinValue;
            lastRateUptimeMs = 0U;
            latestMeasuredRate = 0.0;
            rateValue.Text = "等待同步状态";
            UpdateDerivedMetrics();
        }

        private static void ConfigureButton(Button button, string text, EventHandler handler, int width)
        {
            button.Text = text;
            button.Width = width;
            button.Height = 30;
            button.Margin = new Padding(5, 0, 0, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.Click += handler;
        }

        private void RefreshPortsClicked(object sender, EventArgs e) { RefreshPorts(true); }

        private void RefreshPorts() { RefreshPorts(false); }

        private void RefreshPorts(bool userInitiated)
        {
            if (!userInitiated && (portBox.ContainsFocus || portBox.DroppedDown)) return;
            string selected = portBox.SelectedItem as string;
            string[] ports = NormalizePortInventory(SerialPort.GetPortNames());
            string inventory = String.Join("|", ports);
            if (inventory == lastPortInventory) return;
            lastPortInventory = inventory;
            portBox.BeginUpdate();
            try
            {
                portBox.Items.Clear();
                portBox.Items.AddRange(ports);
            }
            finally { portBox.EndUpdate(); }
            string selectedMatch = selected == null ? null : ports.FirstOrDefault(delegate(string port) { return String.Equals(port, selected, StringComparison.OrdinalIgnoreCase); });
            if (selectedMatch != null) portBox.SelectedItem = selectedMatch;
            else if (ports.Length > 0) portBox.SelectedIndex = 0;
            if ((serialPort == null || !serialPort.IsOpen) && ports.Length > 0)
            {
                connectionLabel.Text = "已发现 " + portBox.SelectedItem;
                connectionLabel.ForeColor = Color.FromArgb(126, 232, 171);
            }
            else if ((serialPort == null || !serialPort.IsOpen) && ports.Length == 0)
            {
                connectionLabel.Text = "等待USB CDC";
                connectionLabel.ForeColor = Color.FromArgb(255, 205, 90);
            }
        }

        internal static string[] NormalizePortInventory(IEnumerable<string> rawPorts)
        {
            return rawPorts
                .Where(delegate(string port) { return !String.IsNullOrWhiteSpace(port); })
                .Select(delegate(string port) { return port.Trim().ToUpperInvariant(); })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(PortOrder)
                .ThenBy(delegate(string port) { return port; }, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool HasActiveNumericEditor(Control root)
        {
            foreach (Control child in root.Controls)
            {
                if (child is NumericUpDown && child.ContainsFocus) return true;
                if (child.HasChildren && HasActiveNumericEditor(child)) return true;
            }
            return false;
        }

        private bool ConfigurationEditing()
        {
            return HasActiveNumericEditor(sideTabs);
        }

        private static int PortOrder(string port)
        {
            int number;
            return Int32.TryParse(port.Replace("COM", ""), out number) ? number : Int32.MaxValue;
        }

        private void ConnectClicked(object sender, EventArgs e)
        {
            if (serialPort != null && serialPort.IsOpen) Disconnect();
            else Connect();
        }

        private void Connect()
        {
            if (portBox.SelectedItem == null)
            {
                MessageBox.Show(this, "没有发现USB CDC端口。请检查USB线和设备供电；端口出现后会自动识别，也可点击“重新扫描”。", "未找到设备", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                serialPort = new SerialPort(portBox.SelectedItem.ToString(), 115200, Parity.None, 8, StopBits.One);
                serialPort.Handshake = Handshake.None;
                serialPort.ReadTimeout = 500;
                serialPort.WriteTimeout = 500;
                serialPort.ReadBufferSize = 1024 * 1024;
                serialPort.DtrEnable = true;
                serialPort.RtsEnable = false;
                serialPort.Encoding = Encoding.ASCII;
                serialPort.Open();
                serialPort.DiscardInBuffer();
                serialPort.DiscardOutBuffer();
                serialPort.DataReceived += SerialDataReceived;
                receiveBuffer.Length = 0;
                lock (pendingSerialLock)
                {
                    pendingSerialText.Length = 0;
                    serialDispatchPending = false;
                }
                firmwareStatusSeen = false;
                firmwareMappingCompatible = true;
                firmwareWarningShown = false;
                firmwareAdcSpectrumFsMv = 0U;
                firmwareHistogramChannels = 0U;
                firmwareFrontendGainMilli = 0U;
                nextRawSequence = 0U;
                rawSequenceValid = false;
                pcStreamGapSamples = 0U;
                latestStreamLostSamples = 0U;
                latestQueueDepth = 0U;
                latestUsbRecoveries = 0U;
                latestRangeOverflows = 0U;
                latestUptimeMs = 0U;
                lastRateUptimeMs = 0U;
                latestBusy = 0U;
                latestSamples = 0U;
                lastSamples = 0U;
                lastRateTime = DateTime.MinValue;
                measurementBaselineValid = false;
                measurementBaselinePending = true;
                measurementClock.Reset();
                lastDeviceActivity = DateTime.Now;
                nextLinkResync = DateTime.Now.AddSeconds(3);
                lastLinkWarning = DateTime.MinValue;
                SetConnectedState(true);
                AppendTerminal("[PC] 已连接 " + serialPort.PortName + "，正在声明所选道址模式。\r\n");
                SendSafe("channels " + activeChannels.ToString(CultureInfo.InvariantCulture));
                SendSafe("status");
            }
            catch (Exception ex)
            {
                if (serialPort != null) serialPort.Dispose();
                serialPort = null;
                MessageBox.Show(this, "打开串口失败：" + ex.Message, "连接失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetConnectedState(false);
            }
        }

        private void Disconnect()
        {
            StopRecording();
            SerialPort port = serialPort;
            serialPort = null;
            try
            {
                if (port != null)
                {
                    port.DataReceived -= SerialDataReceived;
                    if (port.IsOpen) port.Close();
                    port.Dispose();
                }
            }
            catch { }
            histogramTransfer = false;
            histogramRequestPending = false;
            incomingHistogram = null;
            incomingHistogramSeen = null;
            receiveBuffer.Length = 0;
            lock (pendingSerialLock)
            {
                pendingSerialText.Length = 0;
                serialDispatchPending = false;
            }
            SetConnectedState(false);
            AppendTerminal("[PC] 已断开。\r\n");
        }

        private void SetConnectedState(bool connected)
        {
            controlGroup.Enabled = connected;
            recordButton.Enabled = connected;
            portBox.Enabled = !connected;
            refreshPortsButton.Enabled = !connected;
            connectButton.Text = connected ? "断开" : "连接";
            connectionLabel.Text = connected ? "已连接 " + serialPort.PortName : "未连接";
            connectionLabel.ForeColor = connected ? Color.LightGreen : Color.FromArgb(255, 205, 90);
        }

        private void SerialDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            SerialPort port = serialPort;
            if (port == null || !ReferenceEquals(sender, port) || !port.IsOpen) return;
            try
            {
                string text = port.ReadExisting();
                if (text.Length == 0 || IsDisposed) return;
                bool schedule = false;
                lock (pendingSerialLock)
                {
                    pendingSerialText.Append(text);
                    if (!serialDispatchPending)
                    {
                        serialDispatchPending = true;
                        schedule = true;
                    }
                }
                if (schedule) BeginInvoke(new Action(DrainPendingSerialText));
            }
            catch (Exception ex)
            {
                if (!IsDisposed) BeginInvoke(new Action<string>(SerialFailure), ex.Message);
            }
        }

        private void DrainPendingSerialText()
        {
            string text;
            lock (pendingSerialLock)
            {
                text = pendingSerialText.ToString();
                pendingSerialText.Length = 0;
                serialDispatchPending = false;
            }
            if (text.Length != 0) ProcessIncomingText(text);

            bool schedule = false;
            lock (pendingSerialLock)
            {
                if (pendingSerialText.Length != 0 && !serialDispatchPending)
                {
                    serialDispatchPending = true;
                    schedule = true;
                }
            }
            if (schedule && !IsDisposed) BeginInvoke(new Action(DrainPendingSerialText));
        }

        private void SerialFailure(string message)
        {
            AppendTerminal("[PC] 串口错误：" + message + "\r\n");
            Disconnect();
        }

        private void ProcessIncomingText(string text)
        {
            lastDeviceActivity = DateTime.Now;
            nextLinkResync = lastDeviceActivity.AddSeconds(3);
            connectionLabel.Text = "已连接 " + (serialPort == null ? "" : serialPort.PortName);
            connectionLabel.ForeColor = Color.LightGreen;
            receiveBuffer.Append(text);
            while (true)
            {
                string current = receiveBuffer.ToString();
                int newline = current.IndexOf('\n');
                if (newline < 0) break;
                string line = current.Substring(0, newline).TrimEnd('\r');
                receiveBuffer.Remove(0, newline + 1);
                ProcessLine(line);
            }
            if (receiveBuffer.Length > 8192)
            {
                receiveBuffer.Length = 0;
                AppendTerminal("[PC] 丢弃过长且无换行的异常输入。\r\n");
            }
        }

        private void ProcessLine(string line)
        {
            if (recorder != null)
            {
                try
                {
                    recorder.WriteLine(line);
                    recorderLinesSinceFlush++;
                    if (recorderLinesSinceFlush >= 128)
                    {
                        recorder.Flush();
                        recorderLinesSinceFlush = 0;
                    }
                }
                catch (Exception ex) { AppendTerminal("[PC] 记录错误：" + ex.Message + "\r\n"); StopRecording(); }
            }
            if (line.Length == 0) return;

            uint firstSequence;
            ushort[] rawCodes;
            if (ProtocolParser.TryParseRaw16Batch(line, out firstSequence, out rawCodes))
            {
                if (rawSequenceValid && firstSequence != nextRawSequence)
                {
                    uint gap = unchecked(firstSequence - nextRawSequence);
                    if (gap < 0x80000000U) pcStreamGapSamples += gap;
                }
                for (int i = 0; i < rawCodes.Length; i++)
                {
                    int channel = (int)(((uint)rawCodes[i] * (uint)activeChannels) >> 16);
                    if (channel >= activeChannels) channel = activeChannels - 1;
                    spectrum[channel]++;
                }
                nextRawSequence = unchecked(firstSequence + (uint)rawCodes.Length);
                rawSequenceValid = true;
                chartDirty = true;
                return;
            }

            if (line == "channel,count")
            {
                histogramRequestPending = false;
                histogramTransfer = true;
                incomingHistogram = new long[activeChannels];
                incomingHistogramSeen = new bool[activeChannels];
                histogramBinsReceived = 0;
                histogramRequestStarted = DateTime.Now;
                return;
            }
            if (line == "# histogram end")
            {
                if (histogramTransfer && incomingHistogram != null && histogramBinsReceived == activeChannels)
                {
                    Array.Clear(spectrum, 0, spectrum.Length);
                    Array.Copy(incomingHistogram, spectrum, activeChannels);
                    chartDirty = true;
                }
                else if (histogramTransfer)
                {
                    AppendTerminal("[PC] 本次直方图不完整（" + histogramBinsReceived.ToString(CultureInfo.InvariantCulture) + "/" + activeChannels.ToString(CultureInfo.InvariantCulture) + "道），已丢弃本次快照。\r\n");
                }
                histogramTransfer = false;
                histogramRequestPending = false;
                incomingHistogram = null;
                incomingHistogramSeen = null;
                if (showProtocolCheck.Checked) AppendTerminal("[设备] 完整" + activeChannels.ToString(CultureInfo.InvariantCulture) + "道能谱已更新。\r\n");
                return;
            }
            if (histogramTransfer)
            {
                int channel;
                long count;
                if (ProtocolParser.TryParseHistogramBin(line, out channel, out count))
                {
                    if (channel >= 0 && channel < activeChannels)
                    {
                        incomingHistogram[channel] = count;
                        if (incomingHistogramSeen != null && !incomingHistogramSeen[channel])
                        {
                            incomingHistogramSeen[channel] = true;
                            histogramBinsReceived++;
                        }
                    }
                }
                return;
            }

            Dictionary<string, string> status;
            if (ProtocolParser.TryParseStatus(line, out status))
            {
                UpdateStatus(status);
                // Status is already reflected in the metric cards.  Do not flood the
                // terminal when a cumulative drop/overrun counter remains non-zero;
                // show the raw protocol only when explicitly requested.
                if (showProtocolCheck.Checked) AppendTerminal(line + "\r\n");
                return;
            }

            SampleRecord sample;
            if (ProtocolParser.TryParseSample(line, out sample))
            {
                int sampleChannel = (int)(((uint)sample.Raw * (uint)activeChannels) >> 16);
                if (sampleChannel >= activeChannels) sampleChannel = activeChannels - 1;
                spectrum[sampleChannel]++;
                chartDirty = true;
                latestOverruns = sample.Overruns;
                latestDrops = sample.TxDrops;
                overrunValue.Text = sample.Overruns.ToString(CultureInfo.InvariantCulture);
                dropsValue.Text = sample.TxDrops.ToString(CultureInfo.InvariantCulture);
                UpdateAlarmColors(sample.Overruns, sample.TxDrops);
                return;
            }

            if (line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("sample ", StringComparison.Ordinal))
                AppendTerminal(line + "\r\n");
        }

        private void UpdateStatus(Dictionary<string, string> status)
        {
            uint previousSamples = latestSamples;
            uint previousBusy = latestBusy;
            bool hadPreviousStatus = firmwareStatusSeen;
            uint samples = ProtocolParser.GetUInt(status, "samples", 0);
            uint uptimeMs = ProtocolParser.GetUInt(status, "uptime_ms", 0);
            uint busy = ProtocolParser.GetUInt(status, "busy", 0);
            uint recoveries = ProtocolParser.GetUInt(status, "recoveries", 0);
            uint postReadLow = ProtocolParser.GetUInt(status, "postread_low",
                ProtocolParser.GetUInt(status, "stuck_low", 0));
            uint overruns = ProtocolParser.GetUInt(status, "overruns", 0);
            uint drops = ProtocolParser.GetUInt(status, "tx_drops", 0);
            uint streamLost = ProtocolParser.GetUInt(status, "stream_lost_samples", 0);
            uint usbRecoveries = ProtocolParser.GetUInt(status, "usb_recoveries", 0);
            uint queued = ProtocolParser.GetUInt(status, "queued", 0);
            uint rangeOverflows = ProtocolParser.GetUInt(status, "range_overflows", 0);
            uint spectrumFsMv = ProtocolParser.GetUInt(status, "adc_spectrum_fs_mV", 0);
            uint histogramChannels = ProtocolParser.GetUInt(status, "hist_channels", 0);
            uint frontendGainMilli = ProtocolParser.GetUInt(status, "frontend_gain_milli", 0);
            uint mean = ProtocolParser.GetUInt(status, "mean_mV", 0);
            uint peak = ProtocolParser.GetUInt(status, "peak_mV", 0);
            uint expectedMv = ProtocolParser.GetUInt(status, "expected_mV", 0);
            uint expectedHz = ProtocolParser.GetUInt(status, "expected_Hz", 0);
            uint threshold = ProtocolParser.GetUInt(status, "threshold_mV", 0);
            uint decimate = ProtocolParser.GetUInt(status, "decimate", 0);
            string sdo;
            string firmwareVersion;
            string outputFormat;
            string spectrumMode;
            if (!status.TryGetValue("sdo", out sdo)) sdo = "unknown";
            if (!status.TryGetValue("fw", out firmwareVersion)) firmwareVersion = "unknown";
            if (!status.TryGetValue("format", out outputFormat)) outputFormat = "unknown";
            if (!status.TryGetValue("spectrum_mode", out spectrumMode)) spectrumMode = "unknown";
            bool counterEpochChanged = hadPreviousStatus && (samples < previousSamples || busy < previousBusy);
            if (counterEpochChanged) InvalidateMeasurementBaseline();
            latestSamples = samples;
            latestBusy = busy;
            latestOverruns = overruns;
            latestDrops = drops;
            latestStreamLostSamples = streamLost;
            latestUsbRecoveries = usbRecoveries;
            latestQueueDepth = queued;
            latestRangeOverflows = rangeOverflows;
            latestUptimeMs = uptimeMs;
            firmwareAdcSpectrumFsMv = spectrumFsMv;
            firmwareHistogramChannels = histogramChannels;
            firmwareFrontendGainMilli = frontendGainMilli;
            firmwareStatusSeen = true;
            string requiredMode = activeChannels == 4096 ? "mcu_hist4096" : "host_raw16";
            firmwareMappingCompatible = spectrumFsMv == (uint)SpectrumMetrics.AdcSpectrumFullScaleMv &&
                histogramChannels == (uint)activeChannels &&
                firmwareVersion == "2.0.0-adaptive" && spectrumMode == requiredMode &&
                (activeChannels == 4096 || (outputFormat == "b16" && decimate == 1U));
            samplesValue.Text = samples.ToString("N0", CultureInfo.InvariantCulture);
            busyValue.Text = busy.ToString("N0", CultureInfo.InvariantCulture) +
                " / 队列" + queued.ToString(CultureInfo.InvariantCulture) +
                " / 恢复" + recoveries.ToString(CultureInfo.InvariantCulture) +
                " / 读后低" + postReadLow.ToString(CultureInfo.InvariantCulture) +
                " / SDO " + (sdo == "low" ? "低" : sdo == "high" ? "高" : "未知");
            meanValue.Text = mean + " mV";
            overrunValue.Text = overruns.ToString(CultureInfo.InvariantCulture);
            dropsValue.Text = drops.ToString(CultureInfo.InvariantCulture) +
                " / 流丢失" + streamLost.ToString(CultureInfo.InvariantCulture) +
                " / PC序号缺口" + pcStreamGapSamples.ToString(CultureInfo.InvariantCulture) +
                " / USB恢复" + usbRecoveries.ToString(CultureInfo.InvariantCulture);
            UpdateAlarmColors(overruns, drops);
            if (streamLost != 0U || pcStreamGapSamples != 0U) dropsValue.ForeColor = Color.Firebrick;
            liveSummaryLabel.Text = string.Format(CultureInfo.InvariantCulture,
                "{0}道 | {1:N1} cps | Busy {2:N0} | 队列 {3} | Overrun {4} | USB恢复 {5} | SDO {6}",
                activeChannels, latestMeasuredRate, busy, queued, overruns, usbRecoveries,
                sdo == "low" ? "低" : sdo == "high" ? "高" : "未知");

            if (!firmwareMappingCompatible && !firmwareWarningShown)
            {
                firmwareWarningShown = true;
                AppendTerminal("[安全警告] 最终上位机要求fw=2.0.0-adaptive、2.5 V量程，且固件道址模式必须与界面一致。版本不匹配时不计算绝对幅度。\r\n");
            }

            if (!measurementBaselineValid)
            {
                measurementClock.Restart();
                measurementStartBusy = busy;
                measurementStartSamples = samples;
                measurementStartRangeOverflows = rangeOverflows;
                measurementStartUptimeMs = uptimeMs;
                measurementBaselineValid = true;
                measurementBaselinePending = false;
            }

            DateTime now = DateTime.Now;
            if (!counterEpochChanged && lastRateUptimeMs != 0U && uptimeMs != 0U && samples >= lastSamples)
            {
                uint deltaMs = unchecked(uptimeMs - lastRateUptimeMs);
                if (deltaMs >= 50U && deltaMs < 0x80000000U)
                {
                    uint delta = samples - lastSamples;
                    latestMeasuredRate = delta * 1000.0 / deltaMs;
                    rateValue.Text = latestMeasuredRate.ToString("N1", CultureInfo.InvariantCulture) + " cps";
                }
            }
            else if (!counterEpochChanged && lastRateTime != DateTime.MinValue && samples >= lastSamples)
            {
                double seconds = (now - lastRateTime).TotalSeconds;
                if (seconds > 0.05)
                {
                    uint delta = samples - lastSamples;
                    latestMeasuredRate = delta / seconds;
                    rateValue.Text = latestMeasuredRate.ToString("N1", CultureInfo.InvariantCulture) + " cps";
                }
            }
            lastSamples = samples;
            lastRateTime = now;
            lastRateUptimeMs = uptimeMs;
            liveSummaryLabel.Text = string.Format(CultureInfo.InvariantCulture,
                "{0}道 | {1:N1} cps | Busy {2:N0} | 队列 {3} | Overrun {4} | USB恢复 {5} | SDO {6}",
                activeChannels, latestMeasuredRate, busy, queued, overruns, usbRecoveries,
                sdo == "low" ? "低" : sdo == "high" ? "高" : "未知");
            if (peak > 0 && currentMetrics.PeakCount == 0) peakValue.Text = peak + " mV";
            UpdateDerivedMetrics();
        }

        private void UpdateAlarmColors(uint overruns, uint drops)
        {
            overrunValue.ForeColor = overruns == 0 ? Color.SeaGreen : Color.Firebrick;
            dropsValue.ForeColor = drops == 0 ? Color.SeaGreen : Color.DarkOrange;
        }

        private void UpdateChart()
        {
            Series series = spectrumChart.Series[0];
            ChartArea area = spectrumChart.ChartAreas[0];
            bool logarithmic = logScaleCheck.Checked;
            if (viewEndChannelExclusive <= viewStartChannel || viewEndChannelExclusive > activeChannels)
            {
                viewStartChannel = 0;
                viewEndChannelExclusive = activeChannels;
                SynchronizeSpectrumViewInputs();
            }
            ConfigureXAxisView();

            /* Always return to a valid linear state before replacing points.  Setting
             * IsLogarithmic while the automatic linear minimum is zero throws in the
             * WinForms chart control. */
            area.AxisY.IsLogarithmic = false;
            area.AxisY.Minimum = 0.0;
            area.AxisY.Maximum = Double.NaN;
            area.AxisY.Title = "计数";
            series.Points.Clear();
            long maximumPositive = 0;
            int visibleSpan = Math.Max(1, viewEndChannelExclusive - viewStartChannel);
            int displayGroup = Math.Max(1, visibleSpan / 8192);
            for (int start = viewStartChannel; start < viewEndChannelExclusive; start += displayGroup)
            {
                int end = Math.Min(viewEndChannelExclusive, start + displayGroup);
                long count = 0;
                for (int i = start; i < end; i++) if (spectrum[i] > count) count = spectrum[i];
                double displayChannel = start + (end - start - 1) / 2.0;
                if (count > maximumPositive) maximumPositive = count;
                if (!logarithmic) series.Points.AddXY(displayChannel, count);
                else
                {
                    DataPoint point = new DataPoint(displayChannel, count > 0 ? count : 1.0);
                    if (count <= 0) point.IsEmpty = true;
                    series.Points.Add(point);
                }
            }
            if (logarithmic)
            {
                area.AxisY.Minimum = 1.0;
                double maximum = maximumPositive > 0 ? Math.Pow(10.0, Math.Ceiling(Math.Log10(maximumPositive))) : 10.0;
                if (maximum <= 1.0) maximum = 10.0;
                area.AxisY.Maximum = maximum;
                area.AxisY.LogarithmBase = 10.0;
                area.AxisY.IsLogarithmic = true;
                area.AxisY.Title = "计数（log10；0计数道隐藏）";
            }
            else if (maximumPositive <= 0)
            {
                area.AxisY.Maximum = 1.0;
            }
            currentMetrics = SpectrumMetrics.Calculate(spectrum, Decimal.ToInt32(roiStartInput.Value), Decimal.ToInt32(roiEndInput.Value), activeChannels);
            peakValue.Text = string.Format(CultureInfo.InvariantCulture, "Ch{0:F2} / {1:F2}mV", currentMetrics.CentroidChannel, currentMetrics.PeakMv);
            fwhmValue.Text = currentMetrics.FwhmChannels > 0
                ? currentMetrics.FwhmChannels.ToString("F2", CultureInfo.InvariantCulture) + " ch / " + currentMetrics.QualityNote
                : "--";
            resolutionValue.Text = currentMetrics.ResolutionPercent > 0 ? currentMetrics.ResolutionPercent.ToString("F3", CultureInfo.InvariantCulture) + " %" : "--";
            resolutionValue.ForeColor = currentMetrics.ResolutionPercent > 0 && currentMetrics.ResolutionPercent < 1.0 ? Color.SeaGreen : Color.FromArgb(30, 60, 90);
            UpdateDerivedMetrics();
            if (spectrumCursorVisible && spectrumCursorReading != null)
            {
                spectrumCursorCard.Invalidate();
                spectrumChart.Invalidate();
            }
            chartDirty = false;
        }

        private void ServiceTimerTick(object sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            if ((serialPort == null || !serialPort.IsOpen) && now >= nextPortScan)
            {
                RefreshPorts(false);
                // A two-second inventory scan is effectively free, yet still makes
                // USB CDC insertion/removal feel immediate. The ComboBox is only
                // rebuilt when the deduplicated inventory actually changes.
                nextPortScan = now.AddSeconds(2);
            }
            if (chartDirty && !pauseDisplayCheck.Checked && !ConfigurationEditing()) UpdateChart();
            if (serialPort == null || !serialPort.IsOpen) return;
            if ((histogramTransfer || histogramRequestPending) && histogramRequestStarted != DateTime.MinValue &&
                (now - histogramRequestStarted).TotalSeconds > 6.0)
            {
                histogramTransfer = false;
                histogramRequestPending = false;
                incomingHistogram = null;
                incomingHistogramSeen = null;
                AppendTerminal("[告警] 4096道快照超时，已取消本次请求；采集仍继续。\r\n");
            }
            if (lastDeviceActivity != DateTime.MinValue &&
                (now - lastDeviceActivity).TotalSeconds >= 3.0 && now >= nextLinkResync)
            {
                /* Reassert only the firmware whitelist protocol. CNV and acquisition
                 * GPIO are never controlled by the PC. */
                histogramTransfer = false;
                histogramRequestPending = false;
                incomingHistogram = null;
                incomingHistogramSeen = null;
                SendSafe("channels " + activeChannels.ToString(CultureInfo.InvariantCulture));
                SendSafe("status");
                nextLinkResync = now.AddSeconds(3);
                connectionLabel.Text = "已连接但等待设备响应 " + serialPort.PortName;
                connectionLabel.ForeColor = Color.FromArgb(255, 205, 90);
                if (lastLinkWarning == DateTime.MinValue || (now - lastLinkWarning).TotalSeconds >= 12.0)
                {
                    AppendTerminal("[PC] 3秒未收到设备数据，已自动重新同步USB CDC协议。\r\n");
                    lastLinkWarning = now;
                }
            }
            if (now >= nextStatusRequest && !histogramTransfer && !histogramRequestPending)
            {
                SendSafe("status");
                nextStatusRequest = now.AddSeconds(2);
            }
            if (activeChannels == 4096 && autoHistogramCheck.Checked && now >= nextHistogramRequest &&
                !histogramTransfer && !histogramRequestPending)
            {
                RequestHistogram();
                nextHistogramRequest = now.AddSeconds((double)histogramIntervalInput.Value);
            }
        }

        private void RequestHistogram()
        {
            if (activeChannels != 4096)
            {
                if (!pauseDisplayCheck.Checked) UpdateChart();
                return;
            }
            histogramRequestPending = true;
            histogramRequestStarted = DateTime.Now;
            SendSafe("hist dump");
        }

        private void SendProfile()
        {
            if (profileBox.SelectedIndex == 0)
            {
                knownAmplitudeCheck.Checked = false;
                knownRateCheck.Checked = false;
                SendSafe("hist clear");
            }
            else
            {
                string value = profileBox.SelectedIndex == 1 ? "baseline" : profileBox.SelectedIndex == 2 ? "amplitude" : "frequency";
                if (profileBox.SelectedIndex == 1)
                {
                    generatorAmplitudeInput.Value = 500;
                    referenceRateInput.Value = 1000;
                }
                else if (profileBox.SelectedIndex == 2)
                {
                    generatorAmplitudeInput.Value = amplitudeInput.Value;
                    referenceRateInput.Value = 1000;
                }
                else
                {
                    generatorAmplitudeInput.Value = 100;
                    referenceRateInput.Value = frequencyInput.Value * 1000;
                }
                knownAmplitudeCheck.Checked = true;
                knownRateCheck.Checked = true;
                InvalidateMeasurementBaseline();
                SendSafe("profile " + value);
                if (generatorModeBox.SelectedIndex == 2)
                    AppendTerminal("[PC] 已载入赛题参考值，但发生器标定仍为未知；请选择High-Z或50Ω显示后才计算绝对精度。\r\n");
            }
            Array.Clear(spectrum, 0, spectrum.Length);
            InvalidateMeasurementBaseline();
            chartDirty = true;
        }

        private void ApplyThreshold()
        {
            int value = Decimal.ToInt32(thresholdInput.Value);
            if (value < 50 || value > 200) return;
            SendSafe("threshold " + value.ToString(CultureInfo.InvariantCulture));
        }

        private void ApplyAmplitude()
        {
            int value = Decimal.ToInt32(amplitudeInput.Value);
            if (value < 100 || value > 900) return;
            InvalidateMeasurementBaseline();
            SendSafe("amp " + value.ToString(CultureInfo.InvariantCulture));
            generatorAmplitudeInput.Value = value;
            knownAmplitudeCheck.Checked = true;
        }

        private void ApplyFrequency()
        {
            int value = Decimal.ToInt32(frequencyInput.Value);
            InvalidateMeasurementBaseline();
            SendSafe("freq " + value.ToString(CultureInfo.InvariantCulture));
            referenceRateInput.Value = value * 1000;
            knownRateCheck.Checked = true;
        }

        private bool IsWhitelistedCommand(string command)
        {
            return Regex.IsMatch(command, @"^(status|channels (4096|8192|16384|65536)|format b16|stream (on|off)|decimate 1|threshold (5[0-9]|[6-9][0-9]|1[0-9]{2}|200)|profile (baseline|amplitude|frequency)|amp ([1-8][0-9]{2}|900)|freq ([1-9]|[1-9][0-9]|100)|hist (clear|dump)|stats clear)$", RegexOptions.CultureInvariant);
        }

        private void SendSafe(string command)
        {
            if (!IsWhitelistedCommand(command))
            {
                AppendTerminal("[PC] 已阻止非白名单命令：" + command + "\r\n");
                return;
            }
            SerialPort port = serialPort;
            if (port == null || !port.IsOpen) return;
            try
            {
                port.Write(command + "\r\n");
                if (showProtocolCheck.Checked || command != "status")
                    AppendTerminal("[PC→设备] " + command + "\r\n");
            }
            catch (Exception ex) { SerialFailure(ex.Message); }
        }

        private void ClearStatistics()
        {
            if (MessageBox.Show(this, "这会清空PC端所选道数能谱及MCU采集统计，是否继续？", "清空确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            SerialPort port = serialPort;
            try { if (port != null && port.IsOpen) port.DiscardInBuffer(); } catch { }
            receiveBuffer.Length = 0;
            lock (pendingSerialLock)
            {
                pendingSerialText.Length = 0;
            }
            Array.Clear(spectrum, 0, spectrum.Length);
            rawSequenceValid = false;
            pcStreamGapSamples = 0U;
            currentMetrics = new SpectrumMetrics();
            InvalidateMeasurementBaseline();
            chartDirty = true;
            SendSafe("hist clear");
        }

        private void RecordClicked(object sender, EventArgs e)
        {
            if (recorder != null) StopRecording(); else StartRecording();
        }

        private void StartRecording()
        {
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Filter = "CSV记录 (*.csv)|*.csv|文本记录 (*.txt)|*.txt";
            dialog.FileName = "MCA_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv";
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                recordingPath = dialog.FileName;
                recorder = new StreamWriter(recordingPath, true, new UTF8Encoding(false));
                recorder.WriteLine("# pc_capture_started=" + DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
                recorder.Flush();
                recorderLinesSinceFlush = 0;
                recordButton.Text = "停止记录";
                recordLabel.Text = recordingPath;
                recordLabel.ForeColor = Color.LightGreen;
            }
            catch (Exception ex) { MessageBox.Show(this, "无法创建记录文件：" + ex.Message, "记录失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void StopRecording()
        {
            if (recorder != null)
            {
                try { recorder.WriteLine("# pc_capture_stopped=" + DateTime.Now.ToString("o", CultureInfo.InvariantCulture)); recorder.Flush(); recorder.Dispose(); }
                catch { }
            }
            recorder = null;
            recorderLinesSinceFlush = 0;
            recordingPath = null;
            recordButton.Text = "开始记录";
            recordLabel.Text = "未记录";
            recordLabel.ForeColor = Color.White;
        }

        private void ExportSpectrum()
        {
            SaveFileDialog dialog = new SaveFileDialog { Filter = "CSV能谱 (*.csv)|*.csv", FileName = "Spectrum_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                using (StreamWriter writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(false)))
                {
                    writer.WriteLine("channel,count,adc_bin_center_mV,adc_lower_edge_mV,adc_upper_edge_mV,input_equiv_center_mV");
                    for (int i = 0; i < activeChannels; i++)
                    {
                        double adcCenter = (i + 0.5) * SpectrumMetrics.AdcSpectrumFullScaleMv / activeChannels;
                        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2:F6},{3:F6},{4:F6},{5:F6}",
                            i, spectrum[i], adcCenter, i * SpectrumMetrics.AdcSpectrumFullScaleMv / activeChannels,
                            (i + 1.0) * SpectrumMetrics.AdcSpectrumFullScaleMv / activeChannels, BoardInputFromAdcMv(adcCenter)));
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show(this, "导出失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private IEnumerable<string> ExportMetadata()
        {
            double actual;
            bool hasActual = TryGetActualInputMv(out actual);
            yield return "created=" + DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            yield return "signal_mode=" + (profileBox.SelectedItem == null ? "unknown" : profileBox.SelectedItem.ToString());
            yield return "board_termination=" + (terminationBox.SelectedItem == null ? "unknown" : terminationBox.SelectedItem.ToString());
            yield return "generator_display_mode=" + (generatorModeBox.SelectedItem == null ? "unknown" : generatorModeBox.SelectedItem.ToString());
            yield return "source_impedance_ohm=" + ((double)sourceImpedanceInput.Value).ToString("F3", CultureInfo.InvariantCulture);
            yield return "generator_display_mV=" + (knownAmplitudeCheck.Checked ? ((double)generatorAmplitudeInput.Value).ToString("F3", CultureInfo.InvariantCulture) : "unknown");
            yield return "actual_board_input_mV=" + (hasActual ? actual.ToString("F6", CultureInfo.InvariantCulture) : "unknown");
            double sourceReference;
            yield return "source_open_reference_mV=" + (TryGetSourceReferenceMv(out sourceReference) ? sourceReference.ToString("F6", CultureInfo.InvariantCulture) : "unknown");
            double measuredBoard = BoardInputFromAdcMv(currentMetrics.PeakMv);
            bool hasMeasuredBoard = currentMetrics.PeakCount > 0 && !Double.IsNaN(measuredBoard) && !Double.IsInfinity(measuredBoard);
            yield return "measured_board_input_mV=" + (hasMeasuredBoard ? measuredBoard.ToString("F6", CultureInfo.InvariantCulture) : "unknown");
            yield return "measured_source_open_equiv_mV=" + (hasMeasuredBoard ? SourceEquivalentFromBoardMv(measuredBoard).ToString("F6", CultureInfo.InvariantCulture) : "unknown");
            yield return "front_end_gain=" + ((double)analogGainInput.Value).ToString("F6", CultureInfo.InvariantCulture);
            yield return "amplitude_calibration=" + (useMeasuredCurveCheck.Checked ? "system_standard_editable_inverse_linear" : "user_configurable_linear_gain_only");
            yield return "calibration_slope=" + ((double)linearitySlopeInput.Value).ToString("F9", CultureInfo.InvariantCulture);
            yield return "calibration_intercept_mV=" + ((double)linearityInterceptInput.Value).ToString("F9", CultureInfo.InvariantCulture);
            yield return "calibration_reference=system_standard_100kHz_linearity; fit measured=slope*actual+intercept; R2=0.99998297649";
            yield return "firmware_adc_spectrum_fs_mV=" + firmwareAdcSpectrumFsMv.ToString(CultureInfo.InvariantCulture);
            yield return "firmware_histogram_channels=" + firmwareHistogramChannels.ToString(CultureInfo.InvariantCulture);
            yield return "pc_active_channels=" + activeChannels.ToString(CultureInfo.InvariantCulture);
            yield return "metric_smoothing_bins=" + currentMetrics.MetricSmoothingBins.ToString(CultureInfo.InvariantCulture);
            yield return "metric_quality_note=" + currentMetrics.QualityNote;
            yield return "firmware_frontend_gain_milli=" + firmwareFrontendGainMilli.ToString(CultureInfo.InvariantCulture);
            yield return "firmware_mapping_compatible=" + firmwareMappingCompatible.ToString(CultureInfo.InvariantCulture);
            yield return "firmware_queue_depth=" + latestQueueDepth.ToString(CultureInfo.InvariantCulture);
            yield return "firmware_usb_recoveries=" + latestUsbRecoveries.ToString(CultureInfo.InvariantCulture);
            yield return "firmware_range_overflows=" + latestRangeOverflows.ToString(CultureInfo.InvariantCulture);
            yield return "firmware_uptime_ms=" + latestUptimeMs.ToString(CultureInfo.InvariantCulture);
            yield return "adc_offset_mV=" + ((double)analogOffsetInput.Value).ToString("F3", CultureInfo.InvariantCulture);
            yield return "roi=" + roiStartInput.Value.ToString(CultureInfo.InvariantCulture) + "-" + roiEndInput.Value.ToString(CultureInfo.InvariantCulture);
            yield return "display_scale=" + (logScaleCheck.Checked ? "log10_zero_bins_hidden" : "linear");
            yield return "centroid_channel=" + currentMetrics.CentroidChannel.ToString("F6", CultureInfo.InvariantCulture);
            yield return "adc_peak_mV=" + currentMetrics.PeakMv.ToString("F6", CultureInfo.InvariantCulture);
            yield return "estimated_background_counts_per_bin=" + currentMetrics.BackgroundCountsPerBin.ToString("F6", CultureInfo.InvariantCulture);
            yield return "net_peak_area=" + currentMetrics.NetPeakArea.ToString("F6", CultureInfo.InvariantCulture);
            yield return "fwhm_channel=" + currentMetrics.FwhmChannels.ToString("F6", CultureInfo.InvariantCulture);
            yield return "resolution_percent=" + currentMetrics.ResolutionPercent.ToString("F6", CultureInfo.InvariantCulture);
            yield return "input_amplitude_resolution_valid=True";
            yield return "statistical_precision_percent=" + currentMetrics.StatisticalPrecisionPercent.ToString("F6", CultureInfo.InvariantCulture);
            yield return "measured_rate_Hz=" + latestMeasuredRate.ToString("F6", CultureInfo.InvariantCulture);
            yield return "rate_formula=delta_samples/delta_mcu_uptime";
            yield return "pass_formula=delta_samples/(reference_Hz*delta_mcu_uptime_seconds)*100; same synchronized counter window; unclamped";
            yield return "processing_efficiency_formula=delta_samples/delta_busy*100";
            yield return "safety=CNV is hardware-generated; software does not switch JP1 or drive protected grounded pins";
        }

        private void ExportSpectrumText()
        {
            SaveFileDialog dialog = new SaveFileDialog { Filter = "能谱文本 (*.txt)|*.txt", FileName = "Spectrum_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                using (StreamWriter writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(false)))
                {
                    writer.WriteLine("# STM32G474 + AD7980 adaptive spectrum; channels=" + activeChannels.ToString(CultureInfo.InvariantCulture) + "; display_LSB_mV=" + (SpectrumMetrics.AdcSpectrumFullScaleMv / activeChannels).ToString("F9", CultureInfo.InvariantCulture));
                    foreach (string item in ExportMetadata()) writer.WriteLine("# " + item);
                    writer.WriteLine("# channel\tcount\tadc_bin_center_mV\tadc_lower_edge_mV\tadc_upper_edge_mV\tinput_equiv_center_mV");
                    for (int i = 0; i < activeChannels; i++)
                    {
                        double adcCenter = (i + 0.5) * SpectrumMetrics.AdcSpectrumFullScaleMv / activeChannels;
                        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0}\t{1}\t{2:F6}\t{3:F6}\t{4:F6}\t{5:F6}",
                            i, spectrum[i], adcCenter, i * SpectrumMetrics.AdcSpectrumFullScaleMv / activeChannels,
                            (i + 1.0) * SpectrumMetrics.AdcSpectrumFullScaleMv / activeChannels, BoardInputFromAdcMv(adcCenter)));
                    }
                }
                AppendTerminal("[PC] 能谱TXT已导出：" + dialog.FileName + "\r\n");
            }
            catch (Exception ex) { MessageBox.Show(this, "TXT导出失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void CaptureTestPoint()
        {
            if (chartDirty) UpdateChart();
            if (currentMetrics.PeakCount <= 0)
            {
                MessageBox.Show(this, "当前ROI内没有有效谱峰，不能记录测试点。", "没有数据", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            double actual;
            if (!TryGetActualInputMv(out actual)) actual = Double.NaN;
            double measured = BoardInputFromAdcMv(currentMetrics.PeakMv);
            uint overflowDelta = latestRangeOverflows >= measurementStartRangeOverflows
                ? latestRangeOverflows - measurementStartRangeOverflows : latestRangeOverflows;
            double accuracy = firmwareMappingCompatible && overflowDelta == 0U && !Double.IsNaN(actual) && actual > 0.0
                ? Math.Abs(measured - actual) * 100.0 / actual : Double.NaN;
            double referenceRate = knownRateCheck.Checked ? (double)referenceRateInput.Value : Double.NaN;
            double elapsed = Double.NaN, measuredRate = Double.NaN, pass = Double.NaN, efficiency = Double.NaN, triggerAcceptance = Double.NaN;
            uint sampleDelta = 0U, busyDelta = 0U;
            bool validWindow = measurementBaselineValid && InputMath.WindowMetrics(
                measurementStartSamples, measurementStartBusy, measurementStartUptimeMs,
                latestSamples, latestBusy, latestUptimeMs,
                Double.IsNaN(referenceRate) ? 0.0 : referenceRate,
                out elapsed, out sampleDelta, out busyDelta, out measuredRate, out pass, out efficiency, out triggerAcceptance);
            if (!validWindow) elapsed = measuredRate = pass = efficiency = triggerAcceptance = Double.NaN;
            string passQuality = !validWindow ? "invalid_or_reset_window" : elapsed < 1.0 ? "window_too_short" : "same_window_mcu_uptime";
            if (!Double.IsNaN(referenceRate) && elapsed < 1.0)
            {
                MessageBox.Show(this, "通过率统计窗不足1秒。请等待状态栏的测量时长达到1秒后再记录测试点。", "统计窗过短", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            TestPoint point = new TestPoint
            {
                Time = DateTime.Now,
                SignalMode = profileBox.SelectedItem == null ? "unknown" : profileBox.SelectedItem.ToString(),
                Termination = terminationBox.SelectedItem == null ? "unknown" : terminationBox.SelectedItem.ToString(),
                GeneratorDisplayMv = knownAmplitudeCheck.Checked ? (double)generatorAmplitudeInput.Value : Double.NaN,
                ActualInputMv = actual,
                MeasuredInputMv = measured,
                AdcPeakMv = currentMetrics.PeakMv,
                FwhmChannels = currentMetrics.FwhmChannels,
                ResolutionPercent = currentMetrics.ResolutionPercent,
                AccuracyPercent = accuracy,
                ReferenceRateHz = referenceRate,
                MeasuredRateHz = measuredRate,
                PassRatePercent = pass,
                ProcessingEfficiencyPercent = efficiency,
                Counts = currentMetrics.NetPeakArea,
                MeasurementWindowSeconds = elapsed,
                WindowSamples = sampleDelta,
                WindowBusy = busyDelta,
                TriggerAcceptancePercent = triggerAcceptance,
                PassQuality = passQuality
            };
            testPoints.Add(point);
            RefreshTestPointGrid();
        }

        private static string NumberOrUnknown(double value, string format)
        {
            return Double.IsNaN(value) || Double.IsInfinity(value) ? "unknown" : value.ToString(format, CultureInfo.InvariantCulture);
        }

        private void RefreshTestPointGrid()
        {
            testPointGrid.Rows.Clear();
            for (int i = 0; i < testPoints.Count; i++)
            {
                TestPoint p = testPoints[i];
                testPointGrid.Rows.Add(i + 1, NumberOrUnknown(p.ActualInputMv, "F3"), NumberOrUnknown(p.MeasuredInputMv, "F3"),
                    NumberOrUnknown(p.ResolutionPercent, "F3"), NumberOrUnknown(p.PassRatePercent, "F3"), NumberOrUnknown(p.MeasuredRateHz, "F1"));
            }
            UpdateLinearitySummary();
        }

        private void UpdateLinearitySummary()
        {
            double slope, intercept, r2, maxNl, maxSpanNl, maxResidualMv;
            if (!InputMath.LinearFit(testPoints, out slope, out intercept, out r2, out maxNl, out maxSpanNl, out maxResidualMv))
            {
                linearitySummary.Text = "测试点不足。幅度扫描至少记录3个已知且不同的板端实际幅度点；未知输入点仍可保存，但不参与线性拟合。";
                return;
            }
            linearitySummary.Text = string.Format(CultureInfo.InvariantCulture,
                "线性拟合：测得输入 = {0:F6} × 实际输入 + {1:F4} mV\r\nR²={2:F9}；1−R²={3:E3}；跨度归一化最大非线性={4:F4}%（最大残差{5:F4}mV）；点相对最大值={6:F4}%",
                slope, intercept, r2, 1.0 - r2, maxSpanNl, maxResidualMv, maxNl);
            linearitySummary.ForeColor = (1.0 - r2) < 0.001 ? Color.SeaGreen : Color.DarkOrange;
        }

        private void RemoveLastTestPoint()
        {
            if (testPoints.Count == 0) return;
            testPoints.RemoveAt(testPoints.Count - 1);
            RefreshTestPointGrid();
        }

        private void ClearTestPoints()
        {
            if (testPoints.Count > 0 && MessageBox.Show(this, "确定清空所有PC端测试点吗？不会修改MCU能谱。", "清空测试点", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            testPoints.Clear();
            RefreshTestPointGrid();
        }

        private void ExportTestReport(bool textFormat)
        {
            if (testPoints.Count == 0)
            {
                MessageBox.Show(this, "请先记录至少一个测试点。", "没有测试点", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SaveFileDialog dialog = new SaveFileDialog
            {
                Filter = textFormat ? "测试报告文本 (*.txt)|*.txt" : "测试报告CSV (*.csv)|*.csv",
                FileName = "MCA_TestReport_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + (textFormat ? ".txt" : ".csv")
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                using (StreamWriter writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(false)))
                {
                    foreach (string item in ExportMetadata()) writer.WriteLine("# " + item);
                    writer.WriteLine(textFormat
                        ? "序号\t时间\t模式\t端接\t发生器显示mV\t板端实际mV\t测得输入mV\tADC峰mV\tFWHM_ch\t分辨率%\t幅度误差%\t参考Hz\t实测Hz\t通过率%\t处理效率%\t峰区计数\t同步窗s\t样本增量\tBusy增量\t触发接受率%\t通过率质量"
                        : "index,time,mode,termination,generator_display_mV,actual_input_mV,measured_input_mV,adc_peak_mV,fwhm_ch,resolution_percent,accuracy_percent,reference_Hz,measured_Hz,pass_percent,processing_efficiency_percent,net_peak_area,measurement_window_s,sample_delta,busy_delta,trigger_acceptance_percent,pass_quality");
                    string separator = textFormat ? "\t" : ",";
                    for (int i = 0; i < testPoints.Count; i++)
                    {
                        TestPoint p = testPoints[i];
                        string[] fields = new string[] { (i + 1).ToString(CultureInfo.InvariantCulture), p.Time.ToString("o", CultureInfo.InvariantCulture),
                            p.SignalMode.Replace(",", " "), p.Termination.Replace(",", " "), NumberOrUnknown(p.GeneratorDisplayMv, "F6"),
                            NumberOrUnknown(p.ActualInputMv, "F6"), NumberOrUnknown(p.MeasuredInputMv, "F6"), NumberOrUnknown(p.AdcPeakMv, "F6"),
                            NumberOrUnknown(p.FwhmChannels, "F6"), NumberOrUnknown(p.ResolutionPercent, "F6"), NumberOrUnknown(p.AccuracyPercent, "F6"),
                            NumberOrUnknown(p.ReferenceRateHz, "F6"), NumberOrUnknown(p.MeasuredRateHz, "F6"), NumberOrUnknown(p.PassRatePercent, "F6"),
                            NumberOrUnknown(p.ProcessingEfficiencyPercent, "F6"), p.Counts.ToString("F3", CultureInfo.InvariantCulture),
                            NumberOrUnknown(p.MeasurementWindowSeconds, "F6"), p.WindowSamples.ToString(CultureInfo.InvariantCulture),
                            p.WindowBusy.ToString(CultureInfo.InvariantCulture), NumberOrUnknown(p.TriggerAcceptancePercent, "F6"), p.PassQuality ?? "unknown" };
                        writer.WriteLine(string.Join(separator, fields));
                    }
                    double slope, intercept, r2, maxNl, maxSpanNl, maxResidualMv;
                    if (InputMath.LinearFit(testPoints, out slope, out intercept, out r2, out maxNl, out maxSpanNl, out maxResidualMv))
                    {
                        writer.WriteLine("# linear_slope=" + slope.ToString("F9", CultureInfo.InvariantCulture));
                        writer.WriteLine("# linear_intercept_mV=" + intercept.ToString("F9", CultureInfo.InvariantCulture));
                        writer.WriteLine("# R_squared=" + r2.ToString("F12", CultureInfo.InvariantCulture));
                        writer.WriteLine("# one_minus_R_squared=" + (1.0 - r2).ToString("E6", CultureInfo.InvariantCulture));
                        writer.WriteLine("# max_relative_nonlinearity_percent=" + maxNl.ToString("F6", CultureInfo.InvariantCulture));
                        writer.WriteLine("# max_span_nonlinearity_percent=" + maxSpanNl.ToString("F6", CultureInfo.InvariantCulture));
                        writer.WriteLine("# max_residual_mV=" + maxResidualMv.ToString("F6", CultureInfo.InvariantCulture));
                    }
                }
                AppendTerminal("[PC] 测试报告已导出：" + dialog.FileName + "\r\n");
            }
            catch (Exception ex) { MessageBox.Show(this, "报告导出失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void SaveSpectrumImage()
        {
            SaveFileDialog dialog = new SaveFileDialog { Filter = "PNG图像 (*.png)|*.png", FileName = "Spectrum_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try { spectrumChart.SaveImage(dialog.FileName, ChartImageFormat.Png); }
            catch (Exception ex) { MessageBox.Show(this, "保存图片失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void AppendTerminal(string text)
        {
            int level = logLevelBox.SelectedIndex < 0 ? 1 : logLevelBox.SelectedIndex;
            string lower = text.ToLowerInvariant();
            bool alert = lower.Contains("告警") || lower.Contains("警告") || lower.Contains("错误") ||
                lower.Contains("失败") || lower.Contains("丢失") || lower.Contains("overrun") ||
                lower.Contains("drop") || lower.Contains("blocked") || lower.Contains("异常") || lower.Contains("超时");
            bool protocolNoise = lower.StartsWith("# status", StringComparison.Ordinal) ||
                lower.StartsWith("[pc→设备] status", StringComparison.Ordinal) ||
                lower.StartsWith("[pc->设备] status", StringComparison.Ordinal);
            if (level == 0 && !alert) return;
            if (level == 1 && protocolNoise) return;
            terminal.AppendText(text);
            terminalLines += text.Count(delegate(char c) { return c == '\n'; });
            if (terminalLines > 250)
            {
                string content = terminal.Text;
                int cut = content.IndexOf('\n', content.Length / 2);
                if (cut > 0) terminal.Text = content.Substring(cut + 1);
                terminalLines = terminal.Lines.Length;
            }
            terminal.SelectionStart = terminal.TextLength;
            terminal.ScrollToCaret();
        }

        internal void LoadPreviewData()
        {
            controlGroup.Enabled = true;
            recordButton.Enabled = true;
            connectionLabel.Text = "演示数据";
            connectionLabel.ForeColor = Color.FromArgb(126, 232, 171);
            ProcessLine("channel,count");
            for (int channel = 0; channel < activeChannels; channel++)
            {
                double mainPeak = 1800.0 * Math.Exp(-0.5 * Math.Pow((channel - 2048.0) / 32.0, 2.0));
                double smallPeak = 380.0 * Math.Exp(-0.5 * Math.Pow((channel - 1220.0) / 48.0, 2.0));
                long count = (long)Math.Round(2.0 + mainPeak + smallPeak);
                ProcessLine(channel.ToString(CultureInfo.InvariantCulture) + "," + count.ToString(CultureInfo.InvariantCulture));
            }
            ProcessLine("# histogram end");
            ProcessLine("# status fw=2.0.0-adaptive uptime_ms=128000 samples=128000 busy=128004 recoveries=2 postread_low=0 sdo=high overruns=0 queued=0 range_overflows=0 tx_drops=0 usb_recoveries=0 stream_lost_samples=0 mean_mV=996 peak_mV=1014 expected_mV=500 expected_Hz=1000 threshold_mV=100 decimate=1 stream=off format=b16 hist_channels=4096 adc_spectrum_fs_mV=2500 frontend_gain_milli=2000 spectrum_mode=mcu_hist4096 wire=hist_csv");
            UpdateChart();
        }

        /* 指标卡滚轮预览：把会换行的指标设为超长文本，用于无头渲染验证。 */
        internal void PrepareMetricScrollPreview()
        {
            busyValue.Text = "128,004 / 队列0 / 恢复2 / SDO高";
            dropsValue.Text = "0 / 流丢失0 / PC序号缺口10 / USB恢复0";
            fwhmValue.Text = "75.33 ch / 原始半高宽";
            inputPeakValue.Text = "623.757 mV / 623.726 mV";
        }

        internal void ScrollMetricForPreview(int delta)
        {
            ((ScrollableMetricLabel)dropsValue).ProcessWheelDelta(delta);
        }

        internal void SaveMetricScrollPreview(string path)
        {
            ScrollableMetricLabel drops = (ScrollableMetricLabel)dropsValue;
            using (Bitmap bitmap = new Bitmap(Math.Max(1, drops.Width), Math.Max(1, drops.Height)))
            {
                drops.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        internal string MetricScrollDiagnostics()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "busy needs={0} offset={1}; drops needs={2} offset={3}; fwhm needs={4} offset={5}",
                ((ScrollableMetricLabel)busyValue).NeedsScrolling, ((ScrollableMetricLabel)busyValue).ScrollOffset,
                ((ScrollableMetricLabel)dropsValue).NeedsScrolling, ((ScrollableMetricLabel)dropsValue).ScrollOffset,
                ((ScrollableMetricLabel)fwhmValue).NeedsScrolling, ((ScrollableMetricLabel)fwhmValue).ScrollOffset);
        }

        internal void ShowSpectrumCursorPreview()
        {
            spectrumChart.Update();
            Application.DoEvents();
            int channel = Math.Min(activeChannels - 1, 2048);
            RectangleF plot = SpectrumPlotRectangle();
            int x = (int)Math.Round(plot.Left + plot.Width * (channel + 0.5) / activeChannels);
            int y = (int)Math.Round(plot.Top + plot.Height * 0.38F);
            UpdateSpectrumCursor(new Point(x, y));
            spectrumCursorPinned = true;
            spectrumCursorCard.Invalidate();
            spectrumChart.Invalidate();
            Application.DoEvents();
        }

        internal string SpectrumCursorDiagnostics()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "visible={0}; pinned={1}; channel={2}; card={3}; plot={4}", spectrumCursorVisible,
                spectrumCursorPinned, spectrumCursorChannel, spectrumCursorCard.Bounds, SpectrumPlotRectangle());
        }

        internal void ExerciseLogScaleForSelfTest()
        {
            serviceTimer.Stop();
            suppressDisplayDialogs = true;
            Array.Clear(spectrum, 0, spectrum.Length);
            for (int i = 0; i < 12; i++) logScaleCheck.Checked = !logScaleCheck.Checked;
            spectrum[0] = 1;
            spectrum[320] = 12;
            spectrum[512] = 1000;
            spectrum[700] = 3;
            chartDirty = true;
            for (int i = 0; i < 12; i++) logScaleCheck.Checked = !logScaleCheck.Checked;
            logScaleCheck.Checked = true;
            UpdateChart();
            ChartArea area = spectrumChart.ChartAreas[0];
            if (!area.AxisY.IsLogarithmic || area.AxisY.Minimum <= 0.0 || area.AxisY.Maximum <= area.AxisY.Minimum)
                throw new InvalidOperationException("logarithmic axis state invalid");
            logScaleCheck.Checked = false;
            UpdateChart();
            if (area.AxisY.IsLogarithmic || area.AxisY.Minimum != 0.0)
                throw new InvalidOperationException("linear axis rollback invalid");
        }

        internal void ExerciseSpectrumViewForSelfTest()
        {
            serviceTimer.Stop();
            suppressDisplayDialogs = true;
            Array.Clear(spectrum, 0, spectrum.Length);
            spectrum[740] = 20;
            spectrum[741] = 50;
            spectrum[742] = 100;
            spectrum[743] = 50;
            spectrum[744] = 20;
            ApplySpectrumView(700, 800);
            ChartArea area = spectrumChart.ChartAreas[0];
            if (area.AxisX.Minimum != 700.0 || area.AxisX.Maximum != 800.0 || spectrumChart.Series[0].Points.Count < 90)
                throw new InvalidOperationException("spectrum view range invalid");
            int snapped = FindLocalPeak(spectrum, 739, 8, viewStartChannel, viewEndChannelExclusive);
            CursorPeakMetrics local = CalculateCursorPeakMetrics(spectrum, snapped, viewStartChannel, viewEndChannelExclusive);
            if (snapped != 742 || local == null || Math.Abs(local.FwhmChannels - 2.0) > 0.01)
                throw new InvalidOperationException("cursor peak snap/FWHM invalid");
            ResetSpectrumView();
            if (area.AxisX.Minimum != 0.0 || area.AxisX.Maximum != activeChannels)
                throw new InvalidOperationException("spectrum full-view reset invalid");
        }

        internal void ExerciseChannelSwitchForSelfTest()
        {
            serviceTimer.Stop();
            suppressDisplayDialogs = true;
            int[] expected = { 4096, 8192, 16384, 65536, 4096 };
            int[] selected = { 0, 1, 2, 3, 0 };
            for (int i = 0; i < expected.Length; i++)
            {
                logScaleCheck.Checked = (i % 2) == 1;
                channelBox.SelectedIndex = selected[i];
                ApplyChannelMode();
                ChartArea area = spectrumChart.ChartAreas[0];
                if (activeChannels != expected[i] || viewStartChannel != 0 || viewEndChannelExclusive != expected[i] ||
                    area.AxisX.Minimum != 0.0 || area.AxisX.Maximum != expected[i] ||
                    Decimal.ToInt32(viewEndInput.Value) != expected[i] - 1)
                    throw new InvalidOperationException("channel-mode/view atomic reset invalid");
                if (area.AxisY.IsLogarithmic != logScaleCheck.Checked)
                    throw new InvalidOperationException("channel-mode/log-axis state invalid");
            }
            logScaleCheck.Checked = false;
            UpdateChart();
        }

        /* 验证脉冲通过率/处理效率的颜色规则：通过率≥99%且≤100.1%绿色，
         * 处理效率≥99.9%绿色；通过率低于99%或超出参考频率上限保持橙色。 */
        internal void ExercisePassRateColorsForSelfTest()
        {
            serviceTimer.Stop();
            suppressDisplayDialogs = true;
            knownRateCheck.Checked = true;
            referenceRateInput.Value = 1000;
            ProcessLine("# status fw=2.0.0-adaptive uptime_ms=10000 samples=10000 busy=10000 recoveries=0 postread_low=0 sdo=high overruns=0 queued=0 range_overflows=0 tx_drops=0 usb_recoveries=0 stream_lost_samples=0 mean_mV=1000 peak_mV=1000 expected_mV=500 expected_Hz=1000 threshold_mV=100 decimate=1 stream=off format=b16 hist_channels=4096 adc_spectrum_fs_mV=2500 frontend_gain_milli=2000 spectrum_mode=mcu_hist4096 wire=hist_csv");
            // 1s 窗口：+995 样本 → 通过率 99.5%，效率 100% → 两项都绿
            ProcessLine("# status fw=2.0.0-adaptive uptime_ms=11000 samples=10995 busy=10995 recoveries=0 postread_low=0 sdo=high overruns=0 queued=0 range_overflows=0 tx_drops=0 usb_recoveries=0 stream_lost_samples=0 mean_mV=1000 peak_mV=1000 expected_mV=500 expected_Hz=1000 threshold_mV=100 decimate=1 stream=off format=b16 hist_channels=4096 adc_spectrum_fs_mV=2500 frontend_gain_milli=2000 spectrum_mode=mcu_hist4096 wire=hist_csv");
            if (passRateValue.ForeColor != Color.SeaGreen)
                throw new InvalidOperationException("99.5% pass rate should be green");
            if (processingValue.ForeColor != Color.SeaGreen)
                throw new InvalidOperationException("100% processing efficiency should be green");
            // 2s 窗口累计 +1960 样本 → 通过率 98.0%（黄），busy 多 5 → 效率 99.75%（非绿）
            ProcessLine("# status fw=2.0.0-adaptive uptime_ms=12000 samples=11960 busy=11965 recoveries=0 postread_low=0 sdo=high overruns=0 queued=0 range_overflows=0 tx_drops=0 usb_recoveries=0 stream_lost_samples=0 mean_mV=1000 peak_mV=1000 expected_mV=500 expected_Hz=1000 threshold_mV=100 decimate=1 stream=off format=b16 hist_channels=4096 adc_spectrum_fs_mV=2500 frontend_gain_milli=2000 spectrum_mode=mcu_hist4096 wire=hist_csv");
            if (passRateValue.ForeColor != Color.DarkOrange)
                throw new InvalidOperationException("98.0% pass rate should stay orange");
            if (processingValue.ForeColor != TextPrimary)
                throw new InvalidOperationException("99.75% processing efficiency should not be green");
            // 3s 窗口累计 +3005 样本 → 通过率 100.17% 超出参考上限（黄）
            ProcessLine("# status fw=2.0.0-adaptive uptime_ms=13000 samples=13005 busy=13010 recoveries=0 postread_low=0 sdo=high overruns=0 queued=0 range_overflows=0 tx_drops=0 usb_recoveries=0 stream_lost_samples=0 mean_mV=1000 peak_mV=1000 expected_mV=500 expected_Hz=1000 threshold_mV=100 decimate=1 stream=off format=b16 hist_channels=4096 adc_spectrum_fs_mV=2500 frontend_gain_milli=2000 spectrum_mode=mcu_hist4096 wire=hist_csv");
            if (passRateValue.ForeColor != Color.DarkOrange)
                throw new InvalidOperationException("pass rate above the reference cap should stay orange");
            knownRateCheck.Checked = false;
        }

        private void MainFormClosing(object sender, FormClosingEventArgs e)
        {
            serviceTimer.Stop();
            Disconnect();
        }
    }

    internal static class NumericExtensions
    {
        public static void Suffix(this NumericUpDown input, string text)
        {
            input.Tag = text;
        }
    }

    internal static class SelfTest
    {
        public static void Run()
        {
            List<string> results = new List<string>();
            Dictionary<string, string> status;
            if (!ProtocolParser.TryParseStatus("# status fw=1.8.0-full16HR uptime_ms=10000 samples=123 busy=130 recoveries=2 postread_low=1 sdo=high overruns=0 queued=2 range_overflows=0 tx_drops=2 usb_recoveries=1 stream_lost_samples=0 mean_mV=1000 peak_mV=1020 expected_mV=500 expected_Hz=1000 threshold_mV=100 decimate=1 stream=on format=b16 hist_channels=65536 adc_spectrum_fs_mV=2500 frontend_gain_milli=2000 spectrum_mode=host_raw16 wire=base64_crc16", out status))
                throw new InvalidOperationException("status parser failed");
            if (ProtocolParser.GetUInt(status, "busy", 0) != 130 || ProtocolParser.GetUInt(status, "recoveries", 0) != 2 || ProtocolParser.GetUInt(status, "hist_channels", 0) != 65536 || ProtocolParser.GetUInt(status, "adc_spectrum_fs_mV", 0) != 2500)
                throw new InvalidOperationException("status value failed");
            results.Add("status parser PASS");

            uint firstSequence;
            ushort[] rawCodes;
            if (!ProtocolParser.TryParseRaw16Batch("@B16,100,3,2845,AAAAgP//", out firstSequence, out rawCodes) ||
                firstSequence != 100U || rawCodes.Length != 3 || rawCodes[0] != 0x0000 || rawCodes[1] != 0x8000 || rawCodes[2] != 0xFFFF)
                throw new InvalidOperationException("RAW16 batch parser failed");
            results.Add("RAW16 batch parser PASS");

            SampleRecord sample;
            if (!ProtocolParser.TryParseSample("10,20,26214,1000,2048,500,1000,75,0,0", out sample) || sample.Channel != 2048 || sample.VoltageMv != 1000)
                throw new InvalidOperationException("sample parser failed");
            results.Add("sample parser PASS");

            int channel;
            long count;
            if (!ProtocolParser.TryParseHistogramBin("65535,456", out channel, out count) || channel != 65535 || count != 456)
                throw new InvalidOperationException("histogram parser failed");
            results.Add("histogram parser PASS");

            long[] bins = new long[SpectrumMetrics.HistogramChannels];
            for (int i = 31960; i <= 32040; i++) bins[i] = 100 - Math.Abs(32000 - i) * 5 / 2;
            SpectrumMetrics metrics = SpectrumMetrics.Calculate(bins);
            if (Math.Abs(metrics.PeakChannel - 32000) > 2 || Math.Abs(metrics.FwhmChannels - 40.0) > 3.0 ||
                Math.Abs(metrics.PeakMv - ((32000.5 * 2500.0) / SpectrumMetrics.HistogramChannels)) > 0.2)
                throw new InvalidOperationException("spectrum metrics failed");
            results.Add("robust spectrum metrics and 0-2.5V full16 ADC mapping PASS");

            SpectrumCursorReading cursor4096 = SpectrumCursorReading.FromChannel(2048, 4096);
            SpectrumCursorReading cursor65536 = SpectrumCursorReading.FromChannel(65535, 65536);
            if (cursor4096.RawStart != 32768 || cursor4096.RawEnd != 32783 ||
                Math.Abs(cursor4096.AdcCenterMv - 1250.30517578125) > 1e-9 ||
                Math.Abs(cursor4096.ChannelWidthUv - 610.3515625) > 1e-9 ||
                cursor65536.RawStart != 65535 || cursor65536.RawEnd != 65535 ||
                Math.Abs(cursor65536.AdcCenterMv - 2499.980926513671875) > 1e-9)
                throw new InvalidOperationException("spectrum cursor channel/raw/voltage mapping failed");
            results.Add("spectrum cursor exact channel/raw-code/ADC-voltage mapping PASS");

            long[] baselineBins = Enumerable.Repeat(20L, SpectrumMetrics.HistogramChannels).ToArray();
            baselineBins[100] = 2000;
            for (int i = 31960; i <= 32040; i++) baselineBins[i] = 120 - Math.Abs(32000 - i) * 5 / 2;
            SpectrumMetrics baselineMetrics = SpectrumMetrics.Calculate(baselineBins, 31800, 32200);
            if (Math.Abs(baselineMetrics.PeakChannel - 32000) > 2 || Math.Abs(baselineMetrics.BackgroundCountsPerBin - 20.0) > 0.01 || Math.Abs(baselineMetrics.FwhmChannels - 40.0) > 3.0)
                throw new InvalidOperationException("ROI/background-corrected FWHM failed");
            results.Add("ROI/background-corrected FWHM PASS");

            double highZLoad = InputMath.BoardLoadOhms(false);
            double terminatedLoad = InputMath.BoardLoadOhms(true);
            double highZToHighZ = InputMath.ActualInputMv(500.0, 50.0, highZLoad, 0);
            double highZTo50 = InputMath.ActualInputMv(500.0, 50.0, terminatedLoad, 0);
            double load50To50 = InputMath.ActualInputMv(500.0, 50.0, terminatedLoad, 1);
            double load50DisplayWith100Source = InputMath.ActualInputMv(500.0, 100.0, terminatedLoad, 1);
            double sourceFromTerminated250 = InputMath.SourceOpenEquivalentMv(250.0, 50.0, terminatedLoad);
            double sourceReference1000 = InputMath.SourceOpenCircuitMv(500.0, 50.0, 1);
            if (Math.Abs(highZToHighZ - 499.975) > 0.01 || Math.Abs(highZTo50 - 249.994) > 0.02 ||
                Math.Abs(load50To50 - 499.988) > 0.03 || Math.Abs(load50DisplayWith100Source - 499.983) > 0.03 ||
                Math.Abs(sourceFromTerminated250 - 500.013) > 0.03 || Math.Abs(sourceReference1000 - 1000.0) > 0.001)
                throw new InvalidOperationException("impedance conversion failed");
            results.Add("impedance conversion PASS");

            const double configuredGain = 2.25;
            double boardInput = (906.0 - 0.0) / configuredGain;
            double predictedAdc = boardInput * configuredGain;
            if (Math.Abs(boardInput - 402.6666666666667) > 1e-9 || Math.Abs(predictedAdc - 906.0) > 1e-9)
                throw new InvalidOperationException("configurable linear gain conversion failed");
            results.Add("configurable linear amplitude gain PASS");

            List<TestPoint> linear = new List<TestPoint>();
            linear.Add(new TestPoint { ActualInputMv = 100, MeasuredInputMv = 101 });
            linear.Add(new TestPoint { ActualInputMv = 500, MeasuredInputMv = 501 });
            linear.Add(new TestPoint { ActualInputMv = 900, MeasuredInputMv = 901 });
            double slope, intercept, r2, maxNl, maxSpanNl, maxResidualMv;
            if (!InputMath.LinearFit(linear, out slope, out intercept, out r2, out maxNl, out maxSpanNl, out maxResidualMv) || Math.Abs(slope - 1.0) > 1e-9 || Math.Abs(intercept - 1.0) > 1e-9 || Math.Abs(r2 - 1.0) > 1e-12)
                throw new InvalidOperationException("linearity fit failed");
            results.Add("linearity fit PASS");

            double seconds, measuredHz, passPercent, processingPercent, triggerAcceptancePercent;
            uint sampleDelta, busyDelta;
            if (!InputMath.WindowMetrics(1000U, 1010U, 10000U, 100999U, 101009U, 110000U, 1000.0,
                    out seconds, out sampleDelta, out busyDelta, out measuredHz, out passPercent, out processingPercent, out triggerAcceptancePercent) ||
                Math.Abs(seconds - 100.0) > 1e-12 || Math.Abs(measuredHz - 999.99) > 1e-9 || Math.Abs(passPercent - 99.999) > 1e-9)
                throw new InvalidOperationException("same-window pass-rate calculation failed");
            if (InputMath.WindowMetrics(1000U, 1010U, 10000U, 10U, 20U, 12000U, 1000.0,
                    out seconds, out sampleDelta, out busyDelta, out measuredHz, out passPercent, out processingPercent, out triggerAcceptancePercent))
                throw new InvalidOperationException("counter-regression window rejection failed");
            results.Add("same-window MCU uptime rate/pass calculation and reset rejection PASS");

            const double calibrationSlope = 1.00414838025;
            const double calibrationIntercept = -1.160606424;
            double calibrationActual = 250.0;
            double calibrationMeasured = calibrationSlope * calibrationActual + calibrationIntercept;
            double calibrationRecovered = (calibrationMeasured - calibrationIntercept) / calibrationSlope;
            if (Math.Abs(calibrationRecovered - calibrationActual) > 1e-9)
                throw new InvalidOperationException("inverse linear calibration failed");
            results.Add("editable inverse linear calibration PASS");

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (MainForm form = new MainForm())
            {
                form.ExerciseLogScaleForSelfTest();
                form.ExerciseSpectrumViewForSelfTest();
                form.ExerciseChannelSwitchForSelfTest();
                form.ExercisePassRateColorsForSelfTest();
            }
            results.Add("repeated log/linear axis toggle PASS");
            results.Add("empty/full/zoom spectrum view and peak-snap cursor FWHM PASS");
            results.Add("4096/8192/16384/65536 channel-switch atomic view reset PASS");
            results.Add("pass-rate >=99% green and efficiency >=99.9% green color thresholds PASS");

            string[] normalizedPorts = MainForm.NormalizePortInventory(new[] { "COM8", "com8", " COM3 ", "COM12", "", "  " });
            if (normalizedPorts.Length != 3 || normalizedPorts[0] != "COM3" || normalizedPorts[1] != "COM8" || normalizedPorts[2] != "COM12")
                throw new InvalidOperationException("USB CDC port deduplication failed");
            results.Add("USB CDC port deduplication and numeric ordering PASS");

            using (ScrollableMetricLabel scrollLabel = new ScrollableMetricLabel())
            {
                scrollLabel.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
                scrollLabel.Width = 150;
                scrollLabel.Height = 19;
                scrollLabel.Text = "128,004 / 队列0 / 恢复2 / SDO高";
                if (!scrollLabel.NeedsScrolling || scrollLabel.ScrollOffset != 0)
                    throw new InvalidOperationException("long metric text should need scrolling and start at the first line");
                scrollLabel.ProcessWheelDelta(-120);
                if (scrollLabel.ScrollOffset <= 0)
                    throw new InvalidOperationException("wheel down did not reveal the wrapped line");
                int bottom = 0;
                while (true)
                {
                    int before = scrollLabel.ScrollOffset;
                    scrollLabel.ProcessWheelDelta(-120);
                    if (scrollLabel.ScrollOffset < before)
                        throw new InvalidOperationException("wheel down must never move upward");
                    if (scrollLabel.ScrollOffset == before) { bottom = before; break; }
                }
                if (bottom <= 0)
                    throw new InvalidOperationException("long metric text should reach a scrollable bottom");
                scrollLabel.ProcessWheelDelta(-120);
                if (scrollLabel.ScrollOffset != bottom)
                    throw new InvalidOperationException("wheel scrolling must clamp at the last line");
                while (true)
                {
                    int before = scrollLabel.ScrollOffset;
                    scrollLabel.ProcessWheelDelta(120);
                    if (scrollLabel.ScrollOffset > before)
                        throw new InvalidOperationException("wheel up must never move downward");
                    if (scrollLabel.ScrollOffset == before) break;
                }
                if (scrollLabel.ScrollOffset != 0)
                    throw new InvalidOperationException("wheel up must clamp at the first line");
                scrollLabel.Text = "--";
                if (scrollLabel.NeedsScrolling || scrollLabel.ScrollOffset != 0)
                    throw new InvalidOperationException("short metric text must not need scrolling");
                results.Add("metric value wheel scrolling with wrap clamp PASS");
            }
            File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SELF_TEST_RESULT.txt"), results.ToArray());
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--self-test")
            {
                try { SelfTest.Run(); Environment.Exit(0); }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SELF_TEST_RESULT.txt"), "FAIL: " + ex);
                    Environment.Exit(2);
                }
            }
            if (args.Length == 1 && args[0] == "--render-preview")
            {
                string report = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UI_PREVIEW_RESULT.txt");
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using (MainForm form = new MainForm())
                    {
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(-2000, -2000);
                        form.Show();
                        Application.DoEvents();
                        form.LoadPreviewData();
                        Application.DoEvents();
                        using (Bitmap image = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height));
                            image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UI_PREVIEW.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                        form.Close();
                    }
                    File.WriteAllText(report, "PASS");
                }
                catch (Exception ex)
                {
                    File.WriteAllText(report, "FAIL: " + ex);
                }
                return;
            }
            if (args.Length == 1 && args[0] == "--render-empty-preview")
            {
                string report = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EMPTY_PREVIEW_RESULT.txt");
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using (MainForm form = new MainForm())
                    {
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(-2000, -2000);
                        form.Show();
                        Application.DoEvents();
                        using (Bitmap image = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height));
                            image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EMPTY_PREVIEW.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                        form.Close();
                    }
                    File.WriteAllText(report, "PASS");
                }
                catch (Exception ex) { File.WriteAllText(report, "FAIL: " + ex); }
                return;
            }
            if (args.Length == 1 && args[0] == "--render-formula-preview")
            {
                string report = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FORMULA_PREVIEW_RESULT.txt");
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using (MainForm form = new MainForm())
                    {
                        form.RenderFormulaPreviewFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FORMULA_PREVIEW.png"));
                    }
                    File.WriteAllText(report, "PASS");
                }
                catch (Exception ex) { File.WriteAllText(report, "FAIL: " + ex); }
                return;
            }
            if (args.Length == 1 && args[0] == "--render-hover-preview")
            {
                string report = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HOVER_PREVIEW_RESULT.txt");
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using (MainForm form = new MainForm())
                    {
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(-2000, -2000);
                        form.Show();
                        Application.DoEvents();
                        form.LoadPreviewData();
                        form.ShowBottomRightFormulaPreview();
                        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HOVER_DIAGNOSTICS.txt"), form.FormulaHoverDiagnostics());
                        using (Bitmap image = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height));
                            form.CompositeFormulaHoverPreview(image);
                            image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HOVER_PREVIEW.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                        form.Close();
                    }
                    File.WriteAllText(report, "PASS");
                }
                catch (Exception ex) { File.WriteAllText(report, "FAIL: " + ex); }
                return;
            }
            if (args.Length == 1 && args[0] == "--render-cursor-preview")
            {
                string report = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CURSOR_PREVIEW_RESULT.txt");
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using (MainForm form = new MainForm())
                    {
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(-2000, -2000);
                        form.Show();
                        Application.DoEvents();
                        form.LoadPreviewData();
                        form.ShowSpectrumCursorPreview();
                        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CURSOR_DIAGNOSTICS.txt"), form.SpectrumCursorDiagnostics());
                        using (Bitmap image = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height));
                            image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CURSOR_PREVIEW.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                        form.Close();
                    }
                    File.WriteAllText(report, "PASS");
                }
                catch (Exception ex) { File.WriteAllText(report, "FAIL: " + ex); }
                return;
            }
            if (args.Length == 1 && args[0] == "--render-metric-preview")
            {
                string report = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "METRIC_PREVIEW_RESULT.txt");
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using (MainForm form = new MainForm())
                    {
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(-2000, -2000);
                        form.Show();
                        Application.DoEvents();
                        form.LoadPreviewData();
                        form.PrepareMetricScrollPreview();
                        Application.DoEvents();
                        using (Bitmap image = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height));
                            image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "METRIC_PREVIEW.png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                        form.SaveMetricScrollPreview("METRIC_SCROLL_BEFORE.png");
                        form.ScrollMetricForPreview(-120);
                        Application.DoEvents();
                        form.SaveMetricScrollPreview("METRIC_SCROLL_AFTER.png");
                        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "METRIC_DIAGNOSTICS.txt"), form.MetricScrollDiagnostics());
                        form.Close();
                    }
                    File.WriteAllText(report, "PASS");
                }
                catch (Exception ex) { File.WriteAllText(report, "FAIL: " + ex); }
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
