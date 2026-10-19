# Abre GoPilot como aplicación (solo para pruebas): sin el argumento «standalone» se abre en Game Bar.
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
[ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IApplicationActivationManager {
    int ActivateApplication(string appUserModelId, string arguments, int options, out uint processId);
}
[ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")] class ApplicationActivationManager { }
public static class GoPilotLauncher {
    public static uint Open(string aumid) {
        uint pid; ((IApplicationActivationManager)new ApplicationActivationManager()).ActivateApplication(aumid, "standalone", 0, out pid); return pid;
    }
}
'@
[GoPilotLauncher]::Open("$((Get-AppxPackage GoSPanel).PackageFamilyName)!App") | Out-Null
