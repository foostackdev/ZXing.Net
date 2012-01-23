/*
* Copyright 2007 ZXing authors
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

using System;
using System.Collections.Generic;

using com.google.zxing.common;
using Version = com.google.zxing.qrcode.decoder.Version;

namespace com.google.zxing.qrcode.detector
{
   /// <summary> <p>Encapsulates logic that can detect a QR Code in an image, even if the QR Code
   /// is rotated or skewed, or partially obscured.</p>
   /// 
   /// </summary>
   /// <author>  Sean Owen
   /// </author>
   /// <author>www.Redivivus.in (suraj.supekar@redivivus.in) - Ported from ZXING Java Source 
   /// </author>
   public class Detector
   {
      private BitMatrix image;
      private ResultPointCallback resultPointCallback;

      public Detector(BitMatrix image)
      {
         this.image = image;
      }

      virtual protected internal BitMatrix Image
      {
         get
         {
            return image;
         }
      }

      virtual protected internal ResultPointCallback ResultPointCallback
      {
         get
         {
            return resultPointCallback;
         }
      }

      /// <summary> <p>Detects a QR Code in an image, simply.</p>
      /// 
      /// </summary>
      /// <returns> {@link DetectorResult} encapsulating results of detecting a QR Code
      /// </returns>
      /// <throws>  ReaderException if no QR Code can be found </throws>
      public virtual DetectorResult detect()
      {
         return detect(null);
      }

      /// <summary> <p>Detects a QR Code in an image, simply.</p>
      /// 
      /// </summary>
      /// <param name="hints">optional hints to detector
      /// </param>
      /// <returns> {@link DetectorResult} encapsulating results of detecting a QR Code
      /// </returns>
      /// <throws>  ReaderException if no QR Code can be found </throws>
      public virtual DetectorResult detect(IDictionary<DecodeHintType, object> hints)
      {
         resultPointCallback = hints == null || !hints.ContainsKey(DecodeHintType.NEED_RESULT_POINT_CALLBACK) ? null : (ResultPointCallback)hints[DecodeHintType.NEED_RESULT_POINT_CALLBACK];

         FinderPatternFinder finder = new FinderPatternFinder(image, resultPointCallback);
         FinderPatternInfo info = finder.find(hints);

         return processFinderPatternInfo(info);
      }

      protected internal virtual DetectorResult processFinderPatternInfo(FinderPatternInfo info)
      {
         FinderPattern topLeft = info.TopLeft;
         FinderPattern topRight = info.TopRight;
         FinderPattern bottomLeft = info.BottomLeft;

         float moduleSize = calculateModuleSize(topLeft, topRight, bottomLeft);
         if (moduleSize < 1.0f)
         {
            throw ReaderException.Instance;
         }
         int dimension = computeDimension(topLeft, topRight, bottomLeft, moduleSize);
         Version provisionalVersion = Version.getProvisionalVersionForDimension(dimension);
         int modulesBetweenFPCenters = provisionalVersion.DimensionForVersion - 7;

         AlignmentPattern alignmentPattern = null;
         // Anything above version 1 has an alignment pattern
         if (provisionalVersion.AlignmentPatternCenters.Length > 0)
         {

            // Guess where a "bottom right" finder pattern would have been
            float bottomRightX = topRight.X - topLeft.X + bottomLeft.X;
            float bottomRightY = topRight.Y - topLeft.Y + bottomLeft.Y;

            // Estimate that alignment pattern is closer by 3 modules
            // from "bottom right" to known top left location
            //UPGRADE_WARNING: Data types in Visual C# might be different.  Verify the accuracy of narrowing conversions. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1042'"
            float correctionToTopLeft = 1.0f - 3.0f / (float)modulesBetweenFPCenters;
            //UPGRADE_WARNING: Data types in Visual C# might be different.  Verify the accuracy of narrowing conversions. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1042'"
            int estAlignmentX = (int)(topLeft.X + correctionToTopLeft * (bottomRightX - topLeft.X));
            //UPGRADE_WARNING: Data types in Visual C# might be different.  Verify the accuracy of narrowing conversions. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1042'"
            int estAlignmentY = (int)(topLeft.Y + correctionToTopLeft * (bottomRightY - topLeft.Y));

            // Kind of arbitrary -- expand search radius before giving up
            for (int i = 4; i <= 16; i <<= 1)
            {
               try
               {
                  //UPGRADE_WARNING: Data types in Visual C# might be different.  Verify the accuracy of narrowing conversions. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1042'"
                  alignmentPattern = findAlignmentInRegion(moduleSize, estAlignmentX, estAlignmentY, (float)i);
                  break;
               }
               catch (ReaderException)
               {
                  // try next round
               }
            }
            // If we didn't find alignment pattern... well try anyway without it
         }

         PerspectiveTransform transform = createTransform(topLeft, topRight, bottomLeft, alignmentPattern, dimension);

         BitMatrix bits = sampleGrid(image, transform, dimension);

         ResultPoint[] points;
         if (alignmentPattern == null)
         {
            points = new ResultPoint[] { bottomLeft, topLeft, topRight };
         }
         else
         {
            points = new ResultPoint[] { bottomLeft, topLeft, topRight, alignmentPattern };
         }
         return new DetectorResult(bits, points);
      }

      public virtual PerspectiveTransform createTransform(ResultPoint topLeft, ResultPoint topRight, ResultPoint bottomLeft, ResultPoint alignmentPattern, int dimension)
      {
         //UPGRADE_WARNING: Data types in Visual C# might be different.  Verify the accuracy of narrowing conversions. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1042'"
         float dimMinusThree = (float)dimension - 3.5f;
         float bottomRightX;
         float bottomRightY;
         float sourceBottomRightX;
         float sourceBottomRightY;
         if (alignmentPattern != null)
         {
            bottomRightX = alignmentPattern.X;
            bottomRightY = alignmentPattern.Y;
            sourceBottomRightX = sourceBottomRightY = dimMinusThree - 3.0f;
         }
         else
         {
            // Don't have an alignment pattern, just make up the bottom-right point
            bottomRightX = (topRight.X - topLeft.X) + bottomLeft.X;
            bottomRightY = (topRight.Y - topLeft.Y) + bottomLeft.Y;
            sourceBottomRightX = sourceBottomRightY = dimMinusThree;
         }

         return PerspectiveTransform.quadrilateralToQuadrilateral(
            3.5f, 
            3.5f,
            dimMinusThree,
            3.5f,
            sourceBottomRightX,
            sourceBottomRightY,
            3.5f,
            dimMinusThree,
            topLeft.X, 
            topLeft.Y,
            topRight.X, 
            topRight.Y,
            bottomRightX, 
            bottomRightY, 
            bottomLeft.X,
            bottomLeft.Y);
      }

      private static BitMatrix sampleGrid(BitMatrix image, PerspectiveTransform transform, int dimension)
      {

         GridSampler sampler = GridSampler.Instance;
         return sampler.sampleGrid(image, dimension, dimension, transform);
      }

      /// <summary> <p>Computes the dimension (number of modules on a size) of the QR Code based on the position
      /// of the finder patterns and estimated module size.</p>
      /// </summary>
      protected internal static int computeDimension(ResultPoint topLeft, ResultPoint topRight, ResultPoint bottomLeft, float moduleSize)
      {
         int tltrCentersDimension = round(ResultPoint.distance(topLeft, topRight) / moduleSize);
         int tlblCentersDimension = round(ResultPoint.distance(topLeft, bottomLeft) / moduleSize);
         int dimension = ((tltrCentersDimension + tlblCentersDimension) >> 1) + 7;
         switch (dimension & 0x03)
         {
            // mod 4
            case 0:
               dimension++;
               break;
            // 1? do nothing
            case 2:
               dimension--;
               break;
            case 3:
               throw ReaderException.Instance;
         }
         return dimension;
      }

      /// <summary> <p>Computes an average estimated module size based on estimated derived from the positions
      /// of the three finder patterns.</p>
      /// </summary>
      protected internal virtual float calculateModuleSize(ResultPoint topLeft, ResultPoint topRight, ResultPoint bottomLeft)
      {
         // Take the average
         return (calculateModuleSizeOneWay(topLeft, topRight) + calculateModuleSizeOneWay(topLeft, bottomLeft)) / 2.0f;
      }

      /// <summary> <p>Estimates module size based on two finder patterns -- it uses
      /// {@link #sizeOfBlackWhiteBlackRunBothWays(int, int, int, int)} to figure the
      /// width of each, measuring along the axis between their centers.</p>
      /// </summary>
      private float calculateModuleSizeOneWay(ResultPoint pattern, ResultPoint otherPattern)
      {
         //UPGRADE_WARNING: Data types in Visual C# might be different.  Verify the accuracy of narrowing conversions. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1042'"
         float moduleSizeEst1 = sizeOfBlackWhiteBlackRunBothWays((int)pattern.X, (int)pattern.Y, (int)otherPattern.X, (int)otherPattern.Y);
         //UPGRADE_WARNING: Data types in Visual C# might be different.  Verify the accuracy of narrowing conversions. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1042'"
         float moduleSizeEst2 = sizeOfBlackWhiteBlackRunBothWays((int)otherPattern.X, (int)otherPattern.Y, (int)pattern.X, (int)pattern.Y);
         if (Single.IsNaN(moduleSizeEst1))
         {
            return moduleSizeEst2 / 7.0f;
         }
         if (Single.IsNaN(moduleSizeEst2))
         {
            return moduleSizeEst1 / 7.0f;
         }
         // Average them, and divide by 7 since we've counted the width of 3 black modules,
         // and 1 white and 1 black module on either side. Ergo, divide sum by 14.
         return (moduleSizeEst1 + moduleSizeEst2) / 14.0f;
      }

      /// <summary> See {@link #sizeOfBlackWhiteBlackRun(int, int, int, int)}; computes the total width of
      /// a finder pattern by looking for a black-white-black run from the center in the direction
      /// of another point (another finder pattern center), and in the opposite direction too.</p>
      /// </summary>
      private float sizeOfBlackWhiteBlackRunBothWays(int fromX, int fromY, int toX, int toY)
      {

         float result = sizeOfBlackWhiteBlackRun(fromX, fromY, toX, toY);

         // Now count other way -- don't run off image though of course
         float scale = 1.0f;
         int otherToX = fromX - (toX - fromX);
         if (otherToX < 0)
         {
            //UPGRADE_WARNING: Data types in Visual C# might be different.  Verify the accuracy of narrowing conversions. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1042'"
            scale = (float)fromX / (float)(fromX - otherToX);
            otherToX = 0;
         }
         else if (otherToX >= image.Width)
         {
            //UPGRADE_WARNING: Data types in Visual C# might be different.  Verify the accuracy of narrowing conversions. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1042'"
            scale = (float)(image.Width - 1 - fromX) / (float)(otherToX - fromX);
            otherToX = image.Width - 1;
         }
         //UPGRADE_WARNING: Data types in Visual C# might be different.  Verify the accuracy of narrowing conversions. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1042'"
         int otherToY = (int)(fromY - (toY - fromY) * scale);

         scale = 1.0f;
         if (otherToY < 0)
         {
            //UPGRADE_WARNING: Data types in Visual C# might be different.  Verify the accuracy of narrowing conversions. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1042'"
            scale = (float)fromY / (float)(fromY - otherToY);
            otherToY = 0;
         }
         else if (otherToY >= image.Height)
         {
            //UPGRADE_WARNING: Data types in Visual C# might be different.  Verify the accuracy of narrowing conversions. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1042'"
            scale = (float)(image.Height - 1 - fromY) / (float)(otherToY - fromY);
            otherToY = image.Height - 1;
         }
         //UPGRADE_WARNING: Data types in Visual C# might be different.  Verify the accuracy of narrowing conversions. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1042'"
         otherToX = (int)(fromX + (otherToX - fromX) * scale);

         result += sizeOfBlackWhiteBlackRun(fromX, fromY, otherToX, otherToY);
         return result - 1.0f; // -1 because we counted the middle pixel twice
      }

      /// <summary> <p>This method traces a line from a point in the image, in the direction towards another point.
      /// It begins in a black region, and keeps going until it finds white, then black, then white again.
      /// It reports the distance from the start to this point.</p>
      /// 
      /// <p>This is used when figuring out how wide a finder pattern is, when the finder pattern
      /// may be skewed or rotated.</p>
      /// </summary>
      private float sizeOfBlackWhiteBlackRun(int fromX, int fromY, int toX, int toY)
      {
         // Mild variant of Bresenham's algorithm;
         // see http://en.wikipedia.org/wiki/Bresenham's_line_algorithm
         bool steep = Math.Abs(toY - fromY) > Math.Abs(toX - fromX);
         if (steep)
         {
            int temp = fromX;
            fromX = fromY;
            fromY = temp;
            temp = toX;
            toX = toY;
            toY = temp;
         }

         int dx = Math.Abs(toX - fromX);
         int dy = Math.Abs(toY - fromY);
         int error = -dx >> 1;
         int xstep = fromX < toX ? 1 : -1;
         int ystep = fromY < toY ? 1 : -1;

         // In black pixels, looking for white, first or second time.
         int state = 0;
         // Loop up until x == toX, but not beyond
         int xLimit = toX + xstep;
         for (int x = fromX, y = fromY; x != xLimit; x += xstep)
         {
            int realX = steep ? y : x;
            int realY = steep ? x : y;

            // Does current pixel mean we have moved white to black or vice versa?
            // Scanning black in state 0,2 and white in state 1, so if we find the wrong
            // color, advance to next state or end if we are in state 2 already
            if ((state == 1) == image[realX, realY])
            {
               if (state == 2)
               {
                  int diffX = x - fromX;
                  int diffY = y - fromY;
                  return (float)Math.Sqrt(diffX * diffX + diffY * diffY);
               }
               state++;
            }
            error += dy;
            if (error > 0)
            {
               if (y == toY)
               {


                  break;
               }
               y += ystep;
               error -= dx;
            }
         }
         // Found black-white-black; give the benefit of the doubt that the next pixel outside the image
         // is "white" so this last point at (toX+xStep,toY) is the right ending. This is really a
         // small approximation; (toX+xStep,toY+yStep) might be really correct. Ignore this.
         if (state == 2)
         {
            int diffX = toX + xstep - fromX;
            int diffY = toY - fromY;
            return (float)Math.Sqrt(diffX * diffX + diffY * diffY);
         }
         // else we didn't find even black-white-black; no estimate is really possible
         return Single.NaN;

      }

      /// <summary> <p>Attempts to locate an alignment pattern in a limited region of the image, which is
      /// guessed to contain it. This method uses {@link AlignmentPattern}.</p>
      /// 
      /// </summary>
      /// <param name="overallEstModuleSize">estimated module size so far
      /// </param>
      /// <param name="estAlignmentX">x coordinate of center of area probably containing alignment pattern
      /// </param>
      /// <param name="estAlignmentY">y coordinate of above
      /// </param>
      /// <param name="allowanceFactor">number of pixels in all directions to search from the center
      /// </param>
      /// <returns> {@link AlignmentPattern} if found, or null otherwise
      /// </returns>
      /// <throws>  ReaderException if an unexpected error occurs during detection </throws>
      protected internal virtual AlignmentPattern findAlignmentInRegion(float overallEstModuleSize, int estAlignmentX, int estAlignmentY, float allowanceFactor)
      {
         // Look for an alignment pattern (3 modules in size) around where it
         // should be
         //UPGRADE_WARNING: Data types in Visual C# might be different.  Verify the accuracy of narrowing conversions. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1042'"
         int allowance = (int)(allowanceFactor * overallEstModuleSize);
         int alignmentAreaLeftX = Math.Max(0, estAlignmentX - allowance);
         int alignmentAreaRightX = Math.Min(image.Width - 1, estAlignmentX + allowance);
         if (alignmentAreaRightX - alignmentAreaLeftX < overallEstModuleSize * 3)
         {
            throw ReaderException.Instance;
         }

         int alignmentAreaTopY = Math.Max(0, estAlignmentY - allowance);
         int alignmentAreaBottomY = Math.Min(image.Height - 1, estAlignmentY + allowance);

         var alignmentFinder = new AlignmentPatternFinder(
            image, 
            alignmentAreaLeftX, 
            alignmentAreaTopY,
            alignmentAreaRightX - alignmentAreaLeftX, 
            alignmentAreaBottomY - alignmentAreaTopY,
            overallEstModuleSize, 
            resultPointCallback);

         return alignmentFinder.find();
      }

      /// <summary> Ends up being a bit faster than Math.round(). This merely rounds its argument to the nearest int,
      /// where x.5 rounds up.
      /// </summary>
      private static int round(float d)
      {
         //UPGRADE_WARNING: Data types in Visual C# might be different.  Verify the accuracy of narrowing conversions. "ms-help://MS.VSCC.v80/dv_commoner/local/redirect.htm?index='!DefaultContextWindowIndex'&keyword='jlca1042'"
         return (int)(d + 0.5f);
      }
   }
}