// FILTER CMR — hand joint connections drawn between MediaPipe landmarks.
//
//   THUMB  0-1-2-3-4      INDEX  0-5-6-7-8      MIDDLE 0-9-10-11-12
//   RING   0-13-14-15-16  PINKY  0-17-18-19-20  PALM   5-9-13-17-5

namespace FilterCMR.Tracking
{
    public static class HandSkeleton
    {
        /// <summary>Pairs of landmark indices (flattened: a0,b0,a1,b1,...).</summary>
        public static readonly int[] Connections =
        {
            // thumb
            0, 1, 1, 2, 2, 3, 3, 4,
            // index
            0, 5, 5, 6, 6, 7, 7, 8,
            // middle
            0, 9, 9, 10, 10, 11, 11, 12,
            // ring
            0, 13, 13, 14, 14, 15, 15, 16,
            // pinky
            0, 17, 17, 18, 18, 19, 19, 20,
            // palm
            5, 9, 9, 13, 13, 17, 17, 5,
        };

        public static int ConnectionCount => Connections.Length / 2;
    }
}
