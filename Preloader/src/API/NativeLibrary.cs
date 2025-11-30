using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NativeLookupAPI.Proxy;

namespace NativeLookupAPI.API;

public class NativeLibrary
{

    public static NativeLibrary? Find(Predicate<ProcessModule> modulePredicate)
    {
        var entry = KnownModules.FirstOrDefault(
            e => modulePredicate.Invoke(e.Key) && e.Value.TryGetTarget(out _)
            );
        if (entry.Key != null && entry.Value.TryGetTarget(out var library))
            return library;

        var modules = Process.GetCurrentProcess().Modules;

        ProcessModule? target = null;

        for (var i = 0; i < modules.Count; i++)
        {
            var module = modules[i];
            if (!modulePredicate.Invoke(module))
                continue;

            target = module;
            break;
        }

        if (target == null)
            return null;

        library = new NativeLibrary(target);
        KnownModules[target] = new WeakReference<NativeLibrary>(library);
        return library;
    }

    public ProcessModule Module { get; }

    public string Name => Module.ModuleName;

    public IntPtr Address => Module.BaseAddress;

    public string? CacheDirectory { get; set; }

    public string[] SymbolServers { get; set; } = [];

    public int LastError { get; private set; }

    public IDictionary<DbgHelp.SymbolType, IDictionary<string, nint>> PdbSymbols
    {
        get
        {
            Dictionary<DbgHelp.SymbolType, IDictionary<string, nint>> ret = new();

            var symbols = PdbSymbolsInternal;
            foreach (var (key, value) in symbols)
            {
                if (value.Count <= 0)
                    continue;

                ret[key] = new ReadOnlyDictionary<string, nint>(value);
            }

            return new ReadOnlyDictionary<DbgHelp.SymbolType, IDictionary<string, nint>>(ret);
        }
    }

    public void LoadCache()
    {
        var cacheFile = CacheFilePath;
        if (cacheFile == null)
            return;
        if (!File.Exists(cacheFile))
            return;

        try
        {
            using var reader = new StreamReader(cacheFile);
            while (reader.Peek() >= 0)
            {
                var line = reader.ReadLine()!;
                var split = line.Split("\t");

                var symbolType = (DbgHelp.SymbolType)uint.Parse(split[0]);
                if (!_symbolCache.TryGetValue(symbolType, out var cache))
                    cache = _symbolCache[symbolType] = [];

                var symbolName = split[1];

                var addressString = split[2];
                if (!addressString.StartsWith("0x"))
                    throw new FormatException("Address did not start with '0x'");
                var address = (nint)ulong.Parse(addressString[2..], NumberStyles.HexNumber);
                cache[symbolName] = address;
            }
        }
        catch (Exception e)
        {
            NativeLookupAPI.Log.LogError($"Exception while loading cache, it will be regenerated.");
            NativeLookupAPI.Log.LogError(e);
        }
    }

    public void WriteCache()
    {
        var cacheFile = CacheFilePath;
        if (cacheFile == null)
            return;
        try
        {
            using var writer = new StreamWriter(cacheFile);
            foreach (var (symbol, memory) in _symbolCache)
            {
                foreach (var (name, offset) in memory)
                    writer.WriteLine($"{symbol:D}\t{name}\t0x{(ulong)offset:x8}");
            }
        }
        catch (Exception e)
        {
            NativeLookupAPI.Log.LogError($"Exception while writing cache.");
            NativeLookupAPI.Log.LogError(e);
            try
            {
                File.Delete(cacheFile);
            }
            catch
            {
                // ignored
            }
        }
    }

    public bool TryGetExportedFunctionOffset(string functionName, out nint offset)
    {
        if (!_symbolCache.TryGetValue(DbgHelp.SymbolType.Export, out var cache))
            _symbolCache[DbgHelp.SymbolType.Export] = cache = [];

        if (cache.TryGetValue(functionName, out offset))
            return offset != 0;

        var address = Kernel32.GetExportedFunctionAddress(Address, functionName);
        if (address == IntPtr.Zero)
        {
            offset = 0;
            cache[functionName] = 0;
            LastError = Marshal.GetLastWin32Error();
            WriteCache();
            return false;
        }

        offset = address.ToInt32() - Address.ToInt32();
        cache[functionName] = offset;
        WriteCache();
        return true;
    }

    public bool TryGetSymbolOffset(DbgHelp.SymbolType symbolType, string symbolName, out nint symbolOffset)
    {
        if (symbolType == DbgHelp.SymbolType.Export)
            return TryGetExportedFunctionOffset(symbolName, out symbolOffset);

        if (!_symbolCache.TryGetValue(symbolType, out var cache))
            _symbolCache[symbolType] = cache = [];

        if (cache.TryGetValue(symbolName, out symbolOffset))
            return symbolOffset != 0;

        var found = PdbSymbolsInternal.TryGetValue(symbolType, out var symbols) &&
                symbols.TryGetValue(symbolName, out symbolOffset);

        cache[symbolName] = found ? symbolOffset : 0;
        WriteCache();
        return found;
    }

    // Internal Data
    enum PdbState
    {
        Unloaded,
        Loaded,
        Failed,
    }

    private readonly string _signature;

    private PdbState _pdbState;

    private Dictionary<DbgHelp.SymbolType, Dictionary<string, nint>> _symbolCache = [];

    private static readonly Dictionary<ProcessModule, WeakReference<NativeLibrary>> KnownModules = [];

    private readonly WeakReference<Dictionary<DbgHelp.SymbolType, Dictionary<string, nint>>> _weakPdbSymbols = new(null!);


    private Dictionary<DbgHelp.SymbolType, Dictionary<string, nint>> PdbSymbolsInternal
    {
        get
        {
            if (_weakPdbSymbols.TryGetTarget(out var map))
                return map;

            map = [];

            bool PopulateMap(ref DbgHelp.SymbolInfo symbolInfo, uint size, IntPtr context)
            {
                var name = symbolInfo.Name;

                if (!map.TryGetValue(symbolInfo.Tag, out var sub))
                    map[symbolInfo.Tag] = sub = new Dictionary<string, nint>();

                sub[name] = (nint)(symbolInfo.Address - (ulong)Address.ToInt64());

                return true;
            }

            DownloadPdb();

            if (_pdbState == PdbState.Loaded)
            {
                if (!DbgHelp.EnumerateSymbols(Address, Address, "!", PopulateMap, 0))
                {
                    LastError = Marshal.GetLastWin32Error();
                    NativeLookupAPI.Log.LogError($"{nameof(LastError)}: {LastError}");
                }
            }

            _weakPdbSymbols.SetTarget(map);

            return map;
        }
    }

    private string? CacheFilePath
    {
        get
        {
            if (CacheDirectory == null)
                return null;
            return Path.Combine(CacheDirectory, $"{Module.ModuleName}.{_signature}.tsv");
        }
    }

    private string GetPDBSignature()
    {
        if (!DbgHelp.Initialize(Address, "", false))
            throw new Win32Exception();

        var baseAddress = DbgHelp.LoadPdb(Address, IntPtr.Zero, Name, null, Address, 0, IntPtr.Zero, 0);
        if (baseAddress == IntPtr.Zero)
            throw new Win32Exception();

        var moduleInfo = new DbgHelp.ImageHlpModule64()
        {
            SizeOfStruct = (uint)Marshal.SizeOf(typeof(DbgHelp.ImageHlpModule64))
        };

        if (!DbgHelp.CheckPdb(Address, Address, ref moduleInfo))
            throw new Win32Exception();

        DbgHelp.Cleanup(Address);
        return $"{moduleInfo.PdbSig70.ToString("N").ToUpper()}{moduleInfo.PdbAge}";
    }

    private NativeLibrary(ProcessModule module)
    {
        if (module.BaseAddress == IntPtr.Zero)
            throw new DllNotFoundException();

        Module = module;
        _signature = GetPDBSignature();
    }

    private bool DownloadPdb()
    {
        if (_pdbState != PdbState.Unloaded)
            return true;

        _pdbState = PdbState.Failed;

        DbgHelp.Cleanup(Address);

        var symbolPath = $"cache*{NativeLookupAPI.PdbCachePath}";

        foreach (var server in SymbolServers)
        {
            if (server.IndexOfAny(['*', ' ']) != -1)
                continue;

            symbolPath += $";srv*{server}";
        }

        if (!DbgHelp.Initialize(Address, symbolPath, false) ||
            !DbgHelp.RegisterCallback(Address, DbgHelpCallback, Address))
        {
            LastError = Marshal.GetLastWin32Error();
            return false;
        }

        var baseAddress = DbgHelp.LoadPdb(Address, IntPtr.Zero, Name, null, Address, 0, IntPtr.Zero, 0);

        if (baseAddress == IntPtr.Zero)
        {
            LastError = Marshal.GetLastWin32Error();
            return false;
        }

        var moduleInfo = new DbgHelp.ImageHlpModule64()
        {
            SizeOfStruct = (uint)Marshal.SizeOf(typeof(DbgHelp.ImageHlpModule64))
        };

        if (!DbgHelp.CheckPdb(Address, baseAddress, ref moduleInfo))
        {
            LastError = Marshal.GetLastWin32Error();
            return false;
        }

        if (moduleInfo.SymType == DbgHelp.SymType.SymPdb)
        {
            _pdbState = PdbState.Loaded;
            return true;
        }

        return false;
    }

    private bool DbgHelpCallback(IntPtr hProcess, DbgHelp.SymActionCode actionCode, IntPtr data, IntPtr context)
    {
        if (hProcess != Address)
            return false;

        switch (actionCode)
        {
            case DbgHelp.SymActionCode.DebugInfo:
            case DbgHelp.SymActionCode.SrvDebugInfo:
                var dbgMessage = Marshal.PtrToStringAnsi(data)!.TrimEnd();
                NativeLookupAPI.Log.LogDebug($"{Name}\t{new string(dbgMessage.Where(c => !char.IsControl(c) || Environment.NewLine.IndexOf(c) != -1).ToArray())}");
                break;
            case DbgHelp.SymActionCode.SymbolLoadStart:
                NativeLookupAPI.Log.LogInfo($"{Name}\tStarted Loading PDB Files");
                break;
            case DbgHelp.SymActionCode.SymbolLoadEnd:
                NativeLookupAPI.Log.LogInfo($"{Name}\tSuccessfully Loaded PDB Files");
                break;
            case DbgHelp.SymActionCode.SymbolLoadFail:
                NativeLookupAPI.Log.LogInfo($"{Name}\tFailed to Load PDB Files");
                break;
        }

        return false;
    }

}