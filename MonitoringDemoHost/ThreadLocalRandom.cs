using System;
using System.Threading;

/// <remarks>https://blogs.msmvps.com/jonskeet/2009/11/04/revisiting-randomness/</remarks>
static class ThreadLocalRandom
{
    /// <summary> 
    /// Random number generator used to generate seeds, 
    /// which are then used to create new random number 
    /// generators on a per-thread basis. 
    /// </summary> 
    static readonly Random globalRandom = new Random();
    static readonly object globalLock = new object();

    static readonly ThreadLocal<Random> threadRandom = new ThreadLocal<Random>(NewRandom);

    /// <summary> 
    /// Creates a new instance of Random. The seed is derived 
    /// from a global (static) instance of Random, rather 
    /// than time. 
    /// </summary> 
    public static Random NewRandom()
    {
        lock (globalLock) return new Random(globalRandom.Next());
    }

    public static Random Instance => threadRandom.Value;

    public static int Next()
    {
        return Instance.Next();
    }

    public static int Next(int maxValue)
    {
        return Instance.Next(maxValue);
    }

    public static int Next(int minValue, int maxValue)
    {
        return Instance.Next(minValue, maxValue);
    }

    public static double NextDouble()
    {
        return Instance.NextDouble();
    }

    public static void NextBytes(byte[] buffer)
    {
        Instance.NextBytes(buffer);
    }
}