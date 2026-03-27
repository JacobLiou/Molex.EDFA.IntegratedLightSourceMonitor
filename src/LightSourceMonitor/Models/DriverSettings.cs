namespace LightSourceMonitor.Models;

public class DriverSettings
{
    // Supported values: Simulated, Hardware
    public string Mode { get; set; } = "Simulated";

    // Argument used by PD driver Open(...).
    public string PdOpenArgument { get; set; } = "SIM";

    // WM configuration XML path, e.g. .\\config\\UDL_WM.xml
    public string WmConfigXmlPath { get; set; } = "";
}
