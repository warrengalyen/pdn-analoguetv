﻿using PaintDotNet;
using System.Numerics;

namespace AnalogTVFilter
{
    // The SECAM format, used in France, etc.
    public class SECAMFormat : AnalogFormat
    {
        public SECAMFormat() : base(0.299, // R to Y
                                   0.587, // G to Y
                                   0.114, // B to Y
                                   1.333, // Db maximum
                                   -1.333, // Dr maximum
                                   0.0, // Chroma conversion phase relative to YUV (YDbDr is just YUV but scaled differently)
                                   5e+6, // Main bandwidth
                                   0.75e+6, // Side bandwidth
                                   2.0 * 428125, // Color bandwidth lower part
                                   2.0 * 428125, // Color bandwidth upper part
                                   4328125, // Color subcarrier frequency
                                   625, // Total scanlines
                                   576, // Visible scanlines
                                   50.0, // Nominal framerate
                                   5.195e-5, // Active video time
                                   true) // Interlaced?
        { }

        private readonly double[] SubCarrierFrequencies = { 4250000,   // Db
                                                            4406250 }; // Dr
        private readonly double[] SubCarrierAngFrequencies = { 4250000 * 2.0 * Math.PI,   // Db
                                                               4406250 * 2.0 * Math.PI }; // Dr
        private readonly double[] AngFrequencyShifts = { 230000 * 2.0 * Math.PI,   // Db
                                                         280000 * 2.0 * Math.PI }; // Dr
        private readonly double[] SubCarrierLowerFrequencies = { 2.0 * 506000,   // Db
                                                                 2.0 * 350000 }; // Dr
        private readonly double[] SubCarrierUpperFrequencies = { 2.0 * 350000,   // Db
                                                                 2.0 * 506000 }; // Dr

        public override Surface Decode(double[] signal, int activeWidth, double crosstalk = 0.0, double resonance = 1.0, double scanlineJitter = 0.0, int channelFlags = 0x7)
        {
            int[] activeSignalStarts = new int[videoScanlines]; // Start points of the active parts
            byte R = 0;
            byte G = 0;
            byte B = 0;
            double Y = 0.0;
            double Db = 0.0;
            double Dr = 0.0;
            int polarity = 0;
            int pos = 0;
            int DbPos = 0;
            int DrPos = 0;
            int componentAlternate = 0; // SECAM alternates between Db and Dr with each scanline
            double sigNum = 0.0;
            double freqPoint = 0.0;
            double sampleRate = signal.Length / frameTime;

            double blendStr = 1.0 - crosstalk;
            bool inclY = ((channelFlags & 0x1) == 0) ? false : true;
            bool inclDb = ((channelFlags & 0x2) == 0) ? false : true;
            bool inclDr = ((channelFlags & 0x4) == 0) ? false : true;

            for (int i = 0; i < videoScanlines; i++) // Where the active signal starts
            {
                activeSignalStarts[i] = (int)((((double)i * (double)signal.Length) / (double)videoScanlines) + ((scanlineTime - realActiveTime) / (2 * realActiveTime)) * activeWidth);
            }

            Complex[] signalFT = MathUtil.FourierTransform(signal, 1);
            double specRate = (2.0 * Math.PI * sampleRate) / signalFT.Length;
            signalFT = MathUtil.BandPassFilter(signalFT, sampleRate, (mainBandwidth - sideBandwidth) / 2.0, mainBandwidth + sideBandwidth, resonance); // Restrict bandwidth to the actual broadcast bandwidth
            Complex[] DbColorSignalFT = MathUtil.BandPassFilter(signalFT, sampleRate, ((SubCarrierUpperFrequencies[0] - SubCarrierLowerFrequencies[0]) / 2.0) + SubCarrierFrequencies[0], SubCarrierLowerFrequencies[0] + SubCarrierUpperFrequencies[0], resonance, blendStr); // Extract color information
            Complex[] DrColorSignalFT = MathUtil.BandPassFilter(signalFT, sampleRate, ((SubCarrierUpperFrequencies[1] - SubCarrierLowerFrequencies[1]) / 2.0) + SubCarrierFrequencies[1], SubCarrierLowerFrequencies[1] + SubCarrierUpperFrequencies[1], resonance, blendStr);
            DbColorSignalFT = MathUtil.ShiftArrayInterp(DbColorSignalFT, (3916800.0 / sampleRate) * DbColorSignalFT.Length); // apologies for the fudge factor
            DrColorSignalFT = MathUtil.ShiftArrayInterp(DrColorSignalFT, (4060800.0 / sampleRate) * DrColorSignalFT.Length); // apologies for the fudge factor
            Complex[] DbSignalFTDiff = new Complex[DbColorSignalFT.Length];
            Complex[] DrSignalFTDiff = new Complex[DrColorSignalFT.Length];
            for (int i = -DbColorSignalFT.Length / 2; i < DbColorSignalFT.Length / 2; i++)
            {
                freqPoint = i * specRate;
                DbSignalFTDiff[(i + DbColorSignalFT.Length) % DbColorSignalFT.Length] = DbColorSignalFT[(i + DbColorSignalFT.Length) % DbColorSignalFT.Length] * Complex.ImaginaryOne * freqPoint;
                DrSignalFTDiff[(i + DrColorSignalFT.Length) % DrColorSignalFT.Length] = DrColorSignalFT[(i + DrColorSignalFT.Length) % DrColorSignalFT.Length] * Complex.ImaginaryOne * freqPoint;
            }
            Complex[] DbSignalIFT = MathUtil.InverseFourierTransform(DbColorSignalFT);
            Complex[] DrSignalIFT = MathUtil.InverseFourierTransform(DrColorSignalFT);
            Complex[] DbSignalIFTDiff = MathUtil.InverseFourierTransform(DbSignalFTDiff);
            Complex[] DrSignalIFTDiff = MathUtil.InverseFourierTransform(DrSignalFTDiff);
            Complex[] DbSignalFinal = new Complex[DbSignalIFT.Length];
            Complex[] DrSignalFinal = new Complex[DrSignalIFT.Length];
            for (int i = 0; i < DbSignalFinal.Length; i++)
            {
                DbSignalFinal[i] = (DbSignalIFTDiff[i] * Complex.Conjugate(DbSignalIFT[i])) / AngFrequencyShifts[0];
                DrSignalFinal[i] = (DrSignalIFTDiff[i] * Complex.Conjugate(DrSignalIFT[i])) / AngFrequencyShifts[1];
            }
            DbSignalFinal = MathUtil.ShiftFilter(DbSignalFinal, 5.0E-7 * sampleRate, 0.5, 1.0);
            DrSignalFinal = MathUtil.ShiftFilter(DrSignalFinal, 5.0E-7 * sampleRate, 0.5, 1.0);
            double[] DbSignal = new double[signal.Length];
            double[] DrSignal = new double[signal.Length];
            signalFT = MathUtil.NotchFilter(signalFT, sampleRate, ((chromaBandwidthUpper - chromaBandwidthLower) / 2.0) + chromaCarrierFrequency, chromaBandwidthLower + chromaBandwidthUpper, resonance, blendStr);
            Complex[] finalSignal = MathUtil.InverseFourierTransform(signalFT);

            for (int i = 0; i < signal.Length; i++)
            {
                signal[i] = 1.0 * finalSignal[finalSignal.Length - 1 - i].Real;
                DbSignal[i] = 400.0 * (DbSignalFinal[finalSignal.Length - 1 - i].Imaginary);
                DrSignal[i] = 400.0 * (DrSignalFinal[finalSignal.Length - 1 - i].Imaginary);
            }

            Surface writeToSurface = new Surface(activeWidth, videoScanlines);

            MemoryBlock surfaceColors = writeToSurface.Scan0;
            int currentScanline;
            Random rng = new Random();
            int curjit = 0;
            for (int i = 0; i < videoScanlines; i++)
            {
                if (i * 2 >= videoScanlines) // Simulate interlacing
                {
                    polarity = 1;
                }
                currentScanline = isInterlaced ? (i * 2 + polarity) % videoScanlines : i;
                if ((i % 2) == 1) // Do color component alternation
                {
                    componentAlternate = 1;
                }
                else componentAlternate = 0;

                curjit = (int)(scanlineJitter * 2.0 * (rng.NextDouble() - 0.5) * activeWidth);
                pos = activeSignalStarts[i] + curjit;
                DbPos = activeSignalStarts[componentAlternate == 0 ? i : (i - 1)] + curjit;
                DrPos = activeSignalStarts[componentAlternate == 0 ? (i + 1) : i] + curjit;

                for (int j = 0; j < writeToSurface.Width; j++) // Decode active signal region only
                {
                    Y = inclY ? signal[pos] : 0.5;
                    Db = inclDb ? DbSignal[DbPos] : 0.0;
                    Dr = inclDr ? DrSignal[DrPos] : 0.0;
                    R = (byte)(MathUtil.Clamp(Math.Pow(YUVtoRGBConversionMatrix[0] * Y + YUVtoRGBConversionMatrix[2] * Dr, 0.357), 0.0, 1.0) * 255.0);
                    G = (byte)(MathUtil.Clamp(Math.Pow(YUVtoRGBConversionMatrix[3] * Y + YUVtoRGBConversionMatrix[4] * Db + YUVtoRGBConversionMatrix[5] * Dr, 0.357), 0.0, 1.0) * 255.0);
                    B = (byte)(MathUtil.Clamp(Math.Pow(YUVtoRGBConversionMatrix[6] * Y + YUVtoRGBConversionMatrix[7] * Db, 0.357), 0.0, 1.0) * 255.0);
                    surfaceColors[(currentScanline * writeToSurface.Width + j) * 4 + 3] = 255;
                    surfaceColors[(currentScanline * writeToSurface.Width + j) * 4 + 2] = R;
                    surfaceColors[(currentScanline * writeToSurface.Width + j) * 4 + 1] = G;
                    surfaceColors[(currentScanline * writeToSurface.Width + j) * 4] = B;
                    pos++;
                    DbPos++;
                    DrPos++;
                }
            }
            return writeToSurface;
        }

        public override double[] Encode(Surface surface)
        {
            // To get a good analog feel, we must limit the vertical resolution; the horizontal
            // resolution will be limited as we decode the distorted signal.
            int signalLen = (int)(surface.Width * videoScanlines * (scanlineTime / realActiveTime));
            int[] boundaryPoints = new int[videoScanlines + 1]; // Boundaries of the scanline signals
            int[] activeSignalStarts = new int[videoScanlines]; // Start points of the active parts
            double[] signalOut = new double[signalLen];
            double R = 0.0;
            double G = 0.0;
            double B = 0.0;
            double Db = 0.0;
            double Dr = 0.0;
            double time = 0;
            int pos = 0;
            int polarity = 0;
            int componentAlternate = 0; // SECAM alternates between Db and Dr with each scanline
            int remainingSync = 0;
            double sampleTime = realActiveTime / (double)surface.Width;

            Surface wrkSrf = new Surface(surface.Width, videoScanlines);
            wrkSrf.FitSurface(ResamplingAlgorithm.SuperSampling, surface);

            boundaryPoints[0] = 0; // Beginning of the signal
            boundaryPoints[videoScanlines] = signalLen; // End of the signal
            for (int i = 1; i < videoScanlines; i++) // Rest of the signal
            {
                boundaryPoints[i] = (i * signalLen) / videoScanlines;
            }

            boundPoints = boundaryPoints;

            for (int i = 0; i < videoScanlines; i++) // Where the active signal starts
            {
                activeSignalStarts[i] = (int)((((double)i * (double)signalLen) / (double)videoScanlines) + ((scanlineTime - realActiveTime) / (2 * realActiveTime)) * surface.Width) - boundaryPoints[i];
            }

            double instantPhase = 0.0;
            MemoryBlock surfaceColors = wrkSrf.Scan0;
            int currentScanline;
            for (int i = 0; i < videoScanlines; i++)
            {
                instantPhase = 0.0;
                if (i * 2 >= videoScanlines) // Simulate interlacing
                {
                    polarity = 1;
                }
                currentScanline = isInterlaced ? (i * 2 + polarity) % videoScanlines : i;
                if ((i % 2) == 1) // Do color component alternation
                {
                    componentAlternate = 1;
                }
                else componentAlternate = 0;
                for (int j = 0; j < activeSignalStarts[i]; j++) // Front porch, ignore sync signal because we don't see its results
                {
                    signalOut[pos] = 0.0;
                    pos++;
                    time = pos * sampleTime;
                }
                instantPhase = 0.0;
                for (int j = 0; j < surface.Width; j++) // Active signal
                {
                    signalOut[pos] = 0.0;
                    R = surfaceColors[(currentScanline * surface.Width + j) * 4 + 2] / 255.0;
                    G = surfaceColors[(currentScanline * surface.Width + j) * 4 + 1] / 255.0;
                    B = surfaceColors[(currentScanline * surface.Width + j) * 4] / 255.0;
                    R = Math.Pow(R, 2.8); // Gamma correction
                    G = Math.Pow(G, 2.8);
                    B = Math.Pow(B, 2.8);
                    Db = RGBtoYUVConversionMatrix[3] * R + RGBtoYUVConversionMatrix[4] * G + RGBtoYUVConversionMatrix[5] * B; // Encode Db and Dr
                    Dr = RGBtoYUVConversionMatrix[6] * R + RGBtoYUVConversionMatrix[7] * G + RGBtoYUVConversionMatrix[8] * B;
                    instantPhase += sampleTime * (SubCarrierAngFrequencies[componentAlternate] + AngFrequencyShifts[componentAlternate] * (componentAlternate == 0 ? Db : Dr));
                    signalOut[pos] += RGBtoYUVConversionMatrix[0] * R + RGBtoYUVConversionMatrix[1] * G + RGBtoYUVConversionMatrix[2] * B; //Add luma straightforwardly
                    signalOut[pos] += 0.115 * Math.Cos(instantPhase); // Add chroma via FM
                    pos++;
                    time = pos * sampleTime;
                }
                while (pos < boundaryPoints[i + 1]) // Back porch, ignore sync signal because we don't see its results
                {
                    signalOut[pos] = 0.0;
                    pos++;
                    time = pos * sampleTime;
                }
            }
            return signalOut;
        }
    }
}
