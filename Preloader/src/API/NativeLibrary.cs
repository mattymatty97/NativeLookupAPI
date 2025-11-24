using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using NativeLookupAPI.Proxy;
#pragma warning disable CS8500 // This takes the address of, gets the size of, or declares a pointer to a managed type

namespace NativeLookupAPI.API;

public unsafe class NativeLibrary
{

    public static NativeLibrary Find(Predicate<ProcessModule> modulePredicate)
    {
        var entry = KnownModules.FirstOrDefault(
            e => modulePredicate.Invoke(e.Key) && e.Value.TryGetTarget(out _)
            );
        if (entry.Key != null && entry.Value.TryGetTarget(out var library))
            return library;
        
        var modules = Process.GetCurrentProcess().Modules;

        ProcessModule target = null;
        
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

    public IntPtr Address;

    public bool HasPdb { get; private set; }
    
    public DbgHelp.ImageHlpModule64 LibraryInfo { get; private set; } = default;

    public string[] SymbolServers
    {
        get => _symbolServers;
        set => UpdatePdb(value);
    }

    public int LastError { get; private set; }

    public event Action<NativeLibrary> OnFinalize;

    public IDictionary<DbgHelp.SymbolType,IDictionary<string, uint>> SymbolCache { get; }
    
    public IDictionary<DbgHelp.SymbolType, IDictionary<string, uint>> PdbSymbols
    {
        get
        {
            Dictionary<DbgHelp.SymbolType, IDictionary<string, uint>> ret = new();

            var symbols = PdbSymbolsInternal;
            foreach (var (key, value) in symbols)
            {
                if (value.Count <= 0)
                    continue;
                
                ret[key] = new ReadOnlyDictionary<string, uint>(value);
            }
            
            return new ReadOnlyDictionary<DbgHelp.SymbolType, IDictionary<string, uint>>(ret);
        }
    }

    public bool TryGetExportedFunctionOffset(string functionName, out uint offset)
    {
        offset = 0;
        
        if (!SymbolCache.TryGetValue(DbgHelp.SymbolType.Export, out var cache))
            SymbolCache[DbgHelp.SymbolType.Export] = cache = new Dictionary<string, uint>();
        
        if(cache.TryGetValue(functionName, out offset))
        {
            return offset != 0;
        }
            
        var address = Kernel32.GetExportedFunctionAddress(Address, functionName);
        if (address == IntPtr.Zero)
        {
            offset = 0;
            cache[functionName] = 0;
            LastError = Marshal.GetLastWin32Error();
            return false;
        }

        offset = (uint)(address.ToInt32() - Address.ToInt32());
        cache[functionName] = offset;
        return true;
    }

    
    public bool TryGetSymbolOffset(DbgHelp.SymbolType symbolType, string symbolName, out uint symbolOffset)
    {
        if (symbolType == DbgHelp.SymbolType.Export)
            return TryGetExportedFunctionOffset(symbolName, out symbolOffset);
        
        if (!SymbolCache.TryGetValue(symbolType, out var cache))
            SymbolCache[DbgHelp.SymbolType.Export] = cache = new Dictionary<string, uint>();
        
        if (cache.TryGetValue(symbolName, out symbolOffset))
            return symbolOffset != 0;
        
        if (!HasPdb)
            return false;
        
        var found = PdbSymbolsInternal.TryGetValue(symbolType, out var symbols) && 
                symbols.TryGetValue(symbolName, out symbolOffset);
            
        cache[symbolName] = found ? symbolOffset : 0;
            
        return found;
    }
    
    // Internal Data

    private static readonly Dictionary<ProcessModule, WeakReference<NativeLibrary>> KnownModules = [];
    
    private string[] _symbolServers = [];
        
    private readonly WeakReference<IDictionary<DbgHelp.SymbolType, IDictionary<string, uint>>> _weakPdbSymbols = new(null);

    
    private IDictionary<DbgHelp.SymbolType, IDictionary<string, uint>> PdbSymbolsInternal {
        get
        {
            if(_weakPdbSymbols.TryGetTarget(out var map))
                return map;
                
            map = new Dictionary<DbgHelp.SymbolType, IDictionary<string, uint>>();

            if (HasPdb)
            {
                if (!DbgHelp.EnumerateSymbols(Address, Address, "!", PopulateMap, 0))
                {
                    
                    LastError = Marshal.GetLastWin32Error();
                    NativeLookupAPI.Log.LogError($"{nameof(LastError)}: {LastError}");
                }
            }

            _weakPdbSymbols.SetTarget(map);
            
            return map;

            bool PopulateMap(ref DbgHelp.SymbolInfo symbolInfo, uint size, IntPtr context)
            {
                var name = symbolInfo.Name;
                
                if (!map.TryGetValue(symbolInfo.Tag, out var sub ))
                    map[symbolInfo.Tag] = sub = new Dictionary<string, uint>();
                
                sub[name] = (uint)(symbolInfo.Address - (ulong)Address.ToInt64());

                return true;
            }
        }
    }
    
    private NativeLibrary(ProcessModule module)
    {
        if (module.BaseAddress == IntPtr.Zero)
            throw new DllNotFoundException();
        
        Module = module;
            
        Address = module.BaseAddress;
        
        SymbolCache = new Dictionary<DbgHelp.SymbolType, IDictionary<string, uint>>();

        UpdatePdb([]);
    }

    private void UpdatePdb([NotNull] string[] newSymbolServers)
    {
        HasPdb = false;
        
        DbgHelp.Cleanup(Address);

        if (newSymbolServers == null)
            newSymbolServers = [];
        
        _symbolServers = newSymbolServers;

        var symbolPath = $"cache*{NativeLookupAPI.PdbCachePath}";

        foreach (var server in _symbolServers)
        {
            if (server.IndexOfAny(['*', ' ']) != -1)
                continue;
            
            symbolPath += $";srv*{server}";
        }
        
        
        if (!DbgHelp.Initialize(Address, symbolPath, false) || 
            !DbgHelp.RegisterCallback(Address, DbgHelpCallback, Address))
        {
            LastError = Marshal.GetLastWin32Error();
            return;
        }

        var baseAddress = DbgHelp.LoadPdb(Address, IntPtr.Zero, Name, null, Address, 0, IntPtr.Zero, 0 );
        
        if (baseAddress == IntPtr.Zero)
        {
            LastError = Marshal.GetLastWin32Error();
            return;
        }
        
        var moduleInfo = new DbgHelp.ImageHlpModule64()
        {
            SizeOfStruct = (uint)Marshal.SizeOf(typeof(DbgHelp.ImageHlpModule64))
        };
            
        if(!DbgHelp.CheckPdb(Address, baseAddress, ref moduleInfo))
        {
            LastError = Marshal.GetLastWin32Error();
            return;
        }

        LibraryInfo = moduleInfo;
        
        HasPdb = moduleInfo.SymType == DbgHelp.SymType.SymPdb;
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

    ~NativeLibrary() => OnFinalize?.Invoke(this);
    
    
}