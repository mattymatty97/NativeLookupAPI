namespace NativeLookupAPI.API;

public static class CommonLibraries
{
    public static NativeLibrary UnityPlayer { get; }

    static CommonLibraries()
    {
        UnityPlayer = NativeLibrary.Find(m => m.ModuleName.Equals("UnityPlayer.dll"))!;
        UnityPlayer.SymbolServers = ["https://symbolserver.unity3d.com/"];
        UnityPlayer.CacheDirectory = NativeLookupAPI.SymbolCachePath;
        UnityPlayer.LoadCache();
    }
}
