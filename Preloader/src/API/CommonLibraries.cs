using System.IO;
using System.Runtime.CompilerServices;
using NativeLookupAPI.Proxy;

namespace NativeLookupAPI.API;

public static class CommonLibraries
{
    public static NativeLibrary UnityPlayer { get; }
    
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    static CommonLibraries()
    {
        UnityPlayer = NativeLibrary.Find(m => m.ModuleName.Equals("UnityPlayer.dll"));
        if (!UnityPlayer.HasPdb)
            UnityPlayer.SymbolServers = ["https://symbolserver.unity3d.com/"];
        
        if (!UnityPlayer.HasPdb)
            return;

        var info = UnityPlayer.LibraryInfo!.Value;
        var guid = info.PdbSig70.ToString("N").ToUpper() + info.PdbAge;

        var cacheFile = Path.Combine(NativeLookupAPI.ModCachePath, $"{info.ModuleName}.{guid}.tsv");

        if (File.Exists(cacheFile))
        {
            try
            {
                using var reader = new StreamReader(cacheFile);
                while (reader.Peek() >= 0)
                {
                    var line = reader.ReadLine()!;
                    var split = line.Split("\t");
                    UnityPlayer.SymbolCache[(DbgHelp.SymbolType)uint.Parse(split[0])][split[1]] = uint.Parse(split[2]);
                }
            }
            catch
            {
                // ignored
            }
        }

        UnityPlayer.OnFinalize += (library) =>
        {
            if (!library.HasPdb)
                return;
            
            var finalInfo = library.LibraryInfo!.Value;
            var finalGuid = finalInfo.PdbSig70.ToString("N").ToUpper() + finalInfo.PdbAge;

            var outputCacheFile = Path.Combine(NativeLookupAPI.ModCachePath, $"{finalInfo.ModuleName}.{finalGuid}.tsv");
            try
            {
                using var writer = new StreamWriter(outputCacheFile);
                foreach (var (symbol, memory) in library.SymbolCache)
                {
                    foreach (var (name, offset) in memory)
                    {
                        writer.WriteLine($"{symbol:D}\t{name}\t0x{offset:x8}");
                    }
                }
            }
            catch
            {
                // ignored
            }
        };

    }
}