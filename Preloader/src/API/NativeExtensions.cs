using System;
using System.Runtime.InteropServices;
using NativeLookupAPI.Proxy;

namespace NativeLookupAPI.API;

public static class NativeExtensions
{
    
    public static nint? GetExportedFunctionOffset(this NativeLibrary library, string functionName)
    {
        if(library.TryGetExportedFunctionOffset(functionName, out var offset))
            return offset;
        return null;
    }
    
    public static nint? GetSymbolOffset(this NativeLibrary library, DbgHelp.SymbolType symbolType, string symbolName)
    {
        if(library.TryGetSymbolOffset(symbolType, symbolName, out var offset))
            return offset;
        return null;
    }
    
    public static bool TryGetNativeFunction<T>(this NativeLibrary library, string functionName,
        out NativeFunction<T> nativeFunction) where T : Delegate
    {
        nativeFunction = default;
        if (!library.TryGetSymbolOffset(DbgHelp.SymbolType.Export, functionName, out var offset)
            && !library.TryGetSymbolOffset(DbgHelp.SymbolType.PublicSymbol, functionName, out offset))
            return false;
        
        nativeFunction = new NativeFunction<T>(library, functionName, offset);
        return true;
    }
    
    public static NativeFunction<T>? GetNativeFunction<T>(this NativeLibrary library, string functionName) where T : Delegate
    {
        if (library.TryGetNativeFunction<T>(functionName, out var function))
            return function;
        
        return  null;
    }
    
    
    public struct NativeFunction<T> where T : Delegate
    {
        public NativeLibrary Library { get; }

        public nint          Offset { get; }
        
        public string        Name { get; }

        public IntPtr        Address => Library.Address + Offset;
        
        internal NativeFunction(NativeLibrary library, string functionName, nint offset)
        {
            Library = library;
            Name = functionName;
            Offset = offset;
        }
        
        private T? _delegate = null;
        
        public T Delegate
        {
            get
            {
                _delegate ??= Marshal.GetDelegateForFunctionPointer<T>(Address);

                return _delegate;
            }
        }
        
    }
    
}