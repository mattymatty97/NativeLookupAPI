using System;
using System.Runtime.InteropServices;

namespace NativeLookupAPI.Proxy;

public static class DbgHelp
{
    [DllImport("dbghelp.dll", EntryPoint = "SymInitialize", SetLastError = true, CharSet = CharSet.Ansi)]
    internal static extern bool Initialize(IntPtr hProcess, string userSearchPath, bool fInvadeProcess);

    [DllImport("dbghelp.dll", EntryPoint = "SymSetOptions", SetLastError = true, CharSet = CharSet.Ansi)]
    internal static extern DbgHelpOptions SetOptions(DbgHelpOptions options);

    [DllImport("dbghelp.dll", EntryPoint = "SymGetOptions", SetLastError = true, CharSet = CharSet.Ansi)]
    internal static extern DbgHelpOptions GetOptions();

    [DllImport("dbghelp.dll", EntryPoint = "SymCleanup", SetLastError = true, CharSet = CharSet.Ansi)]
    internal static extern bool Cleanup(IntPtr hProcess);

    [DllImport("dbghelp.dll", EntryPoint = "SymLoadModuleEx", SetLastError = true, CharSet = CharSet.Ansi)]
    internal static extern IntPtr LoadPdb(IntPtr hProcess, IntPtr hFile, string iImageName, string? moduleName, IntPtr baseOfDll, uint dllSize, IntPtr data, uint flags);

    [DllImport("dbghelp.dll", EntryPoint = "SymGetModuleInfo64", SetLastError = true, CharSet = CharSet.Ansi)]
    internal static extern bool CheckPdb(IntPtr hProcess, IntPtr address, ref ImageHlpModule64  moduleInfo);

    [DllImport("dbghelp.dll", EntryPoint = "SymFromName", SetLastError = true, CharSet = CharSet.Ansi)]
    internal static extern bool FindSymbol(IntPtr hProcess, string name, ref SymbolInfo symbol);

    [DllImport("dbghelp.dll", EntryPoint = "SymEnumSymbols", SetLastError = true, CharSet = CharSet.Ansi)]
    internal static extern bool EnumerateSymbols(IntPtr hProcess, IntPtr baseOfDll, string filter, EnumerateSymbolsCallbackDelegate callback, uint options);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
    internal delegate bool EnumerateSymbolsCallbackDelegate(ref SymbolInfo symbolInfo, uint size, IntPtr context);

    [DllImport("dbghelp.dll", EntryPoint = "SymRegisterCallback64", SetLastError = true, CharSet = CharSet.Ansi)]
    internal static extern bool RegisterCallback(IntPtr hProcess, LogCallbackDelegate callback, IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
    internal delegate bool LogCallbackDelegate(IntPtr hProcess, SymActionCode actionCode, IntPtr data, IntPtr context);

    static DbgHelp()
    {
	    //force dbghelp to use winhttp instead of winInet
	    Environment.SetEnvironmentVariable("DBGHELP_WINHTTP", "1");

        //set dbghelp options
        SetOptions(
            DbgHelpOptions.Debug |
            DbgHelpOptions.CaseInsensitive | 
            DbgHelpOptions.DisableSymsrvAutodetect |
            DbgHelpOptions.FavorCompressed |
            DbgHelpOptions.NoPrompts |
            DbgHelpOptions.Undname
        );
    }

    public enum SymbolType: uint
    {
        None = 0,
        Exe,
        Compiland,
        CompilandDetails,
        CompilandEnv,
        Function,
        Block,
        Data,
        Annotation,
        Label,
        PublicSymbol,
        UDT,
        Enum,
        FunctionType,
        PointerType,
        ArrayType,
        BaseType,
        Typedef,
        BaseClass,
        Friend,
        FunctionArgType,
        FuncDebugStart,
        FuncDebugEnd,
        UsingNamespace,
        VTableShape,
        VTable,
        Custom,
        Thunk,
        CustomType,
        ManagedType,
        Dimension,
        CallSite,
        InlineSite,
        BaseInterface,
        VectorType,
        MatrixType,
        HLSLType,
        Caller,
        Callee,
        Export,
        HeapAllocationSite,
        CoffGroup,
        MaxValue
    }

    public enum SymActionCode : ulong
    {
        DebugInfo        = 0x10000000,
        SrvDebugInfo     = 0x20000000,
        SymbolLoadStart  = 0x00000001,
        SymbolLoadEnd    = 0x00000002,
        SymbolLoadFail   = 0x00000003
    }

    [Flags]
    public enum DbgHelpOptions : ulong
    {
	    /// <summary>
	    /// Enables the use of symbols that are stored with absolute addresses. Most symbols are stored as RVAs from the base of the module. DbgHelp translates them to absolute addresses. There are symbols that are stored as an absolute address. These have very specialized purposes and are typically not used.
	    /// 
	    /// DbgHelp 5.1 and earlier:  This value is not supported.
	    /// </summary>
        AllowAbsoluteSymbols        = 0x00000800UL,
	    /// <summary>
	    /// Enables the use of symbols that do not have an address. By default, DbgHelp filters out symbols that do not have an address.
	    /// </summary>
        AllowZeroAddress            = 0x01000000UL,
	    /// <summary>
	    /// Do not search the public symbols when searching for symbols by address, or when enumerating symbols, unless they were not found in the global symbols or within the current scope. This option has no effect with SYMOPT_PUBLICS_ONLY.
		///
	    /// DbgHelp 5.1 and earlier:  This value is not supported.
	    /// </summary>
        AutoPublics                 = 0x00010000UL,
	    /// <summary>
	    /// All symbol searches are insensitive to case. 
	    /// </summary>
        CaseInsensitive             = 0x00000001UL,
	    /// <summary>
	    /// Pass debug output through OutputDebugString or the SymRegisterCallbackProc64 callback function. 
	    /// </summary>
        Debug                       = 0x80000000UL,
	    /// <summary>
	    /// Symbols are not loaded until a reference is made requiring the symbols be loaded. This is the fastest, most efficient way to use the symbol handler. 
	    /// </summary>
        DeferredLoads               = 0x00000004UL,
	    /// <summary>
	    /// Disables the auto-detection of symbol server stores in the symbol path, even without the "SRV*" designation, maintaining compatibility with previous behavior.
	    ///
	    /// DbgHelp 6.6 and earlier:  This value is not supported.
	    /// </summary>
        DisableSymsrvAutodetect     = 0x02000000UL,
	    /// <summary>
	    /// Do not load an unmatched .pdb file. Do not load export symbols if all else fails. 
	    /// </summary>
        ExactSymbols                = 0x00000400UL,
	    /// <summary>
	    /// Do not display system dialog boxes when there is a media failure such as no media in a drive. Instead, the failure happens silently. 
	    /// </summary>
        FailCriticalErrors          = 0x00000200UL,
	    /// <summary>
	    /// If there is both an uncompressed and a compressed file available, favor the compressed file. This option is good for slow connections. 
	    /// </summary>
        FavorCompressed             = 0x00800000UL,
	    /// <summary>
	    /// Symbols are stored in the root directory of the default downstream store.
		///
	    /// DbgHelp 6.1 and earlier:  This value is not supported.
	    /// </summary>
        FlatDirectory               = 0x00400000UL,
	    /// <summary>
	    /// Ignore path information in the CodeView record of the image header when loading a .pdb file. 
	    /// </summary>
        IgnoreCvrec                 = 0x00000080UL,
	    /// <summary>
	    /// Ignore the image directory.
		///
	    /// DbgHelp 6.1 and earlier:  This value is not supported.
	    /// </summary>
        IgnoreImagedir              = 0x00200000UL,
	    /// <summary>
	    /// Do not use the path specified by _NT_SYMBOL_PATH if the user calls SymSetSearchPath without a valid path.
	    ///
	    /// DbgHelp 5.1:  This value is not supported.
	    /// </summary>
        IgnoreNtSympath             = 0x00001000UL,
	    /// <summary>
	    /// When debugging on 64-bit Windows, include any 32-bit modules. 
	    /// </summary>
        Include32BitModules         = 0x00002000UL,
	    /// <summary>
	    /// Disable checks to ensure a file (.exe, .dbg., or .pdb) is the correct file. Instead, load the first file located.
	    /// </summary>
        LoadAnything                = 0x00000040UL,
	    /// <summary>
	    /// Loads line number information. 
	    /// </summary>
        LoadLines                   = 0x00000010UL,
	    /// <summary>
	    /// All C++ decorated symbols containing the symbol separator "::" are replaced by "__". This option exists for debuggers that cannot handle parsing real C++ symbol names. 
	    /// </summary>
        NoCpp                       = 0x00000008UL,
	    /// <summary>
	    /// Do not search the image for the symbol path when loading the symbols for a module if the module header cannot be read.
		///
	    /// DbgHelp 5.1:  This value is not supported.
	    /// </summary>
        NoImageSearch               = 0x00020000UL,
	    /// <summary>
	    /// Prevents prompting for validation from the symbol server. 
	    /// </summary>
        NoPrompts                   = 0x00080000UL,
	    /// <summary>
	    /// Do not search the publics table for symbols. This option should have little effect because there are copies of the public symbols in the globals table.
		///
	    /// DbgHelp 5.1:  This value is not supported.
	    /// </summary>
        NoPublics                   = 0x00008000UL,
	    /// <summary>
	    /// Prevents symbols from being loaded when the caller examines symbols across multiple modules. Examine only the module whose symbols have already been loaded. 
	    /// </summary>
        NoUnqualifiedLoads          = 0x00000100UL,
	    /// <summary>
	    /// Overwrite the downlevel store from the symbol store.
		///
	    /// DbgHelp 6.1 and earlier:  This value is not supported.
	    /// </summary>
        Overwrite                   = 0x00100000UL,
	    /// <summary>
	    /// Do not use private symbols. The version of DbgHelp that shipped with earlier Windows release supported only public symbols; this option provides compatibility with this limitation.
	    /// DbgHelp 5.1:  This value is not supported.
	    /// </summary>
        PublicsOnly                 = 0x00004000UL,
	    /// <summary>
	    /// DbgHelp will not load any symbol server other than SymSrv. SymSrv will not use the downstream store specified in _NT_SYMBOL_PATH. After this flag has been set, it cannot be cleared.

	    /// DbgHelp 6.0 and 6.1:  This flag can be cleared.

	    /// DbgHelp 5.1:  This value is not supported.
	    /// </summary>
        Secure                      = 0x00040000UL,
	    /// <summary>
	    /// All symbols are presented in undecorated form.
		///
	    /// This option has no effect on global or local symbols because they are stored undecorated. This option applies only to public symbols.
	    /// </summary>
        Undname                     = 0x00000002UL,
    }

    public enum SymType : uint
    {
	    SymNone = 0,
	    SymCoff = 1,
	    SymCv = 2,
	    SymPdb = 3,
	    SymExport = 4,
	    SymDeferred = 5,
	    SymSym = 6,
	    SymDia = 7,
	    SymVirtual = 8,
	    NumSymTypes = 9,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct ImageHlpModule64
	{
		internal uint SizeOfStruct;

		internal ulong BaseOfImage;

		internal uint ImageSize;

		internal uint TimeDateStamp;

		internal uint CheckSum;

		internal uint NumSyms;

		internal SymType SymType;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		internal string ModuleName;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
		internal string ImageName;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
		internal string LoadedImageName;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
		internal string LoadedPdbName;

		internal uint CVSig;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 780)]
		internal string CVData;

		internal uint PdbSig;

		internal Guid PdbSig70;

		internal uint PdbAge;

		[MarshalAs(UnmanagedType.Bool)]
		internal bool PdbUnmatched;

		[MarshalAs(UnmanagedType.Bool)]
		internal bool DbgUnmatched;

		[MarshalAs(UnmanagedType.Bool)]
		internal bool LineNumbers;

		[MarshalAs(UnmanagedType.Bool)]
		internal bool GlobalSymbols;

		[MarshalAs(UnmanagedType.Bool)]
		internal bool TypeInfo;

		[MarshalAs(UnmanagedType.Bool)]
		internal bool SourceIndexed;

		[MarshalAs(UnmanagedType.Bool)]
		internal bool Publics;

		internal uint MachineType;

		internal uint Reserved;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
	public struct SymbolInfo
	{
		internal uint SizeOfStruct;

		internal uint TypeIndex;

		internal ulong Reserved_1;
		internal ulong Reserved_2;

		internal uint Index;

		internal uint Size;

		internal ulong ModBase;

		internal uint Flags;

		internal ulong Value;

		internal ulong Address;

		internal uint Register;

		internal uint Scope;

		internal SymbolType Tag;

		internal uint NameLen;

		internal uint MaxNameLen;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
		internal string Name;
	}
}