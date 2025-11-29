using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;
using NativeLookupAPI.API;

namespace NativeLookupAPI;

internal class NativeLookupAPI
{
	public const string GUID = MyPluginInfo.PLUGIN_GUID;
	public const string NAME = MyPluginInfo.PLUGIN_NAME;
	public const string VERSION = MyPluginInfo.PLUGIN_VERSION;
	
	internal static readonly BepInPlugin Plugin = new BepInPlugin(GUID, NAME, VERSION);
	
	internal static ManualLogSource Log { get; } = Logger.CreateLogSource(NAME);
	
	public static IEnumerable<string> TargetDLLs => [];
	
	public static void Patch(AssemblyDefinition assembly){}

	public static readonly string ModCachePath = Path.GetFullPath(Path.Combine(Paths.CachePath, NAME));
	public static readonly string PdbCachePath = Path.GetFullPath(Path.Combine(ModCachePath, ".pdb"));
	
	// Cannot be renamed, method name is important
	public static void Initialize()
	{
		Log.LogInfo("Preloader Started");
		Directory.CreateDirectory(PdbCachePath);
		//trigger static constructor
		_ = CommonLibraries.UnityPlayer;
	}

	// Cannot be renamed, method name is important
	public static void Finish()
	{
		Log.LogInfo("Preloader Finished");
	}
}
