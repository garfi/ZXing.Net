/*
 * Copyright 2010 ZXing authors
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System.Collections.Generic;

using ZXing.Common;
using ZXing.Aztec.Internal;
using System;

namespace ZXing.Aztec
{
   /// <summary>
   /// This implementation can detect and decode Aztec codes in an image.
   /// </summary>
   /// <author>David Olivier</author>
   public class AztecReader : Reader
   {
      /// <summary>
      /// Locates and decodes a barcode in some format within an image.
      /// </summary>
      /// <param name="image">image of barcode to decode</param>
      /// <returns>
      /// a String representing the content encoded by the Data Matrix code
      /// </returns>
      public Result decode(BinaryBitmap image)
      {
         return decode(image, null);
      }
        
      private static Dictionary<string, Tuple<BitMatrix, float>> bestAreas;

      /// <summary>
      ///  Locates and decodes a Data Matrix code in an image.
      /// </summary>
      /// <param name="image">image of barcode to decode</param>
      /// <param name="hints">passed as a {@link java.util.Hashtable} from {@link com.google.zxing.DecodeHintType}
      /// to arbitrary data. The
      /// meaning of the data depends upon the hint type. The implementation may or may not do
      /// anything with these hints.</param>
      /// <returns>
      /// String which the barcode encodes
      /// </returns>
      public Result decode(BinaryBitmap image, IDictionary<DecodeHintType, object> hints)
      {
         var blackmatrix = image.BlackMatrix;
         if (blackmatrix == null)
            return null;

         Detector detector = new Detector(blackmatrix);
         ResultPoint[] points = null;
         DecoderResult decoderResult = null;

         var detectorResult = detector.detect(false);
         if (detectorResult != null)
         {
            points = detectorResult.Points;

            decoderResult = new Decoder().decode(detectorResult);
         }
         if (detectorResult != null && decoderResult == null)
         {
            // errors in (known) alignment lines
            var matrix = detectorResult.Bits;
            int alignmentErrors = 0;
            int totalAlignmentPoints = 0;
            for (int y = (matrix.Height >> 1) & 0xf; y < matrix.Height; y += 16)
            {
                for (int x = 0; x < matrix.Width; ++x)
                {
                    bool correctVal = ((matrix.Width >> 1) & 1) == (x & 1);
                    if (matrix[x, y] != correctVal) alignmentErrors++;
                    if (matrix[y, x] != correctVal) alignmentErrors++;
                    totalAlignmentPoints += 2;
                }
            }
            float alignmentErorRatio = ((float)alignmentErrors / totalAlignmentPoints);
            System.Diagnostics.Debug.WriteLine("alignment errors: " + alignmentErorRatio.ToString());

            // this is the best improvement in algorithm: !!!
            // split scanned matrix into areas divided by alignment lines,
            // collect best areas from multiple camera shots (best - scoring by calculating 
            // how good the alignment lines around the area are), and compile the entire matrix 
            // from all best areas
            var areaBorders = new List<int> { 0 }; // inclusive values
            for (int y = (matrix.Height >> 1) & 0xf; y < matrix.Height; y += 16)
                areaBorders.Add(y);
            areaBorders.Add(matrix.Height - 1);
            var areas = new List<Tuple<Detector.Point, Detector.Point, Detector.Point, Detector.Point, float>>();
            for (int i = 0; i + 1 < areaBorders.Count; ++i)
            {
                for (int j = 0; j + 1 < areaBorders.Count; ++j)
                {
                    var p1 = new Detector.Point(areaBorders[i], areaBorders[j]);
                    var p2 = new Detector.Point(areaBorders[i + 1], areaBorders[j]);
                    var p3 = new Detector.Point(areaBorders[i + 1], areaBorders[j + 1]);
                    var p4 = new Detector.Point(areaBorders[i], areaBorders[j + 1]);

                    int numErrors = 0;
                    int numTotal = 0;
                    if (areaBorders[i] != 0)
                    {
                        for (int y = areaBorders[j]; y <= areaBorders[j + 1]; ++y)
                        {
                            numTotal++;
                            bool correctVal = ((matrix.Height >> 1) & 1) == (y & 1);
                            if (matrix[areaBorders[i], y] != correctVal)
                                numErrors++;
                        }
                    }
                    if (areaBorders[i + 1] != matrix.Width)
                    {
                        for (int y = areaBorders[j]; y <= areaBorders[j + 1]; ++y)
                        {
                            numTotal++;
                            bool correctVal = ((matrix.Height >> 1) & 1) == (y & 1);
                            if (matrix[areaBorders[i + 1], y] != correctVal)
                                numErrors++;
                        }
                    }
                    if (areaBorders[j] != 0)
                    {
                        for (int x = areaBorders[i]; x <= areaBorders[i + 1]; ++x)
                        {
                            numTotal++;
                            bool correctVal = ((matrix.Width >> 1) & 1) == (x & 1);
                            if (matrix[x, areaBorders[j]] != correctVal)
                                numErrors++;
                        }
                    }
                    if (areaBorders[j + 1] != matrix.Height)
                    {
                        for (int x = areaBorders[i]; x <= areaBorders[i + 1]; ++x)
                        {
                            numTotal++;
                            bool correctVal = ((matrix.Width >> 1) & 1) == (x & 1);
                            if (matrix[x, areaBorders[j + 1]] != correctVal)
                                numErrors++;
                        }
                    }

                    float errorRatio = (float)numErrors / numTotal;
                    areas.Add(Tuple.Create(p1, p2, p3, p4, errorRatio));
                }
            }

            if (bestAreas == null) bestAreas = new Dictionary<string, Tuple<BitMatrix, float>>();

            bool hasBetterBest = false;
            foreach (var area in areas)
            {
                var ar = Tuple.Create(area.Item1, area.Item2, area.Item3, area.Item4).ToString();
                var errorRate = area.Item5;
                if (bestAreas.ContainsKey(ar) == false || bestAreas[ar].Item2 >= errorRate)
                {
                    var areaMatrix = new BitMatrix(area.Item2.X - area.Item1.X + 1, area.Item3.Y - area.Item2.Y + 1);
                    for (int xp = 0; xp < areaMatrix.Width; ++xp)
                    {
                        for (int yp = 0; yp < areaMatrix.Height; ++yp)
                        {
                            areaMatrix[xp, yp] = matrix[xp + area.Item1.X, yp + area.Item1.Y];
                        }
                    }
                    if (bestAreas.ContainsKey(ar) == true)
                        hasBetterBest = true;
                    bestAreas[ar] = Tuple.Create(areaMatrix, errorRate);
                }
            }

            if (hasBetterBest)
            {
                var matrixFromBestAreas = new BitMatrix(matrix.Dimension);
                foreach (var area in areas)
                {
                    var p1 = area.Item1;
                    var areaMatrix = bestAreas[Tuple.Create(area.Item1, area.Item2, area.Item3, area.Item4).ToString()].Item1;
                    for (int xp = 0; xp < areaMatrix.Width; ++xp)
                    {
                        for (int yp = 0; yp < areaMatrix.Height; ++yp)
                        {
                            matrixFromBestAreas[xp + p1.X, yp + p1.Y] = areaMatrix[xp, yp];
                        }
                    }
                }
                var composedResult = new AztecDetectorResult(matrixFromBestAreas, detectorResult.Points, detectorResult.Compact, detectorResult.NbDatablocks, detectorResult.NbLayers);
                decoderResult = new Decoder().decode(composedResult);
                if (decoderResult != null)
                {
                    bestAreas = null;
                    System.Diagnostics.Debug.WriteLine("got from composed");
                }
            }
         }
         if (decoderResult == null)
         {
            // I don't need mirrors
            //detectorResult = detector.detect(true);
            //if (detectorResult == null)
            //   return null;
               
            //points = detectorResult.Points;
            //decoderResult = new Decoder().decode(detectorResult);
            //if (decoderResult == null)
               return null;
         }

         if (hints != null &&
             hints.ContainsKey(DecodeHintType.NEED_RESULT_POINT_CALLBACK))
         {
            var rpcb = (ResultPointCallback)hints[DecodeHintType.NEED_RESULT_POINT_CALLBACK];
            if (rpcb != null)
            {
               foreach (var point in points)
               {
                  rpcb(point);
               }
            }
         }

         var result = new Result(decoderResult.Text, decoderResult.RawBytes, decoderResult.NumBits, points, BarcodeFormat.AZTEC);

         IList<byte[]> byteSegments = decoderResult.ByteSegments;
         if (byteSegments != null)
         {
            result.putMetadata(ResultMetadataType.BYTE_SEGMENTS, byteSegments);
         }
         var ecLevel = decoderResult.ECLevel;
         if (ecLevel != null)
         {
            result.putMetadata(ResultMetadataType.ERROR_CORRECTION_LEVEL, ecLevel);
         }

         result.putMetadata(ResultMetadataType.AZTEC_EXTRA_METADATA,
                            new AztecResultMetadata(detectorResult.Compact, detectorResult.NbDatablocks, detectorResult.NbLayers));

         return result;
      }

      /// <summary>
      /// Resets any internal state the implementation has after a decode, to prepare it
      /// for reuse.
      /// </summary>
      public void reset()
      {
         // do nothing
      }
   }
}